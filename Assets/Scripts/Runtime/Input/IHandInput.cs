using UnityEngine;

namespace FieldMedic.Input
{
    public enum Handedness { Left = 0, Right = 1 }

    /// <summary>Which device currently drives a hand.</summary>
    public enum HandSource { None, Controller, TrackedHand, Desktop, Scripted }

    /// <summary>
    /// Raw per-hand input every interaction talks to. World-space pose.
    /// Implementations: OVRHandInput (Quest), DesktopHandInput (editor), ScriptedHandInput (tests/bots).
    /// </summary>
    public interface IHandInput
    {
        Handedness Handedness { get; }
        HandSource Source { get; }
        bool IsTracked { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        Vector3 Velocity { get; }
        /// <summary>Angular velocity, world space, radians/s.</summary>
        Vector3 AngularVelocity { get; }
        /// <summary>0..1, grab (controller grip / hand fist).</summary>
        float Grip { get; }
        /// <summary>0..1, fine action (controller trigger / index pinch).</summary>
        float Trigger { get; }
        /// <summary>A (right controller) / X (left controller). w10: left X = the kit tray.</summary>
        bool PrimaryButton { get; }
        /// <summary>B (right controller) / Y (left controller). w10: hold left Y = radio push-to-talk.</summary>
        bool SecondaryButton { get; }
        /// <summary>Thumbstick pressed in (left: headlamp).</summary>
        bool StickClick { get; }
        /// <summary>Primary thumbstick (zero for tracked hands / desktop). Used for pull-to-crawl.</summary>
        Vector2 Thumbstick { get; }
        /// <summary>Called once per frame by HandRig before anyone reads the values.</summary>
        void Tick(float dt);
        void Haptic(float amplitude, float duration);
    }

    /// <summary>Velocity estimation from successive poses (shared by all implementations).</summary>
    public sealed class PoseVelocityTracker
    {
        Vector3 _lastPos;
        Quaternion _lastRot = Quaternion.identity;
        bool _has;
        public Vector3 Velocity { get; private set; }
        public Vector3 AngularVelocity { get; private set; }

        public void Reset() { _has = false; Velocity = Vector3.zero; AngularVelocity = Vector3.zero; }

        public void Sample(Vector3 pos, Quaternion rot, float dt)
        {
            if (_has && dt > 1e-5f)
            {
                // light smoothing: tracked poses are noisy, throws should not be
                Velocity = Vector3.Lerp(Velocity, (pos - _lastPos) / dt, 0.6f);
                var delta = rot * Quaternion.Inverse(_lastRot);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                bool bad = float.IsNaN(axis.x) || float.IsInfinity(axis.x);
                Vector3 w = bad ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / dt);
                AngularVelocity = Vector3.Lerp(AngularVelocity, w, 0.6f);
            }
            _lastPos = pos; _lastRot = rot; _has = true;
        }
    }
}
