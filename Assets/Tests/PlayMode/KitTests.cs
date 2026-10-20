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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure(); // never inherit a battlefield from an earlier test
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
            // w9d: the real kit — CAT + shears in the quick pouch, 18 items (incl. Alpha, Esmarch, splint...) in the main one
            Assert.AreEqual(20, _pouch.Items.Count, "two-echelon kit holds every modelled item");
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
        public IEnumerator Stowed_OnTheHip_ItemsLocked_XOpensTheTray()
        {
            // w10: the kit hangs closed on the left hip (out of the way); X summons it as a tray in front of the eyes
            yield return MakeKit();
            Assert.IsNotNull(_pouch.Tray, "the player's kit is a summonable tray");
            Assert.IsFalse(_pouch.IsOpen, "stowed at start");
            var cat = _pouch.Find<TourniquetCAT>();
            Assert.IsNotNull(_pouch.Find(MedItemKind.AlphaTourniquet), "elastic Alpha tourniquet in the kit");
            Assert.IsNotNull(_pouch.Find(MedItemKind.EsmarchTourniquet), "Esmarch band in the kit");
            Assert.IsFalse(cat.CanBeGrabbedBy(_bot.right), "nothing can be taken out of the stowed kit");
            Vector3 hip = Quaternion.Inverse(Quaternion.Euler(0f, _head.eulerAngles.y, 0f)) * (_pouch.Tray.Hip.position - _head.position);
            Assert.Less(hip.x, -0.1f, "hip kit on the LEFT");
            Assert.Less(hip.y, -0.5f, "hip kit low (hip height)");
            Assert.Less(hip.z, 0.1f, "hip kit never in front of the hands");
            yield return _bot.OpenPouch(_pouch, _bot.left);
            Assert.IsTrue(_pouch.IsOpen, "X opened the tray");
            Vector3 tray = Quaternion.Inverse(Quaternion.Euler(0f, _head.eulerAngles.y, 0f)) * (_pouch.transform.position - _head.position);
            Assert.AreEqual(0.4f, tray.z, 0.06f, "tray ~40 cm in front");
            Assert.Less(tray.y, -0.2f, "tray at chest height");
            yield return _bot.Grab(_bot.right, cat);
            Assert.AreEqual(cat, _bot.right.Held, "CAT taken from the open tray");
            yield return _bot.Move(_bot.right, cat.homeSlot.position + _pouch.transform.up * 0.12f, false, 2f);
            yield return _bot.Release(_bot.right);
            Assert.IsTrue(cat.InPouch, "CAT back in its cell over the tray");
        }

        [UnityTest]
        public IEnumerator OpenKit_IsTheRealModel_ItemsOnTheirSeats_BigGrabZones_NoSolidBody()
        {
            // w10d (owner: "no rectangle — the previous one, with the model and everything"): X summons the real IFAK,
            // unfolded, items on the model's own Slot_* seats; easy to grab; the kit body never blocks the hands
            yield return MakeKit();
            yield return _bot.OpenPouch(_pouch, _bot.left);
            for (float t = 0f; t < 1f && _pouch.Tray.Open01 < 1f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(_pouch.IsOpen);
            if (_pouch.Art != null)
            {
                Assert.AreEqual(1f, _pouch.Art.Open01, 1e-3f, "the model is unfolded");
                foreach (var it in _pouch.Items)
                    Assert.IsTrue(it.homeSlot != null && it.homeSlot.IsChildOf(_pouch.Art.Root), it.name + " sits on a seat of the real kit model");
            }
            Assert.IsNull(_pouch.transform.Find("TrayPanel"), "no flat rectangle panel any more");
            foreach (var c in _pouch.GetComponentsInChildren<Collider>(false))
                if (c.enabled && c.GetComponentInParent<MedItem>() == null) Assert.IsTrue(c.isTrigger, c.name + ": nothing solid on the kit body");
            foreach (var it in _pouch.Items)
            {
                Assert.AreEqual(it.homeSlot, _pouch.Tray.Cells[it], it.name + ": Cells maps the item to its seat");
                foreach (var c in it.GetComponents<Collider>())
                    if (c.enabled) Assert.IsTrue(c.isTrigger, it.name + ": in the kit the item never shoves anything");
                Vector3 biggest = Vector3.zero;
                foreach (var c in it.GetComponentsInChildren<Collider>(false))
                    if (c.enabled) biggest = Vector3.Max(biggest, c.bounds.size);
                Assert.GreaterOrEqual(Mathf.Min(biggest.x, biggest.y, biggest.z), 0.055f, it.name + ": grab zone at least ~6 cm");
            }
            Vector3 face = Quaternion.Inverse(Quaternion.Euler(0f, _head.eulerAngles.y, 0f)) * (_pouch.transform.up);
            Assert.Less(face.z, -0.3f, "the open face is tilted toward the eyes");
            Assert.Greater(face.y, 0.3f);
            // X again: back to the hip, folded
            _pouch.Tray.Close();
            for (float t = 0f; t < 1f && _pouch.Tray.Open01 > 0f; t += Time.deltaTime) yield return null;
            if (_pouch.Art != null) Assert.AreEqual(0f, _pouch.Art.Open01, 1e-3f, "folded on the hip");
            Assert.Less(Vector3.Distance(_pouch.transform.TransformPoint(Vector3.zero), _pouch.Tray.Hip.position), 0.5f, "parked at the hip");
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
