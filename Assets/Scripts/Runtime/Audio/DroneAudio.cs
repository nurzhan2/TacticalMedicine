using FieldMedic.Environment;
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

        // visible silhouette (recon + FPV; the Shahed at 450 m is only a sound)
        DroneVisual _visual;
        Vector3 _lastPos;
        // recon "hover over target" mode for the VOG drop: glide down to the hover point, hold, then climb away
        bool _hover;
        Vector3 _hoverAt;
        float _hoverBlend, _hoverRelease = -1f;
        public bool Hovering => _hover && _hoverBlend > 0.98f;
        public DroneVisual Visual => _visual;

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
            _hover = false; _hoverBlend = 0f; _hoverRelease = -1f;
            if (_visual != null && _visual.Kind != k) { Destroy(_visual.gameObject); _visual = null; }
            if (_visual == null && k != DroneKind.Shahed) _visual = DroneVisual.Build(transform, k);
            if (_visual != null) _visual.gameObject.SetActive(true);
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
            _lastPos = transform.position;
            _src.Play();
        }

        /// <summary>Recon only: come down to <paramref name="height"/> m above <paramref name="aim"/> and hold there
        /// (the operator lines up the drop on the thermal camera) until <see cref="ReleaseHover"/>.</summary>
        public void HoverOver(Vector3 aim, float height)
        {
            if (kind != DroneKind.Recon) return;
            _hover = true;
            _hoverAt = EnvMath.HoverPoint(aim, height, new Vector3(0.6f, 0f, 0.3f));
            _hoverRelease = -1f;
        }

        /// <summary>After the drop: climb back to the patrol orbit over a few seconds.</summary>
        public void ReleaseHover() { if (_hover) _hoverRelease = _age; }

        /// <summary>FPV only: aim the terminal dive at a point (the impact) instead of a random spot near the player.</summary>
        public void DiveAt(Vector3 target)
        {
            if (kind != DroneKind.Fpv) return;
            var d = _from - target; d.y = 0f;
            float a = Random.Range(-0.5f, 0.5f);
            var dir = d.sqrMagnitude > 1f ? Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * d.normalized : Vector3.forward;
            _from = target + dir * 90f + Vector3.up * 20f; // ~140 km/h at the end of the dive
            _to = target;
            Step(0f);
            _lastPos = transform.position;
        }

        public void Stop()
        {
            Active = false;
            _hover = false;
            if (_src != null) _src.Stop();
            if (_visual != null) _visual.gameObject.SetActive(false);
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
                    var orbit = centre + new Vector3(Mathf.Cos(ang) * r, 32f + 6f * Mathf.Sin(_age * 0.07f), 12f + Mathf.Sin(ang) * r);
                    if (_hover)
                    {
                        bool leaving = _hoverRelease >= 0f;
                        _hoverBlend = Mathf.MoveTowards(_hoverBlend, leaving ? 0f : 1f, dt / (leaving ? 6f : 2.5f));
                        if (leaving && _hoverBlend <= 0f) _hover = false;
                        // station-keeping wobble in the wind
                        var hold = _hoverAt + new Vector3(Mathf.Sin(_age * 0.8f + _seed) * 0.25f, Mathf.Sin(_age * 1.3f) * 0.12f, Mathf.Cos(_age * 0.6f + _seed) * 0.2f);
                        float e = Mathf.SmoothStep(0f, 1f, _hoverBlend);
                        orbit = Vector3.Lerp(orbit, hold, e);
                    }
                    transform.position = orbit;
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
            if (_visual != null && dt > 0f)
            {
                _visual.Pose((transform.position - _lastPos) / dt, dt);
                _lastPos = transform.position;
            }
            if (_age >= lifetime) Stop();
        }

        static float Fade(float k) => Mathf.Clamp01(k * 12f) * Mathf.Clamp01((1f - k) * 12f);
    }
}
