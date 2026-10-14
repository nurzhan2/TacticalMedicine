using System;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>Who the enemy may shoot at and whether the player can be hit (GDD_V2 §3).</summary>
    public enum BattleMode
    {
        /// <summary>Battle far in the background, nobody targets the player, no failure.</summary>
        Training = 0,
        /// <summary>Full battle, the player can be hit.</summary>
        Combat = 1,
        /// <summary>Full battle, the player can be hit, scored.</summary>
        Exam = 2,
    }

    public enum BattleEventType
    {
        Wave = 0,
        ArtilleryBarrage = 1,
        AirStrike = 2,
        MgBurst = 3,
        DroneDive = 4,
        TankShot = 5,
        HelicopterRun = 6,
    }

    /// <summary>
    /// Cumulative exposure in a sliding time window (GDD_V2: "exposed &gt; N s cumulative in 10 s → hit").
    /// Fixed ring of 0.1 s bins, no allocations after construction. Pure logic (EditMode-tested).
    /// </summary>
    public sealed class ExposureTracker
    {
        public const float BinSeconds = 0.1f;
        readonly float[] _bins;
        int _head;
        float _binTime;
        float _sum;

        public float Window { get; }
        public float Limit { get; set; }
        /// <summary>Exposed seconds inside the window.</summary>
        public float Exposed => _sum;
        public float Exposure01 => Limit > 0f ? Mathf.Clamp01(_sum / Limit) : 0f;
        public bool IsHit { get; private set; }
        public float TotalExposed { get; private set; }

        public ExposureTracker(float window = 10f, float limit = 4f)
        {
            Window = Mathf.Max(BinSeconds, window);
            Limit = limit;
            _bins = new float[Mathf.CeilToInt(Window / BinSeconds)];
        }

        public void Reset()
        {
            Array.Clear(_bins, 0, _bins.Length);
            _head = 0; _binTime = 0f; _sum = 0f; IsHit = false; TotalExposed = 0f;
        }

        /// <summary>Advances time by <paramref name="dt"/>; returns true on the tick the limit is crossed.</summary>
        public bool Tick(bool exposed, float dt)
        {
            if (dt <= 0f) return false;
            bool was = IsHit;
            // split long ticks across bins so a 1 s hitch is not lost or double counted
            while (dt > 0f)
            {
                float room = BinSeconds - _binTime;
                float step = Mathf.Min(room, dt);
                if (exposed)
                {
                    _bins[_head] += step;
                    _sum += step;
                    TotalExposed += step;
                }
                _binTime += step;
                dt -= step;
                if (_binTime >= BinSeconds - 1e-6f)
                {
                    _binTime = 0f;
                    _head = (_head + 1) % _bins.Length;
                    _sum -= _bins[_head];   // the oldest bin leaves the window
                    _bins[_head] = 0f;
                    if (_sum < 0f) _sum = 0f;
                }
            }
            if (!IsHit && Limit > 0f && _sum >= Limit - 1e-4f) IsHit = true;
            return IsHit && !was;
        }
    }

    /// <summary>
    /// Weighted random battle events every <see cref="minInterval"/>..<see cref="maxInterval"/> s (shorter when the
    /// intensity is high); never the same event three times in a row. Deterministic for a given seed.
    /// </summary>
    public sealed class BattleEventPicker
    {
        readonly System.Random _rng;
        readonly float[] _weights;
        BattleEventType _last = (BattleEventType)(-1), _prev = (BattleEventType)(-2);

        public float minInterval = 5f, maxInterval = 20f;

        public BattleEventPicker(float[] weights, int seed = 1234)
        {
            _weights = new float[Enum.GetValues(typeof(BattleEventType)).Length];
            if (weights != null) Array.Copy(weights, _weights, Mathf.Min(weights.Length, _weights.Length));
            _rng = new System.Random(seed);
        }

        public float Weight(BattleEventType t) => _weights[(int)t];
        public void SetWeight(BattleEventType t, float w) => _weights[(int)t] = Mathf.Max(0f, w);

        /// <summary>Seconds until the next event at this intensity (1 = normal assault, 2 = heavy).</summary>
        public float NextInterval(float intensity)
        {
            float k = Mathf.Clamp(intensity, 0.25f, 3f);
            float t = minInterval + (float)_rng.NextDouble() * (maxInterval - minInterval);
            return Mathf.Clamp(t / k, minInterval * 0.5f, maxInterval * 2f);
        }

        public BattleEventType Next()
        {
            float total = 0f;
            for (int i = 0; i < _weights.Length; i++) if (!Blocked(i)) total += _weights[i];
            if (total <= 0f) return BattleEventType.MgBurst;
            double r = _rng.NextDouble() * total;
            int pick = 0;
            for (int i = 0; i < _weights.Length; i++)
            {
                if (Blocked(i) || _weights[i] <= 0f) continue;
                pick = i;
                r -= _weights[i];
                if (r <= 0) break;
            }
            _prev = _last;
            _last = (BattleEventType)pick;
            return _last;
        }

        bool Blocked(int i) => (int)_last == i && (int)_prev == i;

        public float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }

    /// <summary>
    /// Assault pressure over time: a slow swell (waves come and go, 0.7..1.3) and "focus" windows requested by fine
    /// medical steps that lower the intensity NEAR the player for 3–5 s, while the background never drops below
    /// <see cref="backgroundFloor"/>. Time is passed in (pure, testable).
    /// </summary>
    public sealed class IntensityCurve
    {
        public float baseLevel = 1f;
        public float swellAmplitude = 0.3f;
        public float swellPeriod = 75f;
        public float focusLevel = 0.3f;
        public float backgroundFloor = 0.45f;
        public float maxFocus = 5f;

        float _focusUntil = -1f;
        public int FocusRequests { get; private set; }

        public void RequestFocus(float now, float seconds)
        {
            seconds = Mathf.Clamp(seconds, 0f, maxFocus);
            _focusUntil = Mathf.Max(_focusUntil, now + seconds);
            FocusRequests++;
        }

        public bool InFocus(float now) => now < _focusUntil;
        public float FocusLeft(float now) => Mathf.Max(0f, _focusUntil - now);

        /// <summary>Overall battle intensity (distant layers, event rate).</summary>
        public float Background(float now)
        {
            float swell = Mathf.Sin(now * 2f * Mathf.PI / Mathf.Max(1f, swellPeriod)) * swellAmplitude
                        + Mathf.Sin(now * 2f * Mathf.PI / Mathf.Max(1f, swellPeriod * 0.37f) + 1.3f) * swellAmplitude * 0.35f;
            return Mathf.Max(backgroundFloor, baseLevel + swell);
        }

        /// <summary>Intensity of things that happen close to the player (near blasts, fire on his cover).</summary>
        public float Near(float now)
        {
            float b = Background(now);
            if (!InFocus(now)) return b;
            // ease back in during the last second of the focus window
            float left = FocusLeft(now);
            float k = Mathf.Clamp01(left);
            return Mathf.Lerp(b, Mathf.Min(b, focusLevel), k);
        }
    }

    /// <summary>A drifting smoke cloud that blocks sight (enemy rays) — registered by smoke grenades.</summary>
    public struct SmokeCloud
    {
        public Vector3 centre;
        public float radius;
        /// <summary>0..1, opacity of the core; ≥ <see cref="SmokeScreens.BlockDensity"/> blocks a line through it.</summary>
        public float density;
        public bool active;
    }

    /// <summary>Registry of active smoke clouds + line-of-sight test (segment through a sphere). No allocations.</summary>
    public static class SmokeScreens
    {
        public const int Capacity = 32;
        public const float BlockDensity = 0.5f;
        static readonly SmokeCloud[] _clouds = new SmokeCloud[Capacity];

        public static int ActiveCount
        {
            get { int n = 0; for (int i = 0; i < Capacity; i++) if (_clouds[i].active) n++; return n; }
        }

        public static int Add(Vector3 centre, float radius, float density)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_clouds[i].active) continue;
                _clouds[i] = new SmokeCloud { centre = centre, radius = radius, density = density, active = true };
                return i;
            }
            return -1;
        }

        public static void Set(int id, Vector3 centre, float radius, float density)
        {
            if (id < 0 || id >= Capacity) return;
            _clouds[id].centre = centre; _clouds[id].radius = radius; _clouds[id].density = density;
        }

        public static void Remove(int id)
        {
            if (id >= 0 && id < Capacity) _clouds[id].active = false;
        }

        public static void Clear() { for (int i = 0; i < Capacity; i++) _clouds[i].active = false; }

        public static SmokeCloud Get(int id) => _clouds[id];

        /// <summary>Total optical depth along the segment (sum over clouds of density × chord/diameter).</summary>
        public static float Opacity(Vector3 a, Vector3 b)
        {
            float total = 0f;
            for (int i = 0; i < Capacity; i++)
            {
                var c = _clouds[i];
                if (!c.active || c.radius <= 0f || c.density <= 0f) continue;
                float chord = SegmentSphereChord(a, b, c.centre, c.radius);
                if (chord > 0f) total += c.density * chord / (2f * c.radius);
            }
            return total;
        }

        public static bool Blocks(Vector3 a, Vector3 b) => Opacity(a, b) >= BlockDensity;

        /// <summary>Length of the part of segment ab inside the sphere (0 when it misses).</summary>
        public static float SegmentSphereChord(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return 0f;
            d /= len;
            Vector3 m = a - c;
            float bq = Vector3.Dot(m, d);
            float cq = Vector3.Dot(m, m) - r * r;
            float disc = bq * bq - cq;
            if (disc <= 0f) return 0f;
            float s = Mathf.Sqrt(disc);
            float t0 = Mathf.Max(0f, -bq - s), t1 = Mathf.Min(len, -bq + s);
            return Mathf.Max(0f, t1 - t0);
        }
    }

    /// <summary>Closed/open Catmull-Rom splines for vehicles and aircraft paths.</summary>
    public static class Spline
    {
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Point at parameter u ∈ [0, n) on a closed loop through <paramref name="pts"/>.</summary>
        public static Vector3 Loop(Vector3[] pts, float u)
        {
            int n = pts.Length;
            if (n == 0) return Vector3.zero;
            if (n == 1) return pts[0];
            u = Mathf.Repeat(u, n);
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            return CatmullRom(pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n], t);
        }

        /// <summary>Approximate length of the closed loop (for constant-speed travel).</summary>
        public static float LoopLength(Vector3[] pts, int samplesPerSegment = 16)
        {
            float len = 0f;
            int n = pts.Length;
            if (n < 2) return 0f;
            Vector3 prev = Loop(pts, 0f);
            int total = n * samplesPerSegment;
            for (int k = 1; k <= total; k++)
            {
                var p = Loop(pts, (float)k / samplesPerSegment);
                len += Vector3.Distance(prev, p);
                prev = p;
            }
            return len;
        }
    }

    /// <summary>Small helpers shared by the battle layers.</summary>
    public static class BattleMath
    {
        /// <summary>Distance-based haptic amplitude for a blast (0 beyond ~60 m for a 1.0 charge).</summary>
        public static float BlastHaptic(float distance, float charge)
        {
            float reach = 60f * Mathf.Sqrt(Mathf.Max(0.05f, charge));
            float k = Mathf.Clamp01(1f - distance / reach);
            return k * k;
        }

        /// <summary>Close blast → muffled-mix duration (GDD: 3–6 s); 0 when too far to deafen.</summary>
        public static float MuffleSeconds(float distance, float charge)
        {
            float k = Mathf.Clamp01((18f - distance) / 14f) * Mathf.Clamp01(charge);
            return k <= 0.05f ? 0f : 3f + 3f * k;
        }

        /// <summary>True when the point is inside the horizontal view cone of the head (for off-screen respawns).</summary>
        public static bool InView(Vector3 headPos, Vector3 headForward, Vector3 p, float halfAngleDeg = 60f)
        {
            Vector3 d = p - headPos; d.y = 0f;
            Vector3 f = headForward; f.y = 0f;
            if (d.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) return true;
            return Vector3.Angle(f, d) < halfAngleDeg;
        }
    }
}
