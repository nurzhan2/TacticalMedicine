using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// Quest input: Touch controllers via OVRInput and tracked hands via OVRHand, auto-switching
    /// to whichever source is active. Pose comes from the OVRCameraRig hand anchor (world space).
    /// </summary>
    public sealed class OVRHandInput : IHandInput
    {
        public Handedness Handedness { get; }
        public HandSource Source { get; private set; } = HandSource.None;
        public bool IsTracked { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 Velocity => _vel.Velocity;
        public Vector3 AngularVelocity => _vel.AngularVelocity;
        public float Grip { get; private set; }
        public float Trigger { get; private set; }
        public bool PrimaryButton { get; private set; }
        public Vector2 Thumbstick { get; private set; }

        readonly Transform _anchor;
        readonly OVRHand _hand;
        readonly OVRInput.Controller _touch;
        readonly PoseVelocityTracker _vel = new PoseVelocityTracker();
        float _hapticUntil;

        public OVRHandInput(Handedness handedness, Transform handAnchor, OVRHand hand)
        {
            Handedness = handedness;
            _anchor = handAnchor;
            _hand = hand;
            _touch = handedness == Handedness.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        }

        public void Tick(float dt)
        {
            bool handTracked = _hand != null && _hand.IsTracked && _hand.IsDataHighConfidence;
            bool controller = !handTracked && OVRInput.IsControllerConnected(_touch);

            if (controller)
            {
                Source = HandSource.Controller;
                Grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _touch);
                Trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _touch);
                PrimaryButton = OVRInput.Get(OVRInput.Button.One, _touch);
                Thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, _touch);
                IsTracked = OVRInput.GetControllerPositionTracked(_touch);
            }
            else if (handTracked)
            {
                Source = HandSource.TrackedHand;
                // fist ~ middle/ring/pinky curling toward the thumb; pinch strength is the closest proxy
                float middle = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
                float ring = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
                float pinky = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);
                Grip = Mathf.Clamp01(Mathf.Max(middle, (middle + ring + pinky) / 2.2f));
                Trigger = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                PrimaryButton = false;
                Thumbstick = Vector2.zero;
                IsTracked = true;
            }
            else
            {
                Source = HandSource.None;
                Grip = Trigger = 0f;
                PrimaryButton = false;
                Thumbstick = Vector2.zero;
                IsTracked = false;
            }

            if (_anchor != null)
            {
                Position = _anchor.position;
                Rotation = _anchor.rotation;
            }
            _vel.Sample(Position, Rotation, dt);

            if (_hapticUntil > 0f && Time.unscaledTime >= _hapticUntil)
            {
                _hapticUntil = 0f;
                OVRInput.SetControllerVibration(0f, 0f, _touch);
            }
        }

        public void Haptic(float amplitude, float duration)
        {
            if (Source != HandSource.Controller) return; // tracked hands have no actuator
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), _touch);
            _hapticUntil = Mathf.Max(_hapticUntil, Time.unscaledTime + Mathf.Max(0.01f, duration));
        }
    }
}
