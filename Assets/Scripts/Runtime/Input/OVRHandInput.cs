using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// Quest input: Touch controllers via OVRInput and tracked hands via OVRHand, auto-switching
    /// to whichever source is active. Pose comes from the OVRCameraRig hand anchor (world space).
    /// w10: the Input System XR controller of the same hand (OpenXR Touch / Touch Plus profile) is read as a second
    /// source and merged (max of both): a profile/binding problem on one path can no longer leave the hand without grip.
    /// Source switches and the first presses of each source are logged ([FMVR] input …) so a device log shows which
    /// inputs really arrived.
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
        public bool SecondaryButton { get; private set; }
        public bool StickClick { get; private set; }
        public Vector2 Thumbstick { get; private set; }

        readonly Transform _anchor;
        readonly OVRHand _hand;
        readonly OVRInput.Controller _touch;
        readonly PoseVelocityTracker _vel = new PoseVelocityTracker();
        readonly XRControllerHandInput _xr;
        float _hapticUntil;
        HandSource _loggedSource = (HandSource)(-1);
        bool _loggedOvrGrip, _loggedOvrTrigger, _loggedXrGrip, _loggedXrTrigger;

        public OVRHandInput(Handedness handedness, Transform handAnchor, OVRHand hand)
        {
            Handedness = handedness;
            _anchor = handAnchor;
            _hand = hand;
            _touch = handedness == Handedness.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            _xr = new XRControllerHandInput(handedness, handAnchor != null ? handAnchor.parent : null);
        }

        string Side => Handedness == Handedness.Left ? "L" : "R";

        public void Tick(float dt)
        {
            _xr.Tick(dt);
            bool handTracked = _hand != null && _hand.IsTracked && _hand.IsDataHighConfidence;
            bool ovrController = !handTracked && OVRInput.IsControllerConnected(_touch);
            bool xrController = !handTracked && _xr.Device != null && _xr.IsTracked;

            if (ovrController || xrController)
            {
                Source = HandSource.Controller;
                float og = ovrController ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _touch) : 0f;
                float ot = ovrController ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _touch) : 0f;
                Grip = Mathf.Max(og, _xr.Grip);
                Trigger = Mathf.Max(ot, _xr.Trigger);
                PrimaryButton = (ovrController && OVRInput.Get(OVRInput.Button.One, _touch)) || _xr.PrimaryButton;
                SecondaryButton = (ovrController && OVRInput.Get(OVRInput.Button.Two, _touch)) || _xr.SecondaryButton;
                StickClick = (ovrController && OVRInput.Get(OVRInput.Button.PrimaryThumbstick, _touch)) || _xr.StickClick;
                Vector2 os = ovrController ? OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, _touch) : Vector2.zero;
                Thumbstick = os.sqrMagnitude >= _xr.Thumbstick.sqrMagnitude ? os : _xr.Thumbstick;
                IsTracked = (ovrController && OVRInput.GetControllerPositionTracked(_touch)) || xrController;
                LogFirstPresses(og, ot);
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
                PrimaryButton = SecondaryButton = StickClick = false;
                Thumbstick = Vector2.zero;
                IsTracked = true;
            }
            else
            {
                Source = HandSource.None;
                Grip = Trigger = 0f;
                PrimaryButton = SecondaryButton = StickClick = false;
                Thumbstick = Vector2.zero;
                IsTracked = false;
            }

            if (Source != _loggedSource)
            {
                _loggedSource = Source;
                Debug.Log($"[FMVR] input {Side}: source {Source} (ovr connected {ovrController}, input-system device {(_xr.Device != null ? _xr.Device.layout : "none")}, hand tracked {handTracked})");
            }

            if (_anchor != null && (ovrController || handTracked || !_xr.HasPose))
            {
                Position = _anchor.position;
                Rotation = _anchor.rotation;
            }
            else if (_xr.HasPose)
            {
                Position = _xr.Position;
                Rotation = _xr.Rotation;
            }
            _vel.Sample(Position, Rotation, dt);

            if (_hapticUntil > 0f && Time.unscaledTime >= _hapticUntil)
            {
                _hapticUntil = 0f;
                OVRInput.SetControllerVibration(0f, 0f, _touch);
            }
        }

        void LogFirstPresses(float ovrGrip, float ovrTrigger)
        {
            if (!_loggedOvrGrip && ovrGrip > 0.5f) { _loggedOvrGrip = true; Debug.Log($"[FMVR] input {Side}: first GRIP via OVRInput ({ovrGrip:F2})"); }
            if (!_loggedOvrTrigger && ovrTrigger > 0.5f) { _loggedOvrTrigger = true; Debug.Log($"[FMVR] input {Side}: first TRIGGER via OVRInput ({ovrTrigger:F2})"); }
            if (!_loggedXrGrip && _xr.Grip > 0.5f) { _loggedXrGrip = true; Debug.Log($"[FMVR] input {Side}: first GRIP via Input System {_xr.Device?.layout} ({_xr.Grip:F2})"); }
            if (!_loggedXrTrigger && _xr.Trigger > 0.5f) { _loggedXrTrigger = true; Debug.Log($"[FMVR] input {Side}: first TRIGGER via Input System {_xr.Device?.layout} ({_xr.Trigger:F2})"); }
        }

        public void Haptic(float amplitude, float duration)
        {
            if (Source != HandSource.Controller) return; // tracked hands have no actuator
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), _touch);
            _hapticUntil = Mathf.Max(_hapticUntil, Time.unscaledTime + Mathf.Max(0.01f, duration));
        }
    }
}
