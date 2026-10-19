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
    /// The w7 presentation flow, driven only by simulated hands (<see cref="ScriptedHandInput"/> via
    /// <see cref="MedicBot"/>): ДЕМО → Training with the white hint panel, Erlan bleeding from the thigh in the open →
    /// rip the IFAK lid open by its tab → CAT high and tight → drag him by the plate-carrier handle into the ruins →
    /// time on the CAT → pack the second (left thigh) wound → chest seal → blanket → card → radio by fingertip →
    /// helicopter lands → (w9) drag him to its side door, the crew takes him in, board, fly off → debrief. Shared by <see cref="DemoFlowTests"/> and the video capture.
    /// </summary>
    public sealed class DemoFlowRunner
    {
        public MedicBot bot;
        public Transform head;
        public Action<string> moment;   // key moments (screenshots / video markers)
        public bool headFollowsCamera;  // capture: the hint panel billboards to the video camera
        public string stuck;

        public ScenarioDirector D => ScenarioDirector.Instance;
        public CasualtyController C => D.casualty;
        public Vector3 Chest => C.rig.ChestBone.position;
        public Vector3 Hips => C.rig.Bone(HumanBodyBones.Hips).position;
        public IfakPouch Pouch => Object.FindObjectsByType<IfakPouch>(FindObjectsSortMode.None).First(p => C.Interaction == null || p != C.Interaction.Ifak);

        public static IEnumerator Seconds(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        public static IEnumerator Load(Action request)
        {
            request();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            if (TacevacController.Instance != null) TacevacController.Instance.approachSeconds = 6f;
        }

        public void TakeHead(Vector3 at)
        {
            if (head == null) head = new GameObject("~TestHead").transform;
            if (!headFollowsCamera) head.position = at;
            D.head = head;
        }

        public Vector3 HeadAt(float dist)
        {
            Vector3 dir = D.playerRig.position - Hips; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
            return new Vector3(Hips.x, 0f, Hips.z) + dir.normalized * dist + Vector3.up * (C.transform.position.y + 1.55f);
        }

        public void MakeBot()
        {
            var hands = HandGrabber.All.Where(h => h != null).ToList();
            var l = hands.FirstOrDefault(h => h.name.Contains("_L") || h.name.Contains("Left")) ?? hands[0];
            var r = hands.FirstOrDefault(h => h != l) ?? hands[1];
            bot = new GameObject("~Bot").AddComponent<MedicBot>();
            bot.Init(l, r);
            bot.handSpeed = 0.9f;
        }

        public IEnumerator HandsTo(Vector3 near)
        {
            yield return bot.Move(bot.left, near + new Vector3(0.35f, 0.45f, -0.12f), false, 3f);
            yield return bot.Move(bot.right, near + new Vector3(0.35f, 0.45f, 0.12f), false, 3f);
        }

        /// <summary>The player's belt kit next to Erlan (body-locked, it would follow the camera, not the test head).</summary>
        public void KitNear()
        {
            var p = Pouch;
            p.bodyLocked = false;
            p.transform.position = Hips + new Vector3(0.7f, 0.1f, 0.3f);
            p.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        public IEnumerator WaitStep(TrainingDirector t, string id, float timeout = 10f)
        {
            float t0 = Time.time;
            while (t.Progress.Step != null && t.Progress.Step.id != id && Time.time - t0 < timeout) yield return null;
            if (t.Progress.Step?.id != id) stuck = $"waiting for step {id}, at {t.Progress.Step?.id}; wrong={t.CurrentWrong}; bot={bot?.lastStep}";
            Assert.AreEqual(id, t.Progress.Step?.id, stuck);
        }

        /// <summary>
        /// Drag by the plate-carrier handle toward `dest` like a person would: pull straight; when the body is stopped
        /// by an obstacle, pull along it (toward the side nearer the goal) for a moment, then straight again.
        /// </summary>
        public IEnumerator DragVia(Vector3 dest, float timeout, Func<bool> until = null)
        {
            var h = bot.right;
            var drag = C.Interaction.Drag;
            bot.lastStep = "drag";
            yield return bot.Grab(h, drag, drag.transform.position);
            float t = 0f, blockedFor = 0f, detourUntil = -1f;
            Vector3 detour = Vector3.zero;
            while (t < timeout)
            {
                Vector3 to = dest - Hips; to.y = 0f;
                if (to.magnitude < 0.25f || (until != null && until())) break;
                Vector3 dir = to.normalized;
                blockedFor = drag.BlockedByWall ? blockedFor + Time.deltaTime : 0f;
                if (blockedFor > 0.25f && t > detourUntil)
                {
                    Vector3 tan = Vector3.Cross(Vector3.up, dir);
                    bool leftFree = !drag.Obstructed(tan * 0.4f), rightFree = !drag.Obstructed(-tan * 0.4f);
                    float prefer = Vector3.Dot(tan, BattlefieldLayout.DoorwayCentre - Hips);
                    detour = (leftFree && (!rightFree || prefer >= 0f)) ? tan : -tan;
                    detourUntil = t + 1.4f;
                    blockedFor = 0f;
                }
                if (t < detourUntil) dir = (detour * 0.85f + dir * 0.15f).normalized;
                Vector3 handle = drag.transform.position;
                bot.SetPalm(h, new Vector3(handle.x, handle.y + 0.05f, handle.z) + dir * 0.16f);
                t += Time.deltaTime;
                yield return null;
            }
            yield return bot.Release(h);
        }

        /// <summary>
        /// w9 TACEVAC by hand: out of house A through the east doorway, south along its east wall, then head-first to the
        /// helicopter's side door until the crew takes him in; then the player steps into the door zone and boards.
        /// </summary>
        public IEnumerator DragToHelicopterAndBoard(TacevacController tac, float timeout = 240f)
        {
            var door = BattlefieldLayout.DoorwayCentre;
            float t0 = Time.time;
            Func<bool> taken = () => tac.State >= TacevacController.Stage.TakingAboard;
            Debug.Log($"[FMVR] evac drag: from {Hips:F2}, heli side door {tac.DoorPoint:F2}");
            if (Hips.x < door.x + 0.5f) yield return DragVia(new Vector3(door.x + 1.5f, 0f, door.z), 60f, taken);
            if (!taken() && Hips.z > BattlefieldLayout.HouseAMin.y - 1f)
                yield return DragVia(new Vector3(door.x + 1.7f, 0f, BattlefieldLayout.HouseAMin.y - 2.2f), 60f, taken);
            Vector3 hd = tac.DoorPoint;
            if (!taken()) yield return DragVia(new Vector3(hd.x, 0f, hd.z), Mathf.Max(30f, timeout - (Time.time - t0)), taken);
            float t1 = Time.time;
            while (!tac.CasualtyAboard && Time.time - t1 < 8f) yield return null;
            Debug.Log($"[FMVR] evac drag: aboard={tac.CasualtyAboard} after {Time.time - t0:F1} s, dragged {C.Interaction.Drag.DraggedDistance:F1} m, chest→door {tac.CasualtyDoorDistance():F2} m");
            Assert.IsTrue(tac.CasualtyAboard, $"crew took Erlan in at the door (chest {tac.CasualtyDoorDistance():F2} m from the door, state {tac.State})");
            moment?.Invoke("aboard");
            // the player walks into the door zone → fade, seated, door closes, lift-off, fade out
            TakeHead(tac.DoorPoint + Vector3.up * 1.6f);
            t1 = Time.time;
            while (!tac.PlayerAboard && Time.time - t1 < 6f) yield return null;
            Assert.IsTrue(tac.PlayerAboard, "player boarded by stepping into the door zone");
            moment?.Invoke("seated");
            while (!tac.Loaded && Time.time - t1 < 30f) yield return null;
            Assert.IsTrue(tac.Loaded, "helicopter flew off, evacuation complete; state " + tac.State);
        }

        IEnumerator DragIntoHouseA()
        {
            var door = BattlefieldLayout.DoorwayCentre;
            Debug.Log($"[FMVR] demo flow: drag from {Hips:F2}, doorway {door:F2}");
            float t0 = Time.time;
            // the concrete barriers east of house A end right on the straight line: pass west of their end
            if (Hips.z > door.z + 7f) yield return DragVia(new Vector3(door.x + 1.05f, 0f, door.z + 5.6f), 60f);
            yield return DragVia(new Vector3(door.x + 1.3f, 0f, door.z), 150f);
            Debug.Log($"[FMVR] demo flow: at the door after {Time.time - t0:F1} s, hips {Hips:F2}");
            yield return bot.DragTo(C, new Vector3(-1.0f, 0f, door.z + 0.1f), 60f);
        }

        public IEnumerator Run()
        {
            var t = TrainingDirector.Instance;
            Assert.IsNotNull(t, "training director");
            Assert.AreEqual(TrainingCatalog.DemoId, t.Lesson.id);
            yield return Seconds(1.5f);
            Assert.AreEqual(Phase.CareUnderFire, C.Protocol.phase, "Erlan down in the open");
            Assert.Greater(Vector3.Distance(Hips, BattlefieldLayout.DoorwayCentre), 9f, "Erlan is out in the open");
            moment?.Invoke("open");

            // 1) reach him; the white card appears
            TakeHead(HeadAt(1.3f));
            KitNear();
            MakeBot();
            yield return HandsTo(Hips);
            yield return WaitStep(t, "tq_lock");
            moment?.Invoke("hint");

            // 2) IFAK closed: touching the CAT in the closed pouch does nothing; pull the red tab → open
            var pouch = Pouch;
            Assert.IsFalse(pouch.IsOpen, "the IFAK starts closed");
            var cat = pouch.Find<TourniquetCAT>();
            Assert.IsTrue(cat.CanBeGrabbedBy(bot.right), "w9: the CAT rides in the always-open quick pouch");
            Assert.IsFalse(pouch.Find<GauzeItem>().CanBeGrabbedBy(bot.right), "no item can be taken out of the closed main pouch");
            yield return bot.OpenPouch(pouch, bot.right);
            Assert.IsTrue(pouch.IsOpen, "the lid ripped open by pulling the tab");
            moment?.Invoke("ifak");

            // 3) CAT high and tight on the right thigh: loop, strap, windlass, clip
            yield return bot.ApplyTourniquet(cat, C, Limb.RightLeg, 0.12f, 4, null);
            var wound = C.Patient.wounds.First(w => w.type == WoundType.ArterialLimb);
            Assert.IsTrue(wound.IsControlled, "arterial bleed stopped by the CAT");
            moment?.Invoke("tq");
            yield return WaitStep(t, "drag_cover");

            // 4) drag him head-first by the plate-carrier handle into house A
            Vector3 h0 = Hips;
            yield return DragIntoHouseA();
            for (int k = 0; k < 90 && C.Protocol.phase == Phase.CareUnderFire; k++) yield return null;
            Assert.AreEqual(Phase.TacticalFieldCare, C.Protocol.phase, "in cover: hips " + Hips);
            Assert.Greater(C.Interaction.Drag.DraggedDistance, 8f, "dragged over the ground (no teleport)");
            Debug.Log($"[FMVR] demo flow: dragged {C.Interaction.Drag.DraggedDistance:F1} m from {h0:F1} to {Hips:F1}");
            moment?.Invoke("drag");
            yield return Seconds(0.5f);
            TakeHead(HeadAt(1.3f));
            KitNear();
            yield return HandsTo(Hips);

            // 5) time on the CAT strap
            yield return WaitStep(t, "tq_time");
            var c1 = C.Interaction.Tourniquets.Count > 0 ? C.Interaction.Tourniquets.First() : cat;
            yield return bot.Write(Pouch.Find<MarkerItem>(), c1.TimeLabelPoint);
            Assert.IsTrue(c1.State.TimeMarked, "time written on the CAT");
            moment?.Invoke("time");

            // 6) pack the left thigh wound: touching once does not pack it, strokes do
            yield return WaitStep(t, "packed2");
            var w2 = C.Patient.wounds.First(w => w.region == BodyRegion.ThighL);
            var care2 = C.Interaction.Care(w2);
            Assert.IsFalse(w2.packed);
            yield return HandsTo(Hips);
            var gauze = Pouch.Find<GauzeItem>();
            yield return bot.Pack(gauze, care2);
            if (!w2.packed)
            {
                Debug.Log($"[FMVR] demo flow: packing 1st try failed: into={(gauze.PackingInto != null ? gauze.PackingInto.name : "none")} started={care2.Packing.started} fill={care2.Packing.Fill:F2} " +
                          $"skin={care2.SkinAccessible} canPack={care2.CanPack} held={gauze.IsHeld} L={bot.left.Held?.name} R={bot.right.Held?.name} lastHint={gauze.LastHint} site={care2.transform.position:F2} n={care2.Normal:F2}");
                yield return bot.Release(bot.left); yield return bot.Release(bot.right);
                yield return HandsTo(Hips);
                yield return bot.Pack(gauze, care2, bot.right);
            }
            Assert.IsTrue(w2.packed, $"wound packed by finger strokes (fill {care2.Packing.Fill:F2}, started {care2.Packing.started})");
            moment?.Invoke("pack");

            // 7) chest seal: open, peel, place, press the edges
            yield return WaitStep(t, "seal3");
            var w3 = C.Patient.wounds.First(w => w.type == WoundType.PenetratingChest);
            yield return HandsTo(Chest);
            yield return bot.ChestSeal(Pouch.Find<ChestSealItem>(), C.Interaction.Care(w3));
            Assert.AreEqual(BleedControl.OcclusiveSeal, w3.control, "chest wound sealed by hand");
            moment?.Invoke("seal");

            // 8) blanket, 9) card
            yield return WaitStep(t, "blanket");
            yield return bot.Blanket(Pouch.Find<BlanketItem>(), C);
            Assert.IsTrue(C.Patient.insulated, "blanket over him");
            moment?.Invoke("blanket");
            yield return WaitStep(t, "card");
            var card = Pouch.Find<CardItem>();
            yield return bot.Write(Pouch.Find<MarkerItem>(), card.WritePoint);
            Assert.IsTrue(card.Filled, "card filled by the marker");
            yield return bot.Grab(bot.right, card);
            yield return bot.MoveHeld(bot.right, card.transform, card.transform.position, Chest + C.rig.BodyForward * 0.12f);
            yield return bot.Release(bot.right);
            moment?.Invoke("card");

            // 10) MEDEVAC: four presses of the radio PTT with the fingertip
            yield return WaitStep(t, "medevac");
            var radio = RadioSet.Instance;
            Assert.IsNotNull(radio, "radio on the player");
            var ptt = radio.GetComponentsInChildren<RoundButton>(true).FirstOrDefault();
            Assert.IsNotNull(ptt, "radio PTT button");
            for (int k = 0; k < 6 && !C.Protocol.medevacRequested; k++) yield return bot.Poke(ptt, bot.left);
            Assert.IsTrue(C.Protocol.medevacRequested, "MEDEVAC called by pressing the radio");

            // 11) TACEVAC: the helicopter lands, «ПОГРУЗИТЬ» pressed by the fingertip
            yield return WaitStep(t, "loaded");
            var tac = TacevacController.Instance;
            Assert.IsNotNull(tac);
            moment?.Invoke("heli_call");
            float t0 = Time.time;
            while (!tac.Landed && Time.time - t0 < 40f) yield return null;
            Assert.IsTrue(tac.Landed, "helicopter landed at the LZ");
            moment?.Invoke("heli");
            Assert.IsFalse(tac.stretcher != null && tac.stretcher.gameObject.activeInHierarchy, "no litter cart (w9)");
            yield return DragToHelicopterAndBoard(tac);
            Assert.IsTrue(C.Protocol.log.Any(e => e.code == "evac_loaded"), "evac step logged: dragged to the helicopter");
            t0 = Time.time;
            while (!t.LessonComplete && Time.time - t0 < 8f) yield return null;
            Assert.IsTrue(t.LessonComplete, "demo complete; at " + t.Progress.Step?.id);

            // 12) debrief
            Assert.IsNotNull(t.Report, "demo ends with the debrief");
            Assert.IsTrue(t.Report.survived, "Erlan survives");
            Assert.IsTrue(t.Report.evacLoaded, "debrief counts the drag to the helicopter as the evac step");
            moment?.Invoke("debrief");
            Debug.Log($"[FMVR] demo flow complete: score {t.Report.score}, wrong actions {t.WrongCount}: {string.Join(" | ", t.WrongLog)}; bot fallback grabs {bot.fallbackGrabs}");
        }
    }

    public class DemoFlowTests
    {
        GlobalStateGuard _guard;
        DemoFlowRunner _run;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            _run = new DemoFlowRunner();
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
            if (_run.head != null) Object.Destroy(_run.head.gameObject);
        }

        IEnumerator LoadDemo()
        {
            LogAssert.ignoreFailingMessages = false;
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(TrainingCatalog.DemoId));
            AudioListener.volume = 0f;
            Assert.IsNotNull(ScenarioDirector.Instance, "director in the Battlefield scene");
            ScenarioDirector.Instance.testMode = true;
            if (TacevacController.Instance != null) TacevacController.Instance.approachSeconds = 1.5f;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Demo_FullFlow_ByHands_Drag_Treat_Tacevac_Debrief()
        {
            yield return LoadDemo();
            yield return _run.Run();
        }

        [UnityTest]
        public IEnumerator Demo_MenuButton_StartsTrainingDemo_WithWhitePanel()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Menu());
            AudioListener.volume = 0f;
            var board = Object.FindAnyObjectByType<MenuBoard>();
            Assert.IsNotNull(board, "menu board");
            board.ShowMain();
            yield return null;
            var demo = board.GetComponentsInChildren<RoundButton>(true).FirstOrDefault(b => b.name == "Btn_DEMO");
            Assert.IsNotNull(demo, "big ДЕМО button on the main page");
            Assert.Greater(demo.transform.lossyScale.x * 1f, 0f);
            string launched = null;
            board.launchOverride = w => launched = w;
            demo.Click();
            Assert.AreEqual("lesson:" + TrainingCatalog.DemoId, launched, "ДЕМО launches the demo lesson");
            board.launchOverride = null;
            Assert.IsNotNull(board.transform.Find("Content/Flag_KZ") ?? board.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Flag_KZ"), "KZ flag in the menu header");
            // the demo lesson itself: Training mode, white panel, nothing targets the player
            yield return LoadDemo();
            Assert.AreEqual(BattleMode.Training, BattleDirector.Mode);
            var t = TrainingDirector.Instance;
            Assert.AreEqual(TrainingCatalog.DemoId, t.Lesson.id);
            Assert.IsNotNull(t.Panel, "white hint panel");
            Assert.AreEqual(3, ScenarioDirector.Instance.casualty.Patient.wounds.Count, "thigh (CAT), left thigh (packing), chest (seal)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Drag_ByWrist_TwoHands_And_NotThroughWalls()
        {
            yield return LoadDemo();
            yield return DemoFlowRunner.Seconds(1.0f);
            var C = _run.C;
            _run.TakeHead(_run.HeadAt(1.3f));
            _run.MakeBot();
            var bot = _run.bot;
            var ci = C.Interaction;
            Assert.AreEqual(2, ci.Drag.strapHandles.Count, "both shoulder straps are grips");

            // wrist: the arm straightens, then the whole body follows the hand
            var wrist = ci.Handle(Limb.LeftArm, LimbHandle.Grip.Wrist);
            Vector3 hips0 = _run.Hips;
            yield return bot.Grab(bot.left, wrist);
            Assert.IsTrue(wrist.Held, "wrist grabbed");
            Vector3 away = (_run.Hips - BattlefieldLayout.DoorwayCentre); away.y = 0f; away = -away.normalized; // toward the ruins
            Vector3 handTarget = bot.left.PalmPosition;
            for (int k = 0; k < 4; k++)
            {
                handTarget += away * 0.45f;
                yield return bot.Move(bot.left, handTarget, false, 0.6f);
                yield return DemoFlowRunner.Seconds(0.4f);
            }
            float byWrist = Vector3.Distance(_run.Hips, hips0);
            Debug.Log($"[FMVR] demo drag by the wrist: {byWrist:F2} m");
            Assert.Greater(byWrist, 0.5f, "dragged by the wrist");
            yield return bot.Release(bot.left);

            // two hands (drag handle + shoulder strap) pull faster than one
            yield return bot.Grab(bot.right, ci.Drag);
            yield return bot.Grab(bot.left, ci.Drag.strapHandles[0]);
            Assert.AreEqual(ci.Drag, bot.right.Held);
            Assert.AreEqual(ci.Drag.strapHandles[0], bot.left.Held);
            Vector3 r0 = bot.right.PalmPosition, l0 = bot.left.PalmPosition;
            float maxSpeed = 0f;
            // hands walk backwards at 2.2 m/s (faster than one hand can drag him): the body accelerates behind them
            int pulling = 0;
            for (float tt = 0f; tt < 1.6f; tt += Time.deltaTime)
            {
                bot.SetPalm(bot.right, r0 + away * (2.2f * tt)); bot.SetPalm(bot.left, l0 + away * (2.2f * tt));
                yield return null;
                maxSpeed = Mathf.Max(maxSpeed, ci.Drag.Velocity.magnitude);
                pulling = Mathf.Max(pulling, ci.Drag.PullingHands);
            }
            Debug.Log($"[FMVR] demo two-hand drag: max speed {maxSpeed:F2} m/s, hands {pulling}");
            Assert.AreEqual(2, pulling, "two hands pulling");
            Assert.Greater(maxSpeed, ci.Drag.dragMaxSpeed * 1.05f, "two hands drag faster than the one-hand limit");
            yield return bot.Release(bot.right);
            yield return bot.Release(bot.left);

            // a wall: put a solid block across the path, pull hard into it → the body stops in front of it
            yield return DemoFlowRunner.Seconds(0.5f);
            var head = C.rig.Bone(HumanBodyBones.Head).position;
            Vector3 pullDir = (ci.Drag.transform.position - _run.Hips); pullDir.y = 0f; pullDir.Normalize();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "~TestWall";
            wall.transform.position = new Vector3(head.x, C.transform.position.y + 1f, head.z) + pullDir * 0.6f;
            wall.transform.rotation = Quaternion.LookRotation(pullDir);
            wall.transform.localScale = new Vector3(3f, 2f, 0.3f);
            Physics.SyncTransforms();
            yield return bot.Grab(bot.right, ci.Drag);
            Vector3 p0 = bot.right.PalmPosition;
            for (int k = 0; k < 120; k++)
            {
                bot.SetPalm(bot.right, p0 + pullDir * Mathf.Min(2.5f, 0.03f * k) + Vector3.up * 0.4f);
                yield return null;
            }
            Vector3 headNow = C.rig.Bone(HumanBodyBones.Head).position;
            float frontOfWall = Vector3.Dot(headNow - wall.transform.position, pullDir);
            Debug.Log($"[FMVR] demo drag into a wall: head {frontOfWall:F2} m from the wall centre plane, blocked {ci.Drag.BlockedByWall}");
            Assert.Less(frontOfWall, -0.15f, "head stays in front of the wall (no sliding through)");
            yield return bot.Release(bot.right);
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator Items_DoNotAutoComplete_ByTouching()
        {
            yield return LoadDemo();
            yield return DemoFlowRunner.Seconds(1.0f);
            var C = _run.C;
            _run.TakeHead(_run.HeadAt(1.3f));
            _run.KitNear();
            _run.MakeBot();
            var bot = _run.bot;
            var pouch = _run.Pouch;
            // touching the tab is not opening it
            yield return bot.Grab(bot.right, pouch.Flap.Tab);
            yield return bot.Release(bot.right);
            Assert.IsFalse(pouch.IsOpen, "grab+release of the tab without a pull keeps the lid closed");
            yield return bot.OpenPouch(pouch, bot.right);
            Assert.IsTrue(pouch.IsOpen);
            // the CAT laid on the thigh and let go is not a tourniquet
            var cat = pouch.Find<TourniquetCAT>();
            yield return bot.Grab(bot.right, cat);
            yield return bot.Move(bot.right, LimbMath.PointAt(C.rig, Limb.RightLeg, 0.15f) + Vector3.up * 0.08f);
            yield return bot.Release(bot.right);
            yield return DemoFlowRunner.Seconds(0.5f);
            var art = C.Patient.wounds.First(w => w.type == WoundType.ArterialLimb);
            Assert.IsFalse(art.IsControlled, "touching the leg with the CAT does not stop the bleed");
            // the chest seal pressed on unopened: no seal
            var seal = pouch.Find<ChestSealItem>();
            var w3 = C.Patient.wounds.First(w => w.type == WoundType.PenetratingChest);
            var care3 = C.Interaction.Care(w3);
            yield return bot.Grab(bot.left, seal);
            yield return bot.Move(bot.left, care3.transform.position + C.rig.BodyForward * 0.03f);
            yield return DemoFlowRunner.Seconds(0.4f);
            yield return bot.Release(bot.left);
            Assert.AreNotEqual(BleedControl.OcclusiveSeal, w3.control, "an unopened seal touched to the wound does not seal it");
            // the gauze touched once to the wound: not packed
            var w2 = C.Patient.wounds.First(w => w.region == BodyRegion.ThighL);
            var g = pouch.Find<GauzeItem>();
            yield return bot.Grab(bot.right, g);
            yield return bot.Move(bot.right, C.Interaction.Care(w2).transform.position + Vector3.up * 0.02f);
            yield return DemoFlowRunner.Seconds(0.3f);
            yield return bot.Release(bot.right);
            Assert.IsFalse(w2.packed, "one touch of the gauze is not packing");
        }
    }
}

namespace FieldMedic.Tests
{
    public class DemoPouchDiag
    {
        [UnityTest]
        public IEnumerator Pouch_Items_Visible_After_Opening()
        {
            var run = new DemoFlowRunner();
            // the headset path: menu first, then ДЕМО reloads the scene
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Menu());
            yield return DemoFlowRunner.Seconds(0.5f);
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(TrainingCatalog.DemoId));
            AudioListener.volume = 0f;
            yield return DemoFlowRunner.Seconds(1f);
            run.TakeHead(run.HeadAt(1.3f));
            run.KitNear();
            run.MakeBot();
            yield return run.HandsTo(run.Hips);
            var pouch = run.Pouch;
            yield return run.bot.OpenPouch(pouch, run.bot.right);
            yield return DemoFlowRunner.Seconds(0.5f);
            int visible = 0;
            foreach (var it in pouch.Items)
            {
                var rs = it.GetComponentsInChildren<Renderer>(true);
                int on = rs.Count(r => r.enabled && r.gameObject.activeInHierarchy);
                var b = rs.Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.bounds).FirstOrDefault();
                Debug.Log($"[FMVR] pouch diag {it.name}: active={it.gameObject.activeInHierarchy} inPouch={it.InPouch} local={pouch.transform.InverseTransformPoint(it.transform.position):F3} renderers {on}/{rs.Length} bounds {b.center:F2} {b.size:F3} parent={it.transform.parent?.name}");
                if (on > 0) visible++;
            }
            var pt = pouch.transform;
            foreach (Transform ch in pt) Debug.Log($"[FMVR] pouch child {ch.name} active={ch.gameObject.activeInHierarchy} local={ch.localPosition:F3} rot={ch.localEulerAngles:F0}");
            var vis = pt.Find("PouchVisual");
            if (vis != null) foreach (var r in vis.GetComponentsInChildren<Renderer>()) Debug.Log($"[FMVR] pouch visual {r.name} bounds {r.bounds.center:F3} {r.bounds.size:F3}");
            int q0 = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true); // the Quest pipeline asset (as DemoCapture / the headset)
            yield return null;
            var cam = new GameObject("~DiagCam").AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = 50f; cam.nearClipPlane = 0.02f;
            cam.transform.SetPositionAndRotation(pt.position + new Vector3(0f, 0.55f, -0.3f), Quaternion.LookRotation(pt.position - (pt.position + new Vector3(0f, 0.55f, -0.3f))));
            var rt = new RenderTexture(1280, 720, 24);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt });
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt });
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "w7"));
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "w7", "pouch_diag.jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(cam.gameObject);
            QualitySettings.SetQualityLevel(q0, true);
            Assert.Greater(visible, pouch.Items.Count / 2, "items visible in the opened pouch");
        }
    }
}
