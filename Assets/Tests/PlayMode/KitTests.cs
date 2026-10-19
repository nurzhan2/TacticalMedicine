using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9 owner bug "the IFAK is next to me but the items float somewhere on their own": the player's kit is worn on
    /// the body, every item sits exactly in its slot while not held, a release near the open kit puts it back in its
    /// slot, a release anywhere else drops it to the ground (nothing hovers), and walking carries kit + items along.
    /// </summary>
    public class KitTests
    {
        readonly List<Object> _spawned = new List<Object>();
        GlobalStateGuard _guard;
        Transform _head;
        IfakPouch _pouch;
        MedicBot _bot;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "~Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            _spawned.Add(floor);
            var ear = new GameObject("~Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
            _head = new GameObject("~Head").transform;
            _head.position = new Vector3(0f, 1.62f, 0f);
            _spawned.Add(_head.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            _guard?.Restore();
        }

        HandGrabber MakeHand(Handedness h, Vector3 at)
        {
            var go = new GameObject("~Hand_" + h);
            _spawned.Add(go);
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var g = go.AddComponent<HandGrabber>();
            rig.UseScripted(at);
            go.transform.position = at;
            return g;
        }

        IEnumerator MakeKit()
        {
            var pgo = new GameObject("PlayerIFAK");
            _spawned.Add(pgo);
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.bodyLocked = true;
            _pouch.head = _head;
            yield return null; // Start → Build
            yield return null;
            Assert.IsTrue(_pouch.Built, "kit built");
            var l = MakeHand(Handedness.Left, _head.position + new Vector3(-0.25f, -0.4f, 0.3f));
            var r = MakeHand(Handedness.Right, _head.position + new Vector3(0.25f, -0.4f, 0.3f));
            var bgo = new GameObject("~Bot");
            _spawned.Add(bgo);
            _bot = bgo.AddComponent<MedicBot>();
            _bot.Init(l, r);
            _bot.handSpeed = 1.2f;
        }

        static float SlotError(MedItem it) => it.homeSlot == null ? 0f : Vector3.Distance(it.transform.position, it.homeSlot.position);

        [UnityTest]
        public IEnumerator Walk5m_KitAndSlottedItems_MoveWithPlayer()
        {
            yield return MakeKit();
            Vector3 start = _pouch.transform.position;
            float worst = 0f;
            string worstItem = null;
            // walk 5 m forward with a natural head bob and looking around (inside the torso's yaw slack)
            float t = 0f;
            Vector3 p0 = _head.position;
            while (t < 4f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 3.5f);
                _head.position = p0 + new Vector3(0f, Mathf.Sin(t * 11f) * 0.03f, 5f * k);
                _head.rotation = Quaternion.Euler(Mathf.Sin(t * 2f) * 12f, Mathf.Sin(t * 1.3f) * 25f, 0f);
                yield return null;
                foreach (var it in _pouch.Items)
                {
                    Assert.IsTrue(it.InPouch, it.name + " left the kit by itself");
                    float e = SlotError(it);
                    if (e > worst) { worst = e; worstItem = it.name; }
                }
            }
            Debug.Log($"[FMVR] kit walk: worst item-slot error {worst * 1000f:F1} mm ({worstItem})");
            Assert.Greater(Vector3.Distance(_pouch.transform.position, start), 4.5f, "the kit came along");
            Vector3 flat = _pouch.transform.position - _head.position; flat.y = 0f;
            Assert.Less(flat.magnitude, 0.6f, "kit stays on the body");
            Assert.Less(worst, 0.01f, $"items stay exactly in their slots while walking (worst {worstItem} {worst * 1000f:F0} mm)");
            foreach (var it in _pouch.Items)
            {
                Assert.AreEqual(it.homeSlot, it.transform.parent, it.name + " parented to its slot");
                Assert.IsTrue(it.Body.isKinematic && !it.Body.useGravity, it.name + " kinematic in the slot");
            }
        }

        [UnityTest]
        public IEnumerator ReleaseFarAway_EveryItemFallsToTheGround()
        {
            yield return MakeKit();
            yield return _bot.OpenPouch(_pouch, _bot.left);
            Assert.IsTrue(_pouch.IsOpen, "kit opened by the tab");
            var released = new List<(MedItem item, float at)>();
            var items = _pouch.Items.ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var h = _bot.right;
                yield return _bot.Grab(h, it);
                Assert.AreEqual(it, h.Held, "grabbed " + it.name);
                Assert.IsFalse(it.InPouch, it.name + " out of the kit");
                // 0.9 m to the side of the player, chest height, then let go (spread out so they do not pile up)
                Vector3 drop = _head.position + new Vector3(0.9f + 0.25f * (i % 2), -0.35f, -0.9f + 0.14f * i);
                yield return _bot.Move(h, drop, false, 2.5f);
                yield return _bot.Release(h);
                released.Add((it, Time.time));
                yield return _bot.Move(h, _head.position + new Vector3(0.25f, -0.4f, 0.3f), false, 3f);
            }
            float t0 = Time.time;
            while (Time.time - t0 < 3f) yield return null;
            foreach (var (it, at) in released)
            {
                if (!it.gameObject.activeInHierarchy) continue; // consumed (none expected here)
                Assert.IsFalse(it.InPouch, it.name + " must not jump back from 0.9 m");
                Assert.IsFalse(it.Body.isKinematic, it.name + " is a physics object after release");
                Assert.IsTrue(it.Body.useGravity, it.name + " has gravity after release");
                var b = it.GetComponent<Collider>().bounds;
                Assert.Less(b.min.y, 0.1f, $"{it.name} lies on the ground (bottom at {b.min.y:F2} m, released {Time.time - at:F1} s ago)");
                Assert.Greater(b.max.y, -0.05f, it.name + " did not fall through the ground");
                Assert.Less(it.Body.linearVelocity.magnitude, 0.2f, it.name + " came to rest");
            }
        }

        [UnityTest]
        public IEnumerator EveryItem_HasASwappableVisualChild()
        {
            yield return MakeKit();
            Assert.AreEqual(14, _pouch.Items.Count, "two-echelon kit holds the same 14 items");
            foreach (var it in _pouch.Items)
            {
                Assert.IsNotNull(it.Visual, it.name + " has a Visual child");
                Assert.AreEqual(MedItem.VisualName, it.Visual.name);
                Assert.AreEqual(it.transform, it.Visual.parent);
                Assert.Greater(it.Visual.GetComponentsInChildren<MeshRenderer>(true).Length, 0, it.name + ": the carried look lives in Visual");
                foreach (Transform c in it.transform)
                    Assert.IsFalse(c.name.StartsWith("Merged_"), it.name + ": no loose merged mesh outside Visual (" + c.name + ")");
                if (it.tip != null) Assert.IsFalse(it.tip.IsChildOf(it.Visual), it.name + ": logic points stay outside Visual");
            }
        }

        [UnityTest]
        public IEnumerator QuickPouch_CatAndShears_ReachableWhileMainKitClosed()
        {
            yield return MakeKit();
            Assert.IsFalse(_pouch.IsOpen, "main pouch starts closed");
            Assert.IsNotNull(_pouch.QuickPouch, "two-echelon kit: quick pouch");
            var cat = _pouch.Find<TourniquetCAT>();
            var shears = _pouch.Find<ShearsItem>();
            Assert.AreEqual(_pouch.QuickPouch, cat.homeSlot.parent, "first CAT in the quick pouch");
            Assert.AreEqual(_pouch.QuickPouch, shears.homeSlot.parent, "shears in the quick pouch");
            Assert.IsNotNull(_pouch.Find<TourniquetCAT>(1), "second CAT in the main pouch");
            Assert.IsFalse(_pouch.Find<GauzeItem>().CanBeGrabbedBy(_bot.right), "main pouch items locked while the lid is closed");
            yield return _bot.Grab(_bot.right, cat);
            Assert.AreEqual(cat, _bot.right.Held, "CAT out of the quick pouch without opening the kit");
            Assert.IsFalse(_pouch.IsOpen, "the main lid is still closed");
            yield return _bot.Move(_bot.right, cat.homeSlot.position + Vector3.up * 0.12f, false, 2f);
            yield return _bot.Release(_bot.right);
            Assert.IsTrue(cat.InPouch, "CAT back in the quick pouch (always open)");
        }

        [UnityTest]
        public IEnumerator ReleaseNearOpenKit_ReturnsToItsOwnSlot()
        {
            yield return MakeKit();
            yield return _bot.OpenPouch(_pouch, _bot.left);
            foreach (var it in new MedItem[] { _pouch.Find<TourniquetCAT>(), _pouch.Find<GauzeItem>(), _pouch.Find<ShearsItem>() })
            {
                Assert.IsNotNull(it);
                var slot = it.homeSlot;
                var h = _bot.right;
                yield return _bot.Grab(h, it);
                Assert.IsFalse(it.InPouch);
                // pull it out and bring it back ~18 cm above the kit (not exactly into the slot)
                yield return _bot.Move(h, h.PalmPosition + Vector3.up * 0.35f, false, 2f);
                yield return _bot.Move(h, slot.position + _pouch.transform.up * 0.16f + _pouch.transform.right * 0.06f, false, 2f);
                yield return _bot.Release(h);
                Assert.IsTrue(it.InPouch, it.name + " back in the kit");
                Assert.AreEqual(slot, it.transform.parent, it.name + " in its own slot");
                Assert.Less(SlotError(it), 0.002f, it.name + " exactly in the slot");
                yield return _bot.Move(h, _head.position + new Vector3(0.25f, -0.4f, 0.3f), false, 3f);
            }
        }
    }
}
