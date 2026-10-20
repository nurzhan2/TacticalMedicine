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

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Training mode (GDD_V2 §3): the battle rolls on far away, nobody shoots at the player, nothing fails. One topic
    /// per run — full TCCC (Erlan down in the open: CAT high-and-tight, drag into the ruins, MARCH in cover, MEDEVAC,
    /// load into the helicopter), tourniquet, packing, drag, chest seal, airway. The WHITE hint card
    /// (<see cref="HintPanel"/>) appears when the player's head comes within 2 m of Erlan, shows "Step X of N", the
    /// title, 2–3 actions with checkboxes, ticks them as they happen, turns green when the step is done (sound, then
    /// auto-advance) and yellow with the reason on a wrong action. A ghost glove plays the motion from the IFAK item to
    /// the body target; the item and the target glow; the instructor voice reads each step (RU/KZ).
    /// Steps complete only by doing them with the hands.
    /// </summary>
    public sealed class TrainingDirector : MonoBehaviour
    {
        public static TrainingDirector Instance { get; private set; }
        public ScenarioDirector director;
        public LessonProgress Progress { get; private set; }
        public TrainingLesson Lesson => Progress?.lesson;
        public HintPanel Panel { get; private set; }
        public float slowPhysiology = 0.25f;
        public float showDistance = 2f, hideDistance = 2.4f;
        public float doneHold = 1.1f;
        public bool muted;
        /// <summary>Training never fails: blood loss is held below this fraction.</summary>
        public float maxLossFraction = 0.3f;

        public int WrongCount { get; private set; }
        public string CurrentWrong { get; private set; }
        public readonly List<string> WrongLog = new();
        public bool LessonComplete { get; private set; }
        public float StartedAt { get; private set; }
        public float CompletedAt { get; private set; } = -1f;
        /// <summary>Debrief of a lesson with `debrief` (the DEMO run), built when it completes.</summary>
        public DebriefReport Report { get; private set; }

        CasualtyController C => director.casualty;
        Wound _wound, _wound2, _exit, _wound3;
        bool[] _sticky;
        float _doneAt = -1f, _t, _penaltyWrongUntil, _dragLetGoAt = -1f;
        string _penaltyWrong;
        readonly Dictionary<string, int> _seenPenalties = new();
        AudioSource _voice;
        GuideVisuals _guide;
        IfakPouch _pouch;

        public static TrainingDirector Ensure(ScenarioDirector d)
        {
            var t = d.GetComponent<TrainingDirector>();
            if (t == null) t = d.gameObject.AddComponent<TrainingDirector>();
            t.director = d;
            return t;
        }

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Panel != null) Destroy(Panel.gameObject);
            _guide?.Destroy();
        }

        // ------------------------------------------------------------------ begin
        public void Begin(string lessonId)
        {
            HintTable.EnsureLoaded();
            FieldMedic.Battle.BattleDirector.Mode = FieldMedic.Battle.BattleMode.Training; // battle far away, nobody targets the player
            FieldMedic.Battle.BattleDirector.PlayerArmed = false;
            var l = TrainingCatalog.Get(lessonId) ?? TrainingCatalog.Get("full") ?? TrainingCatalog.All[0];
            if (l.selfAid)
            {
                // w9b "Жгут на себя": the trainee is the casualty — run by the self-aid director (no Erlan)
                SelfAid.SelfAidDirector.Ensure(director).BeginLesson(l);
                enabled = false;
                return;
            }
            Progress = new LessonProgress(l);
            GameSettings.HoldTimeScale = 6f;
            muted |= director.testMode;
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.25f;
            if (ThreatSystem.Instance != null) { ThreatSystem.Instance.underFire = false; ThreatSystem.Instance.enableHits = false; }
            if (Headlamp.Instance != null) Headlamp.Instance.SetMode(LightMode.White); // training: no threat, white light allowed

            // where: in the open (CUF topics) or inside the ruins
            var spotId = l.inTheOpen ? CasualtySpot.FireBay : CasualtySpot.CommTrench;
            var spot = director.spots.FirstOrDefault(s => s.spot == spotId) ?? director.spots.FirstOrDefault(s => s.spot == CasualtySpot.FireBay);
            if (spot != null && spot.casualty != null) C.transform.SetPositionAndRotation(spot.casualty.position, spot.casualty.rotation);
            if (director.playerRig != null)
            {
                if (l.inTheOpen && spot != null && spot.player != null) director.PlacePlayerAt(spot.player);
                else
                {
                    // 2.6 m from him, in free space: the card fades in as the trainee walks up (2 m rule)
                    Vector3 hips = C.transform.position;
                    Vector3 dir = WorldUI.FreeDirection(hips + Vector3.up * 0.9f, spot != null && spot.player != null ? spot.player.position - hips : Vector3.right, 3f, out float clear);
                    Vector3 p = hips + dir * Mathf.Clamp(clear - 0.4f, 1.2f, 2.6f);
                    director.PlacePlayerAt(p, Quaternion.LookRotation(-dir).eulerAngles.y);
                }
            }

            C.EnsurePatient();
            var site = C.AddWound(l.woundType, l.region, ScenarioDirector.Visual(l.visual), "lesson");
            _wound = site.Wound;
            if (l.secondWound)
            {
                var s2 = C.AddWound(l.secondType, l.secondRegion, WoundVisual.FragmentHole, "lesson2");
                _wound2 = s2.Wound;
                _wound2.hiddenUnderClothing = l.secondHidden; // found by the blood sweep (or torn open by the blast)
                if (!l.secondHidden) { _wound2.exposed = true; _wound2.discovered = true; }
            }
            if (l.thirdWound)
            {
                var s3 = C.AddWound(l.thirdType, l.thirdRegion, ScenarioDirector.Visual(l.thirdVisual), "lesson3");
                _wound3 = s3.Wound;
                _wound3.discovered = true;
            }
            if (l.id == "chest")
            {
                var ex = C.AddWound(WoundType.PenetratingChest, l.region, WoundVisual.ExitWound, "lesson_exit", true);
                _exit = ex.Wound;
                _exit.posterior = true;
            }
            if (l.unconscious) C.Patient.tbiUnconscious = true;
            if (l.id != "drag") _wound.discovered = true;
            C.StartDown();
            C.Protocol.phase = l.inTheOpen ? Phase.CareUnderFire : Phase.TacticalFieldCare;
            C.simulationSpeed = slowPhysiology;
            director.IncidentPatientTime = C.Patient.secondsSinceStart;
            if (director.board != null) director.board.gameObject.SetActive(false); // the board returns with the summary
            if (TacevacController.Instance != null) TacevacController.Instance.ResetForRun();
            _pouch = FindObjectsByType<IfakPouch>(FindObjectsSortMode.None).FirstOrDefault(p => C.Interaction == null || p != C.Interaction.Ifak);

            Panel = HintPanel.Create();
            _guide = new GuideVisuals(GameSettings.LeftHanded);
            ShowStep();
            StartedAt = Time.time;
            Say("t_welcome");
            Invoke(nameof(SayCurrent), 3.2f);
            Debug.Log("[FMVR] training: lesson " + l.id + " (" + l.steps.Count + " steps)");
        }

        void SayCurrent() { if (Progress != null && Progress.Step != null) Say(Progress.Step.voice); }

        void ShowStep()
        {
            var s = Progress.Step;
            _sticky = s != null ? new bool[s.actions.Count] : new bool[0];
            _doneAt = -1f;
            _dragLetGoAt = -1f;
            if (s != null) Panel.SetStep(s, Progress.StepNumber, Progress.StepCount, GameSettings.KZ);
        }

        void Say(string id)
        {
            if (muted || string.IsNullOrEmpty(id)) return;
            if (_voice == null)
            {
                _voice = gameObject.AddComponent<AudioSource>();
                _voice.spatialBlend = 0f;
                _voice.volume = 0.9f;
            }
            var clip = Resources.Load<AudioClip>("Radio/" + id + (GameSettings.KZ ? "_kz" : "_ru"));
            if (clip == null) return;
            _voice.Stop();
            _voice.clip = clip;
            _voice.Play();
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (Progress == null || C == null || C.Patient == null) return;
            float dt = Time.deltaTime;
            _t += dt;
            var p = C.Patient;
            // no fail in training: the bleeding is real, but he never bleeds out
            if (p.LossFraction > maxLossFraction) p.bloodLostMl = p.BloodVolumeMl * maxLossFraction;
            C.simulationSpeed = Progress.Step != null && Progress.Step.id == "tension" ? 8f : slowPhysiology;
            if (GameSettings.KZ != _kz) { _kz = GameSettings.KZ; if (Progress.Step != null) Panel.SetStep(Progress.Step, Progress.StepNumber, Progress.StepCount, _kz); }

            if (!LessonComplete) StepLogic();
            Proximity(dt);
            if (!LessonComplete) Guides();
            else _guide?.Hide();
        }

        bool _kz;

        void StepLogic()
        {
            var step = Progress.Step;
            if (step == null) { Complete(); return; }
            // TACEVAC of the full run: the radio call brings the helicopter
            if (step.id == "loaded" && TacevacController.Instance != null && !TacevacController.Instance.Called) TacevacController.Instance.Call(director.testMode);

            _sticky = Progress.ActionStates(Satisfied, _sticky);
            Panel.SetChecks(_sticky);
            if (_doneAt < 0f && Satisfied(step.id))
            {
                _doneAt = Time.time;
                Panel.SetDone();
                Debug.Log("[FMVR] training: step done " + step.id);
            }
            if (_doneAt >= 0f && Time.time - _doneAt >= doneHold)
            {
                Progress.TryAdvanceOne(_ => true, C.Patient.secondsSinceStart);
                if (Progress.Complete) { Complete(); return; }
                ShowStep();
                Say(Progress.Step.voice);
                return;
            }
            if (_doneAt < 0f)
            {
                string why = Wrong(step);
                if (why != CurrentWrong)
                {
                    if (why != null) { WrongCount++; WrongLog.Add(step.id + ":" + why); Debug.Log("[FMVR] training: wrong at " + step.id + " — " + why); }
                    CurrentWrong = why;
                }
                Panel.SetWrong(why);
            }
        }

        void Complete()
        {
            if (LessonComplete) return;
            LessonComplete = true;
            CompletedAt = Time.time;
            Say("t_done");
            C.Protocol.Note("lesson_done", GameSettings.T("Урок выполнен.", "Сабақ орындалды."), Severity.Ok);
            bool kz = GameSettings.KZ;
            float secs = CompletedAt - StartedAt;
            var final = new LessonStep("done", GameSettings.T("Урок выполнен", "Сабақ орындалды"), "Сабақ орындалды") { icon = "check", phase = Lesson.inTheOpen ? "TACEVAC" : "TFC" }
                .Act(GameSettings.T("Время: ", "Уақыт: ") + Mathf.FloorToInt(secs / 60f) + ":" + Mathf.FloorToInt(secs % 60f).ToString("00"),
                     "Уақыт: " + Mathf.FloorToInt(secs / 60f) + ":" + Mathf.FloorToInt(secs % 60f).ToString("00"))
                .Act(GameSettings.T("Ошибок: ", "Қателер: ") + WrongCount, "Қателер: " + WrongCount);
            Panel.SetStep(final, Progress.StepCount, Progress.StepCount, kz);
            Panel.SetDone();
            Debug.Log($"[FMVR] training: lesson {Lesson.id} complete in {secs:0} s, wrong actions {WrongCount}");
            if (Lesson.debrief)
            {
                Report = DebriefBuilder.Build(null, C.Protocol, "evacuated", ScenarioClock.Format, director.IncidentPatientTime, null, kz);
                Report.title = kz ? "ДЕМО" : "ДЕМО"; // short: the board header also carries the flag + patch
                Debug.Log($"[FMVR] training: demo debrief score {Report.score}, survived {Report.survived}, mistakes {string.Join(",", Report.mistakes.Select(m => m.code))}");
            }
            if (director.board != null && !director.testMode)
            {
                director.board.gameObject.SetActive(true);
                if (Report != null) director.board.ShowDebrief(Report, director.head);
                else director.board.ShowTrainingDone(Lesson, secs, WrongCount, director.head);
            }
        }

        // ------------------------------------------------------------------ proximity + card placement
        Vector3 Chest => C.rig != null && C.rig.ChestBone != null ? C.rig.ChestBone.position : C.transform.position + Vector3.up * 0.2f;

        void Proximity(float dt)
        {
            var head = director.head;
            if (head == null || Panel == null) return;
            // horizontal distance: a trainee standing 1.5 m from him (head 1.4 m above his chest) is "within 2 m"
            float d = Vector3.Distance(Flat(head.position), Flat(Chest));
            if (!Panel.Shown && d < showDistance) { Panel.SetVisible(true); }
            else if (Panel.Shown && d > hideDistance) Panel.SetVisible(false);
            Panel.Follow(Chest, head.position, WoundPoint(TargetWound()), dt);
        }

        public bool PanelVisible => Panel != null && Panel.Visibility > 0.95f;

        Wound TargetWound() => Progress.Step != null && Progress.Step.target == "Wound2" && _wound2 != null ? _wound2
            : Progress.Step != null && Progress.Step.target == "Wound3" && _wound3 != null ? _wound3 : _wound;

        Vector3 WoundPoint(Wound w)
        {
            var site = C.WoundSites.FirstOrDefault(s => s != null && s.Wound == w);
            return site != null ? site.transform.position : C.transform.position;
        }

        // ------------------------------------------------------------------ checks
        TourniquetCAT Cat => FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None).OrderByDescending(c => c.State.stage).FirstOrDefault();

        public bool Satisfied(string id)
        {
            var p = C.Patient;
            var cat = Cat;
            var tac = TacevacController.Instance;
            switch (id)
            {
                case "reach":
                    return director.head != null && Mathf.Min(Vector3.Distance(Flat(director.head.position), Flat(Chest)),
                        Vector3.Distance(Flat(director.head.position), Flat(C.transform.position))) < 1.8f;
                case "tq_grab": return cat != null && (cat.IsHeld || cat.State.OnLimb);
                case "tq_on_limb": return cat != null && cat.State.OnLimb;
                case "tq_high": return cat != null && cat.State.OnLimb && !cat.State.OnJoint && cat.State.limbId == _wound.LimbId && cat.State.limbPosition < _wound.LimbPos - 0.05f;
                case "tq_strap": return cat != null && cat.State.stage >= TourniquetStage.StrapPulled;
                case "tq_twist": return _wound.IsControlled;
                case "tq_lock": return cat != null && cat.State.Secured && _wound.IsControlled;
                case "tq_time": return cat != null && cat.State.TimeMarked;
                case "marker_grab": return FindObjectsByType<MarkerItem>(FindObjectsSortMode.None).Any(m => m.IsHeld) || (cat != null && cat.State.TimeMarked);
                case "pulse_foot": return C.Protocol.log.Any(e => e.code == "pulse" && (e.target == "FootR" || e.target == "FootL"));
                case "gloves": return C.Protocol.glovesOn;
                case "exposed": return _wound.exposed;
                case "exposed2": return _wound2 != null && _wound2.exposed;
                case "packed": return _wound.packed;
                case "ifak_open": return _pouch == null || _pouch.IsOpen;
                case "gauze_grab": return FindObjectsByType<GauzeItem>(FindObjectsSortMode.None).Any(g => g.IsHeld) || (_wound2 != null && _wound2.packed);
                case "packed2": return _wound2 != null && _wound2.packed;
                case "seal_open": return FindObjectsByType<ChestSealItem>(FindObjectsSortMode.None).Any(c => c.IsHeld || c.Attached) || (_wound3 != null && _wound3.control == BleedControl.OcclusiveSeal);
                case "seal3": return _wound3 != null && _wound3.control == BleedControl.OcclusiveSeal;
                case "held": return _wound.control is BleedControl.PackedAndHeld or BleedControl.PressureDressing;
                case "bandage": return _wound.control == BleedControl.PressureDressing;
                case "bandage2": return _wound2 != null && _wound2.control == BleedControl.PressureDressing;
                case "bandage2_on":
                    return _wound2 != null && (_wound2.control == BleedControl.PressureDressing ||
                        FindObjectsByType<BandageItem>(FindObjectsSortMode.None).Any(b => b.ApplyingTo != null && b.ApplyingTo.Wound == _wound2));
                case "seal_front": return _wound.control == BleedControl.OcclusiveSeal;
                case "rolled": return C.rolledOnSide || C.Protocol.log.Any(e => e.code.StartsWith("log_roll"));
                case "seal_back": return _exit != null && _exit.control == BleedControl.OcclusiveSeal;
                case "tension": return p.pneumo is PneumoState.Tension or PneumoState.Decompressed;
                case "needle": return p.pneumo == PneumoState.Decompressed;
                case "drag_grab": return C.Interaction != null && C.Interaction.Drag != null && (C.Interaction.Drag.AnyHeld || C.Interaction.Drag.DraggedDistance > 0.3f);
                case "drag_cover": return C.Protocol.phase != Phase.CareUnderFire;
                case "sweep": return p.wounds.All(w => w.discovered);
                case "npa_grab": return FindObjectsByType<NpaItem>(FindObjectsSortMode.None).Any(n => n.IsHeld || n.Attached);
                case "npa_in": return p.airwayAdjunct;
                case "recovery": return C.rolledOnSide;
                case "blanket": return p.insulated;
                case "card": return C.Protocol.log.Any(e => e.code == "card");
                case "medevac": return C.Protocol.medevacRequested;
                case "heli_landed": return tac != null && tac.Landed;
                case "heli_aboard": return tac != null && tac.CasualtyAboard;
                case "loaded": return tac != null ? tac.Loaded : C.Protocol.medevacRequested;
            }
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Why the current action is wrong (null = fine). Live checks first, then protocol penalties.</summary>
        public string Wrong(LessonStep step)
        {
            bool kz = GameSettings.KZ;
            var w = step.watch;
            foreach (var cat in FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None))
            {
                var st = cat.State;
                if (!st.OnLimb) continue;
                // still sliding the band into place: judge the placement once it is let go or the strap is pulled
                if (cat.IsHeld && st.stage < TourniquetStage.StrapPulled) continue;
                if (w.Contains("tq_on_joint") && st.OnJoint) return TrainingCatalog.Reason("tq_on_joint", kz);
                if (w.Contains("tq_wrong_limb") && _wound.LimbId != null && st.limbId != _wound.LimbId) return TrainingCatalog.Reason("tq_wrong_limb", kz);
                if (w.Contains("tq_below_wound") && st.limbId == _wound.LimbId && st.limbPosition > _wound.LimbPos + 0.02f) return TrainingCatalog.Reason("tq_below_wound", kz);
            }
            if (w.Contains("drag_released") && C.Interaction != null && C.Interaction.Drag != null)
            {
                var drag = C.Interaction.Drag;
                bool letGo = drag.DraggedDistance > 0.6f && !drag.AnyHeld && C.Protocol.phase == Phase.CareUnderFire;
                if (!letGo) _dragLetGoAt = -1f;
                else if (_dragLetGoAt < 0f) _dragLetGoAt = Time.time;
                if (letGo && Time.time - _dragLetGoAt > 0.8f) return TrainingCatalog.Reason("drag_released", kz);
            }
            // a new protocol penalty (any — they are all real mistakes) shows for 5 s
            foreach (var kv in C.Protocol.penalties)
            {
                _seenPenalties.TryGetValue(kv.Key, out int seen);
                if (kv.Value > seen)
                {
                    _seenPenalties[kv.Key] = kv.Value;
                    if (!step.watch.Contains(kv.Key) && !ActionMistakes.Contains(kv.Key)) continue; // run-level (blood loss, timing…), not this action
                    _penaltyWrong = TrainingCatalog.Reason(kv.Key, kz) ?? (CareProtocol.Penalty.TryGetValue(kv.Key, out var pen) ? pen.text : kv.Key);
                    _penaltyWrongUntil = Time.time + 5f;
                }
            }
            if (_penaltyWrong != null && Time.time < _penaltyWrongUntil) return _penaltyWrong;
            _penaltyWrong = null;
            return null;
        }

        /// <summary>Protocol penalties that are a wrong ACTION (shown on the card in any step); the rest are run-level.</summary>
        static readonly HashSet<string> ActionMistakes = new HashSet<string>
        {
            "fire_non_tq", "tq_on_joint", "tq_below_wound", "tq_junctional", "march_order", "wrong_tool", "needle_no_signs", "needle_wrong_site",
            "needle_through_seal", "needle_angle", "needle_shallow", "npa_technique", "seal_no_wipe", "elevation", "not_exposed", "pressure_released",
            "blanket_early", "tq_unneeded",
        };

        // ------------------------------------------------------------------ guides: ghost glove, item + target glow
        void Guides()
        {
            var step = Progress.Step;
            if (step == null || !GameSettings.Hints || _doneAt >= 0f) { _guide.Hide(); return; }
            // the first unticked action with its own check decides the motion (e.g. strap before windlass)
            string sub = null;
            for (int i = 0; i < step.actions.Count; i++)
                if (!(_sticky != null && i < _sticky.Length && _sticky[i]) && !string.IsNullOrEmpty(step.actions[i].check)) { sub = step.actions[i].check; break; }
            bool has = Guide(sub ?? step.id, out var from, out var to) || Guide(step.id, out from, out to);
            var item = FindItem(step.item);
            Vector3? target = Target(step.target);
            _guide.Show(has, from, to, item, target, Panel.Visibility > 0.5f || step.phase == "CUF", _t);
        }

        MedItem FindItem(string key)
        {
            System.Type t = key switch
            {
                "CAT" => typeof(TourniquetCAT), "Gauze" => typeof(GauzeItem), "Bandage" => typeof(BandageItem), "ChestSeal" => typeof(ChestSealItem),
                "Needle" => typeof(NeedleItem), "Npa" => typeof(NpaItem), "Gloves" => typeof(GlovesItem), "Shears" => typeof(ShearsItem),
                "Marker" => typeof(MarkerItem), "Blanket" => typeof(BlanketItem), "Card" => typeof(CardItem), _ => null,
            };
            if (t == null) return null;
            var all = FindObjectsByType(t, FindObjectsSortMode.None).Cast<MedItem>().Where(m => m != null && !m.Consumed && (!m.Attached || m.IsHeld));
            // prefer the player's own IFAK
            return all.OrderBy(m => _pouch != null && m.transform.IsChildOf(_pouch.transform) ? 0 : 1).ThenBy(m => Vector3.Distance(m.transform.position, Chest)).FirstOrDefault();
        }

        Vector3? Target(string key)
        {
            var rig = C.rig;
            if (rig == null || string.IsNullOrEmpty(key)) return null;
            var cat = Cat;
            switch (key)
            {
                case "Erlan": return Chest;
                case "ThighHigh": return LimbMath.PointAt(rig, LimbOf(_wound), 0.1f);
                case "Wound": return WoundPoint(_wound);
                case "Wound2": return _wound2 != null && _wound2.discovered ? WoundPoint(_wound2) : (Vector3?)null;
                case "Wound3": return _wound3 != null ? WoundPoint(_wound3) : (Vector3?)null;
                case "Ifak": return _pouch != null ? _pouch.transform.position : (Vector3?)null;
                case "Foot": return HandTreatment.PulsePoint(rig, _wound.region.ToString().EndsWith("L") ? PulseSite.PedalL : PulseSite.PedalR);
                case "TqLabel": return cat != null && cat.State.OnLimb ? cat.TimeLabelPoint : (Vector3?)null;
                case "DragHandle": return C.Interaction != null && C.Interaction.Drag != null ? C.Interaction.Drag.transform.position : (Vector3?)null;
                case "Cover": var cz = FindAnyObjectByType<CoverZone>(); return cz != null ? cz.transform.position : (Vector3?)null;
                case "Chest": return rig.ChestBone.position + rig.BodyRight * 0.15f;
                case "ChestBack": return _exit != null ? WoundPoint(_exit) : (Vector3?)null;
                case "Nose": return rig.Bone(HumanBodyBones.Head).position + rig.BodyUp * 0.08f;
                case "Body": return Chest;
                case "Stretcher":
                case "HeliDoor": return TacevacController.Instance != null && TacevacController.Instance.Landed ? TacevacController.Instance.DoorPoint + Vector3.up * 1.1f : (Vector3?)null;
            }
            return null;
        }

        static Limb LimbOf(Wound w) => w.LimbId switch { "legL" => Limb.LeftLeg, "armL" => Limb.LeftArm, "armR" => Limb.RightArm, _ => Limb.RightLeg };

        /// <summary>Where the ghost glove should move for a check (item → target).</summary>
        public bool Guide(string id, out Vector3 from, out Vector3 to)
        {
            from = to = Vector3.zero;
            var rig = C.rig;
            if (rig == null) return false;
            var cat = Cat;
            Vector3 wound = WoundPoint(_wound);
            Vector3 hand = HandGrabber.All.Count > 0 && HandGrabber.All[0] != null ? HandGrabber.All[0].PalmPosition : wound + Vector3.up * 0.5f;
            Vector3 Item<T>() where T : MedItem
            {
                var it = FindObjectsByType<T>(FindObjectsSortMode.None).Where(x => !x.Attached && !x.Consumed)
                    .OrderBy(x => _pouch != null && x.transform.IsChildOf(_pouch.transform) ? 0 : 1).FirstOrDefault();
                return it != null ? it.transform.position : hand;
            }
            Vector3 foot = HandTreatment.PulsePoint(rig, _wound.region.ToString().EndsWith("L") ? PulseSite.PedalL : PulseSite.PedalR);
            Limb limb = LimbOf(_wound);
            switch (id)
            {
                case "tq_grab": from = Item<TourniquetCAT>(); to = from + Vector3.up * 0.15f; return true;
                case "tq_on_limb": from = cat != null ? cat.transform.position : hand; to = foot; return true;
                case "tq_high": from = cat != null && cat.State.OnLimb ? LimbMath.PointAt(rig, limb, Mathf.Max(0.15f, cat.State.limbPosition)) : foot; to = LimbMath.PointAt(rig, limb, 0.1f); return true;
                case "tq_strap": if (cat == null || cat.StrapHandle == null) return false; from = cat.StrapHandle.transform.position; to = from + (from - cat.transform.position).normalized * 0.18f; return true;
                case "tq_twist":
                case "tq_lock": if (cat == null || cat.RodHandle == null) { from = Item<TourniquetCAT>(); to = LimbMath.PointAt(rig, limb, 0.1f); return true; } from = to = cat.RodHandle.transform.position; return true;
                case "marker_grab": from = hand; to = Item<MarkerItem>(); return true;
                case "tq_time": if (cat == null) return false; from = Item<MarkerItem>(); to = cat.TimeLabelPoint; return true;
                case "pulse_foot": from = hand; to = foot; return true;
                case "gloves": from = hand; to = Item<GlovesItem>(); return true;
                case "exposed": from = Item<ShearsItem>(); to = wound; return true;
                case "packed": from = Item<GauzeItem>(); to = wound; return true;
                case "held": from = hand; to = wound; return true;
                case "bandage": from = Item<BandageItem>(); to = wound; return true;
                case "bandage2_on":
                case "bandage2": if (_wound2 == null) return false; from = Item<BandageItem>(); to = WoundPoint(_wound2); return true;
                case "seal_front": from = Item<ChestSealItem>(); to = wound; return true;
                case "ifak_open": if (_pouch == null || _pouch.Flap == null || _pouch.Flap.Tab == null || _pouch.IsOpen) return false;
                    from = _pouch.Flap.Tab.transform.position; to = from + (_pouch.transform.up - _pouch.transform.forward).normalized * 0.12f; return true;
                case "gauze_grab": from = hand; to = Item<GauzeItem>(); return true;
                case "packed2": if (_wound2 == null) return false; from = Item<GauzeItem>(); to = WoundPoint(_wound2); return true;
                case "seal_open": from = hand; to = Item<ChestSealItem>(); return true;
                case "seal3": if (_wound3 == null) return false; from = Item<ChestSealItem>(); to = WoundPoint(_wound3); return true;
                case "seal_back": var ex = C.WoundSites.FirstOrDefault(s => s != null && s.Wound == _exit); from = Item<ChestSealItem>(); to = ex != null ? ex.transform.position : wound; return true;
                case "needle": from = Item<NeedleItem>(); to = rig.ChestBone.position + rig.BodyRight * 0.15f; return true;
                case "drag_grab": from = hand; to = C.Interaction.Drag.transform.position; return true;
                case "drag_cover":
                    var cz = FindAnyObjectByType<CoverZone>();
                    from = C.Interaction.Drag.transform.position; to = cz != null ? Vector3.MoveTowards(from, cz.transform.position, 1.2f) : from; return true;
                case "rolled":
                case "recovery": from = rig.Bone(HumanBodyBones.RightUpperArm).position; to = from + Vector3.up * 0.3f; return true;
                case "sweep": from = rig.Bone(HumanBodyBones.Head).position; to = rig.Bone(HumanBodyBones.LeftHand).position; return true;
                case "npa_grab": from = hand; to = Item<NpaItem>(); return true;
                case "npa_in": from = Item<NpaItem>(); to = rig.Bone(HumanBodyBones.Head).position; return true;
                case "blanket": from = Item<BlanketItem>(); to = Chest; return true;
                case "card": from = Item<CardItem>(); to = Chest; return true;
                case "loaded":
                {
                    var tac = TacevacController.Instance;
                    if (tac == null || !tac.Landed) return false;
                    // drag him by the handle toward the open side door; once he is aboard: you, into the door
                    from = !tac.CasualtyAboard && C.Interaction != null && C.Interaction.Drag != null ? C.Interaction.Drag.transform.position : hand;
                    to = tac.DoorPoint + Vector3.up * 0.5f;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ visuals
        /// <summary>Ghost glove (translucent, the real glove mesh) + pulsing glow on the item and on the body target.</summary>
        sealed class GuideVisuals
        {
            readonly Transform _ghostRoot;
            readonly GloveHand _ghost;
            readonly Material _ghostMat;
            readonly Transform _itemGlow, _targetGlow;
            readonly Material _itemGlowMat, _targetGlowMat;
            GrabHighlight _lit;

            public GuideVisuals(bool left)
            {
                _ghostRoot = new GameObject("GhostHand").transform;
                var g = new GameObject("Glove");
                g.transform.SetParent(_ghostRoot, false);
                _ghost = g.AddComponent<GloveHand>();
                _ghost.side = left ? -1f : 1f;
                _ghost.driveFromInput = false;
                _ghostMat = UiMesh.Mat(new Color(0.75f, 0.9f, 1f, 0.42f), 3050);
                _ghost.material = _ghostMat;
                _ghost.Build();
                if (_ghost.Renderer != null) { _ghost.Renderer.sharedMaterial = _ghostMat; _ghost.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                var glow = UiMesh.Icon("glow");
                _itemGlowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.8f), 3060, glow);
                _targetGlowMat = UiMesh.Mat(new Color(0.45f, 1f, 0.6f, 0.85f), 3060, glow);
                _itemGlow = UiMesh.Add(null, "ItemGlow", UiMesh.Quad, _itemGlowMat, Vector3.zero).transform;
                _targetGlow = UiMesh.Add(null, "TargetGlow", UiMesh.Quad, _targetGlowMat, Vector3.zero).transform;
                Hide();
            }

            public void Show(bool ghost, Vector3 from, Vector3 to, MedItem item, Vector3? target, bool near, float t)
            {
                var cam = Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : to + Vector3.back;
                // ghost glove: reach, close the grip at the item, carry it to the target, open, fade
                _ghostRoot.gameObject.SetActive(ghost && near);
                if (ghost && near)
                {
                    float k = Mathf.Repeat(t / 1.8f, 1f);
                    float e = k < 0.15f ? 0f : k < 0.8f ? Mathf.SmoothStep(0f, 1f, (k - 0.15f) / 0.65f) : 1f;
                    Vector3 pos = Vector3.Lerp(from, to, e) + Vector3.up * (0.07f * Mathf.Sin(e * Mathf.PI) + 0.03f);
                    _ghostRoot.position = pos;
                    Vector3 dir = to - from; dir.y = 0f;
                    _ghostRoot.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward) * Quaternion.Euler(25f, 0f, 0f);
                    float grip = k < 0.15f ? k / 0.15f : k < 0.85f ? 1f : 1f - (k - 0.85f) / 0.15f;
                    _ghost.gripCurl = _ghost.indexCurl = _ghost.thumbCurl = Mathf.Clamp01(grip) * 0.85f;
                    float a = k < 0.08f ? k / 0.08f : k > 0.9f ? (1f - k) / 0.1f : 1f;
                    _ghostMat.SetFloat("_Alpha", a);
                }
                float pulse = 0.65f + 0.35f * Mathf.Sin(t * 5f);
                // item glow: halo + warm tint on the item itself
                if (item != null)
                {
                    if (_lit != null && _lit.gameObject != item.gameObject) { _lit.SetHighlighted(false); _lit = null; }
                    if (_lit == null) { _lit = item.GetComponent<GrabHighlight>(); if (_lit == null) _lit = item.gameObject.AddComponent<GrabHighlight>(); }
                    _lit.amount = 0.25f + 0.25f * pulse;
                    _lit.SetHighlighted(true);
                    Place(_itemGlow, item.transform.position, eye, 0.16f * (0.9f + 0.2f * pulse));
                    _itemGlowMat.SetFloat("_Alpha", pulse);
                }
                else
                {
                    if (_lit != null) { _lit.SetHighlighted(false); _lit = null; }
                    _itemGlow.gameObject.SetActive(false);
                }
                if (target.HasValue)
                {
                    Place(_targetGlow, target.Value, eye, 0.13f * (0.85f + 0.3f * pulse));
                    _targetGlowMat.SetFloat("_Alpha", 0.5f + 0.5f * pulse);
                }
                else _targetGlow.gameObject.SetActive(false);
            }

            static void Place(Transform t, Vector3 at, Vector3 eye, float size)
            {
                t.gameObject.SetActive(true);
                Vector3 toEye = eye - at;
                // nudge toward the eye so the halo is not buried in the body
                t.position = at + toEye.normalized * 0.04f;
                t.rotation = Quaternion.LookRotation(-toEye.normalized, Vector3.up);
                t.localScale = Vector3.one * size;
            }

            public void Hide()
            {
                if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);
                if (_itemGlow != null) _itemGlow.gameObject.SetActive(false);
                if (_targetGlow != null) _targetGlow.gameObject.SetActive(false);
                if (_lit != null) { _lit.SetHighlighted(false); _lit = null; }
            }

            public void Destroy()
            {
                Hide();
                if (_ghostRoot != null) Object.Destroy(_ghostRoot.gameObject);
                if (_itemGlow != null) Object.Destroy(_itemGlow.gameObject);
                if (_targetGlow != null) Object.Destroy(_targetGlow.gameObject);
            }
        }
    }
}
