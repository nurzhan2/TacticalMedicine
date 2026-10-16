using System.Collections;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 hand feel: every item and handle has a pose and the glove fingers take it; physical hands — the visual glove
    /// stops at walls and at Erlan's body (tracked hand 15 cm inside → the glove stays outside, ≤ 1 cm), with a ghost
    /// outline at the tracked position; heavy things sag the hand; per-grip drag speed.
    /// </summary>
    public class HandPoseTests : W8SceneTest
    {
        GloveHand Glove(HandGrabber h) => h.GetComponentInChildren<GloveHand>();
        HandPhysicalVisual Phys(HandGrabber h) => h.GetComponentInChildren<HandPhysicalVisual>();

        [UnityTest]
        public IEnumerator Every_Item_And_Handle_Has_A_Pose_And_The_Glove_Takes_It()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            foreach (var it in run.Pouch.Items)
                Assert.IsNotNull(it.Pose, "pose for " + it.kind);
            var ci = C.Interaction;
            Assert.AreEqual("drag_handle", ci.Drag.Pose.key);
            Assert.AreEqual("drag_handle", ci.Drag.strapHandles[0].Pose.key);
            Assert.AreEqual("wrist", ci.Handle(Limb.LeftArm, LimbHandle.Grip.Wrist).Pose.key);
            Assert.AreEqual("ankle", ci.Handle(Limb.LeftLeg, LimbHandle.Grip.Ankle).Pose.key);
            var cat = run.Pouch.Find<TourniquetCAT>();
            Assert.AreEqual("cat", cat.Pose.key);

            var bot = run.bot;
            var glove = Glove(bot.right);
            Assert.IsNotNull(glove, "player glove");
            yield return bot.Grab(bot.right, cat);
            yield return DemoFlowRunner.Seconds(0.5f);
            Assert.AreEqual("cat", glove.ActivePose != null ? glove.ActivePose.key : null, "glove wraps the CAT");
            Assert.That(glove.Curl(1), Is.EqualTo(cat.Pose.middle).Within(0.12f));
            yield return bot.Release(bot.right);
            yield return DemoFlowRunner.Seconds(0.4f);
            yield return bot.Grab(bot.right, ci.Drag, ci.Drag.transform.position);
            yield return DemoFlowRunner.Seconds(0.5f);
            Assert.AreEqual("drag_handle", glove.ActivePose.key);
            Assert.Greater(glove.Curl(2), 0.85f, "full fist on the drag strap");
            Assert.Greater(Phys(bot.right).Sag, 0.005f, "his weight pulls the hand down");
            yield return bot.Release(bot.right);
            yield return DemoFlowRunner.Seconds(0.4f);
            Assert.IsNull(glove.ActivePose);
        }

        [UnityTest]
        public IEnumerator Hands_DoNotPenetrate_Walls_Or_Erlan_Ghost_Shows_Tracked()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            var bot = run.bot;
            var h = bot.right;
            var phys = Phys(h);
            Assert.IsNotNull(phys, "physical hand on the player glove");

            // a wall in front of the hand
            Vector3 start = h.PalmPosition + Vector3.up * 0.4f + Vector3.right * 0.3f;
            yield return bot.Move(h, start, false, 2f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "~TestWall";
            wall.transform.position = start + Vector3.forward * 0.3f;
            wall.transform.localScale = new Vector3(1f, 1f, 0.2f);
            Physics.SyncTransforms();
            float surfaceZ = wall.transform.position.z - 0.1f;
            yield return bot.Move(h, new Vector3(start.x, start.y, surfaceZ + 0.15f), false, 0.6f); // tracked 15 cm inside
            yield return DemoFlowRunner.Seconds(0.2f);
            float trackedIn = phys.TrackedPalm.z - surfaceZ, visualIn = phys.VisualPalm.z + phys.palmRadius - surfaceZ;
            Debug.Log($"[FMVR] w8 test wall: tracked {trackedIn * 100f:F1} cm inside, visual sphere {visualIn * 100f:F1} cm, ghost {phys.GhostVisible}, blocker {(phys.LastBlocker != null ? phys.LastBlocker.name : "-")}");
            Assert.Greater(trackedIn, 0.12f);
            Assert.Less(visualIn, 0.01f, "the glove stops at the wall");
            Assert.IsTrue(phys.GhostVisible, "ghost shows the tracked hand (> 5 cm)");
            yield return bot.Move(h, start, false, 1f);
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.Less(phys.Separation, 0.01f, "back out: glove and tracked hand together");
            Assert.IsFalse(phys.GhostVisible);
            Object.Destroy(wall);

            // Erlan's chest: push the tracked palm 12 cm into his body
            var part = C.rig.Parts.First(p => p.region == BodyRegion.ChestR);
            Vector3 surf = part.Collider.ClosestPoint(C.rig.ChestBone.position + Vector3.up * 0.5f);
            yield return bot.Move(h, surf + Vector3.up * 0.15f, false, 1.5f);
            yield return bot.Move(h, surf - Vector3.up * 0.12f, false, 0.5f);
            yield return DemoFlowRunner.Seconds(0.2f);
            float visualAbove = phys.VisualPalm.y - surf.y, trackedBelow = surf.y - phys.TrackedPalm.y;
            Debug.Log($"[FMVR] w8 test body: tracked {trackedBelow * 100f:F1} cm below the chest surface, visual palm {visualAbove * 100f:F1} cm above, ghost {phys.GhostVisible}");
            Assert.Greater(trackedBelow, 0.1f);
            Assert.Greater(visualAbove, -0.01f, "the glove rests on his chest");
            Assert.IsTrue(phys.GhostVisible);
            yield return bot.Move(h, surf + Vector3.up * 0.25f, false, 1f);
        }

        [UnityTest]
        public IEnumerator Hands_RestOn_The_IfakTray()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            var bot = run.bot;
            var h = bot.left;
            var phys = Phys(h);
            var pouch = run.Pouch;
            Vector3 top = pouch.transform.TransformPoint(new Vector3(0.12f, 0f, 0.02f));
            yield return bot.Move(h, top + pouch.transform.up * 0.15f, false, 1.5f);
            yield return bot.Move(h, top - pouch.transform.up * 0.08f, false, 0.4f);   // tracked 8 cm under the tray bottom
            yield return DemoFlowRunner.Seconds(0.2f);
            float visual = Vector3.Dot(phys.VisualPalm - top, pouch.transform.up), tracked = Vector3.Dot(phys.TrackedPalm - top, pouch.transform.up);
            Debug.Log($"[FMVR] w8 test tray: tracked {tracked * 100f:F1} cm, visual palm {visual * 100f:F1} cm above the tray bottom");
            Assert.Less(tracked, -0.06f);
            Assert.Greater(visual, 0f, "the glove rests on the tray bottom, not through it");
            Assert.IsTrue(phys.GhostVisible);
            yield return bot.Move(h, top + pouch.transform.up * 0.2f, false, 1f);
        }

        [UnityTest]
        public IEnumerator Drag_Speed_ByGrip_Wrist_Slow_Handle_Best_TwoHands_Faster()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            var drag = C.Interaction.Drag;
            var bot = run.bot;
            float Limit() => drag.SpeedLimit;
            yield return bot.Grab(bot.left, C.Interaction.Handle(Limb.LeftArm, LimbHandle.Grip.Wrist));
            Vector3 away = Vector3.ProjectOnPlane(C.rig.BodyUp, Vector3.up).normalized;
            yield return bot.Move(bot.left, bot.left.PalmPosition + away * 0.8f, false, 1.2f);
            float wrist = Limit();
            yield return bot.Release(bot.left);
            yield return DemoFlowRunner.Seconds(0.5f);
            yield return bot.Grab(bot.right, drag, drag.transform.position);
            yield return bot.Move(bot.right, bot.right.PalmPosition + away * 0.4f, false, 1.2f);
            float handle = Limit();
            yield return bot.Grab(bot.left, drag.strapHandles[0]);
            yield return bot.Move(bot.left, bot.left.PalmPosition + away * 0.3f, false, 1.2f);
            float two = Limit();
            yield return bot.Release(bot.left);
            yield return bot.Release(bot.right);
            Debug.Log($"[FMVR] w8 test drag limits: wrist {wrist:F2}, handle {handle:F2}, handle+strap {two:F2} m/s");
            Assert.Less(wrist, handle, "one wrist is slower than the plate-carrier handle");
            Assert.Greater(two, handle, "two hands are faster");
        }
    }
}
