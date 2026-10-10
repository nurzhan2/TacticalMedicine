using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Parachute illumination flare: falls slowly (≈1.5–2 m/s), drifts with the wind, swings, burns 30–40 s with
    /// magnesium flicker, leaves a thin smoke trail. Visual = additive glow sprite + small point light; the big
    /// scene lighting (swinging shadows) is done by <see cref="NightLightingDirector"/> which steers the single
    /// shadow-casting directional light from the brightest flare.
    /// </summary>
    public sealed class IlluminationFlare : MonoBehaviour
    {
        public float burnTime = 35f;
        public float fallSpeed = 1.8f;
        public float swing = 2.2f;
        public Vector3 wind = new Vector3(0.6f, 0f, -0.3f);
        public float peakIntensity = 1f;
        public Transform glow;
        public ParticleSystem smoke;
        public Light pointLight;

        public float Age { get; private set; }
        public float Brightness { get; private set; }
        public bool Burning => Age < burnTime;

        Vector3 _start;
        float _seed;
        bool _launched;

        public void Launch(Vector3 start, float seed)
        {
            _start = start; _seed = seed; Age = 0f; _launched = true;
            transform.position = start;
            if (smoke != null) { smoke.Clear(); smoke.Play(); }
            gameObject.SetActive(true);
        }

        /// <summary>Sets the flare to a given age instantly (screenshots, tests).</summary>
        public void Pose(float age)
        {
            Age = age;
            Apply();
        }

        void Update()
        {
            if (!_launched) return;
            Age += Time.deltaTime;
            Apply();
            if (Age > burnTime + 6f) { _launched = false; gameObject.SetActive(false); }
        }

        void Apply()
        {
            transform.position = EnvMath.FlarePosition(_start, wind, fallSpeed, swing, Age, _seed);
            Brightness = EnvMath.FlareBrightness(Age, burnTime, _seed) * peakIntensity;
            if (glow != null)
            {
                glow.gameObject.SetActive(Brightness > 0.01f);
                glow.localScale = Vector3.one * (2.5f + Brightness * 2.5f);
            }
            if (pointLight != null)
            {
                pointLight.enabled = Brightness > 0.01f;
                pointLight.intensity = Brightness * 6f;
            }
            if (smoke != null)
            {
                var em = smoke.emission;
                em.enabled = Brightness > 0.05f;
            }
        }
    }
}
