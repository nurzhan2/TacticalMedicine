using UnityEngine;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// Shaking hands from blood loss (w9b): right after the HandRig moved the hand to the tracked pose
    /// this adds a small, fast, smooth offset — grabbing a strap tail or writing on the TIME tab gets harder, as in
    /// shock. Only the hand moves (never the camera).
    /// </summary>
    [DefaultExecutionOrder(-49)]
    public sealed class HandTremor : MonoBehaviour
    {
        /// Peak offset in metres (0 = steady).
        public float amplitude;
        public float frequency = 9.5f;
        float _seed;

        void Awake() => _seed = Random.value * 100f;

        void Update()
        {
            if (amplitude <= 1e-5f) return;
            float t = Time.time * frequency + _seed;
            var o = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * (2f * amplitude);
            transform.position += o;
        }
    }
}
