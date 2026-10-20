using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w10 owner bug "I grab the way the hint shows and it does not take": these tests drive the REAL input path — an
    /// Input System XR controller device (devicePosition / deviceRotation / grip / trigger / X / Y / thumbstick, like the
    /// OpenXR Touch Plus profile) → <see cref="XRControllerHandInput"/> → <see cref="HandRig"/> → <see cref="HandGrabber"/>
    /// / <see cref="KitTray"/> / <see cref="RadioPanel"/> — instead of the bots' scripted shortcuts.
    /// </summary>
    public class W10InputDeviceTests
    {
        const string Layout = "FMVRTestTouch";
        static bool _registered;

        readonly List<Object> _spawned = new List<Object>();
        readonly List<InputDevice> _devices = new List<InputDevice>();
        GlobalStateGuard _guard;
        Transform _head;
        InputDevice _ldev, _rdev;
        HandGrabber _left, _right;
        IfakPouch _pouch;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure();
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            if (!_registered)
            {
                // explicit offsets/formats: the auto-appended controls of an extended XRController overlapped its pose
                InputSystem.RegisterLayout(@"{
                    ""name"": """ + Layout + @""",
                    ""extend"": ""XRController"",
                    ""format"": ""FMVT"",
                    ""controls"": [
                        { ""name"": ""trackingState"", ""layout"": ""Integer"", ""offset"": 0, ""format"": ""INT"" },
                        { ""name"": ""isTracked"", ""layout"": ""Button"", ""offset"": 4, ""format"": ""FLT"" },
                        { ""name"": ""devicePosition"", ""layout"": ""Vector3"", ""offset"": 8, ""format"": ""VEC3"" },
                        { ""name"": ""deviceRotation"", ""layout"": ""Quaternion"", ""offset"": 20, ""format"": ""QUAT"" },
                        { ""name"": ""grip"", ""layout"": ""Axis"", ""offset"": 36, ""format"": ""FLT"" },
                        { ""name"": ""trigger"", ""layout"": ""Axis"", ""offset"": 40, ""format"": ""FLT"" },
                        { ""name"": ""primaryButton"", ""layout"": ""Button"", ""offset"": 44, ""format"": ""FLT"" },
                        { ""name"": ""secondaryButton"", ""layout"": ""Button"", ""offset"": 48, ""format"": ""FLT"" },
                        { ""name"": ""thumbstick"", ""layout"": ""Stick"", ""offset"": 52, ""format"": ""VEC2"" },
                        { ""name"": ""thumbstickClicked"", ""layout"": ""Button"", ""offset"": 60, ""format"": ""FLT"" }
                    ]
                }");
                _registered = true;
            }
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

            _ldev = AddDevice(CommonUsages.LeftHand);
            _rdev = AddDevice(CommonUsages.RightHand);
            _left = MakeHand(Handedness.Left, _ldev);
            _right = MakeHand(Handedness.Right, _rdev);
            SetPose(_ldev, _head.position + new Vector3(-0.25f, -0.45f, 0.2f));
            SetPose(_rdev, _head.position + new Vector3(0.25f, -0.45f, 0.2f));
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var t in Object.FindObjectsByType<KitTray>(FindObjectsSortMode.None)) Object.Destroy(t);
            foreach (var d in _devices) { if (d != null && d.added) InputSystem.RemoveDevice(d); Pads.Remove(d); }
            _devices.Clear();
            InputLocks.RightStickCaptured = false;
            _guard?.Restore();
        }

        InputDevice AddDevice(UnityEngine.InputSystem.Utilities.InternedString usage)
        {
            var d = InputSystem.AddDevice(Layout);
            InputSystem.SetDeviceUsage(d, usage);
            _devices.Add(d);
            Set(d, "isTracked", 1f);
            Set(d, "trackingState", 3);
            return d;
        }

        HandGrabber MakeHand(Handedness h, InputDevice dev)
        {
            var go = new GameObject("~Hand_" + h);
            _spawned.Add(go);
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var g = go.AddComponent<HandGrabber>();
            rig.UseXRDevice(dev);
            return g;
        }

        /// <summary>The full state of a test controller: every change is pushed as ONE state event (an event snapshots
        /// the device state before earlier queued events are applied, so per-control events would clobber each other).</summary>
        sealed class Pad
        {
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public Vector2 stick;
            public int trackingState = 3;
        }

        static readonly Dictionary<InputDevice, Pad> Pads = new Dictionary<InputDevice, Pad>();

        static Pad PadOf(InputDevice d)
        {
            if (!Pads.TryGetValue(d, out var p)) Pads[d] = p = new Pad();
            return p;
        }

        static void Push(InputDevice d)
        {
            var p = PadOf(d);
            using (UnityEngine.InputSystem.LowLevel.StateEvent.From(d, out var ptr))
            {
                foreach (var kv in p.floats)
                    if (d.TryGetChildControl(kv.Key) is InputControl<float> c) c.WriteValueIntoEvent(kv.Value, ptr);
                ((InputControl<Vector3>)d.TryGetChildControl("devicePosition")).WriteValueIntoEvent(p.pos, ptr);
                ((InputControl<Quaternion>)d.TryGetChildControl("deviceRotation")).WriteValueIntoEvent(p.rot, ptr);
                ((InputControl<Vector2>)d.TryGetChildControl("thumbstick")).WriteValueIntoEvent(p.stick, ptr);
                ((InputControl<int>)d.TryGetChildControl("trackingState")).WriteValueIntoEvent(p.trackingState, ptr);
                InputSystem.QueueEvent(ptr);
            }
            InputSystem.Update();
        }

        static void Set(InputDevice d, string control, float v)
        {
            Assert.IsNotNull(d.TryGetChildControl(control), d.layout + " has " + control);
            PadOf(d).floats[control] = v;
            Push(d);
        }

        static void Set(InputDevice d, string control, int v)
        {
            if (control == "trackingState") { PadOf(d).trackingState = v; Push(d); }
            else Set(d, control, (float)v);
        }

        static void SetStick(InputDevice d, Vector2 v) { PadOf(d).stick = v; Push(d); }

        static void SetCtl(InputDevice d, string control, Vector3 v) { PadOf(d).pos = v; Push(d); }
        static void SetCtl(InputDevice d, string control, Quaternion v) { PadOf(d).rot = v; Push(d); }

        static void SetPose(InputDevice d, Vector3 handPos)
        {
            var p = PadOf(d);
            p.pos = handPos;
            p.rot = Quaternion.identity;
            Push(d);
        }

        /// <summary>Puts the palm (not the controller origin) at `palm`.</summary>
        void PalmTo(HandGrabber h, InputDevice d, Vector3 palm) => SetPose(d, palm - h.palmOffset);

        IEnumerator Frames(int n = 2) { for (int i = 0; i < n; i++) { yield return new WaitForFixedUpdate(); yield return null; } }

        IEnumerator Press(InputDevice d, string button, float seconds = 0.05f)
        {
            Set(d, button, 1f);
            yield return Frames(2);
            float t = 0f; while (t < seconds) { t += Time.deltaTime; yield return null; }
            Set(d, button, 0f);
            yield return Frames(2);
        }

        IEnumerator MakeKit()
        {
            var pgo = new GameObject("PlayerIFAK");
            _spawned.Add(pgo);
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.bodyLocked = true;
            _pouch.head = _head;
            yield return null;
            yield return null;
            Assert.IsTrue(_pouch.Built && _pouch.Tray != null, "player kit with a tray");
        }

        static Vector3 Centre(MedItem it)
        {
            var c = it.GetComponent<BoxCollider>();
            return c != null ? it.transform.TransformPoint(c.center) : it.transform.position;
        }

        // ================================================================== grab on the floor / table
        [UnityTest]
        public IEnumerator Device_Hover_Grip_TakesItem_Release_Drops()
        {
            var marker = Kit.Create(MedItemKind.Marker, new Vector3(0.3f, 0.9f, 0.45f), Quaternion.identity);
            marker.MakeFree();
            marker.Body.isKinematic = true; // lies still on an imaginary table
            yield return Frames(2);
            Assert.AreEqual(XRControllerHandInput.FindDevice(Handedness.Right), _rdev, "the device is found by its RightHand usage");
            Assert.IsTrue(_right.Input is XRControllerHandInput, "right hand driven by the Input System device");

            // 8 cm away from the item: inside the 9 cm hover radius
            PalmTo(_right, _rdev, Centre(marker) + new Vector3(0.0f, 0.075f, 0f));
            yield return Frames(3);
            Assert.AreEqual(marker, _right.Hovered, "hover from 7.5 cm (radius " + _right.grabRadius + ")");
            Assert.IsNotNull(_right.CuePoint, "hover glow cue shown");
            var hl = marker.GetComponent<GrabHighlight>();
            Assert.IsTrue(hl != null && hl.IsHighlighted, "the hovered item is tinted");
            var xin = (XRControllerHandInput)_right.Input;
            Assert.Greater(xin.HapticCount, 0, "light haptic tick on hover");

            Set(_rdev, "grip", 1f);
            yield return Frames(2);
            Assert.AreEqual(marker, _right.Held, "GRIP took the item");
            Assert.AreEqual(GrabButton.Grip, _right.HeldWith);
            Assert.IsNull(_right.CuePoint, "no hover cue while holding");

            PalmTo(_right, _rdev, new Vector3(0.6f, 1.0f, 0.6f));
            yield return Frames(4);
            Assert.Less(Vector3.Distance(Centre(marker), _right.PalmPosition), 0.15f, "the held item follows the device pose");
            Set(_rdev, "grip", 0f);
            yield return Frames(2);
            Assert.IsNull(_right.Held, "button up = release");
            float t = 0f; while (t < 1.5f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(marker.Body.isKinematic, "a dropped item is physics");
            Assert.Less(marker.GetComponent<Collider>().bounds.min.y, 0.1f, "it fell to the floor (no item ever floats)");
        }

        [UnityTest]
        public IEnumerator Device_Trigger_AlsoGrabs_AndBecomesTheActionAfterRepress()
        {
            var shears = Kit.Create(MedItemKind.TraumaShears, new Vector3(-0.2f, 0.9f, 0.45f), Quaternion.identity);
            shears.MakeFree();
            shears.Body.isKinematic = true;
            yield return Frames(2);
            PalmTo(_right, _rdev, Centre(shears) + Vector3.up * 0.03f);
            yield return Frames(3);
            Set(_rdev, "trigger", 1f);
            yield return Frames(2);
            Assert.AreEqual(shears, _right.Held, "TRIGGER took the item (owner: grab like the hint shows)");
            Assert.AreEqual(GrabButton.Trigger, _right.HeldWith);
            Assert.AreEqual(0f, shears.Trigger, "the grabbing squeeze is not a cut");
            // hold on with the grip, let the trigger go, squeeze again = the shears' action
            Set(_rdev, "grip", 1f);
            Set(_rdev, "trigger", 0f);
            yield return Frames(2);
            Assert.AreEqual(shears, _right.Held, "still held by the grip");
            Set(_rdev, "trigger", 1f);
            yield return Frames(2);
            Assert.AreEqual(1f, shears.Trigger, 0.01f, "trigger re-pressed = action");
            Set(_rdev, "trigger", 0f);
            Set(_rdev, "grip", 0f);
            yield return Frames(2);
            Assert.IsNull(_right.Held, "both buttons up = released");
        }

        [UnityTest]
        public IEnumerator Device_NearestItemWins()
        {
            var a = Kit.Create(MedItemKind.Marker, new Vector3(0.0f, 0.9f, 0.5f), Quaternion.identity);
            var b = Kit.Create(MedItemKind.CasualtyCard, new Vector3(0.07f, 0.9f, 0.5f), Quaternion.identity);
            foreach (var it in new[] { a, b }) { it.MakeFree(); it.Body.isKinematic = true; }
            yield return Frames(2);
            PalmTo(_right, _rdev, Centre(b) + new Vector3(0.01f, 0.02f, 0f));
            yield return Frames(3);
            var pc = _rdev.TryGetChildControl("devicePosition") as InputControl<Vector3>;
            Debug.Log($"[FMVR] w10 test pose: want {Centre(b) + new Vector3(0.01f, 0.02f, 0f):F3} palm {_right.PalmPosition:F3} input {_right.Input.Position:F3} device {pc.ReadValue():F3} tracked {_right.Input.IsTracked} src {_right.Input.Source} rigmode {_right.Rig.ActiveMode} layout {_rdev.layout} controls {string.Join(",", System.Linq.Enumerable.Select(_rdev.allControls, c => c.name + "@" + c.stateBlock.byteOffset + ":" + c.stateBlock.sizeInBits))}");
            Assert.AreEqual(b, _right.Hovered, "the item under the palm wins over the one 7 cm away");
        }

        // ================================================================== kit tray
        [UnityTest]
        public IEnumerator Device_X_OpensTray_EveryItemGrabbable_ReturnsToItsCell()
        {
            yield return MakeKit();
            Assert.IsFalse(_pouch.IsOpen);
            yield return Press(_ldev, "primaryButton");
            float t = 0f; while (_pouch.Tray.Open01 < 1f && t < 1f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(_pouch.IsOpen, "left X opened the tray");
            var items = _pouch.Items.ToList();
            Assert.AreEqual(20, items.Count);
            int i = 0;
            foreach (var it in items)
            {
                Assert.IsTrue(it.Visual.gameObject.activeInHierarchy, it.name + " visible in the open tray");
                Assert.IsFalse(string.IsNullOrEmpty(_pouch.Tray.LabelOf(it)), it.name + " has a label");
                bool trigger = (i++ % 2) == 1;
                var cell = it.homeSlot;
                PalmTo(_right, _rdev, Centre(it));
                yield return Frames(3);
                Assert.AreEqual(it, _right.Hovered, $"{it.name}: hovered in the tray (got {(_right.Hovered != null ? _right.Hovered.name : "nothing")})");
                Set(_rdev, trigger ? "trigger" : "grip", 1f);
                yield return Frames(2);
                Assert.AreEqual(it, _right.Held, $"{it.name}: taken from the tray with {(trigger ? "TRIGGER" : "GRIP")}");
                Assert.IsFalse(it.InPouch);
                // out, then back ~12 cm above the tray, let go
                PalmTo(_right, _rdev, _right.PalmPosition + Vector3.up * 0.3f);
                yield return Frames(3);
                PalmTo(_right, _rdev, cell.position + _pouch.transform.up * 0.12f);
                yield return Frames(3);
                Set(_rdev, trigger ? "trigger" : "grip", 0f);
                yield return Frames(2);
                Assert.IsTrue(it.InPouch, it.name + " snapped back into the tray");
                Assert.AreEqual(cell, it.transform.parent, it.name + " in its own cell");
            }
        }

        [UnityTest]
        public IEnumerator Device_ReleaseAwayFromTray_Falls_XAgainStows_HipGrabOpens()
        {
            yield return MakeKit();
            yield return Press(_ldev, "primaryButton");
            float t = 0f; while (_pouch.Tray.Open01 < 1f && t < 1f) { t += Time.deltaTime; yield return null; }
            var gauze = _pouch.Find<GauzeItem>();
            PalmTo(_right, _rdev, Centre(gauze));
            yield return Frames(3);
            Set(_rdev, "grip", 1f);
            yield return Frames(2);
            Assert.AreEqual(gauze, _right.Held);
            PalmTo(_right, _rdev, _head.position + new Vector3(0.75f, -0.5f, 0.1f));
            yield return Frames(4);
            Set(_rdev, "grip", 0f);
            yield return Frames(2);
            t = 0f; while (t < 1.5f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(gauze.InPouch, "released 75 cm to the side: not back in the kit");
            Assert.Less(gauze.GetComponent<Collider>().bounds.min.y, 0.1f, "it lies on the ground");

            // X again: the tray folds to the hip; the rest is hidden and cannot be taken
            yield return Press(_ldev, "primaryButton");
            t = 0f; while (_pouch.Tray.Open01 > 0f && t < 1f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(_pouch.IsOpen, "X folded the tray");
            var cat = _pouch.Find<TourniquetCAT>();
            Assert.IsFalse(cat.Visual.gameObject.activeInHierarchy, "items hidden in the stowed kit");
            PalmTo(_right, _rdev, Centre(cat));
            yield return Frames(3);
            Assert.AreNotEqual(cat, _right.Hovered, "nothing grabbable inside the stowed kit");

            // grab the hip kit with the left hand: it opens
            Release(_right, _rdev);
            PalmTo(_left, _ldev, _pouch.Tray.Hip.position);
            yield return Frames(3);
            Assert.AreEqual(_pouch.Tray.HipHandle, _left.Hovered, "the hip kit is hovered");
            Set(_ldev, "grip", 1f);
            yield return Frames(3);
            Set(_ldev, "grip", 0f);
            yield return Frames(2);
            Assert.IsTrue(_pouch.IsOpen, "grabbing the hip kit summons the tray");
            Assert.IsNull(_left.Held, "the hand stays free");
        }

        void Release(HandGrabber h, InputDevice d) { Set(d, "grip", 0f); Set(d, "trigger", 0f); }

        [UnityTest]
        public IEnumerator Tray_FollowsTheHeadOnlyAfterABigTurn()
        {
            yield return MakeKit();
            yield return Press(_ldev, "primaryButton");
            float t = 0f; while (_pouch.Tray.Open01 < 1f && t < 1f) { t += Time.deltaTime; yield return null; }
            float yaw0 = _pouch.transform.eulerAngles.y;
            _head.rotation = Quaternion.Euler(0f, 35f, 0f);
            yield return Frames(20);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, _pouch.transform.eulerAngles.y), 1f, "a 35° look does not move the tray");
            _head.rotation = Quaternion.Euler(0f, 80f, 0f);
            t = 0f; while (t < 1.2f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(0f, Mathf.DeltaAngle(80f, _pouch.transform.eulerAngles.y), 6f, "after an 80° turn it comes round in front again");
        }

        // ================================================================== radio
        [UnityTest]
        public IEnumerator Device_HoldY_RadioPanel_ReleaseSends_FourHoldsMedevac()
        {
            var rgo = new GameObject("~Radio");
            _spawned.Add(rgo);
            var radio = rgo.AddComponent<RadioSet>();
            radio.head = _head;
            radio.muted = true;
            yield return null;
            var panel = RadioPanel.Instance;
            Assert.IsNotNull(panel, "the radio brings its Y panel");
            _spawned.Add(panel.gameObject);
            Set(_ldev, "secondaryButton", 1f);
            yield return Frames(2);
            Assert.IsTrue(panel.IsOpen, "holding Y opens the radio panel");
            Assert.AreEqual(4, panel.Report.options.Count, "the 4 MEDEVAC lines");
            Assert.AreEqual(0, panel.Cursor, "next line highlighted");
            Assert.IsTrue(InputLocks.RightStickCaptured, "right stick steers the panel (no teleport meanwhile)");
            // stick down skips the not-yet-allowed lines and stays on the next one; trigger marks it
            SetStick(_rdev, new Vector2(0f, -1f));
            yield return Frames(2);
            SetStick(_rdev, Vector2.zero);
            yield return Frames(2);
            Assert.AreEqual(0, panel.Cursor, "only the next line can be chosen");
            Set(_rdev, "trigger", 1f);
            yield return Frames(2);
            Set(_rdev, "trigger", 0f);
            Assert.AreEqual(0, panel.Marked, "trigger marked the highlighted line");
            Set(_ldev, "secondaryButton", 0f);
            yield return Frames(2);
            Assert.IsFalse(panel.IsOpen, "release Y");
            Assert.AreEqual(1, radio.MedevacStep, "line 1 sent");
            for (int k = 0; k < 3; k++)
            {
                yield return Press(_ldev, "secondaryButton", 0.1f);
            }
            Assert.IsTrue(radio.MedevacSent, "four Y holds = the whole MEDEVAC request");
            Assert.IsFalse(InputLocks.RightStickCaptured);
        }

        // ================================================================== watch button
        [UnityTest]
        public IEnumerator Watch_KitButton_TogglesTheTray()
        {
            yield return MakeKit();
            var w = new GameObject("WristWatch");
            _spawned.Add(w);
            w.transform.SetParent(_left.transform, false);
            var watch = w.AddComponent<WristWatch>();
            watch.hand = _left.transform;
            yield return Frames(2);
            Assert.IsNotNull(watch.KitButton, "АПТЕЧКА on the watch");
            watch.KitButton.Click();
            yield return Frames(2);
            Assert.IsTrue(_pouch.IsOpen, "watch button opened the tray");
            yield return Frames(20); // click cooldown
            watch.KitButton.Click();
            yield return Frames(2);
            Assert.IsFalse(_pouch.IsOpen, "and folds it back");
        }

        // ================================================================== controls tutorial through the device path
        [UnityTest]
        public IEnumerator Tutorial_CompletesThroughRealInput()
        {
            bool seen = GameSettings.TutorialSeen;
            yield return MakeKit();
            var rgo = new GameObject("~Radio");
            _spawned.Add(rgo);
            var radio = rgo.AddComponent<RadioSet>();
            radio.head = _head;
            radio.muted = true;
            var w = new GameObject("WristWatch");
            _spawned.Add(w);
            w.transform.SetParent(_left.transform, false);
            var watch = w.AddComponent<WristWatch>();
            watch.hand = _left.transform;
            yield return Frames(2);
            if (RadioPanel.Instance != null) _spawned.Add(RadioPanel.Instance.gameObject);
            var tut = ControlsTutorial.Begin(null, _head);
            try
            {
                Assert.AreEqual("turn", tut.Current.id);
                Assert.IsTrue(StepGuide.Instance != null && StepGuide.Instance.Text.Contains("стик"), "step line names the stick");
                _head.rotation = Quaternion.Euler(0f, 45f, 0f);
                yield return Until(() => tut.Current.id == "grip", 3f, tut);
                _head.rotation = Quaternion.identity;
                PalmTo(_right, _rdev, tut.Canteen.transform.position);
                yield return Frames(3);
                Assert.AreEqual(tut.Canteen, _right.Hovered, "canteen hovered");
                Set(_rdev, "grip", 1f);
                yield return Until(() => tut.Current.id == "drop", 3f, tut);
                Set(_rdev, "grip", 0f);
                yield return Until(() => tut.Current.id == "trigger", 3f, tut);
                yield return Frames(30);
                PalmTo(_right, _rdev, tut.Canteen.GetComponent<Collider>().bounds.center);
                yield return Frames(3);
                Set(_rdev, "trigger", 1f);
                yield return Until(() => tut.Current.id == "kit", 3f, tut);
                Set(_rdev, "trigger", 0f);
                Assert.IsTrue(StepGuide.Instance.Icons.Contains(ControlIcon.X), "the kit step shows the X icon");
                yield return Press(_ldev, "primaryButton");
                yield return Until(() => tut.Current.id == "kit_item", 3f, tut);
                yield return Frames(20);
                var cat = _pouch.Find<TourniquetCAT>();
                PalmTo(_right, _rdev, Centre(cat));
                yield return Frames(3);
                Set(_rdev, "grip", 1f);
                yield return Frames(3);
                Assert.AreEqual(cat, _right.Held, "CAT taken in the tutorial");
                PalmTo(_right, _rdev, cat.homeSlot.position + _pouch.transform.up * 0.1f);
                yield return Frames(3);
                Set(_rdev, "grip", 0f);
                yield return Until(() => tut.Current.id == "kit_close", 3f, tut);
                yield return Press(_ldev, "primaryButton");
                yield return Until(() => tut.Current.id == "radio", 3f, tut);
                yield return Press(_ldev, "secondaryButton", 0.2f);
                yield return Until(() => tut.Current.id == "watch", 3f, tut);
                // raise the left wrist: the watch face toward the eyes, ~30 cm away
                Vector3 at = _head.position + new Vector3(-0.05f, -0.22f, 0.25f);
                Vector3 toHead = (_head.position - at).normalized;
                PadOf(_ldev).rot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, toHead), toHead);
                PadOf(_ldev).pos = at;
                Push(_ldev);
                yield return Until(() => tut.Current.id == "done", 4f, tut);
                yield return Until(() => tut == null || tut.Finished, 5f, tut);
                Assert.IsTrue(tut == null || !tut.Skipped, "completed, not skipped");
                Assert.IsTrue(GameSettings.TutorialSeen, "remembered: it will not start by itself again");
            }
            finally
            {
                GameSettings.TutorialSeen = seen;
                if (tut != null) Object.Destroy(tut.gameObject);
            }
        }

        static IEnumerator Until(System.Func<bool> cond, float timeout, ControlsTutorial tut)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(cond(), "tutorial stuck at " + (tut != null && tut.Current != null ? tut.Current.id : "-"));
        }
    }
}
