using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// Editor/desktop hand. Its pose is an offset in camera space driven by <see cref="DesktopSimulator"/>,
    /// so the hand follows the head like a real arm.
    /// </summary>
    public sealed class DesktopHandInput : IHandInput
    {
        public Handedness Handedness { get; }
        public HandSource Source => HandSource.Desktop;
        public bool IsTracked => true;
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 Velocity => _vel.Velocity;
        public Vector3 AngularVelocity => _vel.AngularVelocity;
        public float Grip { get; set; }
        public float Trigger { get; set; }
        public bool PrimaryButton { get; set; }

        /// <summary>Hand position in camera-local space (metres).</summary>
        public Vector3 LocalOffset;
        /// <summary>Extra wrist roll (degrees) around the hand forward axis (Q/E).</summary>
        public float WristRoll;
        /// <summary>Rest pose when this hand is not the active one.</summary>
        public readonly Vector3 RestOffset;

        public Transform Camera { get; set; }
        public float LastHapticAmplitude { get; private set; }
        public float HapticUntil { get; private set; }

        readonly PoseVelocityTracker _vel = new PoseVelocityTracker();

        public DesktopHandInput(Handedness handedness, Transform camera)
        {
            Handedness = handedness;
            Camera = camera;
            float side = handedness == Handedness.Left ? -1f : 1f;
            RestOffset = new Vector3(0.18f * side, -0.32f, 0.38f);
            LocalOffset = RestOffset;
        }

        public void Tick(float dt)
        {
            if (Camera != null)
            {
                Position = Camera.TransformPoint(LocalOffset);
                // palm faces forward-down slightly, like reaching toward something in front
                Rotation = Camera.rotation * Quaternion.Euler(20f, 0f, 0f) * Quaternion.AngleAxis(WristRoll, Vector3.forward);
            }
            _vel.Sample(Position, Rotation, dt);
        }

        public void Haptic(float amplitude, float duration)
        {
            // no actuator on desktop; DesktopSimulator shows it as a small on-screen flash
            LastHapticAmplitude = amplitude;
            HapticUntil = Time.unscaledTime + duration;
        }
    }
}
