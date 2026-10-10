using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>Hand driven by code (PlayMode tests, bots, ghost-hand demos). Set the properties.</summary>
    public sealed class ScriptedHandInput : IHandInput
    {
        public Handedness Handedness { get; }
        public HandSource Source => HandSource.Scripted;
        public bool IsTracked { get; set; } = true;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public Vector3 Velocity => _vel.Velocity;
        public Vector3 AngularVelocity => _vel.AngularVelocity;
        public float Grip { get; set; }
        public float Trigger { get; set; }
        public bool PrimaryButton { get; set; }
        public Vector2 Thumbstick { get; set; }

        /// <summary>Total haptic requests received (tests assert that feedback happened).</summary>
        public int HapticCount { get; private set; }
        public float LastHapticAmplitude { get; private set; }

        readonly PoseVelocityTracker _vel = new PoseVelocityTracker();

        public ScriptedHandInput(Handedness handedness, Vector3 position)
        {
            Handedness = handedness;
            Position = position;
        }

        public void Tick(float dt) => _vel.Sample(Position, Rotation, dt);

        public void Haptic(float amplitude, float duration)
        {
            HapticCount++;
            LastHapticAmplitude = amplitude;
        }
    }
}
