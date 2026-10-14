using FieldMedic.Audio;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Battle
{
    public enum ProjectileKind { TankShell = 0, Rocket = 1, Bomb = 2, HeatFlare = 3, Mortar = 4 }

    /// <summary>
    /// Pooled battle VFX on top of <see cref="CombatVfx"/>: muzzle flashes (one particle system for every gun),
    /// bullet impacts (dirt / concrete chips + sparks), blast debris chunks, a ground dust ring, the tall column of
    /// a heavy bomb, scorch marks on the ground, and simulated projectiles (tank shells, rockets with smoke trails,
    /// falling bombs, heat-decoy flares) that detonate on arrival. Controller haptics by blast distance.
    /// Everything is preallocated; no per-frame allocations.
    /// </summary>
    public sealed class BattleFx : MonoBehaviour
    {
        public static BattleFx Instance { get; private set; }

        public Material glowMaterial;    // additive soft
        public Material smokeMaterial;   // alpha soft (puff texture)
        public Material dirtMaterial;    // alpha soft, dark
        public Material scorchMaterial;  // alpha, scorch texture
        public Mesh scorchMesh;          // unit quad on XZ
        public Transform head;

        ParticleSystem _muzzle, _dirtPuff, _hardPuff, _sparks, _chunks, _ring, _column, _trail, _glow;
        ParticleSystem.EmitParams _ep;

        // projectiles
        const int MaxProjectiles = 48;
        struct Projectile
        {
            public bool active;
            public ProjectileKind kind;
            public Vector3 pos, vel, target;
            public float age, life, charge;
        }
        readonly Projectile[] _proj = new Projectile[MaxProjectiles];

        // scorch decals
        const int Scorches = 18;
        readonly Matrix4x4[] _scorchM = new Matrix4x4[Scorches];
        readonly float[] _scorchBorn = new float[Scorches];
        int _scorchNext, _scorchCount;
        RenderParams _rp;

        public int Detonations { get; private set; }
        public int ActiveProjectiles { get; private set; }

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            _muzzle = Make("Muzzle", glowMaterial, 0.05f, 0.08f, 0.2f, 0.45f, 0f, 0f, 256);
            _dirtPuff = Make("ImpactDirt", dirtMaterial, 0.6f, 1.3f, 0.12f, 0.45f, 2.2f, 0.5f, 400);
            _hardPuff = Make("ImpactHard", smokeMaterial, 0.5f, 1.1f, 0.1f, 0.35f, 1.6f, 0.1f, 300);
            _sparks = Make("ImpactSparks", glowMaterial, 0.1f, 0.35f, 0.02f, 0.05f, 7f, 1.2f, 300);
            _chunks = Make("BlastChunks", dirtMaterial, 1.4f, 2.6f, 0.06f, 0.22f, 16f, 1.6f, 600);
            _ring = Make("BlastRing", smokeMaterial, 2.5f, 4.5f, 1.4f, 3.2f, 7f, -0.02f, 300);
            _column = Make("BombColumn", smokeMaterial, 9f, 15f, 5f, 11f, 3.5f, -0.05f, 160);
            _trail = Make("SmokeTrail", smokeMaterial, 1.5f, 3.2f, 0.35f, 0.9f, 0.3f, -0.02f, 900);
            _glow = Make("ProjectileGlow", glowMaterial, 0.06f, 0.09f, 0.35f, 0.6f, 0f, 0f, 200);
            var sz = _ring.sizeOverLifetime; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));
            var csz = _column.sizeOverLifetime; csz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f));
            var tsz = _trail.sizeOverLifetime; tsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            _rp = new RenderParams(scorchMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 900f) };
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        ParticleSystem Make(string name, Material m, float lifeMin, float lifeMax, float sizeMin, float sizeMax, float speed, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.75f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void Emit(ParticleSystem ps, Vector3 at, int count, Color color, Vector3 velocity = default, float size = -1f)
        {
            if (ps == null || count <= 0) return;
            _ep = new ParticleSystem.EmitParams { applyShapeToPosition = true, position = at, startColor = color };
            if (velocity != Vector3.zero) _ep.velocity = velocity;
            if (size > 0f) _ep.startSize = size;
            ps.Emit(_ep, count);
        }

        // ------------------------------------------------------------------ guns
        /// <summary>Muzzle flash at the barrel tip (one particle, ~50 ms).</summary>
        public void MuzzleFlash(Vector3 at, float size = 0.35f)
        {
            Emit(_muzzle, at, 1, new Color(1f, 0.78f, 0.45f, 1f), default, size * Random.Range(0.8f, 1.25f));
        }

        /// <summary>Bullet impact: hard surfaces chip grey dust and spark, soil kicks a dirt puff.</summary>
        public void Impact(Vector3 at, Vector3 normal, bool hard)
        {
            Vector3 n = normal.sqrMagnitude > 0.01f ? normal : Vector3.up;
            if (hard)
            {
                Emit(_hardPuff, at + n * 0.03f, 4, new Color(0.62f, 0.58f, 0.52f, 0.65f), n * 1.2f);
                Emit(_sparks, at + n * 0.02f, 3, new Color(1f, 0.75f, 0.4f, 1f), (n + Random.insideUnitSphere * 0.6f) * 5f);
            }
            else
            {
                Emit(_dirtPuff, at + n * 0.03f, 5, new Color(0.3f, 0.25f, 0.19f, 0.8f), n * 2.2f);
            }
        }

        // ------------------------------------------------------------------ blasts
        /// <summary>Full explosion: CombatAmbience blast (flash, dirt, smoke, sound, tinnitus/muffle/shake near
        /// the player) + debris chunks, ground dust ring, scorch mark, haptics; heavy bombs add a smoke column.</summary>
        public void Blast(Vector3 at, float charge, bool heavy = false)
        {
            Detonations++;
            var amb = CombatAmbience.Instance;
            if (amb != null) amb.Detonate(at, charge);
            else CombatVfx.Instance?.ExplosionNear(at, charge);
            float d = head != null ? Vector3.Distance(head.position, at) : 999f;
            int chunks = (int)(Mathf.Lerp(20f, 70f, Mathf.Clamp01(charge)) * (d < 150f ? 1f : 0.3f));
            Emit(_chunks, at + Vector3.up * 0.3f, chunks, new Color(0.13f, 0.1f, 0.075f, 1f));
            Emit(_ring, at + Vector3.up * 0.4f, (int)(14 * Mathf.Clamp(charge, 0.5f, 2.5f)), new Color(0.46f, 0.4f, 0.32f, 0.55f));
            if (heavy)
            {
                Emit(_column, at + Vector3.up * 3f, 26, new Color(0.22f, 0.2f, 0.18f, 0.85f), Vector3.up * 3f);
                var ba = BattleAudio.Instance;
                if (ba != null && d > 40f) ba.PlayFar(BattleAudio.Pick(ba.bombLarge), at, 1.2f, Random.Range(0.85f, 1f));
            }
            if (d < 220f) Scorch(at, Mathf.Clamp(1.4f + charge * 1.6f, 1.2f, heavy ? 7f : 4f));
            Haptic(d, charge * (heavy ? 2f : 1f));
        }

        /// <summary>Dust kicked up around a point on the ground (tank muzzle blast, rotor wash).</summary>
        public void GroundDust(Vector3 at, float amount)
        {
            at.y = BattlefieldLayout.GroundHeight(at.x, at.z) + 0.3f;
            Emit(_ring, at, (int)(12 * amount), new Color(0.5f, 0.44f, 0.35f, 0.5f));
        }

        /// <summary>Both controllers rumble with the blast's distance (comfort: short, capped).</summary>
        public static void Haptic(float distance, float charge)
        {
            float a = BattleMath.BlastHaptic(distance, charge);
            if (a <= 0.02f) return;
            float delay = distance / 343f; // ignored by the API, the rumble comes with the flash; keep it short
            float dur = Mathf.Lerp(0.08f, 0.4f, a) + Mathf.Min(0.05f, delay * 0.01f);
            var all = HandGrabber.All;
            for (int i = 0; i < all.Count; i++)
            {
                var input = all[i] != null ? all[i].Input : null;
                input?.Haptic(Mathf.Clamp01(a), dur);
            }
        }

        void Scorch(Vector3 at, float radius)
        {
            float y = BattlefieldLayout.GroundHeight(at.x, at.z) + 0.04f;
            int i = _scorchNext;
            _scorchNext = (_scorchNext + 1) % Scorches;
            _scorchCount = Mathf.Min(Scorches, _scorchCount + 1);
            _scorchM[i] = Matrix4x4.TRS(new Vector3(at.x, y, at.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(radius * 2f, 1f, radius * 2f));
            _scorchBorn[i] = Time.time;
        }

        public int ScorchCount => _scorchCount;

        // ------------------------------------------------------------------ projectiles
        /// <summary>Launches a simulated projectile; it detonates at <paramref name="target"/> (or on the ground).</summary>
        public bool Launch(ProjectileKind kind, Vector3 from, Vector3 target, float speed, float charge)
        {
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (_proj[i].active) continue;
                Vector3 d = target - from;
                float dist = d.magnitude;
                float life = Mathf.Max(0.05f, dist / Mathf.Max(1f, speed));
                Vector3 vel;
                if (kind == ProjectileKind.Bomb || kind == ProjectileKind.HeatFlare || kind == ProjectileKind.Mortar)
                {
                    // ballistic: solve v so that p(t)=target at t=life under gravity
                    vel = d / life - 0.5f * Physics.gravity * life;
                }
                else vel = d / life;
                _proj[i] = new Projectile { active = true, kind = kind, pos = from, vel = vel, target = target, age = 0f, life = life, charge = charge };
                return true;
            }
            return false;
        }

        /// <summary>Heat-decoy flare: released with a velocity, burns ~3 s falling, no detonation.</summary>
        public void Decoy(Vector3 from, Vector3 velocity)
        {
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (_proj[i].active) continue;
                _proj[i] = new Projectile { active = true, kind = ProjectileKind.HeatFlare, pos = from, vel = velocity, target = from, age = 0f, life = Random.Range(2.5f, 3.5f), charge = 0f };
                return;
            }
        }

        void StepProjectiles(float dt)
        {
            int n = 0;
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (!_proj[i].active) continue;
                var p = _proj[i];
                p.age += dt;
                bool ballistic = p.kind == ProjectileKind.Bomb || p.kind == ProjectileKind.HeatFlare || p.kind == ProjectileKind.Mortar;
                if (ballistic) p.vel += Physics.gravity * (p.kind == ProjectileKind.HeatFlare ? 0.25f : 1f) * dt;
                if (p.kind == ProjectileKind.HeatFlare) p.vel *= 1f - 0.9f * dt; // drag
                p.pos += p.vel * dt;
                switch (p.kind)
                {
                    case ProjectileKind.Rocket:
                        Emit(_trail, p.pos, 2, new Color(0.78f, 0.76f, 0.72f, 0.55f), -p.vel * 0.02f);
                        Emit(_glow, p.pos, 1, new Color(1f, 0.7f, 0.35f, 1f), default, 0.5f);
                        break;
                    case ProjectileKind.HeatFlare:
                        Emit(_trail, p.pos, 1, new Color(0.85f, 0.85f, 0.85f, 0.4f), Vector3.zero, 0.7f);
                        Emit(_glow, p.pos, 1, new Color(1f, 0.92f, 0.7f, 1f), default, 1.3f);
                        break;
                    case ProjectileKind.TankShell:
                        Emit(_glow, p.pos, 1, new Color(1f, 0.6f, 0.3f, 0.8f), default, 0.25f);
                        break;
                }
                float ground = BattlefieldLayout.GroundHeight(p.pos.x, p.pos.z);
                bool arrived = p.age >= p.life || (ballistic && p.vel.y < 0f && p.pos.y <= ground + 0.1f);
                if (arrived)
                {
                    p.active = false;
                    if (p.kind != ProjectileKind.HeatFlare)
                    {
                        Vector3 at = p.kind == ProjectileKind.TankShell || p.kind == ProjectileKind.Rocket ? p.target : new Vector3(p.pos.x, ground, p.pos.z);
                        Blast(at, p.charge, p.kind == ProjectileKind.Bomb);
                    }
                }
                else n++;
                _proj[i] = p;
            }
            ActiveProjectiles = n;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            StepProjectiles(dt);
            if (scorchMaterial != null && scorchMesh != null && _scorchCount > 0)
            {
                // one instanced draw for every mark; the ring buffer recycles the oldest
                _rp.material = scorchMaterial;
                Graphics.RenderMeshInstanced(_rp, scorchMesh, 0, _scorchM, _scorchCount);
            }
        }
    }
}
