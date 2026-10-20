using System;
using System.Collections;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w4 modes on the real Battlefield scene: the white hint panel (appears only within 2 m, billboards, keeps the
    /// wound visible, advances with a green check, turns yellow with the reason on a wrong action), the bot medic
    /// completing Training (tourniquet topic and the full TCCC run with TACEVAC loading) and a Combat run (S1:
    /// CAT under fire → drag into the ruins → field care → MEDEVAC → helicopter → load → debrief with map markers),
    /// Exam launch, the menu pages and the wrist display.
    /// </summary>
    public class W4ModesTests
    {
        GlobalStateGuard _guard;
        Transform _head;
        MedicBot _bot;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
        }

        [TearDown]
        public void TearDown()
        {
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            GameSettings.HoldTimeScale = 1f;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            SmokeScreens.Clear();
            if (_head != null) Object.Destroy(_head.gameObject);
        }

        static IEnumerator Seconds(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        IEnumerator Load(Action request)
        {
            request();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
            Assert.IsNotNull(ScenarioDirector.Instance, "director in the Battlefield scene");
            if (TacevacController.Instance != null) TacevacController.Instance.approachSeconds = 1.5f;
        }

        ScenarioDirector D => ScenarioDirector.Instance;
        CasualtyController C => D.casualty;
        Vector3 Chest => C.rig.ChestBone.position;
        Vector3 Hips => C.rig.Bone(HumanBodyBones.Hips).position;

        /// <summary>A test-controlled head (the director and the trainer read it for proximity).</summary>
        void TakeHead(Vector3 at)
        {
            if (_head == null) _head = new GameObject("~TestHead").transform;
            _head.position = at;
            D.head = _head;
        }

        Vector3 HeadAt(float dist)
        {
            Vector3 dir = D.playerRig.position - Hips; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
            return new Vector3(Hips.x, 0f, Hips.z) + dir.normalized * dist + Vector3.up * (C.transform.position.y + 1.55f);
        }

        void MakeBot()
        {
            var hands = HandGrabber.All.Where(h => h != null).ToList();
            var l = hands.FirstOrDefault(h => h.name.Contains("_L") || h.name.Contains("Left")) ?? hands[0];
            var r = hands.FirstOrDefault(h => h != l) ?? hands[1];
            var go = new GameObject("~Bot");
            _bot = go.AddComponent<MedicBot>();
            _bot.Init(l, r);
        }

        IEnumerator HandsTo(Vector3 near)
        {
            yield return _bot.Move(_bot.left, near + new Vector3(0.35f, 0.45f, -0.12f), false, 6f);
            yield return _bot.Move(_bot.right, near + new Vector3(0.35f, 0.45f, 0.12f), false, 6f);
        }

        IfakPouch Pouch => Object.FindObjectsByType<IfakPouch>(FindObjectsSortMode.None).First(p => C.Interaction == null || p != C.Interaction.Ifak);

        /// <summary>The player's kit next to Erlan (the body-locked pouch would follow the camera, not the test head).</summary>
        void KitNear()
        {
            var p = Pouch;
            p.bodyLocked = false;
            p.transform.position = Hips + new Vector3(0.7f, 0.1f, 0.3f);
            p.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        // ================================================================== hint panel
        [UnityTest]
        public IEnumerator HintPanel_AppearsOnlyWithin2m_Billboards_KeepsWoundVisible()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            var t = TrainingDirector.Instance;
            Assert.IsNotNull(t, "training director");
            Assert.AreEqual(BattleMode.Training, BattleDirector.Mode);
            Assert.AreEqual(false, BattleDirector.PlayerArmed, "nobody shoots at the trainee");
            yield return Seconds(1.5f); // he is down
            TakeHead(HeadAt(3.4f));
            yield return Seconds(0.8f);
            Assert.IsFalse(t.Panel.Shown, "3.4 m away: no card");
            Assert.Less(t.Panel.Visibility, 0.02f);

            TakeHead(HeadAt(1.5f));
            yield return Seconds(0.15f);
            Assert.IsTrue(t.Panel.Shown);
            float mid = t.Panel.Visibility;
            yield return Seconds(0.7f);
            Assert.Greater(t.Panel.Visibility, 0.97f, "faded in");
            Assert.Less(mid, 0.99f, "animated, not popped");
            var panel = t.Panel.transform;
            Assert.Greater(panel.position.y, Chest.y + 0.5f, "above Erlan");
            Assert.Less(Vector3.Distance(new Vector3(panel.position.x, 0f, panel.position.z), new Vector3(Chest.x, 0f, Chest.z)), 0.6f, "over him");
            Vector3 face = panel.position - _head.position; face.y = 0f;
            Vector3 fwd = panel.forward; fwd.y = 0f;
            Assert.Greater(Vector3.Dot(face.normalized, fwd.normalized), 0.95f, "billboards to the player");
            Assert.That(t.Panel.Counter, Does.Contain("Шаг 1 из " + t.Lesson.steps.Count));
            Assert.AreEqual(t.Lesson.steps[0].ru, t.Panel.Title);
            // the line of sight to the wound passes under the card
            var site = C.WoundSites.First(s => s != null);
            Vector3 eye = _head.position, w = site.transform.position;
            var plane = new Plane(panel.forward, panel.position);
            var ray = new Ray(eye, (w - eye).normalized);
            if (plane.Raycast(ray, out float enter) && enter < Vector3.Distance(eye, w))
            {
                Vector3 local = panel.InverseTransformPoint(ray.GetPoint(enter));
                Assert.That(Mathf.Abs(local.x) > HintPanel.Width / 2f || Mathf.Abs(local.y) > t.Panel.Height / 2f, Is.True, "card hides the wound: " + local);
            }

            TakeHead(HeadAt(3.2f));
            yield return Seconds(0.6f);
            Assert.IsFalse(t.Panel.Shown, "walked away: hidden");
            Assert.Less(t.Panel.Visibility, 0.05f);
        }

        [UnityTest]
        public IEnumerator HintPanel_StepDone_GreenCheck_ThenAdvances()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            var t = TrainingDirector.Instance;
            yield return Seconds(1.5f);
            TakeHead(HeadAt(1.4f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            yield return Seconds(0.5f);
            Assert.AreEqual("tq_grab", t.Progress.Step.id);
            var cat = Pouch.Find<TourniquetCAT>();
            yield return _bot.Grab(_bot.right, cat);
            float t0 = Time.time;
            while (t.Panel.CurrentMood != HintPanel.Mood.Done && Time.time - t0 < 2f) yield return null;
            Assert.AreEqual(HintPanel.Mood.Done, t.Panel.CurrentMood, "green check when the CAT is in hand");
            Assert.IsTrue(t.Panel.Checks.All(c => c), "all boxes ticked");
            Assert.AreEqual("tq_grab", t.Progress.Step.id, "holds the check for a moment");
            yield return Seconds(t.doneHold + 0.3f);
            Assert.AreEqual("tq_high", t.Progress.Step.id, "auto-advanced");
            Assert.AreEqual(HintPanel.Mood.Normal, t.Panel.CurrentMood);
            Assert.That(t.Panel.Counter, Does.Contain("Шаг 2"));
            Assert.AreEqual(t.Lesson.steps[1].ru, t.Panel.Title);
        }

        [UnityTest]
        public IEnumerator HintPanel_WrongAction_TurnsYellow_WithReason()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            var t = TrainingDirector.Instance;
            yield return Seconds(1.5f);
            TakeHead(HeadAt(1.4f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            var cat = Pouch.Find<TourniquetCAT>();
            // band on the knee: does not compress the artery
            yield return _bot.ApplyTourniquet(cat, C, Limb.RightLeg, 1.0f, 4, null);
            yield return Seconds(0.4f);
            Assert.AreEqual(HintPanel.Mood.Wrong, t.Panel.CurrentMood, "yellow card; step " + t.Progress.Step?.id + " " + _bot.lastStep);
            Assert.AreEqual(TrainingCatalog.Reason("tq_on_joint", false), t.Panel.Reason);
            Assert.AreEqual("tq_high", t.Progress.Step.id, "does not advance past a wrong placement");
            Assert.GreaterOrEqual(t.WrongCount, 1);
        }

        [UnityTest]
        public IEnumerator HintPanel_TourniquetBelowWound_ExplainsWhy()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            var t = TrainingDirector.Instance;
            yield return Seconds(1.5f);
            TakeHead(HeadAt(1.4f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            yield return _bot.ApplyTourniquet(Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 0.72f, 4, null);
            yield return Seconds(0.4f);
            Assert.AreEqual(HintPanel.Mood.Wrong, t.Panel.CurrentMood, _bot.lastStep);
            Assert.That(t.Panel.Reason, Does.Contain("ниже раны"));
        }

        // ================================================================== bot runs
        [UnityTest]
        public IEnumerator Training_Tourniquet_BotCompletesLesson()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            var t = TrainingDirector.Instance;
            yield return Seconds(1.5f);
            TakeHead(HeadAt(1.4f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            yield return _bot.ApplyTourniquet(Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 0.12f, 4, Pouch.Find<MarkerItem>());
            yield return _bot.CheckPulse(C, PulseSite.PedalR, 5.5f);
            float t0 = Time.time;
            while (!t.LessonComplete && Time.time - t0 < 20f) yield return null;
            Assert.IsTrue(t.LessonComplete, $"lesson complete; stuck at {t.Progress.Step?.id}, wrong={t.CurrentWrong}, bot={_bot.lastStep}");
            Assert.AreEqual(0, t.WrongCount, string.Join(" | ", t.WrongLog));
            Assert.AreEqual(Outcome.Ongoing, C.Patient.outcome);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Training_FullTccc_BotRunsCufTfcTacevac()
        {
            yield return Load(() => ScenarioLaunch.Training("full"));
            var t = TrainingDirector.Instance;
            Assert.AreEqual("full", t.Lesson.id);
            yield return Seconds(1.5f);
            Assert.AreEqual(Phase.CareUnderFire, C.Protocol.phase, "Erlan down in the open");
            Assert.Greater(Vector3.Distance(Hips, BattlefieldLayout.DoorwayCentre), 9f);
            // 1) reach him
            TakeHead(HeadAt(1.2f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            yield return WaitStep(t, "tq_lock");
            // 2) CAT high and tight under fire
            yield return _bot.ApplyTourniquet(Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 0.12f, 4, null);
            yield return WaitStep(t, "drag_cover");
            // 3) drag into house A
            var cover = Object.FindObjectsByType<CoverZone>(FindObjectsSortMode.None).OrderBy(z => Vector3.Distance(z.transform.position, BattlefieldLayout.TreatmentSpot)).First();
            yield return DragIntoHouseA();
            for (int k = 0; k < 60 && C.Protocol.phase == Phase.CareUnderFire; k++) yield return null;
            Assert.AreEqual(Phase.TacticalFieldCare, C.Protocol.phase, "in cover: hips " + Hips + " zone " + cover.transform.position);
            yield return Seconds(0.5f);
            TakeHead(HeadAt(1.2f));
            KitNear();
            yield return HandsTo(Hips);
            yield return WaitStep(t, "gloves");
            // 4) MARCH in cover
            yield return _bot.Gloves(Pouch.Find<GlovesItem>());
            yield return WaitStep(t, "pulse_foot");
            yield return _bot.CheckPulse(C, PulseSite.PedalR, 5.5f);
            yield return WaitStep(t, "tq_time");
            var cat = C.Interaction.Tourniquets.First();
            // like a person: look, and write again if the strap is still blank (he moves, the strap shifts)
            for (int k = 0; k < 3 && !cat.State.TimeMarked; k++)
            {
                if (_bot.right.Held != null) yield return _bot.Release(_bot.right);
                yield return _bot.Write(Pouch.Find<MarkerItem>(), cat.TimeLabelPoint);
            }
            yield return WaitStep(t, "sweep");
            yield return _bot.BloodSweep(C);
            yield return WaitStep(t, "bandage2");
            var forearm = C.Patient.wounds.First(w => w.region == BodyRegion.ForearmL);
            yield return HandsTo(Hips);
            // a dressing goes on skin, not on the sleeve: cut the sleeve open over the wound first (w9 lesson action)
            if (!forearm.exposed)
                yield return _bot.CutClothing(Pouch.Find<ShearsItem>(), C, Limb.LeftArm, Mathf.Min(2.25f, forearm.LimbPos + 0.5f), Mathf.Max(1.02f, forearm.LimbPos - 0.5f));
            Assert.IsTrue(forearm.exposed, "sleeve cut open over the forearm wound (" + _bot.lastStep + ")");
            yield return _bot.Bandage(Pouch.Find<BandageItem>(), C.Interaction.Care(forearm));
            if (forearm.control != BleedControl.PressureDressing)
            {
                Debug.Log("[FMVR] w4 full bot: forearm bandage by hand failed (" + _bot.lastStep + "), retry with his own");
                yield return _bot.Release(_bot.right);
                yield return HandsTo(Hips);
                var own = C.Interaction.Ifak != null ? C.Interaction.Ifak.Find<BandageItem>() : null;
                if (own != null) yield return _bot.Bandage(own, C.Interaction.Care(forearm));
            }
            yield return WaitStep(t, "blanket");
            yield return _bot.Blanket(Pouch.Find<BlanketItem>(), C);
            yield return WaitStep(t, "card");
            var card = Pouch.Find<CardItem>();
            yield return _bot.Write(Pouch.Find<MarkerItem>(), card.WritePoint);
            Assert.IsTrue(card.Filled, "card filled by the marker");
            yield return _bot.Grab(_bot.right, card);
            yield return _bot.MoveHeld(_bot.right, card.transform, card.transform.position, Chest + C.rig.BodyForward * 0.12f);
            yield return _bot.Release(_bot.right);
            yield return WaitStep(t, "medevac");
            // 5) TACEVAC
            C.Protocol.Perform(MedAction.MedevacRequest);
            yield return WaitStep(t, "loaded");
            var tac = TacevacController.Instance;
            Assert.IsNotNull(tac, "TACEVAC in the scene");
            float t0 = Time.time;
            while (!tac.Landed && Time.time - t0 < 10f) yield return null;
            Assert.IsTrue(tac.Landed, "helicopter landed at the LZ");
            Assert.Less(Vector3.Distance(tac.heli.position, BattlefieldLayout.LandingZone), 1.5f);
            // w9: no button — drag him to the side door (crew takes him in), then board
            var run = new DemoFlowRunner { bot = _bot };
            yield return run.DragToHelicopterAndBoard(tac);
            t0 = Time.time;
            while (!t.LessonComplete && Time.time - t0 < 6f) yield return null;
            Assert.IsTrue(t.LessonComplete, "full TCCC lesson complete; at " + t.Progress.Step?.id);
            Debug.Log($"[FMVR] w4 full TCCC bot: complete, wrong actions {t.WrongCount}: {string.Join(" | ", t.WrongLog)}");
        }

        /// <summary>Head-first through the east doorway (a straight line would run into the north wall).</summary>
        IEnumerator DragIntoHouseA()
        {
            var door = BattlefieldLayout.DoorwayCentre;
            Vector3 h0 = Hips;
            float t0 = Time.time;
            // w7: the body no longer slides through the concrete barriers east of house A: pass west of their end
            if (Hips.z > door.z + 7f) yield return _bot.DragTo(C, new Vector3(door.x + 1.05f, 0f, door.z + 5.6f), 60f);
            yield return _bot.DragTo(C, new Vector3(door.x + 1.3f, 0f, door.z), 150f);
            Debug.Log($"[FMVR] w4 drag leg 1: {Vector3.Distance(h0, Hips):F1} m in {Time.time - t0:F1} s, hips {Hips:F2}");
            yield return _bot.DragTo(C, new Vector3(-1.0f, 0f, door.z + 0.1f), 60f);
            Debug.Log($"[FMVR] w4 drag done: hips {Hips:F2} after {Time.time - t0:F1} s, distance dragged {C.Interaction.Drag.DraggedDistance:F1} m");
        }

        IEnumerator WaitStep(TrainingDirector t, string id, float timeout = 8f)
        {
            float t0 = Time.time;
            while (t.Progress.Step != null && t.Progress.Step.id != id && Time.time - t0 < timeout) yield return null;
            Assert.AreEqual(id, t.Progress.Step?.id, $"waiting for step {id}; wrong={t.CurrentWrong}; bot={(_bot != null ? _bot.lastStep : "-")}");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Combat_S1_Bot_UnderFire_Drag_Tacevac_Debrief()
        {
            yield return Load(() => ScenarioLaunch.Scenario("S1", ScenarioVariant.Default));
            var d = D;
            Assert.IsNotNull(d.Run, "scenario running");
            Assert.AreEqual(BattleMode.Combat, BattleDirector.Mode);
            d.testMode = true;                 // remaining incident waits are instant
            BattleDirector.PlayerArmed = false; // the test head is not a player; enemy fire is covered by BattleTests
            Assert.IsTrue(d.Timeline.waitForLoading, "V2: the run ends with the helicopter loading");
            float t0 = Time.time;
            while (d.State != ScenarioState.CareUnderFire && Time.time - t0 < 25f) yield return null;
            Assert.AreEqual(ScenarioState.CareUnderFire, d.State);
            while (C.IsFalling && Time.time - t0 < 30f) yield return null;
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(Hips, BattlefieldLayout.DoorwayCentre), 9f, "down in the open");
            TakeHead(HeadAt(1.2f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            var cat = C.Interaction.Ifak != null ? C.Interaction.Ifak.Find<TourniquetCAT>() : Pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, C, Limb.RightLeg, 0.12f, 4, null);
            Assert.IsTrue(C.Patient.wounds.First(w => w.type == WoundType.ArterialLimb).IsControlled, _bot.lastStep);
            yield return DragIntoHouseA();
            t0 = Time.time;
            while (d.State == ScenarioState.CareUnderFire && Time.time - t0 < 3f) yield return null;
            Assert.AreEqual(ScenarioState.TacticalFieldCare, d.State, "in the ruins");
            yield return Seconds(0.5f);
            TakeHead(HeadAt(1.2f));
            KitNear();
            yield return HandsTo(Hips);
            yield return _bot.Gloves(Pouch.Find<GlovesItem>());
            yield return _bot.Write(Pouch.Find<MarkerItem>(), C.Interaction.Tourniquets.First().TimeLabelPoint);
            yield return _bot.BloodSweep(C);
            yield return _bot.Blanket(Pouch.Find<BlanketItem>(), C);
            C.Protocol.Perform(MedAction.MedevacRequest);
            d.Timeline.evacTimeScale = 900f;
            t0 = Time.time;
            while (!d.Timeline.EvacArrived && Time.time - t0 < 20f) yield return null;
            Assert.IsTrue(d.Timeline.EvacArrived, "evacuation wait over; state " + d.State);
            var tac = TacevacController.Instance;
            t0 = Time.time;
            while (!tac.Landed && Time.time - t0 < 6f) yield return null;
            Assert.IsTrue(tac.Landed);
            Assert.AreEqual(ScenarioState.Evacuation, d.State, "waits for the loading");
            Assert.IsFalse(tac.stretcher != null && tac.stretcher.gameObject.activeInHierarchy, "w9: no litter cart");
            Assert.IsNull(Object.FindObjectsByType<RoundButton>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "LoadButton"), "w9: no load button");
            // w9: the medic drags Erlan to the helicopter's side door himself, the crew takes him in, then he boards
            yield return new DemoFlowRunner { bot = _bot }.DragToHelicopterAndBoard(tac);
            Assert.AreEqual(tac.heli, C.transform.parent, "Erlan flew off in the cabin");
            t0 = Time.time;
            while (d.Report == null && Time.time - t0 < 5f) yield return null;
            var r = d.Report;
            Assert.IsNotNull(r, "debrief");
            Assert.IsTrue(r.evacLoaded, "debrief: dragged to the helicopter = the evac step");
            Assert.IsTrue(r.timeline.Any(e => e.text.Contains("вертол")), "timeline shows the hand-over at the helicopter");
            Debug.Log($"[FMVR] w4 combat S1 bot: survived={r.survived} score={r.score} end={r.endReason} mistakes=[{string.Join(",", r.mistakes.Select(m => m.code + (m.hasPos ? "@" + m.x.ToString("0") + "," + m.z.ToString("0") : "")))}] phases=[{string.Join(",", r.phases.Select(p => p.id))}]");
            Assert.AreEqual("evacuated", r.endReason);
            Assert.IsTrue(r.survived);
            Assert.GreaterOrEqual(r.score, 70, "clean run");
            CollectionAssert.AreEqual(new[] { "CUF", "TFC", "TACEVAC" }, r.phases.Select(p => p.id).ToArray());
            Assert.IsTrue(r.hasCasualtyPos);
            Assert.IsTrue(r.mistakes.All(m => m.number > 0));
            Assert.AreEqual("debrief", d.board.Page);
            Assert.IsTrue(d.board.gameObject.activeInHierarchy, "board back for the debrief");
            Assert.IsNotNull(d.board.transform.Find("Content/Map"), "mini map on the debrief");
            Assert.IsNotNull(d.board.transform.Find("Content/Strip"), "timeline strip on the debrief");
        }

        [UnityTest]
        public IEnumerator Exam_Launches_FullBattle_NoHints()
        {
            yield return Load(() => ScenarioLaunch.Exam(12345));
            var d = D;
            Assert.IsNotNull(d.Run);
            Assert.IsTrue(d.Exam);
            Assert.AreEqual(BattleMode.Exam, BattleDirector.Mode);
            Assert.IsFalse(GameSettings.Hints, "no hints in the exam");
            Assert.IsNull(TrainingDirector.Instance, "no hint panel");
            GameSettings.Hints = true;
        }

        // ================================================================== menu + wrist
        [UnityTest]
        public IEnumerator Menu_ModesAndTopics_LaunchTheRightRuns()
        {
            yield return Load(() => ScenarioLaunch.Menu());
            var board = D.board;
            Assert.IsTrue(D.InMenu);
            string launched = null;
            board.launchOverride = w => launched = w;
            board.ShowMain();
            RoundButton B(string label) => board.GetComponentsInChildren<RoundButton>().First(b => b.name == "Btn_" + label);
            B("1").Click();
            yield return null; // the old page's buttons are destroyed at the end of the frame
            Assert.AreEqual("training", board.Page);
            var labels = board.GetComponentsInChildren<RoundButton>().Select(b => b.name).ToList();
            Assert.Contains("Btn_TCCC", labels);
            B("TCCC").Click();
            Assert.AreEqual("lesson:full", launched);
            foreach (var (label, lesson) in new[] { ("1", "tq"), ("2", "pack"), ("3", "drag"), ("4", "chest"), ("6", "selftq") })
            {
                B(label).Click();
                Assert.AreEqual("lesson:" + lesson, launched, label);
            }
            board.ShowMain();
            yield return null;
            B("2").Click();
            yield return null;
            Assert.AreEqual("scenarios", board.Page, "combat → scenario list");
            B("9").Click(); // w9b: the self-aid drill is the 9th card
            yield return null;
            Assert.AreEqual("briefing", board.Page);
            B("ПУСК").Click();
            Assert.AreEqual("scenario:SA", launched);
            board.ShowMain();
            yield return null;
            B("3").Click();
            Assert.AreEqual("exam", launched);
            // KZ toggle relabels
            board.ShowMain();
            yield return null;
            B("KZ").Click();
            Assert.IsTrue(GameSettings.KZ);
            GameSettings.Language = GameLanguage.RU;
        }

        [UnityTest]
        public IEnumerator WristDisplay_ShowsPulse_AndGoldenHour()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            yield return Seconds(2f);
            var w = Object.FindAnyObjectByType<WristWatch>();
            Assert.IsNotNull(w, "wrist watch on the left hand");
            Assert.That(w.PulseText, Does.Match(@"пульс \d+"));
            Assert.That(w.GoldenText, Does.Match(@"^5\d:\d\d$|^60:00$"));
        }
    }
}
