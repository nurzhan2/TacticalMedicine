using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty.Blood;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// Scenario "Самопомощь: один под огнём" (w9b, owner: "what to do if you are hit and nobody is around"). The player
    /// carries a box of ammo alone across the open ground; an FPV hits next to him: haptics in both hands, red flash,
    /// muffled hearing and tinnitus, then his own heartbeat in the ears and in the hands and his arm spurting in rhythm.
    /// No HUD clock — the view drains of colour and closes in with the blood loss, the hands shake past 40 %, vision
    /// dims past 70 %. TCCC self-aid: get out of the open (10 s → the drone comes back), self tourniquet with the free
    /// hand from the quick pouch (the wounded hand cannot grip), check, write the time, report on the radio (list menu),
    /// rescue blanket, answer the check-ins until two comrades arrive → debrief with the self-aid checklist.
    /// Rules and timings live in the pure <see cref="SelfAidRun"/>; this class turns them into the world. Also runs
    /// the training lesson "Жгут на себя" (<see cref="BeginLesson"/>: in cover, white hint card, nothing fails).
    /// </summary>
    [DefaultExecutionOrder(65)]
    public sealed class SelfAidDirector : MonoBehaviour
    {
        public static SelfAidDirector Instance { get; private set; }

        public ScenarioDirector director;
        public Transform head;
        public Transform playerRig;
        public MenuBoard board;
        public IfakPouch pouch;
        public HandGrabber leftHand, rightHand;
        [Tooltip("Tests: no audio, no real-time waits, no fades, no tremor.")]
        public bool testMode;
        public bool tremor = true;
        /// Thigh variant (B) is offered by the random config only when the leg rig exists.
        public static bool ThighSupported = true;
        /// Tests / screenshots: force the variant (null = random per seed).
        public SelfAidLimb? forceLimb;
        /// Screenshots / demo: the variant of the next run started through the scenario launch (consumed once).
        public static SelfAidLimb? ForceNext;
        [Tooltip("Thigh variant: crawling speed (m/s) - the leg does not carry him.")]
        public float crawlSpeed = 0.9f;
        /// In the open, ~11.5 m from the doorway of house A (the nearest cover).
        public Vector3 startPosition = new Vector3(7.4f, 0f, 11.4f);
        public float startYaw = 208f;
        public float hitDelay = 8f;
        public float hitWalkDistance = 4f;

        public ScenarioDefinition Def { get; private set; }
        public SelfAidConfig Config { get; private set; }
        public SelfAidRun Run { get; private set; }
        public DebriefReport Report { get; private set; }
        public bool HitHappened { get; private set; }
        public bool Finishing { get; private set; }
        public TrainingLesson Lesson { get; private set; }
        public LessonProgress Progress { get; private set; }
        public HintPanel Panel { get; private set; }
        public bool LessonComplete { get; private set; }
        public SelfLimb Limb { get; private set; }
        public SelfTourniquet Tq { get; private set; }
        public SelfAidVision Vision { get; private set; }
        public SelfAidReportMenu Menu { get; private set; }
        public HandGrabber Wounded { get; private set; }
        public HandGrabber Free { get; private set; }
        public MedItem KitCat { get; private set; }
        public MedItem Marker { get; private set; }
        public MedItem BlanketItem { get; private set; }
        public string CoverName { get; private set; }
        public readonly List<string> Events = new List<string>();
        public readonly List<SelfAidCue> CueLog = new List<SelfAidCue>();
        public readonly Dictionary<string, Vector3> MistakePlaces = new Dictionary<string, Vector3>();
        public event Action<DebriefReport> Finished;

        AudioSource _voice, _heart, _sfx;
        AudioClip _beatClip, _crinkleClip;
        float _beat, _blanketNear, _doneAt = -1f, _lessonStart;
        bool[] _sticky;
        Vector3 _hitPoint;
        Transform _crate;
        readonly List<Transform> _rescuers = new List<Transform>();
        HandTremor _tremorL, _tremorR;
        CoverZone[] _covers;
        float _coversAt = -10f;

        public static SelfAidDirector Ensure(ScenarioDirector d)
        {
            var s = d.GetComponent<SelfAidDirector>();
            if (s == null) s = d.gameObject.AddComponent<SelfAidDirector>();
            s.director = d;
            s.head = d.head != null ? d.head : s.head;
            s.playerRig = d.playerRig;
            s.board = d.board;
            s.testMode |= d.testMode;
            return s;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (RadioSet.Instance != null) { RadioSet.Instance.pttOverride = null; RadioSet.Instance.reportOverride = null; RadioSet.Instance.expectOverride = null; }
            foreach (var r in _rescuers) if (r != null) Destroy(r.gameObject);
            if (Menu != null) Destroy(Menu.gameObject);
            if (Panel != null) Destroy(Panel.gameObject);
            Crawl(false);
        }

        // ------------------------------------------------------------------ begin
        public void Begin(ScenarioDefinition def, int seed)
        {
            Def = def;
            if (ForceNext.HasValue) { forceLimb = ForceNext; ForceNext = null; }
            Config = SelfAidConfig.Random(seed, GameSettings.LeftHanded, ThighSupported);
            if (forceLimb.HasValue) Config.limb = forceLimb.Value;
            Config.clockHour = def != null ? def.clockHour : 14;
            if (def != null && def.tqDeadline > 0f) Config.tqDeadline = def.tqDeadline;
            if (Config.limb == SelfAidLimb.Thigh)
            {
                // crawling 10-15 m takes longer than walking: the drone's patience is scaled to the crawl
                Config.exposureLimit = 22f;
                Config.droneWarning = 14f;
            }
            Setup();
            Log($"begin SA seed {seed}: {Config.LimbId} {(Config.limb == SelfAidLimb.Thigh ? "thigh" : Config.upperArmWound ? "upper arm" : "forearm")}, wounded side {(Config.left ? "L" : "R")}");
            PlacePlayer(startPosition, startYaw);
            HoldCrate();
            if (!testMode && def != null) BriefingCard.Show(def, head);
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.9f;
            StartCoroutine(Incident());
        }

        /// <summary>Training lesson "Жгут на себя": wounded at once, inside the ruins (cover), white hint card, no fail.</summary>
        public void BeginLesson(TrainingLesson lesson, int seed = 1)
        {
            Lesson = lesson;
            Def = ScenarioCatalog.Get("SA");
            Config = new SelfAidConfig { training = true, left = !GameSettings.LeftHanded, seed = seed, upperArmWound = false };
            Setup();
            FieldMedic.Battle.BattleDirector.Mode = FieldMedic.Battle.BattleMode.Training;
            PlacePlayer(BattlefieldLayout.TreatmentSpot + new Vector3(0.6f, 0f, -0.4f), 120f);
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.25f;
            Hit(false);
            Progress = new LessonProgress(lesson);
            Panel = HintPanel.Create("SelfAidPanel");
            Panel.SetVisibleImmediate(false);
            Panel.SetVisible(true);
            ShowStep();
            _lessonStart = Time.time;
            Say(Progress.Step?.voice);
            Log("lesson " + lesson.id);
        }

        void Setup()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            ScenarioClock.StartHour = Config.clockHour;
            ScenarioClock.StartMinute = Config.clockMinute;
            FieldMedic.Battle.BattleDirector.Mode = FieldMedic.Battle.BattleMode.Combat;
            FieldMedic.Battle.BattleDirector.PlayerArmed = false; // the drone is the threat here (our own rules)
            var ts = ThreatSystem.Instance;
            if (ts != null) { ts.underFire = false; ts.enableHits = false; ts.showRunEndOverlay = false; }
            // nobody to treat: the player is the casualty
            if (director != null && director.casualty != null) director.casualty.gameObject.SetActive(false);
            if (board != null && !testMode) board.gameObject.SetActive(false);
            if (pouch == null)
                pouch = FindObjectsByType<IfakPouch>(FindObjectsSortMode.None).OrderByDescending(p => p.bodyLocked).FirstOrDefault();
            if (pouch != null && !pouch.Built) pouch.Build();
            if (leftHand == null) leftHand = HandGrabber.All.FirstOrDefault(h => h != null && h.Handedness == Handedness.Left);
            if (rightHand == null) rightHand = HandGrabber.All.FirstOrDefault(h => h != null && h.Handedness == Handedness.Right);
            Wounded = Config.left ? leftHand : rightHand; // hand on the wounded side (only the arm variant disables it)
            Free = Config.left ? rightHand : leftHand;
            if (pouch != null)
            {
                KitCat = pouch.Find<TourniquetCAT>();
                Marker = pouch.Find<MarkerItem>();
                BlanketItem = pouch.Find<FieldMedic.Treatment.BlanketItem>();
            }
            if (!testMode)
            {
                _voice = gameObject.AddComponent<AudioSource>();
                _voice.spatialBlend = 0f;
                _voice.volume = 0.9f;
                _heart = gameObject.AddComponent<AudioSource>();
                _heart.spatialBlend = 0f;
                _sfx = gameObject.AddComponent<AudioSource>();
                _sfx.spatialBlend = 0f;
                _beatClip = HeartbeatClip();
                _crinkleClip = CrinkleClip();
            }
        }

        void PlacePlayer(Vector3 p, float yaw)
        {
            p.y = 0f;
            if (director != null) director.PlacePlayerAt(p, yaw);
            else if (playerRig != null) playerRig.SetPositionAndRotation(new Vector3(p.x, playerRig.position.y, p.z), Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>The ammo box he carries (free hand), dropped at the hit.</summary>
        void HoldCrate()
        {
            if (Free == null) return;
            var crate = new GameObject("AmmoBox").transform;
            var olive = Kit.Mat("AmmoBoxOlive", new Color(0.24f, 0.26f, 0.15f), 0.25f);
            var dark = Kit.Mat("AmmoBoxDark", new Color(0.08f, 0.08f, 0.07f), 0.4f);
            Kit.Part(crate, PrimitiveType.Cube, "Box", new Vector3(0f, -0.1f, 0f), new Vector3(0.12f, 0.17f, 0.28f), olive);
            Kit.Part(crate, PrimitiveType.Cube, "Lid", new Vector3(0f, -0.012f, 0f), new Vector3(0.125f, 0.012f, 0.285f), dark);
            Kit.Part(crate, PrimitiveType.Cube, "Handle", new Vector3(0f, 0f, 0f), new Vector3(0.02f, 0.02f, 0.1f), dark);
            Kit.Merge(crate);
            crate.SetParent(Free.transform, false);
            crate.localPosition = new Vector3(0f, -0.03f, 0.03f);
            _crate = crate;
        }

        void DropCrate()
        {
            if (_crate == null) return;
            _crate.SetParent(null, true);
            var col = _crate.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -0.09f, 0f);
            col.size = new Vector3(0.125f, 0.19f, 0.285f);
            var rb = _crate.gameObject.AddComponent<Rigidbody>();
            rb.mass = 8f;
            rb.linearVelocity = Free != null && Free.Input != null ? Free.Input.Velocity * 0.5f : Vector3.zero;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 2f;
            _crate = null;
        }

        IEnumerator Incident()
        {
            var amb = CombatAmbience.Instance;
            Vector3 start = HeadFlat;
            DroneAudio fpv = null;
            if (!testMode && amb != null) fpv = amb.StartDrone(DroneKind.Fpv, hitDelay + 4f);
            float t0 = Time.time;
            while (Time.time - t0 < (testMode ? 0.2f : hitDelay) && Vector3.Distance(HeadFlat, start) < hitWalkDistance) yield return null;
            // it dives at the medic and goes off a few metres away
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd) * (Config.left ? -1f : 1f);
            _hitPoint = HeadFlat + side * 3.2f + fwd * 1.5f + Vector3.up * 0.4f;
            if (fpv != null) { fpv.DiveAt(_hitPoint); yield return new WaitForSeconds(1.6f); fpv.Stop(); }
            Hit(true);
        }

        Vector3 HeadFlat => head != null ? new Vector3(head.position.x, 0f, head.position.z) : Vector3.zero;

        /// <summary>The hit: the run starts, the arm is wounded, the senses take the blast (no camera motion).</summary>
        public void Hit(bool blast)
        {
            if (HitHappened) return;
            HitHappened = true;
            Run = new SelfAidRun(Config) { clock = ScenarioClock.Format };
            var amb = CombatAmbience.Instance;
            if (blast && amb != null && !testMode) amb.Detonate(_hitPoint, 0.75f, false); // flash, sound, tinnitus + muffle
            if (head != null)
            {
                Vision = SelfAidVision.Attach(head);
                Vision.Flash(blast ? 1f : 0.35f);
            }
            foreach (var h in new[] { leftHand, rightHand })
                if (h != null && h.Input != null) h.Input.Haptic(blast ? 1f : 0.5f, blast ? 0.6f : 0.2f);
            DropCrate();
            BuildLimb();
            if (Wounded != null && Arm) Wounded.gripDisabled = true;
            if (!Arm) Crawl(true);
            if (RadioSet.Instance != null)
            {
                RadioSet.Instance.pttOverride = OnPtt;
                RadioSet.Instance.reportOverride = RadioLines;
                RadioSet.Instance.expectOverride = () => Run != null && !Run.Ended && (Run.CheckPending || (!Run.ReportSent && Run.InCover));
            }
            Menu = SelfAidReportMenu.Create(Run, head, Config.left ? 1f : -1f);
            Menu.Chosen += o => { if (RadioSet.Instance != null) RadioSet.Instance.ShowLine(o.Text(GameSettings.KZ)); Log("report: " + o.ru); };
            if (tremor && !testMode)
            {
                if (leftHand != null) _tremorL = leftHand.gameObject.GetComponent<HandTremor>() ?? leftHand.gameObject.AddComponent<HandTremor>();
                if (rightHand != null) _tremorR = rightHand.gameObject.GetComponent<HandTremor>() ?? rightHand.gameObject.AddComponent<HandTremor>();
            }
            if (Lesson == null) StartCoroutine(SayLater("sa_hit", testMode ? 0f : 2.2f));
            Log("hit " + Config.LimbId + (blast ? " (FPV)" : " (lesson)"));
        }

        public bool Arm => Config != null && Config.limb == SelfAidLimb.Arm;

        void BuildLimb()
        {
            var go = new GameObject("SelfLimb_" + Config.LimbId);
            if (Arm)
            {
                var arm = go.AddComponent<SelfArm>();
                arm.run = Run;
                arm.head = head;
                arm.left = Config.left;
                arm.hand = Wounded != null ? Wounded.transform : null;
                arm.Build();
                Limb = arm;
            }
            else
            {
                // variant B: he is down on the ground - both legs drawn (look down), the wounded one bleeds
                Limb = Leg(go, Config.left, Run);
                OtherLeg = Leg(new GameObject("SelfLimb_leg_other"), !Config.left, null);
            }
            var tq = new GameObject("SelfTourniquet").AddComponent<SelfTourniquet>();
            tq.run = Run;
            tq.limb = Limb;
            tq.kitCat = KitCat;
            tq.woundedHand = Arm ? Wounded : null;
            Tq = tq;
        }

        public SelfLimb OtherLeg { get; private set; }
        float _locoSpeed = -1f, _highHead;
        bool _teleportWas, _downSaid, _crawling;

        SelfLeg Leg(GameObject go, bool left, SelfAidRun run)
        {
            var leg = go.AddComponent<SelfLeg>();
            leg.rootRadius = 0.085f; leg.jointRadius = 0.06f; leg.distalRadius = 0.045f;
            leg.run = run;
            leg.head = head;
            leg.left = left;
            leg.Build();
            return leg;
        }

        /// <summary>Thigh variant: the leg does not carry him - slow crawl, no teleport (restored at the end).</summary>
        void Crawl(bool on)
        {
            var loco = playerRig != null ? playerRig.GetComponent<Locomotion>() : null;
            if (on && !_crawling)
            {
                _crawling = true;
                if (loco != null) { _locoSpeed = loco.speed; loco.speed = crawlSpeed; }
                _teleportWas = Locomotion.TeleportEnabled;
                Locomotion.TeleportEnabled = false;
            }
            else if (!on && _crawling)
            {
                _crawling = false;
                if (loco != null && _locoSpeed >= 0f) loco.speed = _locoSpeed;
                Locomotion.TeleportEnabled = _teleportWas;
            }
        }

        /// <summary>Thigh variant: standing up tall on the bleeding leg - "stay down, crawl".</summary>
        void HeadLow(float dt)
        {
            if (Arm || head == null || Limb == null) return;
            float above = head.position.y - (Limb.Root.y - 0.13f);
            _highHead = above > 1.3f ? _highHead + dt : 0f;
            if (_highHead > 3f && !_downSaid) { _downSaid = true; Say("sa_down"); }
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!HitHappened || Run == null || Finishing) return;
            float dt = Time.deltaTime;
            bool cover = InCover();
            var cues = Run.Tick(dt, cover || Lesson != null);
            foreach (var c in cues) Cue(c);
            if (Finishing) return;
            if (Vision != null) Vision.bloodLoss = Run.LethalFraction;
            HeadLow(dt);
            if (Wounded != null && Arm)
            {
                Wounded.gripDisabled = !Run.tq.Locked; // no strength, slippery glove — until the band is on
                var glove = Wounded.GetComponent<BloodOnHands>();
                if (glove != null && glove.bloodiness < 0.9f) { glove.bloodiness = 0.9f; glove.wetness = 1f; }
            }
            Heart(dt);
            Shake();
            BlanketCheck(dt);
            foreach (var code in Run.Mistakes.Keys)
                if (!MistakePlaces.ContainsKey(code)) MistakePlaces[code] = head != null ? head.position : Vector3.zero;
            if (Lesson != null && !LessonComplete) StepLogic();
            if (Panel != null) PlacePanel(dt);
            MoveRescuers(dt);
            UpdateStepGuide();
        }

        /// <summary>w10 step line (wrist + bottom of view): what to do now and with which button.</summary>
        void UpdateStepGuide()
        {
            var r = Run;
            if (r == null || r.Ended) return;
            var tq = r.tq;
            if (!r.InCover && r.CoverReachedAt < 0f)
                StepGuide.Set(Arm ? "Сначала в укрытие: дверь дома А" : "Ползи в укрытие: дверь дома А (стик)", Arm ? "Алдымен баспанаға: А үйінің есігі" : "Баспанаға жорғала: А үйінің есігі (стик)", Arm ? ControlIcon.Hand : ControlIcon.Stick);
            else if (!tq.OnLimb)
                StepGuide.Set("X — аптечка. Свободной рукой возьми CAT (GRIP/КУРОК)", "X — қобдиша. Бос қолмен CAT ал (GRIP/ШҮРІППЕ)", ControlIcon.X, ControlIcon.Grip, ControlIcon.Trigger);
            else if (!tq.Locked)
                StepGuide.Set("Жгут высоко, затяни ленту, крути вороток, защёлкни", "Жгут жоғары, таспаны тарт, бұра, бекіт", ControlIcon.Grip);
            else if (r.TimeMarkedAt < 0f)
                StepGuide.Set("X — аптечка, маркер: КУРОК — пиши время на ленте", "X — қобдиша, маркер: ШҮРІППЕ — таспаға уақытты жаз", ControlIcon.X, ControlIcon.Grip, ControlIcon.Trigger);
            else if (r.CheckPending)
                StepGuide.Set("Зажми Y — ответь по рации", "Y-ты басып тұр — рациямен жауап бер", ControlIcon.Y);
            else if (!r.ReportSent)
                StepGuide.Set("Зажми Y — доклад: выбери строку, отпусти Y", "Y-ты басып тұр — баяндама: жолды таңда, Y жібер", ControlIcon.Y, ControlIcon.Trigger);
            else if (!r.Blanket)
                StepGuide.Set("X — аптечка: одеяло, прижми к груди", "X — қобдиша: көрпені кеудеңе бас", ControlIcon.X, ControlIcon.Grip);
            else
                StepGuide.Set("Жди своих в укрытии, отвечай по рации (Y)", "Өзіміздікілерді баспанада күт, рациямен жауап бер (Y)", ControlIcon.Y);
        }

        bool InCover()
        {
            if (head == null) return false;
            if (_covers == null || Time.time - _coversAt > 2f) { _covers = FindObjectsByType<CoverZone>(FindObjectsSortMode.None); _coversAt = Time.time; }
            foreach (var z in _covers)
            {
                if (z == null || !z.Contains(head.position)) continue;
                if (CoverName != z.name)
                {
                    CoverName = z.name;
                    var (ru, kz) = Where(z.name);
                    Run.whereRu = ru; Run.whereKz = kz;
                }
                return true;
            }
            return false;
        }

        static (string, string) Where(string zone) => zone switch
        {
            "CoverZone_HouseA" => ("руины, дом А у флага", "қираған А үйі, ту жанында"),
            "CoverZone_HouseC" => ("руины, дом C, западный край", "қираған C үйі, батыс шеті"),
            _ => ("укрытие на позиции", "позициядағы баспана"),
        };

        void Cue(SelfAidCue c)
        {
            CueLog.Add(c);
            Log("cue " + c);
            var amb = CombatAmbience.Instance;
            switch (c)
            {
                case SelfAidCue.DroneWarning:
                    Say("sa_drone");
                    if (amb != null && !testMode) amb.StartDrone(DroneKind.Fpv, 6f);
                    break;
                case SelfAidCue.DronePass:
                    Blast(3.5f, 0.6f);
                    break;
                case SelfAidCue.Killed:
                    Blast(0.8f, 1f);
                    StartCoroutine(Finish(2.5f));
                    break;
                case SelfAidCue.CoverReached:
                    if (!Run.tq.OnLimb && Lesson == null) Say("sa_tq");
                    break;
                case SelfAidCue.TqLocked:
                    if (Wounded != null && Wounded.Input != null) Haptics.Play(Wounded.Input, Haptics.PressClick);
                    if (Lesson == null) Say("sa_time");
                    break;
                case SelfAidCue.TimeMarked:
                    if (Lesson == null) StartCoroutine(SayLater("sa_report", testMode ? 0f : 1.5f));
                    break;
                case SelfAidCue.Ack:
                    Radio("sa_ack");
                    break;
                case SelfAidCue.BlanketPrompt:
                    Say("sa_blanket");
                    break;
                case SelfAidCue.CheckIn:
                    Radio("sa_check");
                    break;
                case SelfAidCue.CheckInLoud:
                    Radio("sa_check2", 1.4f);
                    foreach (var h in new[] { leftHand, rightHand }) if (h != null && h.Input != null) h.Input.Haptic(0.6f, 0.3f);
                    break;
                case SelfAidCue.Answered:
                    if (RadioSet.Instance != null) RadioSet.Instance.ShowLine(GameSettings.T("«На связи. Держусь.»", "«Байланыстамын. Шыдап жатырмын.»"));
                    StartCoroutine(RadioLater("sa_heard", testMode ? 0f : 1.6f));
                    break;
                case SelfAidCue.RescueArrive:
                    Radio("sa_arrive");
                    SpawnRescuers();
                    break;
                case SelfAidCue.Rescued:
                    StartCoroutine(Finish(1.2f));
                    break;
                case SelfAidCue.Died:
                    StartCoroutine(Finish(3f));
                    break;
            }
        }

        /// <summary>The drone's second pass: a blast this far from the head (no camera motion, flash + haptics).</summary>
        void Blast(float distance, float strength)
        {
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 p = HeadFlat + Quaternion.Euler(0f, UnityEngine.Random.Range(-60f, 60f), 0f) * fwd * distance + Vector3.up * 0.3f;
            var amb = CombatAmbience.Instance;
            if (amb != null && !testMode) amb.Detonate(p, 0.7f, false);
            if (Vision != null) Vision.Flash(strength);
            foreach (var h in new[] { leftHand, rightHand }) if (h != null && h.Input != null) h.Input.Haptic(strength, 0.4f);
        }

        // ------------------------------------------------------------------ radio
        bool OnPtt()
        {
            if (Run == null || Run.Ended) return HitHappened;
            if (Run.CheckPending) { Run.AnswerCheck(); return true; }
            if (!Run.ReportSent)
            {
                if (Menu.Open) Menu.Hide(); else Menu.Show();
                return true;
            }
            if (RadioSet.Instance != null) RadioSet.Instance.ShowLine(GameSettings.T("«На связи.»", "«Байланыстамын.»"));
            return true;
        }

        /// <summary>w10: the Y radio panel — the report lines to choose from (null: a check-in answer / "on the air",
        /// transmitted by the press itself).</summary>
        RadioReport RadioLines()
        {
            if (Run == null || Run.Ended || Run.CheckPending || Run.ReportSent || Menu == null) return null;
            bool kz = GameSettings.KZ;
            var opts = Run.ReportOptions();
            var r = new RadioReport { title = (kz ? "ДОКЛАД  " : "ДОКЛАД  ") + SelfAidRun.ReportStepTitle(Run.ReportStep, kz) };
            foreach (var o in opts) r.Add(o.Text(kz));
            r.send = i => Menu.ChooseFromRadio(i);
            return r;
        }

        /// <summary>Radio PTT as the player would press it (tests, desktop key M).</summary>
        public void PressRadio()
        {
            if (RadioSet.Instance != null) RadioSet.Instance.PressToTalk();
            else OnPtt();
        }

        void Radio(string id, float volume = 1f)
        {
            if (RadioSet.Instance != null && !testMode) RadioSet.Instance.Say(id, volume);
            else Log("radio " + id);
        }

        void Say(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Log("voice " + id);
            if (testMode || _voice == null) return;
            var clip = FieldMedic.Audio.VoiceOver.Clip(id);
            if (clip == null) return;
            _voice.Stop();
            _voice.clip = clip;
            _voice.Play();
        }

        IEnumerator SayLater(string id, float s) { if (s > 0f) yield return new WaitForSeconds(s); Say(id); }
        IEnumerator RadioLater(string id, float s) { if (s > 0f) yield return new WaitForSeconds(s); if (!Finishing) Radio(id); }

        // ------------------------------------------------------------------ body
        void Heart(float dt)
        {
            var p = Run.patient;
            float hz = Mathf.Max(0.5f, p.HeartRate / 60f);
            float prev = _beat;
            _beat += dt * hz;
            if (Mathf.FloorToInt(_beat) == Mathf.FloorToInt(prev)) return;
            float f = Run.LethalFraction;
            if (_heart != null && _beatClip != null) _heart.PlayOneShot(_beatClip, Mathf.Clamp01(0.3f + 0.6f * f));
            // the wounded hand feels the pulse at the wrist — it fades out under a tight band
            var site = Config.left ? PulseSite.RadialL : PulseSite.RadialR;
            float wrist = Arm ? p.PulseStrength(site) : 0.45f;
            if (Wounded != null && wrist > 0.02f) Haptics.Play(Wounded.Input, Haptics.Heartbeat, 0.4f + 0.6f * wrist);
            if (Free != null) Haptics.Play(Free.Input, Haptics.Heartbeat, 0.3f);
        }

        void Shake()
        {
            float f = Run.LethalFraction;
            float a = f >= 0.4f ? 0.0015f + (f - 0.4f) * 0.014f : 0f;
            if (_tremorL != null) _tremorL.amplitude = a;
            if (_tremorR != null) _tremorR.amplitude = a;
        }

        /// <summary>One hand: grab the blanket pack and pull it to the chest — it unfolds over you.</summary>
        void BlanketCheck(float dt)
        {
            if (Run.Blanket || BlanketItem == null || !BlanketItem.IsHeld || head == null) { _blanketNear = 0f; return; }
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 chest = head.position + Vector3.down * 0.35f + fwd * 0.12f;
            if (Vector3.Distance(BlanketItem.transform.position, chest) < 0.3f) _blanketNear += dt; else _blanketNear = 0f;
            if (_blanketNear < 0.25f) return;
            Run.PutOnBlanket();
            BlanketItem.ForceRelease();
            BlanketItem.gameObject.SetActive(false);
            if (Vision != null) Vision.Blanket();
            if (_sfx != null && _crinkleClip != null) _sfx.PlayOneShot(_crinkleClip, 0.8f);
            Log("blanket on");
        }

        // ------------------------------------------------------------------ rescue
        void SpawnRescuers()
        {
            if (head == null) return;
            var ni = FieldMedic.Battle.NearInfantry.Instance;
            var src = ni != null ? ni.friendlies.Where(a => a != null).Take(2).ToList() : new List<FieldMedic.Battle.SoldierAgent>();
            Vector3 me = HeadFlat;
            Vector3 from = (BattlefieldLayout.LandingZone - me); from.y = 0f;
            from = from.sqrMagnitude > 1f ? from.normalized : Vector3.back;
            // in house A they come in through the drag doorway (not through the wall)
            bool viaDoor = CoverName == "CoverZone_HouseA";
            Vector3 door = BattlefieldLayout.DoorwayCentre;
            if (viaDoor) { from = Vector3.right; me = door; }
            _waypoint = viaDoor ? door + Vector3.left * 0.6f : Vector3.zero;
            for (int i = 0; i < src.Count; i++)
            {
                var go = Instantiate(src[i].gameObject);
                go.name = "Rescuer_" + i;
                foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                go.SetActive(true);
                Vector3 side = Vector3.Cross(Vector3.up, from) * (i == 0 ? 0.9f : -0.9f);
                go.transform.position = me + from * (9f + i * 1.5f) + side;
                go.transform.rotation = Quaternion.LookRotation(-from);
                var an = go.GetComponentInChildren<Animator>();
                if (an != null) { an.enabled = true; an.CrossFade("Run", 0.1f); }
                _rescuers.Add(go.transform);
            }
            Log("rescuers " + _rescuers.Count);
        }

        Vector3 _waypoint;
        readonly HashSet<Transform> _passedDoor = new HashSet<Transform>();

        void MoveRescuers(float dt)
        {
            if (_rescuers.Count == 0 || head == null) return;
            Vector3 me = HeadFlat;
            for (int i = 0; i < _rescuers.Count; i++)
            {
                var r = _rescuers[i];
                if (r == null) continue;
                Vector3 side = Vector3.Cross(Vector3.up, (r.position - me).normalized) * (i == 0 ? 0.7f : -0.7f);
                Vector3 goal = me + (r.position - me).normalized * 1.1f + side;
                if (_waypoint != Vector3.zero && !_passedDoor.Contains(r))
                {
                    goal = _waypoint + Vector3.forward * (i == 0 ? 0.3f : -0.3f);
                    Vector3 w = goal - r.position; w.y = 0f;
                    if (w.magnitude < 0.3f) _passedDoor.Add(r);
                }
                Vector3 to = goal - r.position; to.y = 0f;
                if (to.magnitude < 0.15f) continue;
                float step = Mathf.Min(to.magnitude, 2.4f * dt);
                r.position += to.normalized * step;
                r.position = new Vector3(r.position.x, BattlefieldLayout.GroundHeight(r.position.x, r.position.z), r.position.z);
                r.rotation = Quaternion.LookRotation(to.normalized);
                if (to.magnitude - step < 0.2f && (_waypoint == Vector3.zero || _passedDoor.Contains(r))) { var an = r.GetComponentInChildren<Animator>(); if (an != null) an.CrossFade("Kneel", 0.3f); }
            }
        }

        // ------------------------------------------------------------------ end
        IEnumerator Finish(float fade)
        {
            if (Finishing) yield break;
            Finishing = true;
            var cf = testMode ? null : ComfortFade.Instance;
            if (cf != null)
            {
                cf.fadeSpeed = 1f / Mathf.Max(0.2f, fade);
                cf.Fade = 1f;
                yield return new WaitForSeconds(fade + 0.3f);
            }
            BuildReport();
            Cleanup();
            if (FieldMedic.Treatment.KitTray.Player != null && FieldMedic.Treatment.KitTray.Player.IsOpen) FieldMedic.Treatment.KitTray.Player.Close();
            // w10 audit: the last run line ("Жди своих…") used to stay up during the debrief
            StepGuide.Set("Итог на доске: наведи луч, КУРОК — кнопка", "Қорытынды тақтада: сәулені бағытта, ШҮРІППЕ — түйме", ControlIcon.Trigger);
            if (board != null && !testMode)
            {
                board.gameObject.SetActive(true);
                board.ShowDebrief(Report, head);
            }
            if (cf != null) { cf.fadeSpeed = 2.2f; cf.Fade = 0f; }
            Finished?.Invoke(Report);
        }

        void BuildReport()
        {
            if (!Run.Ended) Run.End("ended");
            bool kz = GameSettings.KZ;
            Report = SelfAidDebrief.Build(Def, Run, ScenarioClock.Format, kz);
            foreach (var m in Report.mistakes)
                if (MistakePlaces.TryGetValue(m.code, out var at)) { m.hasPos = true; m.x = at.x; m.z = at.z; }
            Report.hasCasualtyPos = true;
            Report.casualtyX = _hitPoint.x; Report.casualtyZ = _hitPoint.z;
            string id = Def != null ? Def.id : "SA";
            int best = GameSettings.BestScore(id);
            if ((DebriefBuilder.UpdateBest(Report, Mathf.Max(0, best)) || best < 0) && !testMode) GameSettings.SetBestScore(id, Report.bestScore);
            Log($"debrief SA: {Run.EndReason}, score {Report.score}, survived {Report.survived}, mistakes {string.Join(",", Report.mistakes.Select(m => m.code))}");
        }

        void Cleanup()
        {
            if (Wounded != null) Wounded.gripDisabled = false;
            Crawl(false);
            if (_tremorL != null) _tremorL.amplitude = 0f;
            if (_tremorR != null) _tremorR.amplitude = 0f;
            if (RadioSet.Instance != null) { RadioSet.Instance.pttOverride = null; RadioSet.Instance.reportOverride = null; RadioSet.Instance.expectOverride = null; }
            if (Menu != null) Menu.Hide();
            if (Vision != null) Vision.bloodLoss = 0f;
            FieldMedic.Battle.BattleDirector.PlayerArmed = false;
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.3f;
        }

        /// <summary>Force the end (tests / menu).</summary>
        public void EndNow(string reason)
        {
            if (Run == null || Finishing) return;
            Run.End(reason);
            StartCoroutine(Finish(0.2f));
        }

        // ------------------------------------------------------------------ training lesson
        void ShowStep()
        {
            var s = Progress.Step;
            _sticky = s != null ? new bool[s.actions.Count] : new bool[0];
            _doneAt = -1f;
            if (s != null) Panel.SetStep(s, Progress.StepNumber, Progress.StepCount, GameSettings.KZ);
        }

        public bool Satisfied(string id)
        {
            var tq = Run.tq;
            switch (id)
            {
                case "s_grab": return (KitCat != null && KitCat.IsHeld) || tq.OnLimb;
                case "s_loop":
                case "s_on_arm": return tq.OnLimb;
                case "s_high": return tq.stage >= TourniquetStage.Positioned && tq.limbPosition <= Config.highAndTightMax;
                case "s_high_pos": return tq.OnLimb && tq.limbPosition <= Config.highAndTightMax;
                case "s_strap": return tq.stage >= TourniquetStage.StrapPulled;
                case "s_stopped": return Run.wound.IsControlled;
                case "s_twist": return tq.Locked && Run.wound.IsControlled;
                case "s_secure": return tq.Secured;
                case "marker_grab": return (Marker != null && Marker.IsHeld) || tq.TimeMarked;
                case "s_time": return tq.TimeMarked;
            }
            return false;
        }

        string Wrong(LessonStep s)
        {
            var tq = Run.tq;
            bool kz = GameSettings.KZ;
            if (s.id == "s_high" && tq.stage >= TourniquetStage.Positioned && tq.limbPosition > Config.highAndTightMax)
                return kz ? "Жгут тым төмен — иыққа қарай жоғары жылжыт" : "Жгут низко — сдвинь его выше, к плечу";
            if (s.id == "s_twist" && tq.Locked && !Run.wound.IsControlled)
                return kz ? "Қан әлі ағып тұр — тағы бұра" : "Кровь ещё идёт — докрути вороток";
            if (s.id == "s_grab" && Wounded != null && Wounded.Input != null && Wounded.Input.Grip > 0.6f)
                return kz ? "Жаралы қол ұстамайды — бос қолды пайдалан" : "Раненая рука не держит — бери свободной";
            return null;
        }

        void StepLogic()
        {
            var step = Progress.Step;
            if (step == null) { CompleteLesson(); return; }
            _sticky = Progress.ActionStates(Satisfied, _sticky);
            Panel.SetChecks(_sticky);
            if (_doneAt < 0f && Satisfied(step.id)) { _doneAt = Time.time; Panel.SetDone(); Log("lesson step done " + step.id); }
            if (_doneAt >= 0f && Time.time - _doneAt >= (testMode ? 0.05f : 1.1f))
            {
                Progress.TryAdvanceOne(_ => true, Run.Clock);
                if (Progress.Complete) { CompleteLesson(); return; }
                ShowStep();
                Say(Progress.Step.voice);
                return;
            }
            if (_doneAt < 0f) Panel.SetWrong(Wrong(step));
        }

        void CompleteLesson()
        {
            if (LessonComplete) return;
            LessonComplete = true;
            float secs = Time.time - _lessonStart;
            Panel.SetDone();
            Say("t_done");
            Log($"lesson {Lesson.id} complete in {secs:0} s");
            Cleanup();
            if (board != null && !testMode)
            {
                board.gameObject.SetActive(true);
                board.ShowTrainingDone(Lesson, secs, 0, head);
            }
        }

        void PlacePanel(float dt)
        {
            if (head == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd) * (Config.left ? 1f : -1f); // on the free-hand side
            Vector3 target = head.position + fwd * 0.85f + side * 0.32f + Vector3.up * 0.05f;
            Panel.transform.position = Vector3.Lerp(Panel.transform.position, target, 1f - Mathf.Exp(-dt * 4f));
            Vector3 face = Panel.transform.position - head.position; face.y = 0f;
            if (face.sqrMagnitude > 1e-4f) Panel.transform.rotation = Quaternion.LookRotation(face.normalized);
        }

        // ------------------------------------------------------------------ sounds (procedural, no assets)
        static AudioClip HeartbeatClip()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.42f);
            var d = new float[n];
            void Thump(float at, float freq, float amp, float decay)
            {
                int s0 = (int)(at * rate);
                for (int i = s0; i < n; i++)
                {
                    float t = (i - s0) / (float)rate;
                    d[i] += amp * Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t / decay) * Mathf.Clamp01(t * 400f);
                }
            }
            Thump(0f, 52f, 0.9f, 0.045f);
            Thump(0.15f, 64f, 0.6f, 0.035f);
            var c = AudioClip.Create("SA_Heartbeat", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip CrinkleClip()
        {
            const int rate = 22050;
            int n = (int)(rate * 1.3f);
            var d = new float[n];
            var r = new System.Random(7);
            float env = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                if (r.NextDouble() < 0.004) env = 0.4f + (float)r.NextDouble() * 0.6f; // a crackle burst
                env *= 0.9975f;
                float noise = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.55f;
                float fade = Mathf.Clamp01((n - i) / (rate * 0.3f));
                d[i] = (noise - lp) * env * 0.8f * fade;
            }
            var c = AudioClip.Create("SA_Crinkle", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        void Log(string s)
        {
            Events.Add(s);
            Debug.Log("[FMVR] self-aid: " + s);
        }
    }
}
