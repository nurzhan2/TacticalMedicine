using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Pooled combat visual effects built from code at start (no prefabs to break): explosion flash lights,
    /// dirt burst + smoke column, debris raining on the player, dust rolling along the trench, dust falling from
    /// the dugout roof, tracer lines across the field, optional rain around the head.
    /// Materials come from the scene builder (FieldMedic/Particle shader) so they are included in builds.
    /// </summary>
    public sealed class CombatVfx : MonoBehaviour
    {
        public static CombatVfx Instance { get; private set; }

        public Material dirtMaterial;    // alpha blended, soft round
        public Material smokeMaterial;   // alpha blended, soft round
        public Material glowMaterial;    // additive
        public Material tracerMaterial;  // additive
        public Material rainMaterial;    // additive streak
        public Transform head;
        [Tooltip("0..1 — comfort cap for flash brightness (photosensitivity).")]
        public float flashLimit = 1f;
        public bool rain;

        const int LightPool = 4;
        readonly List<Light> _lights = new List<Light>();
        readonly float[] _lightEnd = new float[LightPool];
        readonly float[] _lightStart = new float[LightPool];
        readonly float[] _lightPeak = new float[LightPool];
        ParticleSystem _dirt, _smoke, _debris, _dust, _roofDust, _sparks, _rain;
        readonly List<LineRenderer> _tracers = new List<LineRenderer>();
        readonly List<Vector4> _tracerState = new List<Vector4>(); // x=t, y=speed, z=len, w=active
        readonly List<Vector3> _tracerFrom = new List<Vector3>();
        readonly List<Vector3> _tracerDir = new List<Vector3>();

        public int ActiveFlashes { get; private set; }

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            for (int i = 0; i < LightPool; i++)
            {
                var go = new GameObject("FlashLight" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.72f, 0.4f);
                l.enabled = false;
                _lights.Add(l);
            }
            _dirt = MakeSystem("DirtBurst", dirtMaterial, new Color(0.18f, 0.13f, 0.09f), 0.12f, 0.35f, 1.6f, 2.2f, 9f, 1.5f, 400);
            _smoke = MakeSystem("Smoke", smokeMaterial, new Color(0.16f, 0.15f, 0.14f, 0.55f), 1.2f, 3.2f, 5f, 9f, 1.4f, -0.04f, 120);
            _debris = MakeSystem("DebrisOnPlayer", dirtMaterial, new Color(0.14f, 0.1f, 0.07f), 0.015f, 0.05f, 1.2f, 2f, 0.3f, 1f, 600);
            _dust = MakeSystem("TrenchDust", smokeMaterial, new Color(0.35f, 0.3f, 0.25f, 0.28f), 0.6f, 1.6f, 3f, 5f, 1.2f, 0.01f, 200);
            _roofDust = MakeSystem("RoofDust", smokeMaterial, new Color(0.42f, 0.36f, 0.3f, 0.35f), 0.03f, 0.12f, 1.4f, 2.6f, 0.1f, 0.25f, 300);
            _sparks = MakeSystem("Sparks", glowMaterial, new Color(1f, 0.65f, 0.3f), 0.03f, 0.08f, 0.3f, 0.7f, 14f, 1.2f, 200);
            var sh = _dust.shape; sh.shapeType = ParticleSystemShapeType.Box;
            var rsh = _roofDust.shape; rsh.shapeType = ParticleSystemShapeType.Box; rsh.scale = new Vector3(1.8f, 0.05f, 1.4f);
            var dsh = _debris.shape; dsh.shapeType = ParticleSystemShapeType.Circle; dsh.radius = 1.1f;
            dsh.rotation = new Vector3(90f, 0f, 0f);
            BuildRain();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        ParticleSystem MakeSystem(string name, Material m, Color c, float sizeMin, float sizeMax, float lifeMin,
            float lifeMax, float speed, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = max;
            main.startColor = c;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void BuildRain()
        {
            _rain = MakeSystem("Rain", rainMaterial, new Color(0.6f, 0.65f, 0.75f, 0.25f), 0.012f, 0.02f, 0.8f, 1f, 0f, 0f, 700);
            var main = _rain.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var vel = _rain.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.8f, -0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-8f, -7f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            var shape = _rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(8f, 0.1f, 8f);
            var em = _rain.emission; em.rateOverTime = 450f;
            var r = _rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1f;
        }

        public void SetRain(bool on)
        {
            rain = on;
            if (on && !_rain.isPlaying) _rain.Play();
            if (!on && _rain.isPlaying) _rain.Stop();
        }

        // ------------------------------------------------------------------ API
        /// <summary>Short bright point light (pooled). Strength 1 ≈ 82 mm mortar at ~10 m.</summary>
        public void Flash(Vector3 at, float strength, float duration = 0.18f, float range = 25f)
        {
            int best = 0;
            for (int i = 0; i < LightPool; i++) if (_lightEnd[i] < _lightEnd[best]) best = i;
            var l = _lights[best];
            l.transform.position = at;
            l.range = range;
            _lightStart[best] = Time.time;
            _lightEnd[best] = Time.time + duration;
            _lightPeak[best] = Mathf.Min(strength * 8f, 8f * Mathf.Clamp01(flashLimit) + 0.5f);
            l.intensity = _lightPeak[best];
            l.enabled = true;
        }

        /// <summary>Near explosion: flash, dirt fountain, smoke, sparks; dust in the trench and debris on the player if close.</summary>
        public void ExplosionNear(Vector3 at, float charge = 1f)
        {
            Flash(at + Vector3.up * 0.6f, 1.2f * charge, 0.22f, 30f);
            Emit(_dirt, at, (int)(160 * charge));
            Emit(_smoke, at + Vector3.up * 0.5f, (int)(18 * charge));
            Emit(_sparks, at + Vector3.up * 0.3f, (int)(40 * charge));
            float d = head != null ? Vector3.Distance(head.position, at) : 99f;
            if (d < 25f) DebrisOnPlayer(Mathf.Clamp01(1.2f - d / 25f) * charge);
            if (d < 30f) TrenchDust(head != null ? new Vector3(head.position.x, 0.4f, head.position.z) : at, Mathf.Clamp01(1.3f - d / 30f));
        }

        /// <summary>Far explosion: only a flash low on the horizon (sound arrives later — CombatAmbience).</summary>
        public void ExplosionFar(Vector3 at, float charge = 1f)
        {
            Flash(at + Vector3.up * 3f, 0.9f * charge, 0.35f, Mathf.Clamp(Vector3.Distance(at, transform.position) * 1.3f, 40f, 200f));
            Emit(_sparks, at + Vector3.up, 12);
        }

        public void DebrisOnPlayer(float amount)
        {
            if (head == null || amount <= 0f) return;
            Emit(_debris, head.position + Vector3.up * 0.9f, (int)(220 * amount));
        }

        public void TrenchDust(Vector3 at, float amount)
        {
            var sh = _dust.shape; sh.scale = new Vector3(6f, 0.6f, 0.9f);
            Emit(_dust, at, (int)(40 * amount));
        }

        /// <summary>Dirt trickling from the dugout log roof after a nearby hit.</summary>
        public void RoofDust(Vector3 roofCentre, float amount = 1f)
        {
            Emit(_roofDust, roofCentre, (int)(160 * amount));
        }

        static void Emit(ParticleSystem ps, Vector3 at, int count)
        {
            if (ps == null || count <= 0) return;
            ps.transform.position = at;
            ps.Emit(count);
        }

        /// <summary>A burst of tracers along <paramref name="dir"/> from a distant gun (every 4th–5th round glows).</summary>
        public void TracerBurst(Vector3 from, Vector3 dir, int rounds = 4)
        {
            for (int k = 0; k < rounds; k++)
            {
                int i = FreeTracer();
                var jitter = new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(-0.01f, 0.025f), Random.Range(-0.02f, 0.02f));
                _tracerFrom[i] = from;
                _tracerDir[i] = (dir.normalized + jitter).normalized;
                _tracerState[i] = new Vector4(-k * 0.09f, Random.Range(650f, 800f) * 0.25f, 9f, 1f);
            }
        }

        int FreeTracer()
        {
            for (int i = 0; i < _tracers.Count; i++) if (_tracerState[i].w < 0.5f) return i;
            var go = new GameObject("Tracer" + _tracers.Count);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = tracerMaterial;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.09f;
            lr.startColor = new Color(1f, 0.35f, 0.15f, 0f);
            lr.endColor = new Color(1f, 0.55f, 0.25f, 1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            _tracers.Add(lr);
            _tracerState.Add(Vector4.zero);
            _tracerFrom.Add(Vector3.zero);
            _tracerDir.Add(Vector3.forward);
            return _tracers.Count - 1;
        }

        /// <summary>Advances transient effects; public so edit-mode screenshot tools can step it.</summary>
        public void Step(float dt)
        {
            int flashes = 0;
            for (int i = 0; i < LightPool; i++)
            {
                var l = _lights[i];
                if (!l.enabled) continue;
                float dur = Mathf.Max(0.01f, _lightEnd[i] - _lightStart[i]);
                float k = 1f - (Time.time - _lightStart[i]) / dur;
                if (k <= 0f) { l.enabled = false; continue; }
                l.intensity = _lightPeak[i] * k * k * (0.8f + 0.2f * Random.value);
                flashes++;
            }
            ActiveFlashes = flashes;

            for (int i = 0; i < _tracers.Count; i++)
            {
                var s = _tracerState[i];
                if (s.w < 0.5f) continue;
                s.x += dt;
                var lr = _tracers[i];
                if (s.x < 0f) { lr.enabled = false; _tracerState[i] = s; continue; }
                float travelled = s.x * s.y;
                if (travelled > 600f) { s.w = 0f; lr.enabled = false; _tracerState[i] = s; continue; }
                var dir = _tracerDir[i];
                Vector3 head = _tracerFrom[i] + dir * travelled + Vector3.down * (0.5f * 9.81f * s.x * s.x * 0.3f);
                lr.enabled = true;
                lr.SetPosition(0, head - dir * s.z);
                lr.SetPosition(1, head);
                _tracerState[i] = s;
            }

            if (_rain != null && head != null) _rain.transform.position = head.position + Vector3.up * 5f;
        }

        void Update() => Step(Time.deltaTime);
    }
}
