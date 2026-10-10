using UnityEngine;

namespace FieldMedic.Audio
{
    public enum DroneKind { Fpv, Recon, Shahed }

    /// <summary>
    /// A drone you hear but rarely see: moves its looping 3D AudioSource along a type-specific path.
    /// Recon (Mavic-class) hovers and circles 25–45 m above the trench with a steady hum; FPV comes in low from
    /// the field with its whine rising as it dives (pitch + speed); Shahed crosses high above with a two-stroke
    /// "moped" drone. <see cref="IsOverhead"/> feeds the threat system.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DroneAudio : MonoBehaviour
    {
        public DroneKind kind;
        public Vector3 centre;
        public float lifetime = 40f;

        AudioSource _src;
        float _age, _seed;
        Vector3 _from, _to;
        public bool Active { get; private set; }

        public bool IsOverhead(Vector3 player, float radius = 35f)
        {
            if (!Active || kind == DroneKind.Shahed) return false;
            var d = transform.position - player; d.y = 0f;
            return d.magnitude < radius;
        }

        void Awake() => _src = GetComponent<AudioSource>();

        public void Begin(DroneKind k, AudioClip clip, Vector3 around, float life)
        {
            if (_src == null) _src = GetComponent<AudioSource>();
            kind = k; centre = around; lifetime = life; _age = 0f; _seed = Random.value * 100f; Active = true;
            _src.clip = clip;
            _src.loop = true;
            _src.spatialBlend = 1f;
            _src.dopplerLevel = k == DroneKind.Fpv ? 1.2f : 0.6f;
            _src.rolloffMode = AudioRolloffMode.Logarithmic;
            switch (k)
            {
                case DroneKind.Recon:
                    _src.minDistance = 6f; _src.maxDistance = 220f; _src.volume = 0.55f; _src.pitch = 1f;
                    break;
                case DroneKind.Fpv:
                    _src.minDistance = 4f; _src.maxDistance = 400f; _src.volume = 0.8f; _src.pitch = 1.1f;
                    float a = Random.Range(-0.6f, 0.6f);
                    _from = around + new Vector3(Mathf.Sin(a) * 260f, 18f, Mathf.Cos(a) * 260f);
                    _to = around + new Vector3(Random.Range(-25f, 25f), 3f, Random.Range(8f, 30f));
                    break;
                case DroneKind.Shahed:
                    _src.minDistance = 30f; _src.maxDistance = 2500f; _src.volume = 0.9f; _src.pitch = 0.95f;
                    float b = Random.Range(0f, Mathf.PI * 2f);
                    var dir = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                    _from = around - dir * 1800f + Vector3.up * 450f;
                    _to = around + dir * 1800f + Vector3.up * 450f;
                    break;
            }
            Step(0f);
            _src.Play();
        }

        public void Stop()
        {
            Active = false;
            if (_src != null) _src.Stop();
        }

        void Update()
        {
            if (!Active) return;
            Step(Time.deltaTime);
        }

        void Step(float dt)
        {
            _age += dt;
            float k = Mathf.Clamp01(_age / lifetime);
            switch (kind)
            {
                case DroneKind.Recon:
                {
                    float r = 18f + 10f * Mathf.Sin(_age * 0.05f + _seed);
                    float ang = _age * 0.12f + _seed;
                    transform.position = centre + new Vector3(Mathf.Cos(ang) * r, 32f + 6f * Mathf.Sin(_age * 0.07f), 12f + Mathf.Sin(ang) * r);
                    _src.pitch = 1f + 0.03f * Mathf.Sin(_age * 0.9f + _seed);
                    _src.volume = 0.55f * Fade(k);
                    break;
                }
                case DroneKind.Fpv:
                {
                    float e = k * k; // accelerates
                    transform.position = Vector3.Lerp(_from, _to, e);
                    _src.pitch = 1.05f + 0.55f * e; // the angry whine rises on the dive
                    _src.volume = 0.8f * Fade(k);
                    break;
                }
                case DroneKind.Shahed:
                    transform.position = Vector3.Lerp(_from, _to, k);
                    _src.volume = 0.9f * Fade(k);
                    break;
            }
            if (_age >= lifetime) Stop();
        }

        static float Fade(float k) => Mathf.Clamp01(k * 12f) * Mathf.Clamp01((1f - k) * 12f);
    }
}
