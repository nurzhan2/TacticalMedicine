using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace FieldMedic.Input
{
    /// <summary>
    /// w10: a hand driven by an Input System XR controller (OpenXR "Oculus Touch" / "Meta Quest Touch Plus" layouts on the
    /// headset, any <see cref="XRController"/> with LeftHand/RightHand usage, or a test device). Controls are looked up by
    /// name (grip, trigger, primaryButton, secondaryButton, thumbstick/primary2DAxis, devicePosition, deviceRotation,
    /// isTracked) so it works with every profile Unity's OpenXR plugin registers.
    /// Two uses: the second input source merged into <see cref="OVRHandInput"/> on the Quest (if OVRInput ever reads
    /// zeros for a profile, the Input System values still arrive), and the PlayMode tests that drive the real
    /// device → Input System → IHandInput → HandGrabber path instead of bot shortcuts.
    /// Pose: device space → world through `trackingSpace` (the OVRCameraRig TrackingSpace); identity when null.
    /// </summary>
    public sealed class XRControllerHandInput : IHandInput
    {
        public Handedness Handedness { get; }
        public HandSource Source => Device != null ? HandSource.Controller : HandSource.None;
        public bool IsTracked { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 Velocity => _vel.Velocity;
        public Vector3 AngularVelocity => _vel.AngularVelocity;
        public float Grip { get; private set; }
        public float Trigger { get; private set; }
        public bool PrimaryButton { get; private set; }
        public bool SecondaryButton { get; private set; }
        public bool StickClick { get; private set; }
        public Vector2 Thumbstick { get; private set; }

        public InputDevice Device { get; private set; }
        public Transform TrackingSpace { get; set; }
        /// <summary>The device has a pose control (false: buttons only, pose must come from elsewhere).</summary>
        public bool HasPose => _pos != null;

        readonly bool _autoFind;
        readonly PoseVelocityTracker _vel = new PoseVelocityTracker();
        AxisControl _grip, _trigger;
        ButtonControl _gripPressed, _triggerPressed, _primary, _secondary, _stickClick, _tracked;
        Vector2Control _stick;
        Vector3Control _pos;
        QuaternionControl _rot;
        float _findAt;

        /// <summary>`device` null → the first XRController with the matching hand usage (re-checked while missing).</summary>
        public XRControllerHandInput(Handedness handedness, Transform trackingSpace = null, InputDevice device = null)
        {
            Handedness = handedness;
            TrackingSpace = trackingSpace;
            _autoFind = device == null;
            Bind(device);
        }

        public void Bind(InputDevice d)
        {
            Device = d;
            _grip = _trigger = null;
            _gripPressed = _triggerPressed = _primary = _secondary = _stickClick = _tracked = null;
            _stick = null; _pos = null; _rot = null;
            if (d == null) return;
            _grip = Find<AxisControl>(d, "grip", "gripValue");
            _trigger = Find<AxisControl>(d, "trigger", "triggerValue");
            _gripPressed = Find<ButtonControl>(d, "gripPressed", "gripButton");
            _triggerPressed = Find<ButtonControl>(d, "triggerPressed", "triggerButton");
            _primary = Find<ButtonControl>(d, "primaryButton");
            _secondary = Find<ButtonControl>(d, "secondaryButton");
            _stickClick = Find<ButtonControl>(d, "thumbstickClicked", "primary2DAxisClick", "joystickClicked");
            _stick = Find<Vector2Control>(d, "thumbstick", "primary2DAxis", "joystick");
            _tracked = Find<ButtonControl>(d, "isTracked");
            _pos = Find<Vector3Control>(d, "devicePosition", "pointerPosition");
            _rot = Find<QuaternionControl>(d, "deviceRotation", "pointerRotation");
        }

        static T Find<T>(InputDevice d, params string[] names) where T : InputControl
        {
            foreach (var n in names)
            {
                var c = d.TryGetChildControl<T>(n);
                if (c != null) return c;
            }
            return null;
        }

        /// <summary>First connected XR controller for this hand (OpenXR Touch layouts derive from XRController).</summary>
        public static InputDevice FindDevice(Handedness h)
        {
            var usage = h == Handedness.Left ? CommonUsages.LeftHand : CommonUsages.RightHand;
            foreach (var d in InputSystem.devices)
            {
                if (!(d is XRController) || !d.added) continue;
                foreach (var u in d.usages) if (u == usage) return d;
            }
            return null;
        }

        public void Tick(float dt)
        {
            if ((Device == null || !Device.added) && _autoFind && Time.unscaledTime >= _findAt)
            {
                _findAt = Time.unscaledTime + 1f; // device list changes rarely: look once a second
                Bind(FindDevice(Handedness));
            }
            if (Device == null || !Device.added)
            {
                Grip = Trigger = 0f;
                PrimaryButton = SecondaryButton = StickClick = false;
                Thumbstick = Vector2.zero;
                IsTracked = false;
                return;
            }
            Grip = Mathf.Max(_grip != null ? _grip.ReadValue() : 0f, _gripPressed != null && _gripPressed.isPressed ? 1f : 0f);
            Trigger = Mathf.Max(_trigger != null ? _trigger.ReadValue() : 0f, _triggerPressed != null && _triggerPressed.isPressed ? 1f : 0f);
            PrimaryButton = _primary != null && _primary.isPressed;
            SecondaryButton = _secondary != null && _secondary.isPressed;
            StickClick = _stickClick != null && _stickClick.isPressed;
            Thumbstick = _stick != null ? _stick.ReadValue() : Vector2.zero;
            IsTracked = _tracked == null || _tracked.isPressed;
            if (_pos != null)
            {
                Vector3 p = _pos.ReadValue();
                Quaternion r = _rot != null ? _rot.ReadValue() : Quaternion.identity;
                if (r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f) r = Quaternion.identity;
                Position = TrackingSpace != null ? TrackingSpace.TransformPoint(p) : p;
                Rotation = TrackingSpace != null ? TrackingSpace.rotation * r : r;
            }
            _vel.Sample(Position, Rotation, dt);
        }

        public void Haptic(float amplitude, float duration)
        {
            if (Device is XRControllerWithRumble rumble) rumble.SendImpulse(Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
            LastHapticAmplitude = amplitude;
            HapticCount++;
        }

        public int HapticCount { get; private set; }
        public float LastHapticAmplitude { get; private set; }
    }
}
