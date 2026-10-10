using UnityEngine;

namespace FieldMedic.Comfort
{
    /// <summary>Global comfort options (session-level; S7 may persist them).</summary>
    public static class ComfortSettings
    {
        /// <summary>Max camera shake offset in metres. VR default is tiny (translation only, no roll).</summary>
        public static float ShakeLimit = 0.012f;
        /// <summary>0..1 cap on explosion flash brightness (photosensitivity).</summary>
        public static float FlashLimit = 1f;
        public static int SnapTurnDegrees = 45;
        public static bool TeleportEnabled = false;
        public static bool Seated = false;
        public static float SeatedOffset = 0.45f;
    }

    /// <summary>
    /// Translation-only, short, damped shake applied to the rig root (never rotation — comfort). Removes last
    /// frame's offset before applying the new one, so other movers (desktop sim, snap turn) are unaffected.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        static CameraShake _instance;
        float _amp, _until, _dur;
        Vector3 _applied;

        void Awake() => _instance = this;
        void OnDestroy() { if (_instance == this) _instance = null; }

        public static float CurrentAmplitude => _instance != null ? _instance._amp : 0f;

        public static void Add(float amplitude, float duration)
        {
            if (_instance == null || amplitude <= 0f) return;
            _instance._amp = Mathf.Max(_instance._amp * Mathf.Clamp01((_instance._until - Time.time) / Mathf.Max(0.01f, _instance._dur)), amplitude);
            _instance._dur = duration;
            _instance._until = Time.time + duration;
        }

        void LateUpdate()
        {
            transform.position -= _applied;
            _applied = Vector3.zero;
            float left = _until - Time.time;
            if (left <= 0f || _amp <= 0f) return;
            float k = left / Mathf.Max(0.01f, _dur);
            float a = _amp * k * k;
            float t = Time.time * 38f;
            _applied = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * (2f * a);
            transform.position += _applied;
        }
    }
}
