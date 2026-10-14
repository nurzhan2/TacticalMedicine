using FieldMedic.EditorTools.SceneBuilders;
using FieldMedic.Environment;
using NUnit.Framework;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>w2: pure layout / generator math of the V2 battlefield (no scene needed).</summary>
    public class BattlefieldLayoutTests
    {
        [Test]
        public void Centre_Is_Flat_Outside_Craters()
        {
            foreach (var p in new[] { Vector3.zero, BattlefieldLayout.TreatmentSpot, BattlefieldLayout.DoorwayCentre, new Vector3(-17f, 0f, 7f) })
                Assert.AreEqual(0f, BattlefieldLayout.GroundHeight(p.x, p.z), 0.02f, p.ToString());
        }

        [Test]
        public void Craters_Are_Bowls_With_Raised_Rims()
        {
            foreach (var c in BattlefieldLayout.Craters)
            {
                float centre = BattlefieldLayout.CraterOffset(c.x, c.y, out float burn);
                float rim = BattlefieldLayout.CraterOffset(c.x + c.z * 1.05f, c.y, out _);
                Assert.Less(centre, -0.25f * c.w, "bowl");
                Assert.Greater(rim, centre + 0.3f * c.w, "rim");
                Assert.Greater(burn, 0.5f, "scorched centre");
            }
        }

        [Test]
        public void No_Crater_Cuts_Into_House_A_Or_The_Doorway()
        {
            var a0 = BattlefieldLayout.HouseAMin; var a1 = BattlefieldLayout.HouseAMax;
            for (float x = a0.x - 1f; x <= a1.x + 1f; x += 0.5f)
            for (float z = a0.y - 1f; z <= a1.y + 1f; z += 0.5f)
                Assert.AreEqual(0f, BattlefieldLayout.CraterOffset(x, z, out _), 0.01f, $"crater at {x},{z}");
        }

        [Test]
        public void Grid_Is_Dense_At_The_Centre_And_Covers_400m()
        {
            Assert.AreEqual(BattlefieldLayout.TerrainHalfSize, BattlefieldTerrain.Map(1f), 0.01f);
            Assert.AreEqual(-BattlefieldLayout.TerrainHalfSize, BattlefieldTerrain.Map(-1f), 0.01f);
            float step = BattlefieldTerrain.Map(1f / 80f) - BattlefieldTerrain.Map(0f);
            Assert.Less(step, 0.7f, "centre spacing");
            // craters around the position (out to ~20 m) still get sub-metre resolution
            float u30 = 0f; while (BattlefieldTerrain.Map(u30) < 20f) u30 += 1f / 80f;
            Assert.Less(BattlefieldTerrain.Map(u30) - BattlefieldTerrain.Map(u30 - 1f / 80f), 1.0f, "spacing at 20 m");
        }

        [Test]
        public void Interval_Subtract_Cuts_Openings()
        {
            var a = new System.Collections.Generic.List<Vector2> { new Vector2(0f, 3f) };
            var b = new System.Collections.Generic.List<Vector2> { new Vector2(1f, 2f) };
            var r = RuinBuilder.Subtract(a, b);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(new Vector2(0f, 1f), r[0]);
            Assert.AreEqual(new Vector2(2f, 3f), r[1]);
            Assert.AreEqual(0, RuinBuilder.Subtract(b, a).Count);
        }

        [Test]
        public void Tracks_Are_Dirt_And_The_Steppe_Is_Mostly_Grass()
        {
            var t = BattlefieldTerrain.Track[3];
            Assert.Greater(BattlefieldTerrain.DirtAt(t.x, t.z, out _, out _), 0.4f);
            int grass = 0, n = 0;
            for (float x = -150f; x <= 150f; x += 15f)
            for (float z = 60f; z <= 180f; z += 15f) { n++; if (BattlefieldTerrain.DirtAt(x, z, out _, out _) < 0.35f) grass++; }
            Assert.Greater(grass, n * 0.6f, "steppe should be mostly grass");
        }
    }
}
