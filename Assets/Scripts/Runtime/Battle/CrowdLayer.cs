using FieldMedic.Environment;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Mid layer (60–200 m): 30–60 enemy infantry as GPU-instanced baked meshes (a low-poly soldier baked at 8
    /// frames of the run cycle + a kneeling and a fallen pose). A tiny crowd FSM: rush forward in bounds, drop to a
    /// knee and fire (muzzle flashes), rush again; blasts and our fire kill them (fallen pose), the dead and the
    /// ones who reached the near zone are recycled at the back in the haze. One RenderMeshInstanced per pose
    /// (≤ 10 draw calls), preallocated matrix buckets, no per-frame allocations.
    /// </summary>
    public sealed class CrowdLayer : MonoBehaviour
    {
        public static CrowdLayer Instance { get; private set; }

        public Mesh[] runFrames = new Mesh[0];
        public Mesh kneelMesh;
        public Mesh deadMesh;
        public Material material;
        public int count = 44;
        public Vector2 xRange = new Vector2(-130f, 130f);
        public Vector2 zRange = new Vector2(68f, 190f);
        public float runFps = 10f;
        public Transform head;
        [Tooltip("Probability per second that a crowd soldier is cut down by our fire.")]
        public float casualtyRate = 0.012f;
        public int seed = 99;

        enum S : byte { Rush, Kneel, Dead }
        struct Agent { public Vector2 p; public float speed, until, phase, yaw; public S s; }
        Agent[] _a;
        Matrix4x4[][] _buckets;
        int[] _counts;
        RenderParams _rp;
        System.Random _rng;

        public int Alive { get; private set; }
        public int Killed { get; private set; }
        public int DrawCalls { get; private set; }
        public float Surge { get; private set; }

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            _rng = new System.Random(seed);
            _a = new Agent[Mathf.Max(0, count)];
            int poses = runFrames.Length + 2;
            _buckets = new Matrix4x4[poses][];
            _counts = new int[poses];
            for (int i = 0; i < poses; i++) _buckets[i] = new Matrix4x4[Mathf.Max(1, count)];
            _rp = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(new Vector3(0f, 0f, (zRange.x + zRange.y) * 0.5f), new Vector3(600f, 60f, 400f)),
            };
            for (int i = 0; i < _a.Length; i++) Recycle(ref _a[i], true);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Recycle(ref Agent a, bool anywhere)
        {
            a.p = new Vector2(R(xRange.x, xRange.y), anywhere ? R(zRange.x + 15f, zRange.y) : R(zRange.y - 25f, zRange.y));
            a.s = S.Rush;
            a.speed = R(1.6f, 3.4f);
            a.until = Time.time + R(3f, 10f);
            a.phase = R(0f, 10f);
            a.yaw = 180f + R(-14f, 14f);
        }

        /// <summary>A wave: everyone gets up and rushes forward for a while.</summary>
        public void StartSurge(float seconds = 12f)
        {
            Surge = Time.time + seconds;
            for (int i = 0; i < _a.Length; i++)
                if (_a[i].s == S.Kneel) { _a[i].s = S.Rush; _a[i].until = Time.time + R(4f, 9f); }
        }

        /// <summary>Blast in the field: crowd soldiers inside the radius fall.</summary>
        public int BlastAt(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < _a.Length; i++)
            {
                if (_a[i].s == S.Dead) continue;
                float dx = _a[i].p.x - p.x, dz = _a[i].p.y - p.z;
                if (dx * dx + dz * dz < r2) { Die(ref _a[i]); n++; }
            }
            return n;
        }

        void Die(ref Agent a)
        {
            a.s = S.Dead;
            a.until = Time.time + R(9f, 18f);
            Killed++;
        }

        void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            bool surging = now < Surge;
            int alive = 0;
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            var fx = BattleFx.Instance;
            for (int i = 0; i < _a.Length; i++)
            {
                ref var a = ref _a[i];
                switch (a.s)
                {
                    case S.Rush:
                        a.p.y -= a.speed * (surging ? 1.35f : 1f) * dt;
                        a.p.x += Mathf.Sin(a.phase + now * 0.3f) * 0.25f * dt;
                        if (a.p.y < zRange.x) { Die(ref a); break; }
                        if (now >= a.until && !surging) { a.s = S.Kneel; a.until = now + R(3f, 9f); }
                        if (_rng.NextDouble() < casualtyRate * 1.5f * dt) Die(ref a);
                        break;
                    case S.Kneel:
                        if (now >= a.until) { a.s = S.Rush; a.until = now + R(3f, 8f); }
                        if (fx != null && _rng.NextDouble() < 1.2 * dt) fx.MuzzleFlash(Pos(a) + new Vector3(0f, 1.05f, -0.6f), 0.55f);
                        if (_rng.NextDouble() < casualtyRate * dt) Die(ref a);
                        break;
                    case S.Dead:
                        if (now >= a.until) Recycle(ref a, false);
                        break;
                }
                if (a.s != S.Dead) alive++;
                int pose = a.s == S.Dead ? runFrames.Length + 1 : a.s == S.Kneel ? runFrames.Length
                         : (runFrames.Length > 0 ? (int)((now * runFps * a.speed / 2.6f + a.phase * 3f) % runFrames.Length) : 0);
                if (pose >= _buckets.Length) continue;
                _buckets[pose][_counts[pose]++] = Matrix4x4.TRS(Pos(a), Quaternion.Euler(0f, a.yaw, 0f), Vector3.one);
            }
            Alive = alive;
            Draw();
        }

        static Vector3 Pos(in Agent a) => new Vector3(a.p.x, BattlefieldLayout.GroundHeight(a.p.x, a.p.y), a.p.y);

        void Draw()
        {
            if (material == null) return;
            int calls = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                if (_counts[i] == 0) continue;
                Mesh m = i < runFrames.Length ? runFrames[i] : i == runFrames.Length ? kneelMesh : deadMesh;
                if (m == null) continue;
                Graphics.RenderMeshInstanced(_rp, m, 0, _buckets[i], _counts[i]);
                calls++;
            }
            DrawCalls = calls;
        }
    }
}
