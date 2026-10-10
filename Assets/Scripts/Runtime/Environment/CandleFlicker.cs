using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>Slow organic flicker for the dugout trench candle (the only warm light in the scene).</summary>
    [RequireComponent(typeof(Light))]
    public sealed class CandleFlicker : MonoBehaviour
    {
        Light _l;
        float _base, _seed;

        void Awake() { _l = GetComponent<Light>(); _base = _l.intensity; _seed = Random.value * 10f; }

        void Update()
        {
            float t = Time.time * 3.1f + _seed;
            _l.intensity = _base * (0.82f + 0.12f * Mathf.PerlinNoise(t, 0.5f) + 0.06f * Mathf.Sin(t * 7.3f));
        }
    }
}
