using System.Collections;
using System.Collections.Generic;
using FieldMedic.Input;
using FieldMedic.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>Scripted hands drive the real grab/poke components through physics frames.</summary>
    public class GrabInteractionTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public System.Collections.IEnumerator LogState()
        {
            yield return FreshScene.Ensure(); // w9: the old order-dependent flake was a battlefield left by an earlier test
            Debug.Log("[FMVR] grab test state: " + GlobalStateGuard.Describe());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        GameObject Spawn(GameObject go) { _spawned.Add(go); return go; }

        (HandGrabber grabber, ScriptedHandInput input) MakeHand(Handedness h, Vector3 palmAt)
        {
            var go = Spawn(new GameObject("Hand_" + h));
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var grabber = go.AddComponent<HandGrabber>();
            var input = rig.UseScripted(palmAt - grabber.palmOffset);
            go.transform.position = input.Position;
            return (grabber, input);
        }

        static void MovePalm(HandGrabber g, ScriptedHandInput input, Vector3 palm) =>
            input.Position = palm - input.Rotation * g.palmOffset;

        Grabbable MakeBox(string name, Vector3 pos, Vector3 size, float mass)
        {
            var go = Spawn(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            return go.AddComponent<Grabbable>();
        }

        void MakeGround()
        {
            var g = Spawn(GameObject.CreatePrimitive(PrimitiveType.Cube));
            g.name = "Ground";
            g.transform.position = new Vector3(0f, -0.5f, 0f);
            g.transform.localScale = new Vector3(10f, 1f, 10f);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate();
            yield return null;
        }

        static IEnumerator MoveGradually(HandGrabber g, ScriptedHandInput input, Vector3 from, Vector3 to, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                MovePalm(g, input, Vector3.Lerp(from, to, i / (float)steps));
                yield return new WaitForFixedUpdate();
                yield return null;
            }
        }

        [Test]
        public void Default_Layer_Collides_With_Itself()
        {
            // regression: an unreadable collision matrix made everything fall through the floor
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(0, 0));
        }

        [UnityTest]
        public IEnumerator Grab_Move_Release_MovesCube()
        {
            MakeGround();
            var cube = MakeBox("Cube", new Vector3(0f, 0.05f, 0.5f), Vector3.one * 0.1f, 0.4f);
            var (hand, input) = MakeHand(Handedness.Right, new Vector3(0f, 0.4f, 0.5f));
            yield return Frames(10); // let the cube settle

            Vector3 start = cube.transform.position;
            yield return MoveGradually(hand, input, new Vector3(0f, 0.4f, 0.5f), start, 10);
            Assert.AreEqual(cube, hand.Hovered, $"cube should be the hover candidate (cube {cube.transform.position}, palm {hand.PalmPosition})");
            Assert.Greater(input.HapticCount, 0, "hover should tick a haptic");

            input.Grip = 1f;
            yield return Frames(2);
            Assert.AreEqual(cube, hand.Held, "grip should grab the cube");
            Assert.IsTrue(cube.IsGrabbed);

            Vector3 target = start + new Vector3(0.4f, 0.3f, 0.1f);
            yield return MoveGradually(hand, input, start, target, 30);
            yield return Frames(15);
            Assert.Less(Vector3.Distance(cube.transform.position, target), 0.05f, "held cube follows the hand");

            input.Grip = 0f;
            yield return Frames(2);
            Assert.IsNull(hand.Held);
            Assert.IsFalse(cube.IsGrabbed);
            yield return Frames(60);
            Assert.Greater(Vector3.Distance(cube.transform.position, start), 0.3f, "cube ended up moved");
        }

        [UnityTest]
        public IEnumerator Heavy_Object_Lags_Behind_Hand()
        {
            var light = MakeBox("Light", new Vector3(-1f, 1f, 0f), Vector3.one * 0.1f, 0.3f);
            var heavy = MakeBox("Heavy", new Vector3(1f, 1f, 0f), Vector3.one * 0.1f, 25f);
            light.Body.useGravity = false;
            heavy.Body.useGravity = false;
            var (hl, il) = MakeHand(Handedness.Left, light.transform.position);
            var (hh, ih) = MakeHand(Handedness.Right, heavy.transform.position);
            yield return Frames(2);
            il.Grip = 1f;
            ih.Grip = 1f;
            yield return Frames(2);
            Assert.AreEqual(light, hl.Held);
            Assert.AreEqual(heavy, hh.Held);
            Assert.Less(heavy.Responsiveness, light.Responsiveness);

            Vector3 dl = light.transform.position, dh = heavy.transform.position;
            MovePalm(hl, il, dl + Vector3.up * 0.3f);
            MovePalm(hh, ih, dh + Vector3.up * 0.3f);
            yield return Frames(3);
            float movedLight = light.transform.position.y - dl.y;
            float movedHeavy = heavy.transform.position.y - dh.y;
            Assert.Greater(movedLight, movedHeavy * 1.5f, $"heavy should lag (light {movedLight:F3}, heavy {movedHeavy:F3})");
        }

        [UnityTest]
        public IEnumerator Two_Hands_Hold_One_Object()
        {
            var bar = MakeBox("Bar", new Vector3(0f, 1f, 0f), new Vector3(0.6f, 0.05f, 0.05f), 2f);
            bar.Body.useGravity = false;
            var (l, il) = MakeHand(Handedness.Left, new Vector3(-0.25f, 1f, 0f));
            var (r, ir) = MakeHand(Handedness.Right, new Vector3(0.25f, 1f, 0f));
            yield return Frames(2);
            il.Grip = 1f;
            ir.Grip = 1f;
            yield return Frames(2);
            Assert.AreEqual(2, bar.HolderCount);

            // raise the right hand: the bar tilts to follow the line between the hands
            yield return MoveGradually(r, ir, new Vector3(0.25f, 1f, 0f), new Vector3(0.25f, 1.25f, 0f), 15);
            yield return Frames(20);
            Vector3 axis = bar.transform.right;
            Assert.Greater(Mathf.Abs(axis.y), 0.3f, "bar should tilt with two-hand grip");

            il.Grip = 0f;
            yield return Frames(2);
            Assert.AreEqual(1, bar.HolderCount);
            Assert.AreEqual(bar, r.Held);
        }

        [UnityTest]
        public IEnumerator GrabPoint_Is_Preferred_And_Required()
        {
            var box = MakeBox("Box", new Vector3(0f, 1f, 0f), new Vector3(0.4f, 0.2f, 0.2f), 5f);
            box.Body.useGravity = false;
            box.requireGrabPoint = true;
            var gp = new GameObject("Handle").AddComponent<GrabPoint>();
            gp.transform.SetParent(box.transform, false);
            gp.transform.localPosition = new Vector3(0.4f, 0f, 0f); // world x = +0.16
            gp.radius = 0.05f;
            box.RefreshGrabPoints();

            var (hand, input) = MakeHand(Handedness.Right, new Vector3(-0.15f, 1f, 0f)); // far end, no handle
            yield return Frames(2);
            Assert.IsNull(hand.Hovered, "requireGrabPoint: body alone is not grabbable");

            MovePalm(hand, input, gp.transform.position);
            yield return Frames(2);
            Assert.AreEqual(box, hand.Hovered);
            Assert.AreEqual(gp, hand.HoveredPoint);
            input.Grip = 1f;
            yield return Frames(2);
            Assert.AreEqual(gp, hand.HeldPoint);
        }

        [UnityTest]
        public IEnumerator Fingertip_Presses_Button()
        {
            var btn = Spawn(new GameObject("Button")).AddComponent<Pressable>();
            btn.transform.position = new Vector3(0f, 1f, 0f);
            btn.size = new Vector2(0.05f, 0.05f);
            btn.maxDepth = 0.012f;
            var (hand, input) = MakeHand(Handedness.Right, new Vector3(0f, 1.3f, 0f));
            // point the fingers straight down so the fingertip leads
            input.Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Vector3 tipLocal = hand.fingertipOffset;
            Vector3 PosForTip(Vector3 tip) => tip - input.Rotation * tipLocal;

            input.Position = PosForTip(new Vector3(0f, 1.02f, 0f));
            yield return Frames(2);
            Assert.IsFalse(btn.IsTouching);

            input.Position = PosForTip(new Vector3(0f, 1f - 0.005f, 0f));
            yield return Frames(2);
            Assert.IsTrue(btn.IsTouching);
            Assert.IsFalse(btn.IsPressed, "shallow touch does not click");

            input.Position = PosForTip(new Vector3(0f, 1f - 0.011f, 0f));
            yield return Frames(2);
            Assert.IsTrue(btn.IsPressed);
            Assert.AreEqual(1, btn.PressCount);

            input.Position = PosForTip(new Vector3(0f, 1.05f, 0f));
            yield return Frames(2);
            Assert.IsFalse(btn.IsPressed);
            Assert.AreEqual(1, btn.PressCount);
        }
    }
}
