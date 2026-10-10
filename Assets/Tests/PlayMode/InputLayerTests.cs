using FieldMedic.Input;
using FieldMedic.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace FieldMedic.Tests
{
    public class InputLayerTests
    {
        [Test]
        public void VelocityTracker_Estimates_Linear_And_Angular_Velocity()
        {
            var t = new PoseVelocityTracker();
            const float dt = 1f / 72f;
            for (int i = 0; i <= 20; i++)
            {
                // 1 m/s along +X while spinning 90 deg/s around +Y
                t.Sample(new Vector3(i * dt, 0f, 0f), Quaternion.Euler(0f, 90f * i * dt, 0f), dt);
            }
            Assert.AreEqual(1f, t.Velocity.x, 0.02f);
            Assert.AreEqual(0f, t.Velocity.y, 0.001f);
            Assert.AreEqual(Mathf.PI / 2f, t.AngularVelocity.y, 0.03f);

            t.Reset();
            Assert.AreEqual(Vector3.zero, t.Velocity);
        }

        [Test]
        public void VelocityTracker_First_Sample_Has_No_Velocity()
        {
            var t = new PoseVelocityTracker();
            t.Sample(new Vector3(5f, 5f, 5f), Quaternion.identity, 0.01f);
            Assert.AreEqual(Vector3.zero, t.Velocity, "teleport to the first pose must not read as a throw");
        }

        [Test]
        public void DesktopHand_Follows_Camera_Offset()
        {
            var cam = new GameObject("Cam").transform;
            try
            {
                cam.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f));
                var h = new DesktopHandInput(Handedness.Right, cam) { LocalOffset = new Vector3(0f, 0f, 0.5f) };
                h.Tick(0.02f);
                // camera looks along +X, so 0.5 m forward is +X
                Assert.AreEqual(0.5f, h.Position.x, 1e-4f);
                Assert.AreEqual(1.6f, h.Position.y, 1e-4f);
                Assert.IsTrue(h.IsTracked);
                Assert.AreEqual(HandSource.Desktop, h.Source);
            }
            finally { Object.DestroyImmediate(cam.gameObject); }
        }

        [Test]
        public void Haptics_Presets_Are_Clamped_And_Scaled()
        {
            var s = new ScriptedHandInput(Handedness.Left, Vector3.zero);
            Haptics.Play(s, Haptics.Grab, 10f);
            Assert.AreEqual(1, s.HapticCount);
            Assert.AreEqual(1f, s.LastHapticAmplitude, 1e-5f);
            Haptics.Play(s, Haptics.Heartbeat, 0.5f);
            Assert.AreEqual(Haptics.Heartbeat.Amplitude * 0.5f, s.LastHapticAmplitude, 1e-5f);
            Haptics.Play(null, Haptics.Grab); // no hand → no throw
        }
    }
}
