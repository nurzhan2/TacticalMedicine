using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Shared coordinates of the V2 steppe battlefield (GDD_V2 §1). +Z points at the enemy. The centre of our
    /// position (~40×40 m) is the origin; the main ruin (house A) spans x −6..2, z −2..4 with the drag doorway in
    /// its east wall; Erlan goes down in the open ~14 m north-east of that doorway; the helicopter LZ is ~40 m
    /// behind the ruins. Used by the scene builder, scenarios (w4), the battle director (w3) and tests.
    /// </summary>
    public static class BattlefieldLayout
    {
        public const string SceneName = "Battlefield";
        public const string ScenePath = "Assets/Scenes/Battlefield.unity";

        public static readonly Vector3 EnemyDirection = Vector3.forward;
        public const float TerrainHalfSize = 200f;
        /// <summary>Radius of the player's position that stays flat (no height noise, no craters).</summary>
        public const float FlatRadius = 24f;

        // ---- house A (the ruin we treat in)
        public static readonly Vector2 HouseAMin = new Vector2(-6f, -2f);   // x, z
        public static readonly Vector2 HouseAMax = new Vector2(2f, 4f);
        public const float WallThickness = 0.38f;                            // 1.5 bricks
        /// <summary>Gap in house A's east wall (x = 2): z range, used for the drag in/out.</summary>
        public static readonly Vector2 DoorwayZ = new Vector2(0.2f, 1.7f);
        public static Vector3 DoorwayCentre => new Vector3(HouseAMax.x, 0f, (DoorwayZ.x + DoorwayZ.y) * 0.5f);

        /// <summary>Where Erlan falls (open ground, ~14 m from the doorway, nothing between him and the enemy).</summary>
        public static readonly Vector3 ErlanFallPoint = new Vector3(5.5f, 0f, 14f);
        /// <summary>S7 (barrage): Erlan lies far out in the field, ~20 m from the doorway (long drag between impacts).</summary>
        public static readonly Vector3 ErlanFarPoint = new Vector3(-0.5f, 0f, 19.5f);
        /// <summary>Treatment spot in cover inside house A (behind the north wall, out of the doorway's line).</summary>
        public static readonly Vector3 TreatmentSpot = new Vector3(-1.6f, 0f, 1.6f);
        public static readonly Vector3 PlayerStart = new Vector3(0.6f, 0f, 0.4f);
        public const float PlayerStartYaw = 30f;
        /// <summary>Helicopter landing zone (panel + smoke) behind the ruins.</summary>
        public static readonly Vector3 LandingZone = new Vector3(-3f, 0f, -44f);
        public static readonly Vector3 FlagPole = new Vector3(-6.9f, 0f, 4.9f);

        /// <summary>Kneeling eye height used for cover checks (GDD: cover must work at 1.0–1.2 m).</summary>
        public const float KneelEyeHeight = 1.1f;
        public const float StandEyeHeight = 1.65f;

        /// <summary>Shell craters (x, z, radius, depth). Deterministic so tests and the builder agree.</summary>
        public static readonly Vector4[] Craters =
        {
            new Vector4(8.2f, 17.5f, 2.1f, 0.6f),    // the one that dropped Erlan
            new Vector4(3.0f, 9.5f, 1.6f, 0.45f),    // mid-way on the drag line: a little dip, not cover
            new Vector4(-4.0f, 13.0f, 2.4f, 0.7f),
            new Vector4(14.0f, 9.0f, 2.8f, 0.8f),
            new Vector4(-12.0f, 18.0f, 3.2f, 0.9f),
            new Vector4(18.0f, 22.0f, 2.2f, 0.6f),
            new Vector4(1.0f, 27.0f, 3.6f, 1.0f),
            new Vector4(-20.0f, 30.0f, 2.6f, 0.7f),
            new Vector4(24.0f, 33.0f, 3.0f, 0.8f),
            new Vector4(-8.0f, 40.0f, 4.0f, 1.1f),
            new Vector4(10.0f, 46.0f, 3.4f, 0.9f),
            new Vector4(-30.0f, 12.0f, 2.8f, 0.7f),
            new Vector4(30.0f, 4.0f, 2.5f, 0.6f),
            new Vector4(-16.0f, -18.0f, 2.2f, 0.6f),
            new Vector4(12.0f, -26.0f, 2.6f, 0.7f),
            new Vector4(-34.0f, 52.0f, 4.5f, 1.2f),
            new Vector4(36.0f, 60.0f, 4.0f, 1.1f),
            new Vector4(4.0f, 70.0f, 5.0f, 1.3f),
            new Vector4(-50.0f, 80.0f, 5.0f, 1.2f),
            new Vector4(52.0f, 95.0f, 5.5f, 1.3f),
        };

        /// <summary>Ground height of the steppe at (x, z): flat core, gentle long swells outside, crater bowls with
        /// raised rims. Pure function (same in editor and at runtime).</summary>
        public static float GroundHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float swell = (Mathf.PerlinNoise(x * 0.012f + 31.7f, z * 0.012f + 7.3f) - 0.5f) * 2.2f
                        + (Mathf.PerlinNoise(x * 0.05f + 3.1f, z * 0.05f + 91.3f) - 0.5f) * 0.5f;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatRadius, FlatRadius + 30f, r));
            float h = swell * w;
            // keep the far rim slightly raised so the fogged edge never shows the void
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 200f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)))) * 3f;
            return h + CraterOffset(x, z, out _);
        }

        /// <summary>Crater bowl (negative) + rim (positive); burn = 0..1 scorch amount at this point.</summary>
        public static float CraterOffset(float x, float z, out float burn)
        {
            float off = 0f;
            burn = 0f;
            foreach (var c in Craters)
            {
                float dx = x - c.x, dz = z - c.y;
                float d = Mathf.Sqrt(dx * dx + dz * dz) / c.z;   // 1 = crater lip
                if (d > 2.2f) continue;
                if (d < 1f) off -= c.w * (1f - d * d) * (1f - d * d * 0.3f);
                // ejecta rim: peaks just outside the lip
                off += c.w * 0.32f * Mathf.Exp(-Mathf.Pow((d - 1.05f) / 0.28f, 2f));
                burn = Mathf.Max(burn, Mathf.Clamp01(1.6f - d) * 0.95f);
            }
            return off;
        }

        /// <summary>Distance from the Erlan fall point to the doorway, metres (GDD: 10–15 m of open ground).</summary>
        public static float DragDistance => Vector3.Distance(new Vector3(ErlanFallPoint.x, 0f, ErlanFallPoint.z), DoorwayCentre);

        public static IEnumerable<Vector3> SmokeColumnSites()
        {
            yield return new Vector3(-70f, 0f, 160f);
            yield return new Vector3(35f, 0f, 175f);
            yield return new Vector3(120f, 0f, 140f);
            yield return new Vector3(-150f, 0f, 95f);
            yield return new Vector3(170f, 0f, 40f);
            yield return new Vector3(-125f, 0f, -60f);
            yield return new Vector3(80f, 0f, -150f);
        }
    }
}
