using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Runs one scenario (GDD §4) on top of the living casualty: briefing → scripted incident (drone drop, sniper,
    /// mine, mortar, FPV, glide bomb, barrage) → care under fire (threats armed) → tactical field care (in cover) →
    /// evacuation wait (MEDEVAC by radio, time compressed) → debrief on the board. Executes the complications the
    /// pure <see cref="ScenarioTimeline"/> schedules and watches the scenario rules (TQ ≤ 60 s, airway left supine,
    /// footsteps on the mine path, TQ re-check after the drag, light discipline, head above the parapet).
    /// Without a pending launch request the scene opens on the menu board in the dugout.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class ScenarioDirector : MonoBehaviour
    {
        [Serializable]
        public sealed class SpotAnchor
        {
            public CasualtySpot spot;
            public Transform casualty;
            public Transform player;
        }

        public static ScenarioDirector Instance { get; private set; }

        public CasualtyController casualty;
        public Transform playerRig;
        public Transform head;
        public MenuBoard board;
        public Transform menuPlayerSpot;
        public SpotAnchor[] spots = Array.Empty<SpotAnchor>();
        public MinePath minePath;
        [Tooltip("Run the launch request (or the menu) on Start.")]
        public bool autoStart = true;
        [Tooltip("Tests: no scene reloads, no real-time waits in incidents, no audio.")]
        public bool testMode;

        public ResolvedScenario Run { get; private set; }
        public ScenarioTimeline Timeline { get; private set; }
        public DebriefReport Report { get; private set; }
        public ScenarioState State => Timeline != null ? Timeline.State : ScenarioState.Briefing;
        public bool InMenu { get; private set; }
        public bool Exam { get; private set; }
        public float IncidentPatientTime { get; set; } = -1f;
        /// <summary>First place each protocol code happened (x, z) — the debrief mini map.</summary>
        public readonly Dictionary<string, Vector3> MistakePlaces = new Dictionary<string, Vector3>();
        readonly List<DebriefPhase> _phases = new List<DebriefPhase>();
        int _logSeen;
        Vector3 _fallPoint;
        bool _fallKnown;
        public readonly List<string> Events = new List<string>();
        public event Action<DebriefReport> Finished;

        TourniquetCAT _preTq;
        float _supineUnconscious, _exposedSeconds, _whiteUnderDrone;
        bool _tqDeadlineChecked, _slipDone, _evacAnnounced, _reportAsked, _hit;
        string _hitText;
        int _milestone;

        void Awake() => Instance = this;

        /// <summary>w10b: outside the exam the helicopter wait (30–40 game minutes) passes in about this many seconds.</summary>
        public const float NonExamEvacWaitSeconds = 10f;
        /// <summary>w10b: calm seconds after the injury before enemy fire may target the player (not in the exam).</summary>
        public const float NonExamCalmSeconds = 8f;
        /// <summary>The casualty's physiology never runs faster than this during the shortened wait (big steps).</summary>
        public const float MaxEvacSimSpeed = 20f;

        // ------------------------------------------------------------------ w10 controls line (no medical answers)
        float _dragScanAt;
        CasualtyDrag _drag;

        // w10 audit: there was no way out of a run (the board is hidden while playing) — hold B for 2 s → menu
        float _bHeld;
        public const float ExitHoldSeconds = 2f;

        void ExitHold()
        {
            if (InMenu || testMode) { _bHeld = 0f; return; }
            var right = HandRig.Find(Handedness.Right);
            if (right == null || !right.SecondaryButton) { _bHeld = 0f; return; }
            float before = _bHeld;
            _bHeld += Time.unscaledDeltaTime;
            if (before < 0.5f && _bHeld >= 0.5f)
                FieldMedic.UI.StepGuide.Set("Держи B — выход в меню", "B ұста — мәзірге шығу", FieldMedic.UI.ControlIcon.B);
            if (before < ExitHoldSeconds && _bHeld >= ExitHoldSeconds)
            {
                Log("exit to menu (B held)");
                Haptics.Play(right, Haptics.PressStart);
                Launch(() => ScenarioLaunch.Menu());
            }
        }

        void LateUpdate()
        {
            ExitHold();
            if (InMenu || Timeline == null || casualty == null) return;
            if (_drag == null && Time.time > _dragScanAt) { _dragScanAt = Time.time + 2f; _drag = casualty.GetComponentInChildren<CasualtyDrag>(true); }
            bool dragging = _drag != null && _drag.AnyHeld;
            Coach();
            if (State == ScenarioState.Debrief) return;
            if (Autoplay) { _autoplay.Tick(); return; }
            if (dragging && (_coach == null || _coach.id is "drag_cover" or "loaded" or "board" or "wait"))
            {
                FieldMedic.UI.StepGuide.Set("Тащи: держи хват, стик назад — ползти с раненым", "Сүйре: ұстап тұр, стикті артқа — жаралымен жорғалау",
                    FieldMedic.UI.ControlIcon.Grip, FieldMedic.UI.ControlIcon.Stick);
                return;
            }
            if (Coaching)
            {
                if (_coach != null) { FieldMedic.UI.StepGuide.Set(CoachLine(false), CoachLine(true), CoachIcons(_coach)); return; }
                if (State == ScenarioState.Briefing || State == ScenarioState.Incident || IncidentPatientTime < 0f)
                {
                    FieldMedic.UI.StepGuide.Set(FirstStepRu, FirstStepKz, FieldMedic.UI.ControlIcon.Hand);
                    return;
                }
            }
            FieldMedic.UI.StepGuide.Set(ControlsLineRu, ControlsLineKz,
                FieldMedic.UI.ControlIcon.X, FieldMedic.UI.ControlIcon.Y, FieldMedic.UI.ControlIcon.Grip, FieldMedic.UI.ControlIcon.Trigger);
        }

        // ------------------------------------------------------------------ w10b coach: step-by-step hints outside the exam
        /// <summary>w10b (owner: "сделай подсказки даже в сценарии, и только в экзамене без подсказок"): the next MARCH step
        /// of this run (null in the exam / before the injury / after the end).</summary>
        public CoachStep CurrentStep => _coach;
        /// <summary>Hints are shown in this run (every mode except the exam).</summary>
        public bool Coaching => !Exam && Run != null && !InMenu;
        CoachStep _coach;
        float _coachAt;

        void Coach()
        {
            if (!Coaching || State == ScenarioState.Debrief || IncidentPatientTime < 0f || casualty.Protocol == null)
            {
                if (_coach != null || State == ScenarioState.Debrief) SetWanted(null);
                _coach = null;
                return;
            }
            if (Time.time < _coachAt) return;
            _coachAt = Time.time + 0.25f;
            var tac = TacevacController.Instance;
            var evac = tac == null || !tac.Called ? (casualty.Protocol.medevacRequested ? CoachEvac.Waiting : CoachEvac.None)
                : tac.CasualtyAboard ? CoachEvac.Aboard : tac.Landed ? CoachEvac.Landed : CoachEvac.Waiting;
            var next = ScenarioCoach.Next(casualty.Protocol, evac, Run.def.EndsWithTacevac);
            if (next != null && (_coach == null || _coach.id != next.id)) Log("coach step " + next.id);
            _coach = next;
            SetWanted(next != null ? TrainingDirector.KindOf(next.item) : null);
        }

        static void SetWanted(MedItemKind? kind)
        {
            if (KitTray.Player != null) KitTray.Player.Wanted = kind;
            StepFocus.WantedItem = kind;
        }

        string CoachLine(bool kz)
        {
            string t = _coach.Text(kz);
            if (_coach.id == "wait" && Run.def.EndsWithTacevac)
                t += kz ? " (Ұрыста тікұшақты " + Mathf.RoundToInt(Run.def.evacMinutes) + " минут күтеді. Мұнда — 10 секунд.)"
                        : " (В бою вертолёт ждут " + Mathf.RoundToInt(Run.def.evacMinutes) + " минут. Здесь — 10 секунд.)";
            if (_coach.skippable) t += kz ? "   · A ұста — қадамды өткізу" : "   · держи A — пропустить шаг";
            return t;
        }

        static FieldMedic.UI.ControlIcon[] CoachIcons(CoachStep s)
        {
            if (s.item == "Radio") return new[] { FieldMedic.UI.ControlIcon.Y };
            if (s.id is "drag_cover" or "loaded") return new[] { FieldMedic.UI.ControlIcon.Grip, FieldMedic.UI.ControlIcon.Stick };
            if (s.id is "rolled" or "recovery") return new[] { FieldMedic.UI.ControlIcon.Grip };
            if (!string.IsNullOrEmpty(s.item))
                return s.item is "Marker" or "Shears" or "ChestSeal"
                    ? new[] { FieldMedic.UI.ControlIcon.X, FieldMedic.UI.ControlIcon.Grip, FieldMedic.UI.ControlIcon.Trigger }
                    : new[] { FieldMedic.UI.ControlIcon.X, FieldMedic.UI.ControlIcon.Grip };
            if (s.id == "tq_twist") return new[] { FieldMedic.UI.ControlIcon.Grip };
            return new[] { FieldMedic.UI.ControlIcon.Hand };
        }

        string FirstStepRu => Run != null && Run.def.startPhase == Phase.CareUnderFire
            ? "Пока тихо. Когда Ерлана ранит: сначала жгут «высоко и туго», потом тащи его в укрытие. Подсказки — здесь и на часах."
            : "Пока тихо. Когда Ерлана ранит: перчатки, затем осмотр по MARCH. Подсказки — здесь и на часах.";
        string FirstStepKz => Run != null && Run.def.startPhase == Phase.CareUnderFire
            ? "Әзірге тыныш. Ерлан жараланса: алдымен жгут «жоғары әрі қатты», сосын баспанаға сүйре. Кеңестер — осында және сағатта."
            : "Әзірге тыныш. Ерлан жараланса: қолғап, сосын MARCH бойынша тексеру. Кеңестер — осында және сағатта.";

        /// <summary>w10b skip step (hold A / watch button): the game performs the current coached step.</summary>
        public bool SkipStep()
        {
            if (Exam || InMenu || Timeline == null || casualty == null || casualty.Protocol == null || State == ScenarioState.Debrief) return false;
            _coachAt = 0f;
            Coach();
            var step = _coach;
            if (step == null || !step.skippable) return false;
            var proto = casualty.Protocol;
            proto.BeginSkip(step.id, step.ru);
            bool ok;
            try { ok = StepAutoDo.Do(step.id, casualty, step.wound, this); }
            finally { proto.EndSkip(); }
            Log("skip step " + step.id + (ok ? "" : " (nothing done)"));
            _coachAt = 0f;
            return ok;
        }

        bool CanSkipStep() => Coaching && _coach != null && _coach.skippable && State != ScenarioState.Debrief;

        const string ControlsLineRu = "X — аптечка · Y (держать) — рация · GRIP / КУРОК — взять · B (держать) — меню";
        const string ControlsLineKz = "X — қобдиша · Y (ұста) — рация · GRIP / ШҮРІППЕ — алу · B (ұста) — мәзір";
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StepSkip.Unregister(SkipStep);
        }

        IEnumerator Start()
        {
            if (casualty == null) casualty = FindAnyObjectByType<CasualtyController>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (ThreatSystem.Instance != null)
            {
                ThreatSystem.Instance.showRunEndOverlay = false;
                ThreatSystem.Instance.PlayerHit += OnPlayerHit;
            }
            FieldMedic.UI.GameSettings.OverrideHintsForRun(null); // every scene load starts with the player's own hint setting
            if (!autoStart) yield break;
            var req = ScenarioLaunch.Consume();
            if (req == null || req.mode == LaunchMode.Menu) { EnterMenu(req?.lastReport); yield break; }
            // w0b: a run needs the cover/battle scenes that Main_Core streams in additively — wait for them (max 30 s)
            float waitUntil = Time.realtimeSinceStartup + 30f;
            while (!testMode && FieldMedic.Startup.GameplayScenes.Loading && Time.realtimeSinceStartup < waitUntil) yield return null;
            if (req.mode == LaunchMode.Menu) EnterMenu(req.lastReport);
            else if (req.mode == LaunchMode.Training) TrainingDirector.Ensure(this).Begin(req.lessonId);
            else Begin(ScenarioCatalog.Get(req.scenarioId) ?? ScenarioCatalog.All[0], req.variant, req.mode == LaunchMode.Exam, req.autoplay);
        }

        // ------------------------------------------------------------------ menu
        public void EnterMenu(DebriefReport last = null)
        {
            InMenu = true;
            GameSettings.OverrideHintsForRun(null);
            if (FieldMedic.Treatment.KitTray.Player != null && FieldMedic.Treatment.KitTray.Player.IsOpen) FieldMedic.Treatment.KitTray.Player.Close();
            FieldMedic.Battle.BattleDirector.PlayerArmed = false; // nobody shoots at the player in the menu
            SetQuiet(0.35f);
            if (menuPlayerSpot != null) PlacePlayer(menuPlayerSpot);
            if (board != null) { if (last != null) board.ShowDebrief(last); else board.ShowMain(); }
            FieldMedic.UI.StepGuide.Set("Меню: наведи луч на кнопку, КУРОК — нажать (или нажми пальцем)", "Мәзір: сәулені түймеге бағытта, ШҮРІППЕ — басу (не саусақпен)", FieldMedic.UI.ControlIcon.Trigger);
            // w10: the very first launch on the headset starts with the controls tutorial (skippable, replay from the menu)
            if (!testMode && !Application.isEditor && !FieldMedic.UI.GameSettings.TutorialSeen && last == null && ControlsTutorial.Instance == null)
                ControlsTutorial.Begin(this);
        }

        /// <summary>Menu → scenario: reload the scene with a launch request (fresh casualty, no leftovers).</summary>
        public static void Launch(Action request)
        {
            request();
            FieldMedic.Startup.GameplayScenes.Reload(); // async (w0b): a blocking LoadScene froze the headset → black
        }

        // ------------------------------------------------------------------ run
        /// <summary>w10g: the game plays this run itself (<see cref="AutoplayDriver"/>).</summary>
        public bool Autoplay => _autoplay != null && _autoplay.enabled;
        AutoplayDriver _autoplay;

        public void Begin(ScenarioDefinition def, ScenarioVariant variant, bool exam = false, bool autoplay = false)
        {
            if (def != null && def.selfAid)
            {
                // w9b: the player is the casualty — a separate director, no Erlan, no TACEVAC
                InMenu = false;
                Exam = false;
                Log("begin " + def.id + " (self-aid)");
                var sa = SelfAid.SelfAidDirector.Ensure(this);
                sa.Finished += r => { Report = r; Finished?.Invoke(r); };
                sa.Begin(def, variant.seed != 0 ? variant.seed : System.Environment.TickCount);
                return;
            }
            InMenu = false;
            Exam = exam;
            FieldMedic.Battle.BattleDirector.Mode = exam ? FieldMedic.Battle.BattleMode.Exam : FieldMedic.Battle.BattleMode.Combat;
            FieldMedic.Battle.BattleDirector.PlayerArmed = !testMode && !autoplay;
            Run = ScenarioCatalog.Resolve(def, variant);
            Timeline = new ScenarioTimeline(Run);
            Timeline.StateChanged += OnState;
            if (testMode) Timeline.autoEvacAfter = 1e6f;
            // V2 battlefield: the helicopter lands at the LZ and the run ends when Erlan is loaded
            var tac = TacevacController.Instance;
            if (tac != null) { tac.ResetForRun(); Timeline.waitForLoading = true; }
            _phases.Clear();
            _landSaid = false;
            MistakePlaces.Clear();
            _logSeen = 0;
            _fallKnown = false;
            float preMin = Run.preTourniquet != null ? Run.preTourniquet.minutesAgo : 0f;
            int startMin = def.clockHour * 60 + def.clockMinute - Mathf.RoundToInt(preMin);
            startMin = (startMin + 24 * 60) % (24 * 60);
            ScenarioClock.StartHour = startMin / 60;
            ScenarioClock.StartMinute = startMin % 60;
            // w10b: the 3-minute hold is 10 s outside the exam (the hint says "в бою — 3 минуты"); the exam keeps real times
            GameSettings.HoldTimeScale = testMode ? GameSettings.HoldTimeScale : HoldTimes.ScaleFor(exam);
            if (!exam && !testMode) Timeline.evacTimeScale = Mathf.Max(Timeline.evacTimeScale, def.evacMinutes * 60f / NonExamEvacWaitSeconds);
            if (Exam) GameSettings.OverrideHintsForRun(false);
            _coach = null;
            if (autoplay && !exam) _autoplay = AutoplayDriver.Ensure(this);
            if (!exam) { StepSkip.Ensure(gameObject); StepSkip.Register(SkipStep, CanSkipStep); }
            else StepSkip.Unregister(StepSkip.Handler);

            var anchor = spots.FirstOrDefault(s => s.spot == def.spot);
            if (anchor != null)
            {
                if (anchor.casualty != null && casualty != null)
                    casualty.transform.SetPositionAndRotation(anchor.casualty.position, anchor.casualty.rotation);
                if (anchor.player != null) PlacePlayer(anchor.player);
            }
            if (board != null) board.ShowBriefing(def, variant, false);
            // w10: controls on the wrist / bottom of the view (no medical answers in Combat/Exam)
            FieldMedic.UI.StepGuide.Set(ControlsLineRu, ControlsLineKz,
                FieldMedic.UI.ControlIcon.X, FieldMedic.UI.ControlIcon.Y, FieldMedic.UI.ControlIcon.Grip, FieldMedic.UI.ControlIcon.Trigger);
            if (minePath != null) minePath.gameObject.SetActive(def.incident == IncidentKind.Mine);
            Log("begin " + def.id + " " + variant);
            SetQuiet(def.incident == IncidentKind.None ? 0.6f : 0.8f);
            if (!testMode) BriefingCard.Show(def, head);
            RadioSay("briefing");
            StartCoroutine(Incident(def));
        }

        void PlacePlayer(Transform spot)
        {
            if (spot == null) return;
            PlacePlayerAt(spot.position, spot.eulerAngles.y);
        }

        public void PlacePlayerAt(Transform spot) => PlacePlayer(spot);

        public void PlacePlayerAt(Vector3 pos, float yaw)
        {
            if (playerRig == null) return;
            var sim = playerRig.GetComponent<DesktopSimulator>();
            playerRig.SetPositionAndRotation(new Vector3(pos.x, playerRig.position.y, pos.z), Quaternion.Euler(0f, yaw, 0f));
            if (sim != null) sim.SnapYaw(yaw);
        }

        void SetQuiet(float intensity)
        {
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = intensity;
            if (ThreatSystem.Instance != null) ThreatSystem.Instance.underFire = false;
        }

        IEnumerator Wait(float s)
        {
            if (testMode) { yield return null; yield break; }
            yield return new WaitForSeconds(s);
        }

        Vector3 CasualtyPos => casualty.rig != null && casualty.rig.Bone(HumanBodyBones.Hips) != null ? casualty.rig.Bone(HumanBodyBones.Hips).position : casualty.transform.position;
        Vector3 HeadPos => head != null ? head.position : CasualtyPos + Vector3.up;

        IEnumerator Incident(ScenarioDefinition def)
        {
            var amb = CombatAmbience.Instance;
            if (def.incident == IncidentKind.None || def.incident == IncidentKind.Barrage)
            {
                yield return Wait(Mathf.Min(def.incidentDelay, 2f));
                ApplyWounds(true);
                if (def.incident == IncidentKind.Barrage && !testMode) IncidentProps.Spawn(IncidentKind.Barrage, CasualtyPos, CasualtyPos);
                Timeline.Incident();
                Timeline.CasualtyDown(def.startPhase);
                CalmStart();
                HideBoardForRun();
                yield break;
            }
            yield return Wait(def.incidentDelay);
            Vector3 c = CasualtyPos;
            Vector3 blast = c;
            switch (def.incident)
            {
                case IncidentKind.DroneDrop:
                {
                    blast = new Vector3(c.x + 0.9f, 0.05f, c.z + 0.25f);
                    var recon = amb != null ? amb.StartDrone(DroneKind.Recon, 45f) : null;
                    if (recon != null) recon.HoverOver(blast, CombatAmbience.DropHeight); // it comes down over the trench
                    yield return Wait(3f);
                    if (amb != null) amb.PlayDropGrenade(blast);
                    yield return Wait(CombatAmbience.DropDelay);
                    break;
                }
                case IncidentKind.SniperShot:
                    if (amb != null) { amb.PlaySniperCrack(); amb.BulletImpact(c + Vector3.up * 1.3f); }
                    break;
                case IncidentKind.Mine:
                    blast = new Vector3(c.x, 0.02f, c.z - 0.5f);
                    if (amb != null) amb.Detonate(blast, 0.45f);
                    break;
                case IncidentKind.Mortar:
                    if (amb != null) amb.PlayIncomingShell(new Vector3(c.x + 0.6f, 1.75f, c.z + 1.0f));
                    blast = new Vector3(c.x + 0.7f, 0.05f, c.z + 0.3f);
                    yield return Wait(2.4f);
                    break;
                case IncidentKind.Fpv:
                {
                    blast = new Vector3(c.x + 1.4f, 0.05f, c.z + 0.1f);
                    var fpv = amb != null ? amb.StartDrone(DroneKind.Fpv, 4.6f) : null;
                    if (fpv != null) fpv.DiveAt(blast + Vector3.up * 0.35f); // it comes over the parapet straight at you
                    yield return Wait(4.5f);
                    if (fpv != null) fpv.Stop();
                    if (amb != null) amb.Detonate(blast + Vector3.up * 0.35f, 0.7f);
                    break;
                }
                case IncidentKind.GlideBomb:
                    if (amb != null) amb.Detonate(new Vector3(c.x + 14f, 1.75f, c.z + 22f), 2.5f);
                    if (amb != null) amb.NearBlast(6f, 1f);
                    yield return Wait(0.3f);
                    break;
            }
            ApplyWounds(false);
            casualty.Hit(def.hit);
            if (!testMode) IncidentProps.Spawn(def.incident, blast, c);
            casualty.Protocol.phase = def.startPhase;
            Timeline.Incident();
            Timeline.CasualtyDown(def.startPhase);
            CalmStart();
            if (def.startPhase == Phase.CareUnderFire && ThreatSystem.Instance != null) ThreatSystem.Instance.underFire = true;
            if (amb != null) amb.intensity = 1.2f;
            HideBoardForRun();
            Log("incident " + def.incident);
        }

        /// <summary>w10b: a few seconds of calm after the injury (no aimed fire at the medic) outside the exam.</summary>
        void CalmStart()
        {
            if (Exam || testMode) return;
            FieldMedic.Battle.EnemyFire.CalmUntil = Time.time + NonExamCalmSeconds;
            if (FieldMedic.Battle.EnemyFire.Instance != null) FieldMedic.Battle.EnemyFire.Instance.ResetExposure();
        }

        void ApplyWounds(bool alreadyDown)
        {
            var def = Run.def;
            casualty.EnsurePatient();
            var p = casualty.Patient;
            p.tbiUnconscious = def.tbi;
            p.eardrumsRuptured = def.eardrums;
            foreach (var w in Run.wounds)
            {
                var site = casualty.AddWound(w.type, w.region, Visual(w.visual), w.id, w.exitSide);
                site.Wound.posterior = w.posterior;
                site.Wound.onsetDelaySeconds = w.onsetDelay;
                site.Wound.hiddenUnderClothing = w.hidden;
            }
            if (Run.preTourniquet != null)
            {
                // the patient's clock starts at the injury: the band went on at t = 0 (written on the strap)
                var tq = Run.preTourniquet;
                p.secondsSinceStart = tq.minutesAgo * 60f;
                p.bloodLostMl = def.id == "S7" ? 750f : 500f;
                casualty.EnsureInteraction();
                _preTq = casualty.Interaction.Ifak != null ? casualty.Interaction.Ifak.Find<TourniquetCAT>() : null;
                if (_preTq != null)
                {
                    var limb = tq.limb == "legL" ? Limb.LeftLeg : tq.limb == "armL" ? Limb.LeftArm : tq.limb == "armR" ? Limb.RightArm : Limb.RightLeg;
                    _preTq.PreApply(casualty, limb, tq.position, tq.halfTurns, tq.timeMarked, 0f);
                    _preTq.allowConversion = def.timeSkipMinutes.Length > 0;
                    casualty.Protocol.tourniquetTimeMarked = tq.timeMarked;
                }
                foreach (var w in p.wounds) { w.discovered = true; casualty.Interaction.Expose(w.region); } // cut open at the earlier treatment
            }
            if (alreadyDown)
            {
                casualty.StartDown();
                casualty.Protocol.phase = def.startPhase;
                if (def.startPhase == Phase.CareUnderFire && ThreatSystem.Instance != null) ThreatSystem.Instance.underFire = true;
            }
            IncidentPatientTime = p.secondsSinceStart;
        }

        public static WoundVisual Visual(string v) => v switch
        {
            "Ragged" => WoundVisual.RaggedLaceration,
            "ChestEntry" => WoundVisual.ChestEntry,
            "Exit" => WoundVisual.ExitWound,
            "Stump" => WoundVisual.AmputationStump,
            _ => WoundVisual.FragmentHole,
        };

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (Timeline == null || casualty == null || casualty.Protocol == null) return;
            if (Timeline.State == ScenarioState.Debrief) return;
            float dt = Time.deltaTime;
            var proto = casualty.Protocol;
            float evacDt = -1f;
            if (Timeline.State == ScenarioState.Evacuation)
            {
                // only the wait for the helicopter is time-compressed; from touchdown on the drag to the door, the
                // hand-over and the boarding are played in real time (w9)
                var landed = TacevacController.Instance != null && TacevacController.Instance.Landed;
                casualty.simulationSpeed = landed ? 1f : Mathf.Max(casualty.simulationSpeed, Exam || testMode ? Timeline.evacTimeScale : Mathf.Min(Timeline.evacTimeScale, MaxEvacSimSpeed));
                // w10b: outside the exam the wait clock runs faster than his physiology (about 10 s of waiting, stable integration)
                evacDt = landed ? dt : dt * (Exam || testMode ? casualty.simulationSpeed : Timeline.evacTimeScale);
            }
            // the schedule follows the casualty's clock (tests and time-compression speed it up together)
            var due = Timeline.Tick(dt * Mathf.Max(0f, Timeline.State == ScenarioState.Evacuation ? 1f : casualty.simulationSpeed), proto.phase, proto.medevacRequested, casualty.Patient.outcome, _hit, evacDt);
            foreach (var c in due) Complication(c);
            TrackPlaces();
            var tac = TacevacController.Instance;
            if (tac != null && Timeline.waitForLoading && Timeline.State == ScenarioState.Evacuation)
            {
                if (Timeline.EvacArrived && !tac.Called) tac.Call(testMode);
                // w9: on touchdown the radio tells the medic to drag him to the helicopter (no litter cart)
                if (tac.Landed && !_landSaid)
                {
                    _landSaid = true;
                    RadioSay("evac_arrived");
                    // the LZ is secured by the escort and the door gunner: no aimed fire at the medic during the
                    // long drag to the helicopter and the lift-off (the battle itself goes on around)
                    FieldMedic.Battle.BattleDirector.PlayerArmed = false;
                }
                if (tac.Loaded && !Timeline.Loaded) Timeline.MarkLoaded();
            }
            if (Timeline.State == ScenarioState.Debrief) return;
            WatchRules(dt);
        }

        /// <summary>The board on the ruin wall is only needed for the menu, the briefing and the debrief (draw calls).</summary>
        bool _landSaid;

        void HideBoardForRun()
        {
            if (board != null && !testMode) board.gameObject.SetActive(false);
        }

        static readonly HashSet<string> PlayerCodes = new HashSet<string> { "player_exposed", "white_light", "stepped_off_path" };

        void TrackPlaces()
        {
            var log = casualty.Protocol.log;
            if (!_fallKnown && IncidentPatientTime >= 0f) { _fallKnown = true; _fallPoint = CasualtyPos; }
            for (; _logSeen < log.Count; _logSeen++)
            {
                var code = log[_logSeen].code;
                if (string.IsNullOrEmpty(code) || MistakePlaces.ContainsKey(code)) continue;
                MistakePlaces[code] = PlayerCodes.Contains(code) && head != null ? head.position : CasualtyPos;
            }
        }

        void OnState(ScenarioState s)
        {
            Log("state " + s);
            float now = casualty != null && casualty.Patient != null ? casualty.Patient.secondsSinceStart : 0f;
            if (_phases.Count > 0 && _phases[_phases.Count - 1].end < 0f) _phases[_phases.Count - 1].end = now;
            string id = s switch { ScenarioState.CareUnderFire => "CUF", ScenarioState.TacticalFieldCare => "TFC", ScenarioState.Evacuation => "TACEVAC", _ => null };
            if (id != null) _phases.Add(new DebriefPhase(id, now, -1f));
            switch (s)
            {
                case ScenarioState.TacticalFieldCare:
                    if (ThreatSystem.Instance != null) ThreatSystem.Instance.underFire = false;
                    if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.6f;
                    foreach (var c in Timeline.TakeDeferred(ComplicationKind.TqSlip)) Complication(c);
                    break;
                case ScenarioState.Evacuation:
                    RadioSay(Timeline.AutoEvac ? "evac_auto" : "medevac_ack");
                    if (Timeline.AutoEvac) casualty.Protocol.ScenarioMistake("no_medevac");
                    break;
                case ScenarioState.Debrief:
                    casualty.simulationSpeed = 1f;
                    if (Timeline.EndReason == "evacuated" && TacevacController.Instance == null) RadioSay("evac_arrived");
                    Finish();
                    break;
            }
        }

        void Complication(ComplicationSpec c)
        {
            Log("complication " + c.kind + (c.arg != null ? " " + c.arg : ""));
            var amb = CombatAmbience.Instance;
            Vector3 cp = CasualtyPos;
            switch (c.kind)
            {
                case ComplicationKind.RadioLine:
                    RadioSay(c.arg);
                    break;
                case ComplicationKind.SecondDrop:
                    if (c.arg == "mortar") { if (amb != null) amb.PlayIncomingShell(new Vector3(cp.x - 3f, 1.75f, cp.z + 2.5f)); }
                    else
                    {
                        if (amb != null) { amb.StartDrone(DroneKind.Recon, 30f); amb.PlayDropGrenade(new Vector3(cp.x + 2.5f, 1.75f, cp.z + 1.6f)); }
                        RadioSay("air");
                    }
                    if (ThreatSystem.Instance != null && State == ScenarioState.CareUnderFire) ThreatSystem.Instance.forceDroneOverhead = true;
                    break;
                case ComplicationKind.SecondFpv:
                    if (amb != null) { amb.StartDrone(DroneKind.Fpv, 9f); }
                    RadioSay("fpv");
                    break;
                case ComplicationKind.Vomit:
                    casualty.Patient.Vomit();
                    if (casualty.voice != null) casualty.voice.Vocalise(casualty.voice.bank != null ? casualty.voice.bank.cough : null, 1f, true);
                    casualty.Protocol.Note("vomit", GameSettings.T("Рвота!", "Құсу!"), Severity.Warning);
                    break;
                case ComplicationKind.TqSlip:
                    if (_preTq != null && !_slipDone) { _slipDone = true; _preTq.SlipLoose(); casualty.Protocol.Note("tq_slipped", "Жгут сполз при перемещении — кровотечение возобновилось.", Severity.Warning); }
                    break;
                case ComplicationKind.Barrage:
                    StartCoroutine(Barrage());
                    break;
                case ComplicationKind.CasualtyCrawl:
                    casualty.Pain.AddSpike(0.6f);
                    if (casualty.voice != null) casualty.voice.Say("hit_leg", true);
                    if (casualty.rig != null) casualty.rig.Flinch(1f);
                    break;
                case ComplicationKind.Wake:
                    if (casualty.voice != null) casualty.voice.Say("cold", false);
                    break;
            }
        }

        IEnumerator Barrage()
        {
            var amb = CombatAmbience.Instance;
            if (ThreatSystem.Instance != null && State == ScenarioState.CareUnderFire) ThreatSystem.Instance.underFire = true;
            for (int i = 0; i < 4; i++)
            {
                Vector3 h = HeadPos;
                var p = new Vector3(h.x + UnityEngine.Random.Range(-14f, 14f), 1.75f, h.z + UnityEngine.Random.Range(5f, 26f) * (UnityEngine.Random.value < 0.75f ? 1f : -1f));
                if (amb != null) amb.PlayIncomingShell(p);
                yield return Wait(UnityEngine.Random.Range(2.5f, 5f));
            }
        }

        // ------------------------------------------------------------------ rules
        void WatchRules(float dt)
        {
            var def = Run.def;
            var p = casualty.Patient;
            var proto = casualty.Protocol;
            float since = p.secondsSinceStart - IncidentPatientTime;

            // TQ under fire within the deadline
            if (def.tqDeadline > 0f && !_tqDeadlineChecked && IncidentPatientTime >= 0f && since > def.tqDeadline)
            {
                _tqDeadlineChecked = true;
                if (!proto.log.Any(e => e.code == "tq_ok" || e.code == "tq_partial")) proto.ScenarioMistake("tq_slow");
            }

            // unconscious, supine, airway unprotected
            if (casualty.IsDown && p.AVPU >= Consciousness.Pain && p.AirwayCompromised && p.LossFraction < 0.3f)
            {
                _supineUnconscious += dt * casualty.simulationSpeed;
                if (_supineUnconscious > 90f && !proto.penalties.ContainsKey("left_supine")) proto.ScenarioMistake("left_supine");
            }

            // light discipline / exposure from the threat system
            var ts = ThreatSystem.Instance;
            if (ts != null && State == ScenarioState.CareUnderFire && ts.HeadExposed && ts.underFire)
            {
                _exposedSeconds += dt;
                if (_exposedSeconds > 2.5f && !proto.penalties.ContainsKey("player_exposed")) proto.ScenarioMistake("player_exposed");
            }
            if (ts != null && Headlamp.Instance != null && Headlamp.Instance.Mode == LightMode.White && (ts.forceDroneOverhead || (CombatAmbience.Instance != null && CombatAmbience.Instance.DroneOverhead)))
            {
                _whiteUnderDrone += dt;
                if (_whiteUnderDrone > 3f && !proto.penalties.ContainsKey("white_light")) proto.ScenarioMistake("white_light");
            }

            // mine path
            if (minePath != null && minePath.isActiveAndEnabled && def.incident == IncidentKind.Mine && head != null && State != ScenarioState.Evacuation)
                if (minePath.OffPath(head.position) && !proto.penalties.ContainsKey("stepped_off_path"))
                {
                    proto.ScenarioMistake("stepped_off_path");
                    RadioSay("mine");
                }

            // S8: time-skip milestone reached → reassessment due by 2 h
            if (def.timeSkipMinutes.Length > 0 && _preTq != null)
            {
                float bandMin = (p.secondsSinceStart - Mathf.Max(0f, _preTq.State.appliedAtSeconds)) / 60f;
                if (bandMin > 120f && !proto.tourniquetReassessed && !proto.penalties.ContainsKey("tq_not_reassessed"))
                    proto.ScenarioMistake("tq_not_reassessed");
            }

            // commander asks for a report once in field care
            if (State == ScenarioState.TacticalFieldCare && !_reportAsked && Timeline.StateTime > 150f && !proto.medevacRequested)
            {
                _reportAsked = true;
                RadioSay("report");
            }
        }

        // ------------------------------------------------------------------ S8 time skip
        public int NextMilestone => Run != null && _milestone < Run.def.timeSkipMinutes.Length ? Run.def.timeSkipMinutes[_milestone] : -1;

        /// <summary>Wrist watch "+" button: jump to the next milestone (30 min / 1 h / 2 h / 4 h after the band).</summary>
        public bool TimeSkip()
        {
            if (Run == null || NextMilestone < 0 || State == ScenarioState.Debrief) return false;
            var p = casualty.Patient;
            float target = NextMilestone * 60f;
            float bandStart = _preTq != null ? Mathf.Max(0f, _preTq.State.appliedAtSeconds) : 0f;
            float skip = bandStart + target - p.secondsSinceStart;
            _milestone++;
            if (skip > 0f) p.Advance(skip, 2f);
            casualty.Protocol.Note("skip", GameSettings.T("Прошло время: ", "Уақыт өтті: ") + ScenarioClock.Format(p.secondsSinceStart), Severity.Info);
            Log("time skip to " + ScenarioClock.Format(p.secondsSinceStart));
            if (NextMilestone < 0 && !casualty.Protocol.medevacRequested)
            {
                // dawn: the road opens
                casualty.Protocol.Perform(MedAction.MedevacRequest);
            }
            return true;
        }

        // ------------------------------------------------------------------ end
        void OnPlayerHit(string reason)
        {
            if (Timeline == null || State == ScenarioState.Debrief) return;
            _hit = true;
            _hitText = ThreatModel.Explain(reason, GameSettings.KZ);
            if (casualty != null && casualty.Protocol != null) casualty.Protocol.ScenarioMistake("player_exposed");
        }

        /// <summary>Force the end (tests / desktop key).</summary>
        public void EndNow(string reason) => Timeline?.End(reason);

        void Finish()
        {
            FieldMedic.Battle.BattleDirector.PlayerArmed = false;
            var proto = casualty.Protocol;
            var p = casualty.Patient;
            if (_slipDone && _preTq != null && !_preTq.Retightened && !_preTq.State.FullOcclusion) proto.ScenarioMistake("tq_not_rechecked");
            if (Timeline.EndReason == "evacuated")
            {
                if (p.coreTempC < 35.5f && !p.insulated) proto.ScenarioMistake("hypothermia");
                if (p.vomitInMouth) proto.ScenarioMistake("airway_not_cleared");
                if (_preTq != null && _preTq.Slipped == false && Run.def.timeSkipMinutes.Length > 0 && p.conversionFailed && !_preTq.State.Locked)
                    proto.ScenarioMistake("conv_not_retightened");
            }
            casualty.autoTick = Timeline.EndReason != "evacuated"; // he is handed over; keep a dead/abandoned one as is
            TrackPlaces();
            Report = DebriefBuilder.Build(Run.def, proto, Timeline.EndReason, ScenarioClock.Format, IncidentPatientTime, _hitText, GameSettings.KZ);
            foreach (var ph in _phases) Report.phases.Add(new DebriefPhase(ph.id, ph.start, ph.end < 0f ? p.secondsSinceStart : ph.end));
            foreach (var m in Report.mistakes)
                if (MistakePlaces.TryGetValue(m.code, out var at)) { m.hasPos = true; m.x = at.x; m.z = at.z; }
            if (_fallKnown) { Report.hasCasualtyPos = true; Report.casualtyX = _fallPoint.x; Report.casualtyZ = _fallPoint.z; }
            int best = GameSettings.BestScore(Run.def.id);
            if (Autoplay)
            {
                // w10g: the bot's run is shown as such and never becomes the player's record
                Report.title = (Report.title ?? Run.def.id) + GameSettings.T("  ·  автопрохождение", "  ·  автоөту");
                Report.bestScore = Mathf.Max(0, best);
            }
            else if ((DebriefBuilder.UpdateBest(Report, Mathf.Max(0, best)) || best < 0) && !testMode) GameSettings.SetBestScore(Run.def.id, Report.bestScore);
            if (ThreatSystem.Instance != null) { ThreatSystem.Instance.underFire = false; ThreatSystem.Instance.forceDroneOverhead = false; }
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.3f;
            Log($"debrief {Run.def.id}: {Timeline.EndReason}, score {Report.score}, survived {Report.survived}, mistakes {string.Join(",", Report.mistakes.Select(m => m.code))}");
            if (FieldMedic.Treatment.KitTray.Player != null && FieldMedic.Treatment.KitTray.Player.IsOpen) FieldMedic.Treatment.KitTray.Player.Close();
            SetWanted(null);
            _coach = null;
            if (board != null) { board.gameObject.SetActive(true); board.ShowDebrief(Report, head); }
            FieldMedic.UI.StepGuide.Set("Итог на доске: наведи луч, КУРОК — кнопка", "Қорытынды тақтада: сәулені бағытта, ШҮРІППЕ — түйме", FieldMedic.UI.ControlIcon.Trigger);
            Finished?.Invoke(Report);
        }

        void RadioSay(string id)
        {
            if (RadioSet.Instance != null && !testMode) RadioSet.Instance.Say(id);
            else Log("radio " + id);
        }

        void Log(string s)
        {
            Events.Add(s);
            Debug.Log("[FMVR] scenario: " + s);
        }
    }
}
