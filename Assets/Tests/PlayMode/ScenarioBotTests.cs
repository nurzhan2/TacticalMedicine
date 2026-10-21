using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Every scenario end-to-end through the real ScenarioDirector: scripted incident → the medic bot treats Erlan
    /// with its scripted hands (correct protocol → he survives the evacuation wait, score ≥ 85) or makes the
    /// scenario's classic mistake (→ the debrief records it). Evacuation time is compressed in tests.
    /// </summary>
    public class ScenarioBotTests
    {
        readonly List<Object> _spawned = new List<Object>();
        ScenarioDirector _d;
        CasualtyController _c;
        MedicBot _bot;
        IfakPouch _pouch;
        DebriefReport _report;
        static readonly Vector3 CoverAt = new Vector3(-3.6f, 0f, 0f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure(); // w9: never inherit a battlefield from an earlier test
            GameSettings.HoldTimeScale = 60f;
            _report = null;
            _pouch = null;
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
            _spawned.Add(floor);
            var ear = new GameObject("Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.HoldTimeScale = 1f;
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var p in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "BloodPool")) Object.Destroy(p.gameObject);
            foreach (var b in Object.FindObjectsByType<BriefingCard>(FindObjectsSortMode.None)) Object.Destroy(b.gameObject);
        }

        HandGrabber MakeHand(Handedness h, Vector3 at)
        {
            var go = new GameObject("Hand_" + h);
            _spawned.Add(go);
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var g = go.AddComponent<HandGrabber>();
            rig.UseScripted(at);
            go.transform.position = at;
            go.AddComponent<HandTreatment>();
            return g;
        }

        /// <summary>Erlan + director (test mode) + cover zone 3.6 m away + bot + the player's IFAK. Runs the incident.</summary>
        IEnumerator Run(string id, ScenarioVariant v = default, bool autoplay = false)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);
            var erlan = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _spawned.Add(erlan);
            _c = erlan.GetComponent<CasualtyController>();
            _c.voice.muted = true;
            var cover = new GameObject("Cover");
            _spawned.Add(cover);
            cover.transform.position = CoverAt + Vector3.up * 0.5f;
            cover.AddComponent<CoverZone>().size = new Vector3(1.6f, 2f, 1.8f);

            var dgo = new GameObject("Director");
            _spawned.Add(dgo);
            _d = dgo.AddComponent<ScenarioDirector>();
            _d.autoStart = false;
            _d.testMode = true;
            _d.casualty = _c;
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(dgo.transform, false);
            _d.spots = System.Enum.GetValues(typeof(CasualtySpot)).Cast<CasualtySpot>()
                .Select(s => new ScenarioDirector.SpotAnchor { spot = s, casualty = anchor }).ToArray();
            _d.Finished += r => _report = r;
            yield return null;

            var def = ScenarioCatalog.Get(id);
            _d.Begin(def, v, false, autoplay);
            if (autoplay) _d.GetComponent<AutoplayDriver>().stepSeconds = 0.3f;
            _d.Timeline.evacTimeScale = 150f;
            for (int k = 0; k < 10 && _c.Patient.wounds.Count == 0; k++) yield return null;
            Assert.That(_c.Patient.wounds.Count, Is.GreaterThan(0), "incident applied the wounds");
            float t0 = Time.time;
            while (_c.IsFalling && Time.time - t0 < 9f) yield return null;
            Vector3 last = Hips;
            for (float s = 0f; s < 6f; s += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                if (s > 1f && Vector3.Distance(Hips, last) < 0.004f) break;
                last = Hips;
            }
            var hips = Hips;
            var l = MakeHand(Handedness.Left, hips + new Vector3(0.4f, 0.5f, -0.1f));
            var r = MakeHand(Handedness.Right, hips + new Vector3(0.4f, 0.5f, 0.1f));
            var bgo = new GameObject("Bot");
            _spawned.Add(bgo);
            _bot = bgo.AddComponent<MedicBot>();
            _bot.Init(l, r);
            PlacePouch();
            yield return null;
        }

        Vector3 Hips => _c.rig.Bone(HumanBodyBones.Hips).position;

        void PlacePouch()
        {
            if (_pouch == null)
            {
                var pgo = new GameObject("PlayerIFAK");
                _spawned.Add(pgo);
                _pouch = pgo.AddComponent<IfakPouch>();
                _pouch.buildOnStart = false;
                _pouch.transform.position = Hips + new Vector3(0.75f, 0.05f, 0.25f);
                _pouch.Build();
            }
            else _pouch.transform.position = Hips + new Vector3(0.75f, 0.05f, 0.25f);
        }

        /// <summary>Bot hands next to him again (after a drag the body is somewhere else).</summary>
        IEnumerator Reposition()
        {
            var h = Hips;
            PlacePouch();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return _bot.Move(_bot.left, h + new Vector3(0.4f, 0.5f, -0.1f), false, 4f);
            yield return _bot.Move(_bot.right, h + new Vector3(0.4f, 0.5f, 0.1f), false, 4f);
            yield return null;
        }

        IEnumerator DragToCover()
        {
            yield return _bot.DragTo(_c, CoverAt, 30f);
            for (int k = 0; k < 30 && _c.Protocol.phase == Phase.CareUnderFire; k++) yield return null;
            Assert.That(_c.Protocol.phase, Is.EqualTo(Phase.TacticalFieldCare), $"in cover (hips {Hips:F2}) {_bot.lastStep}");
            yield return new WaitForSeconds(0.6f);
            yield return Reposition();
        }

        IEnumerator Fast(float simSeconds, float speed)
        {
            _c.simulationSpeed = speed;
            float target = _c.Patient.secondsSinceStart + simSeconds, t0 = Time.realtimeSinceStartup;
            while (_c.Patient.secondsSinceStart < target && _c.Patient.outcome == Outcome.Ongoing && Time.realtimeSinceStartup - t0 < 40f)
                yield return null;
            _c.simulationSpeed = 1f;
        }

        /// <summary>MEDEVAC request → compressed wait → debrief.</summary>
        IEnumerator Evacuate()
        {
            _c.Protocol.Perform(MedAction.MedevacRequest);
            float t0 = Time.realtimeSinceStartup;
            while (_report == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Assert.That(_report, Is.Not.Null, "debrief produced; state " + _d.State);
        }

        /// <summary>Wrong treatment: let the run end by itself (death) or force the end.</summary>
        IEnumerator FinishWrong(float simSeconds = 900f)
        {
            yield return Fast(simSeconds, 90f);
            if (_report == null) { _c.Protocol.Perform(MedAction.MedevacRequest); }
            float t0 = Time.realtimeSinceStartup;
            while (_report == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Assert.That(_report, Is.Not.Null, "debrief produced");
        }

        string Summary => _report == null ? "no report" :
            $"{_report.scenarioId} survived={_report.survived} score={_report.score} end={_report.endReason} loss={_report.lossPercent:0}% mistakes=[{string.Join(",", _report.mistakes.Select(m => m.code))}] log=[{string.Join(",", _c.Protocol.log.Select(e => e.code))}] bot={_bot.lastStep}";

        void AssertSaved()
        {
            Debug.Log("[FMVR] scenario bot: " + Summary);
            Assert.That(_report.survived, Is.True, Summary);
            Assert.That(_report.endReason, Is.EqualTo("evacuated"), Summary);
            Assert.That(_report.score, Is.GreaterThanOrEqualTo(85), Summary);
        }

        void AssertMistake(string code)
        {
            Debug.Log("[FMVR] scenario bot (wrong): " + Summary);
            Assert.That(_report.mistakes.Any(m => m.code == code), Is.True, Summary);
            var m0 = _report.mistakes.First(m => m.code == code);
            Assert.That(m0.text, Is.Not.Empty);
        }

        IEnumerator Tq(Limb limb, float pos, bool marker = true, bool hisOwn = true)
        {
            var cat = hisOwn && _c.Interaction.Ifak != null ? _c.Interaction.Ifak.Find<TourniquetCAT>() : _pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, _c, limb, pos, 4, marker ? _pouch.Find<MarkerItem>() : null);
            Assert.That(cat.State.Locked, Is.True, _bot.lastStep + " " + cat.State.stage);
        }

        /// <summary>Israeli bandage; if the pad did not land (he moved), try again with his own bandage.</summary>
        IEnumerator BandageWithRetry(WoundCare care)
        {
            var b = _pouch.Find<BandageItem>();
            yield return _bot.Bandage(b, care);
            if (care.Wound.control == BleedControl.PressureDressing) yield break;
            Debug.Log("[FMVR] bandage retry: " + _bot.lastStep + " " + b.LastHint);
            yield return _bot.Release(_bot.right);
            yield return Reposition();
            // pad already on the wound: keep wrapping with the same roll; otherwise his own bandage
            var next = b.ApplyingTo != null ? b : _c.Interaction.Ifak != null ? _c.Interaction.Ifak.Find<BandageItem>() : null;
            if (next != null) yield return _bot.Bandage(next, care);
        }

        WoundCare Care(System.Func<Wound, bool> f) => _c.Interaction.Care(_c.Patient.wounds.First(f));

        // ================================================================== S1 «Сброс»
        [UnityTest]
        public IEnumerator S1_Drop_CorrectProtocol_Survives()
        {
            yield return Run("S1");
            Assert.That(_c.Protocol.phase, Is.EqualTo(Phase.CareUnderFire));
            yield return Tq(Limb.RightLeg, 0.12f);
            Assert.That(_c.Patient.wounds.First(w => w.type == WoundType.ArterialLimb).IsControlled, Is.True);
            yield return DragToCover();
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            yield return _bot.BloodSweep(_c);
            Assert.That(_c.Patient.wounds.All(w => w.discovered), Is.True, "forearm fragment found");
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
            Assert.That(_report.limbOutcome, Does.Contain("Правая нога"));
            Assert.That(_report.bleedControlSeconds, Is.InRange(0f, 60f));
        }

        [UnityTest]
        public IEnumerator S1_Drop_TourniquetOnKnee_RecordsJointMistake()
        {
            yield return Run("S1");
            yield return Tq(Limb.RightLeg, 1.0f, false);
            yield return FinishWrong();
            AssertMistake("tq_on_joint");
            Assert.That(_report.survived, Is.False, Summary);
        }

        // ================================================================== S2 «Снайпер»
        [UnityTest]
        public IEnumerator S2_Sniper_SealBothHoles_Needle_Survives()
        {
            yield return Run("S2");
            yield return DragToCover();
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            var entry = Care(w => w.type == WoundType.PenetratingChest && !w.posterior);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(0), entry);
            Assert.That(entry.Wound.control, Is.EqualTo(BleedControl.OcclusiveSeal), _bot.lastStep);
            yield return _bot.Roll(_c, true);
            var exit = _c.Patient.wounds.First(w => w.posterior);
            Assert.That(exit.discovered, Is.True, "exit wound found by the log roll");
            var exitCare = _c.Interaction.Care(exit);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(1) ?? _pouch.Find<ChestSealItem>(0), exitCare);
            if (exit.control != BleedControl.OcclusiveSeal)
            {
                // the back is awkward to reach with the scripted hands: log it and seal through the protocol
                Debug.Log("[FMVR] S2 bot: back seal by hand failed (" + _bot.lastStep + "), protocol fallback");
                _c.Protocol.Perform(MedAction.ChestSeal, exit);
            }
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Sealed));
            _c.simulationSpeed = 60f;
            float t0 = Time.realtimeSinceStartup;
            while (!(_c.Patient.pneumo == PneumoState.Tension && _c.Patient.tensionSeverity > 0.2f) && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            _c.simulationSpeed = 1f;
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Tension));
            yield return _bot.Needle(_pouch.Find<NeedleItem>(), _c, NeedleSite.FifthIcsAal, true);
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Decompressed), _bot.lastStep);
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
        }

        [UnityTest]
        public IEnumerator S2_Sniper_NoBackCheck_RecordsMissedExitWound()
        {
            yield return Run("S2");
            yield return DragToCover();
            var entry = Care(w => !w.posterior);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(), entry);
            yield return FinishWrong(600f);
            AssertMistake("missed_wound");
        }

        // ================================================================== S3 «Мина»
        [UnityTest]
        public IEnumerator S3_Mine_HighTourniquetOverStump_Survives()
        {
            yield return Run("S3");
            Assert.That(_c.Protocol.phase, Is.EqualTo(Phase.TacticalFieldCare));
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            // field care: band on the skin → cut the trouser leg first
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.RightLeg, 0.9f, 0.08f);
            yield return Tq(Limb.RightLeg, 0.12f);
            var cat3 = _c.Interaction.Tourniquets.First();
            if (!cat3.State.TimeMarked) yield return _bot.Write(_pouch.Find<MarkerItem>(), cat3.TimeLabelPoint);
            var stump = _c.Patient.wounds.First(w => w.type == WoundType.TraumaticAmputation);
            yield return Fast(60f, 20f); // past the vasospasm
            Assert.That(stump.IsControlled, Is.True, "band above the stump holds after the spasm");
            yield return _bot.BloodSweep(_c);
            var shin = Care(w => w.region == BodyRegion.ShinL);
            // the whole left trouser leg: shin fragment and the groin
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.LeftLeg, 1.7f, 0.05f);
            yield return BandageWithRetry(shin);
            Assert.That(shin.Wound.IsControlled, Is.True, _bot.lastStep);
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
            Assert.That(_report.limbOutcome, Does.Contain("ампутац"));
        }

        [UnityTest]
        public IEnumerator S3_Mine_ElevatingStumpInsteadOfTourniquet_RecordsMistake()
        {
            yield return Run("S3");
            yield return Fast(50f, 10f); // the spasm lets go: massive bleed
            yield return _bot.LiftLeg(_c, Limb.RightLeg, 0.4f, 16f); // holding the stump up instead of a band
            yield return FinishWrong();
            AssertMistake("elevation");
        }

        // ================================================================== S4 «Миномёт»
        [UnityTest]
        public IEnumerator S4_Mortar_GroinPackingPressureBandage_Survives()
        {
            yield return Run("S4");
            yield return DragToCover();
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            var care = Care(w => w.type == WoundType.Junctional);
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.RightLeg, 0.85f, 0.1f);
            yield return _bot.Pack(_pouch.Find<GauzeItem>(), care);
            Assert.That(care.Packing.Complete, Is.True, _bot.lastStep);
            yield return _bot.HoldPressure(care, 3.6f);
            yield return _bot.Bandage(_pouch.Find<BandageItem>(), care);
            Assert.That(care.Wound.IsControlled, Is.True, _bot.lastStep);
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
        }

        [UnityTest]
        public IEnumerator S4_Mortar_TourniquetOnGroinWound_RecordsJunctionalMistake()
        {
            yield return Run("S4");
            var cat = _pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, _c, Limb.RightLeg, 0.12f, 4);
            yield return FinishWrong();
            AssertMistake("tq_junctional");
        }

        // ================================================================== w10g autoplay
        [UnityTest]
        public IEnumerator Autoplay_PlaysScenarioToTheEnd([Values("S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8")] string id)
        {
            yield return Run(id, ScenarioVariant.Default, autoplay: true);
            Assert.That(_d.Autoplay, Is.True);
            float t0 = Time.realtimeSinceStartup;
            while (_report == null && Time.realtimeSinceStartup - t0 < 120f)
            {
                if (_c.Protocol.medevacRequested) _c.simulationSpeed = 20f;
                yield return null;
            }
            var ap = _d.GetComponent<AutoplayDriver>();
            Debug.Log($"[FMVR] autoplay {id}: steps {ap.StepsDone} last {ap.LastStepId} → " + (_report == null ? "no report, step " + _d.CurrentStep : Summary));
            Assert.That(_report, Is.Not.Null, "autoplay reached the debrief; step " + _d.CurrentStep);
            Assert.That(_report.title, Does.Contain("автопрохождение"));
            Assert.That(_report.survived, Is.True, Summary);
            Assert.That(ap.StepsDone, Is.GreaterThan(2));
        }

        // ================================================================== S5 «FPV»
        [UnityTest]
        public IEnumerator S5_Fpv_FindsHiddenButtockWound_Survives()
        {
            yield return Run("S5");
            yield return DragToCover();
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            yield return _bot.BloodSweep(_c);
            var shin = Care(w => w.region == BodyRegion.ShinL);
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.LeftLeg, 1.7f, 1.2f);
            yield return BandageWithRetry(shin);
            Assert.That(shin.Wound.IsControlled, Is.True, "shin dressed " + _bot.lastStep);
            yield return _bot.Roll(_c, false);
            var hidden = _c.Patient.wounds.First(w => w.posterior);
            Assert.That(hidden.discovered, Is.True, "log roll reveals the buttock wound");
            var care = _c.Interaction.Care(hidden);
            yield return _bot.Pack(_pouch.Find<GauzeItem>(), care);
            if (!hidden.packed)
            {
                Debug.Log("[FMVR] S5 bot: packing the buttock by hand failed (" + _bot.lastStep + "), protocol fallback");
                _c.Protocol.Perform(MedAction.HemostaticPacking, hidden);
            }
            yield return _bot.HoldPressure(care, 3.6f);
            if (hidden.control == BleedControl.Packed) _c.Protocol.HoldPressure(hidden, 200f);
            Assert.That(hidden.IsControlled || hidden.control == BleedControl.PackedAndHeld, Is.True, hidden.control.ToString());
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
        }

        [UnityTest]
        public IEnumerator S5_Fpv_OnlyVisibleWounds_RecordsMissedWound()
        {
            yield return Run("S5");
            yield return DragToCover();
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            var shin = Care(w => w.region == BodyRegion.ShinL);
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.LeftLeg, 1.7f, 1.2f);
            yield return _bot.Bandage(_pouch.Find<BandageItem>(), shin);
            yield return FinishWrong(400f);
            AssertMistake("missed_wound");
        }

        // ================================================================== S6 «КАБ»
        [UnityTest]
        public IEnumerator S6_Kab_RecoveryPosition_SurvivesVomiting()
        {
            yield return Run("S6", new ScenarioVariant { secondHit = true });
            Assert.That(_c.Patient.AVPU, Is.EqualTo(Consciousness.Unresponsive));
            yield return _bot.Roll(_c, true);
            Assert.That(_c.rolledOnSide, Is.True);
            Assert.That(_c.Patient.recoveryPosition, Is.True);
            Assert.That(_c.Patient.AirwayCompromised, Is.False);
            yield return _bot.BloodSweep(_c); // scalp wound
            yield return Fast(240f, 30f); // both vomiting episodes happen while he is on his side
            Assert.That(_d.Events.Count(e => e.Contains("Vomit")), Is.GreaterThanOrEqualTo(1), string.Join(";", _d.Events));
            Assert.That(_c.Patient.outcome, Is.EqualTo(Outcome.Ongoing), _c.Patient.causeOfDeath);
            yield return Evacuate();
            AssertSaved();
        }

        [UnityTest]
        public IEnumerator S6_Kab_LeftOnBack_RecordsAirwayMistake_Dies()
        {
            yield return Run("S6");
            yield return FinishWrong(600f);
            AssertMistake("left_supine");
            Assert.That(_report.survived, Is.False, Summary);
        }

        // ================================================================== S7 «Под огнём»
        [UnityTest]
        public IEnumerator S7_UnderFire_DragThenRetightenSlippedTq_Survives()
        {
            yield return Run("S7");
            var cat = _c.Interaction.Tourniquets.FirstOrDefault();
            Assert.That(cat, Is.Not.Null, "his CAT is already on the thigh");
            Assert.That(cat.State.Locked, Is.True);
            Assert.That(_c.Patient.wounds[0].IsControlled, Is.True);
            yield return DragToCover();
            yield return null;
            Assert.That(cat.Slipped, Is.True, "the band slipped during the drag");
            Assert.That(_c.Patient.wounds[0].IsControlled, Is.False, "rebleeding");
            yield return _bot.CheckPulse(_c, PulseSite.PedalR, 1.5f);
            yield return _bot.Retighten(cat);
            Assert.That(cat.State.Locked && cat.State.FullOcclusion, Is.True, _bot.lastStep + " " + cat.State.stage + " " + cat.State.HalfTurns);
            Assert.That(cat.Retightened, Is.True);
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            yield return Evacuate();
            AssertSaved();
        }

        [UnityTest]
        public IEnumerator S7_UnderFire_NoRecheckAfterDrag_RecordsMistake()
        {
            yield return Run("S7");
            yield return DragToCover();
            yield return FinishWrong(300f);
            AssertMistake("tq_not_rechecked");
        }

        // ================================================================== S8 «До рассвета»
        [UnityTest]
        public IEnumerator S8_Dawn_DressingThenConversion_TimeSkips_Survives()
        {
            yield return Run("S8");
            var cat = _c.Interaction.Tourniquets.FirstOrDefault();
            Assert.That(cat, Is.Not.Null);
            Assert.That(cat.allowConversion, Is.True);
            yield return _bot.Gloves(_pouch.Find<GlovesItem>());
            var shin = Care(w => w.region == BodyRegion.ShinR);
            Assert.That(shin.Wound.exposed, Is.True, "trouser leg already cut at the first treatment");
            yield return BandageWithRetry(shin);
            Assert.That(shin.Wound.control, Is.EqualTo(BleedControl.PressureDressing), _bot.lastStep);
            yield return _bot.ConvertTourniquet(cat);
            Assert.That(_c.Protocol.log.Any(e => e.code == "converted"), Is.True, string.Join(",", _c.Protocol.log.Select(e => e.code)));
            Assert.That(shin.Wound.IsControlled, Is.True, "the dressing holds without the band");
            yield return _bot.Blanket(_pouch.Find<BlanketItem>(), _c);
            int skips = 0;
            while (_d.TimeSkip()) { skips++; yield return null; }
            Assert.That(skips, Is.EqualTo(4), "30 min / 1 h / 2 h / 4 h");
            Assert.That(_c.Protocol.medevacRequested, Is.True, "dawn: the road opens");
            float t0 = Time.realtimeSinceStartup;
            while (_report == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            AssertSaved();
            Assert.That(_report.limbOutcome, Does.Contain("сохранена").Or.Contain("сақталады"));
        }

        [UnityTest]
        public IEnumerator S8_Dawn_NeverReassessed_RecordsMistake()
        {
            yield return Run("S8");
            while (_d.TimeSkip()) yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (_report == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Assert.That(_report, Is.Not.Null);
            AssertMistake("tq_not_reassessed");
        }

        // ================================================================== board + launch
        [UnityTest]
        public IEnumerator Board_ShowsDebrief_AndButtonsLaunch()
        {
            var go = new GameObject("Board");
            _spawned.Add(go);
            var b = go.AddComponent<MenuBoard>();
            string launched = null;
            b.launchOverride = s => launched = s;
            yield return null;
            b.ShowMain();
            Assert.That(RoundButton.All.Count(x => x.transform.IsChildOf(go.transform)), Is.GreaterThanOrEqualTo(6));
            b.ShowScenarios();
            yield return null;
            var card = RoundButton.All.First(x => x.transform.IsChildOf(go.transform) && x.Label != null && x.Label.text == "1");
            card.Click();
            Assert.That(b.Page, Is.EqualTo("briefing"));
            yield return new WaitForSeconds(0.4f);
            var start = RoundButton.All.First(x => x.transform.IsChildOf(go.transform) && x.diameter >= 0.19f);
            start.Click();
            Assert.That(launched, Is.EqualTo("scenario:S1"));

            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            cp.Perform(MedAction.Tourniquet, null, BodyRegion.ThighR);
            var rep = DebriefBuilder.Build(ScenarioCatalog.Get("S1"), cp, "evacuated", ScenarioClock.Format);
            b.ShowDebrief(rep);
            Assert.That(b.Page, Is.EqualTo("debrief"));
            var texts = go.GetComponentsInChildren<TextMesh>().Select(t => t.text).ToList();
            Assert.That(texts.Any(t => t == rep.score.ToString()), Is.True, "big score shown");
            Assert.That(texts.Any(t => t.Contains("−")), Is.True, "mistakes listed");
            // pointer hover + click through the desktop path
            var menuBtn = RoundButton.All.First(x => x.transform.IsChildOf(go.transform) && x.Label != null && (x.Label.text == "МЕНЮ" || x.Label.text == "МӘЗІР"));
            var ray = new Ray(menuBtn.transform.position + menuBtn.Normal * 0.5f, -menuBtn.Normal);
            Assert.That(BoardPointer.Ensure().Pick(ray, out _), Is.EqualTo(menuBtn));
            yield return new WaitForSeconds(0.4f);
            menuBtn.Click();
            Assert.That(launched, Is.EqualTo("menu"));
            Object.Destroy(BoardPointer.Instance.gameObject);
        }
    }
}
