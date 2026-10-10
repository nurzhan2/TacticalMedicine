using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Night lighting: one shadow-casting directional light that is the moon most of the time and is steered
    /// towards the brightest burning illumination flare (→ swinging shadows in the trench at the cost of a single
    /// shadow map). Launches flares on a random schedule, flickers the horizon glow in the sky material, flashes
    /// the sky for far explosions, and shoots occasional tracer bursts across the field.
    /// </summary>
    public sealed class NightLightingDirector : MonoBehaviour
    {
        public static NightLightingDirector Instance { get; private set; }

        public Light mainLight;
        public Material skyMaterial;
        public Vector3 moonEuler = new Vector3(22f, -35f, 0f);
        public Color moonColor = new Color(0.55f, 0.65f, 0.95f);
        public float moonIntensity = 0.22f;
        public Color flareColor = new Color(1f, 0.92f, 0.78f);
        public float flareIntensity = 1.25f;
        public Vector3 focus = Vector3.zero;          // trench centre the flare light aims at
        public List<IlluminationFlare> flares = new List<IlluminationFlare>();
        public bool autoFlares = true;
        public Vector2 flareInterval = new Vector2(25f, 55f);
        public bool autoTracers = true;
        public Vector2 tracerInterval = new Vector2(4f, 14f);
        public Color ambientSky = new Color(0.035f, 0.045f, 0.075f);
        public Color ambientEquator = new Color(0.025f, 0.025f, 0.03f);
        public Color ambientGround = new Color(0.01f, 0.009f, 0.008f);

        float _nextFlare, _nextTracer;
        float _skyFlash;
        Vector3 _skyFlashDir = Vector3.forward;
        Quaternion _smoothedRot;
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly int FlashDirId = Shader.PropertyToID("_FlashDir");

        public IlluminationFlare Brightest { get; private set; }

        void Awake()
        {
            Instance = this;
            _nextFlare = Time.time + Random.Range(4f, 9f);
            _nextTracer = Time.time + Random.Range(2f, 5f);
            _smoothedRot = Quaternion.Euler(moonEuler);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Launches a flare from the pool at a random spot over no-man's land (in front, +Z).</summary>
        public IlluminationFlare LaunchFlare(Vector3? at = null)
        {
            IlluminationFlare f = null;
            foreach (var x in flares) if (x != null && !x.gameObject.activeSelf) { f = x; break; }
            if (f == null) return null;
            var p = at ?? focus + new Vector3(Random.Range(-35f, 35f), Random.Range(70f, 85f), Random.Range(25f, 65f));
            f.burnTime = Random.Range(30f, 40f);
            f.Launch(p, Random.value * 10f);
            return f;
        }

        /// <summary>Lights the sky in a direction (far explosion / glow); decays over ~0.5 s.</summary>
        public void SkyFlash(Vector3 worldPos, float strength)
        {
            _skyFlashDir = (worldPos - focus).normalized;
            _skyFlash = Mathf.Max(_skyFlash, strength);
        }

        void Update()
        {
            if (autoFlares && Time.time >= _nextFlare)
            {
                LaunchFlare();
                _nextFlare = Time.time + Random.Range(flareInterval.x, flareInterval.y);
            }
            if (autoTracers && Time.time >= _nextTracer && CombatVfx.Instance != null)
            {
                var from = focus + new Vector3(Random.Range(-120f, 120f), Random.Range(1f, 3f), Random.Range(90f, 200f));
                var dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.01f, 0.08f), Random.Range(-0.3f, 0.1f));
                CombatVfx.Instance.TracerBurst(from, dir, Random.Range(2, 6));
                _nextTracer = Time.time + Random.Range(tracerInterval.x, tracerInterval.y);
            }
            Apply(Time.deltaTime);
        }

        /// <summary>Blends moon/flare onto the main light. Public for edit-mode screenshots (dt = 0 snaps).</summary>
        public void Apply(float dt)
        {
            Brightest = null;
            float best = 0f;
            foreach (var f in flares)
                if (f != null && f.gameObject.activeSelf && f.Brightness > best) { best = f.Brightness; Brightest = f; }

            Quaternion target = Quaternion.Euler(moonEuler);
            Color col = moonColor * moonIntensity;
            if (Brightest != null)
            {
                Vector3 dir = focus - Brightest.transform.position;
                // keep the light from going too horizontal (shadow acne + everything in shadow behind the parapet)
                dir.y = Mathf.Min(dir.y, -0.35f * new Vector2(dir.x, dir.z).magnitude);
                float w = Mathf.Clamp01(best);
                target = Quaternion.Slerp(target, Quaternion.LookRotation(dir.normalized), Mathf.Clamp01(w * 3f));
                col = Color.Lerp(col, flareColor * flareIntensity * w, Mathf.Clamp01(w * 2f));
            }
            _smoothedRot = dt <= 0f ? target : Quaternion.Slerp(_smoothedRot, target, 1f - Mathf.Exp(-dt * 6f));
            if (mainLight != null)
            {
                mainLight.transform.rotation = _smoothedRot;
                float intensity = Mathf.Max(col.r, Mathf.Max(col.g, col.b));
                mainLight.color = intensity > 1e-4f ? col / intensity : moonColor;
                mainLight.intensity = intensity;
            }
            float amb = 1f + best * 2.2f;
            RenderSettings.ambientSkyColor = ambientSky * amb;
            RenderSettings.ambientEquatorColor = ambientEquator * amb;
            RenderSettings.ambientGroundColor = ambientGround * amb;

            _skyFlash = dt <= 0f ? _skyFlash : Mathf.MoveTowards(_skyFlash, 0f, dt * 2f);
            if (skyMaterial != null)
            {
                Color flash = new Color(1f, 0.6f, 0.3f) * _skyFlash;
                if (Brightest != null)
                {
                    flash += flareColor * best * 0.06f;
                    if (_skyFlash < best * 0.1f) _skyFlashDir = (Brightest.transform.position - focus).normalized;
                }
                skyMaterial.SetColor(FlashColorId, flash);
                skyMaterial.SetVector(FlashDirId, _skyFlashDir);
            }
        }
    }
}
