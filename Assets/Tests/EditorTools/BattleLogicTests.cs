using System.Linq;
using FieldMedic.Battle;
using NUnit.Framework;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>w3 pure battle logic: exposure window, event table, intensity/focus, smoke line blocking, splines.</summary>
    public class BattleLogicTests
    {
        // ------------------------------------------------------------------ exposure
        [Test]
        public void Exposure_FourSecondsContinuous_IsHit()
        {
            var t = new ExposureTracker(10f, 4f);
            bool hitTick = false;
            for (int i = 0; i < 39; i++) hitTick |= t.Tick(true, 0.1f);
            Assert.IsFalse(t.IsHit, "3.9 s exposed must not be a hit yet");
            Assert.AreEqual(3.9f, t.Exposed, 0.02f);
            hitTick |= t.Tick(true, 0.1f);
            Assert.IsTrue(t.IsHit);
            Assert.IsTrue(hitTick, "the crossing tick reports the hit once");
        }

        [Test]
        public void Exposure_IsCumulativeInsideTheWindow()
        {
            var t = new ExposureTracker(10f, 4f);
            // 4 peeks of 0.9 s with 1 s in cover between them = 3.6 s exposed in ~7.6 s → still alive
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i < 9; i++) t.Tick(true, 0.1f);
                for (int i = 0; i < 10; i++) t.Tick(false, 0.1f);
            }
            Assert.IsFalse(t.IsHit);
            Assert.AreEqual(3.6f, t.Exposed, 0.05f);
            // the fifth peek crosses 4 s cumulative
            for (int i = 0; i < 5; i++) t.Tick(true, 0.1f);
            Assert.IsTrue(t.IsHit);
        }

        [Test]
        public void Exposure_OldPeeksLeaveTheWindow()
        {
            var t = new ExposureTracker(10f, 4f);
            for (int i = 0; i < 35; i++) t.Tick(true, 0.1f);    // 3.5 s exposed
            for (int i = 0; i < 110; i++) t.Tick(false, 0.1f);  // 11 s in cover
            Assert.AreEqual(0f, t.Exposed, 0.05f, "exposure older than the window is forgotten");
            for (int i = 0; i < 35; i++) t.Tick(true, 0.1f);
            Assert.IsFalse(t.IsHit, "two 3.5 s exposures 11 s apart are not a hit");
            Assert.AreEqual(7f, t.TotalExposed, 0.05f);
        }

        [Test]
        public void Exposure_LongFrameIsSplitAcrossBins()
        {
            var t = new ExposureTracker(10f, 4f);
            t.Tick(true, 2.5f);
            Assert.AreEqual(2.5f, t.Exposed, 0.02f);
            t.Tick(false, 9f);
            Assert.AreEqual(1f, t.Exposed, 0.15f, "the last second of the 2.5 s is still inside the 10 s window");
            t.Tick(false, 1.5f);
            Assert.Less(t.Exposed, 0.15f);
            t.Reset();
            Assert.AreEqual(0f, t.Exposed);
            Assert.IsFalse(t.IsHit);
        }

        [Test]
        public void Exposure_NeverExposed_NeverHit()
        {
            var t = new ExposureTracker(10f, 4f);
            for (int i = 0; i < 6000; i++) Assert.IsFalse(t.Tick(false, 0.1f));
            Assert.AreEqual(0f, t.Exposure01);
        }

        // ------------------------------------------------------------------ events
        [Test]
        public void Picker_RespectsWeightsAndNeverTriplesAnEvent()
        {
            var w = new[] { 0f, 0f, 0f, 3f, 1f, 0f, 0f }; // only MG bursts and drone dives
            var p = new BattleEventPicker(w, 7);
            int mg = 0, drone = 0;
            BattleEventType a = (BattleEventType)(-1), b = (BattleEventType)(-2);
            for (int i = 0; i < 4000; i++)
            {
                var e = p.Next();
                Assert.IsTrue(e == BattleEventType.MgBurst || e == BattleEventType.DroneDive, "zero-weight event picked: " + e);
                Assert.IsFalse(e == a && e == b, "the same event three times in a row");
                b = a; a = e;
                if (e == BattleEventType.MgBurst) mg++; else drone++;
            }
            // 3:1 weights, damped by the no-triple rule
            Assert.Greater(mg, drone * 1.25f, "weights must bias the table");
        }

        [Test]
        public void Picker_IntervalShrinksWithIntensity()
        {
            var p = new BattleEventPicker(new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f }, 3) { minInterval = 5f, maxInterval = 20f };
            float calm = 0f, heavy = 0f;
            for (int i = 0; i < 500; i++) { calm += p.NextInterval(1f); heavy += p.NextInterval(2f); }
            calm /= 500f; heavy /= 500f;
            Assert.AreEqual(12.5f, calm, 1.2f, "intensity 1: 5–20 s");
            Assert.Less(heavy, calm * 0.7f);
            for (int i = 0; i < 200; i++) Assert.GreaterOrEqual(p.NextInterval(3f), 2.5f);
        }

        [Test]
        public void Picker_IsDeterministicForASeed()
        {
            var w = new[] { 2.5f, 2f, 1.2f, 3f, 1.5f, 2.2f, 1f };
            var a = new BattleEventPicker(w, 42); var b = new BattleEventPicker(w, 42);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Next(), b.Next());
        }

        // ------------------------------------------------------------------ intensity
        [Test]
        public void Focus_LowersNearIntensity_BackgroundNeverSilent()
        {
            var c = new IntensityCurve { baseLevel = 1f, swellAmplitude = 0.3f, focusLevel = 0.3f, backgroundFloor = 0.45f };
            for (float t = 0f; t < 600f; t += 0.5f) Assert.GreaterOrEqual(c.Background(t), 0.45f);
            c.RequestFocus(100f, 4f);
            Assert.IsTrue(c.InFocus(101f));
            Assert.LessOrEqual(c.Near(101f), 0.3f + 1e-4f);
            Assert.GreaterOrEqual(c.Background(101f), 0.45f, "background keeps going during focus");
            Assert.IsFalse(c.InFocus(104.5f));
            Assert.AreEqual(c.Background(105f), c.Near(105f), 1e-4f, "back to normal after the window");
        }

        [Test]
        public void Focus_IsCappedAtFiveSeconds()
        {
            var c = new IntensityCurve();
            c.RequestFocus(0f, 30f);
            Assert.IsTrue(c.InFocus(4.9f));
            Assert.IsFalse(c.InFocus(5.1f));
            c.RequestFocus(5.1f, 3f);
            Assert.AreEqual(2, c.FocusRequests);
            Assert.AreEqual(3f, c.FocusLeft(5.1f), 1e-4f);
        }

        // ------------------------------------------------------------------ smoke
        [Test]
        public void Smoke_ChordLength()
        {
            Assert.AreEqual(4f, SmokeScreens.SegmentSphereChord(new Vector3(-10, 0, 0), new Vector3(10, 0, 0), Vector3.zero, 2f), 1e-3f);
            Assert.AreEqual(0f, SmokeScreens.SegmentSphereChord(new Vector3(-10, 3, 0), new Vector3(10, 3, 0), Vector3.zero, 2f), 1e-3f);
            // segment ending inside the sphere
            Assert.AreEqual(2f, SmokeScreens.SegmentSphereChord(new Vector3(-10, 0, 0), Vector3.zero, Vector3.zero, 2f), 1e-3f);
        }

        [Test]
        public void Smoke_BlocksLineOnlyWhenDense()
        {
            SmokeScreens.Clear();
            try
            {
                var shooter = new Vector3(0f, 1.2f, 60f);
                var head = new Vector3(0f, 1.6f, 0f);
                Assert.IsFalse(SmokeScreens.Blocks(shooter, head));
                int id = SmokeScreens.Add(new Vector3(0f, 1.5f, 10f), 4f, 0.2f);
                Assert.IsFalse(SmokeScreens.Blocks(shooter, head), "thin young smoke does not hide the player");
                SmokeScreens.Set(id, new Vector3(0f, 1.5f, 10f), 4f, 1f);
                Assert.IsTrue(SmokeScreens.Blocks(shooter, head), "a dense cloud on the line blocks it");
                Assert.IsFalse(SmokeScreens.Blocks(shooter + new Vector3(40f, 0f, 0f), head + new Vector3(40f, 0f, 0f)), "a line beside the cloud stays open");
                SmokeScreens.Remove(id);
                Assert.AreEqual(0, SmokeScreens.ActiveCount);
            }
            finally { SmokeScreens.Clear(); }
        }

        // ------------------------------------------------------------------ splines / math
        [Test]
        public void Spline_LoopPassesThroughPointsAndIsClosed()
        {
            var pts = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 10), new Vector3(0, 0, 10) };
            for (int i = 0; i < pts.Length; i++) Assert.Less(Vector3.Distance(Spline.Loop(pts, i), pts[i]), 1e-4f);
            Assert.Less(Vector3.Distance(Spline.Loop(pts, 4f), pts[0]), 1e-4f);
            float len = Spline.LoopLength(pts);
            Assert.Greater(len, 30f); Assert.Less(len, 45f);
        }

        [Test]
        public void Blast_HapticsAndMuffleFallOffWithDistance()
        {
            Assert.Greater(BattleMath.BlastHaptic(5f, 1f), BattleMath.BlastHaptic(30f, 1f));
            Assert.AreEqual(0f, BattleMath.BlastHaptic(80f, 1f));
            Assert.Greater(BattleMath.BlastHaptic(80f, 4f), 0f, "a heavy bomb is felt farther away");
            float near = BattleMath.MuffleSeconds(4f, 1f), mid = BattleMath.MuffleSeconds(12f, 1f);
            Assert.That(near, Is.InRange(3f, 6f)); Assert.That(mid, Is.InRange(3f, 6f));
            Assert.Greater(near, mid);
            Assert.AreEqual(0f, BattleMath.MuffleSeconds(25f, 1f), "no muffle beyond ~18 m");
        }

        [Test]
        public void InView_Cone()
        {
            Assert.IsTrue(BattleMath.InView(Vector3.zero, Vector3.forward, new Vector3(5f, 0f, 50f)));
            Assert.IsFalse(BattleMath.InView(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -50f)));
            Assert.IsFalse(BattleMath.InView(Vector3.zero, Vector3.forward, new Vector3(80f, 0f, 20f), 60f));
        }

        [Test]
        public void Settings_DefaultsMatchTheDesign()
        {
            var s = ScriptableObject.CreateInstance<BattleSettings>();
            Assert.AreEqual(5f, s.eventInterval.x); Assert.AreEqual(20f, s.eventInterval.y);
            Assert.AreEqual(4f, s.exposureLimit); Assert.AreEqual(10f, s.exposureWindow);
            Assert.AreEqual(System.Enum.GetValues(typeof(BattleEventType)).Length, s.eventWeights.Length);
            Assert.That(s.focusSeconds, Is.InRange(3f, 5f));
            Assert.That(s.nearEnemies + s.nearFriendlies, Is.InRange(8, 12));
            Assert.That(s.crowdSoldiers, Is.InRange(30, 60));
            Object.DestroyImmediate(s);
        }
    }
}
