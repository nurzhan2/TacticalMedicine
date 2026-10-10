using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>Small pure helpers for the environment layer (kept static and side-effect free for tests).</summary>
    public static class EnvMath
    {
        public const float SpeedOfSound = 343f;

        /// <summary>Seconds between a flash and its sound at <paramref name="distance"/> metres.</summary>
        public static float SoundDelay(float distance) => Mathf.Max(0f, distance) / SpeedOfSound;

        /// <summary>
        /// Para-flare position at <paramref name="t"/> seconds after deployment: falls at <paramref name="fallSpeed"/>,
        /// drifts with the wind and swings under its parachute (pendulum ~2.4 s period, a few metres).
        /// </summary>
        public static Vector3 FlarePosition(Vector3 start, Vector3 wind, float fallSpeed, float swing, float t, float seed = 0f)
        {
            float sx = Mathf.Sin(t * 2.6f + seed) * swing;
            float sz = Mathf.Sin(t * 1.9f + seed * 1.7f) * swing * 0.6f;
            return start + wind * t + new Vector3(sx, -fallSpeed * t, sz);
        }

        /// <summary>
        /// Flare brightness 0..1: ignition pop (0.6 s), full burn with magnesium flicker, sputter + fade in the last
        /// 15% of <paramref name="burnTime"/>.
        /// </summary>
        public static float FlareBrightness(float t, float burnTime, float seed = 0f)
        {
            if (t < 0f || t > burnTime) return 0f;
            float ign = Mathf.Clamp01(t / 0.6f);
            float flicker = 0.88f + 0.08f * Mathf.Sin(t * 23f + seed) + 0.04f * Mathf.Sin(t * 57f + seed * 3f);
            float end = Mathf.Clamp01((burnTime - t) / (burnTime * 0.15f));
            float sputter = end < 1f ? (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 31f + seed))) : 1f;
            return ign * flicker * end * sputter;
        }

        /// <summary>Unsigned distance from a point to a closed polygon outline in the XZ plane.</summary>
        public static float DistanceToPolygonXZ(Vector3 p, IList<Vector3> poly)
        {
            if (poly == null || poly.Count < 2) return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = new Vector2(poly[i].x, poly[i].z);
                var b = new Vector2(poly[(i + 1) % poly.Count].x, poly[(i + 1) % poly.Count].z);
                best = Mathf.Min(best, DistanceToSegment(q, a, b));
            }
            return best;
        }

        public static bool InsidePolygonXZ(Vector3 p, IList<Vector3> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            }
            return inside;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Passthrough opacity ramp: 0 beyond <paramref name="far"/> metres, 1 at or inside <paramref name="near"/>.</summary>
        public static float ProximityOpacity(float distance, float far = 0.6f, float near = 0.25f)
        {
            if (distance >= far) return 0f;
            if (distance <= near) return 1f;
            float x = (far - distance) / (far - near);
            return x * x * (3f - 2f * x); // smoothstep: gentle start, firm at the wall
        }

        /// <summary>Camera shake amplitude (m) for a blast: falls with distance, clamped by the comfort limit.</summary>
        public static float ShakeAmplitude(float distance, float charge, float comfortLimit)
        {
            float a = charge * 0.05f / Mathf.Max(1f, distance * distance * 0.15f);
            return Mathf.Min(a, comfortLimit);
        }
    }
}
