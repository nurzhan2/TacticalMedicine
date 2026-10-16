using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// Erlan's breath as procedural 3D audio at his mouth (w8 breathing check): band-limited noise whose loudness is
    /// the airflow of the procedural chest (derivative of <c>CasualtyRig.BreathSignal</c>) — so the sound follows the
    /// physiology exactly (rate, depth, agonal gasps) — plus a low snore buzz when the airway is blocked. Quiet at
    /// a distance; the medic hears it clearly with the ear within ~25 cm of the mouth (log roll-off from 0.12 m).
    /// Audio thread reads a few floats written on the main thread; no allocations.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BreathSynth : MonoBehaviour
    {
        [Range(0f, 1f)] public float gain = 0.5f;
        public bool muted;

        // main thread → audio thread
        volatile float _flow;       // 0..1 airflow (inhale or exhale)
        volatile float _snore;      // 0..1
        volatile float _inhale;     // 1 inhale, 0 exhale (spectral tilt)
        float _prevSignal;
        bool _hasPrev;

        // audio thread state
        uint _rng = 22222u;
        float _lp1, _lp2, _hp, _env, _phase;
        int _rate = 48000;

        public float Flow => _flow;
        public AudioSource Source { get; private set; }

        public static BreathSynth Create(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("BreathAudio");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 0.12f;
            src.maxDistance = 4f;
            src.dopplerLevel = 0f;
            src.volume = 1f;
            var s = go.AddComponent<BreathSynth>();
            return s;
        }

        void Awake()
        {
            Source = GetComponent<AudioSource>();
            _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        }

        void OnEnable() { if (Source != null && !Source.isPlaying) Source.Play(); }

        /// <summary>Feed the chest signal (−1..1), depth 0..1, snore 0..1 once per frame.</summary>
        public void Drive(float chestSignal, float depth, float snore, float dt)
        {
            if (dt <= 0f) return;
            float d = _hasPrev ? (chestSignal - _prevSignal) / dt : 0f;
            _prevSignal = chestSignal;
            _hasPrev = true;
            // ~1 at a normal breath (amplitude 1 at 15/min ≈ 1.6 rad/s), louder when deep or fast
            float flow = Mathf.Clamp01(Mathf.Abs(d) / 1.6f * (0.35f + depth));
            _flow = muted ? 0f : flow * gain;
            _inhale = d > 0f ? 1f : 0f;
            _snore = muted ? 0f : snore * gain * Mathf.Clamp01(Mathf.Abs(d) * 0.8f + 0.2f);
        }

        float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xffff) / 32768f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float flow = _flow, snore = _snore, inh = _inhale;
            float a1 = inh > 0.5f ? 0.18f : 0.1f;            // inhale brighter, exhale softer/darker
            float attack = 1f - Mathf.Exp(-1f / (0.03f * _rate));
            float f0 = 72f / _rate;
            for (int i = 0; i < data.Length; i += channels)
            {
                _env += (flow - _env) * attack;
                float n = Noise();
                _lp1 += (n - _lp1) * a1;
                _lp2 += (_lp1 - _lp2) * a1;
                _hp += (_lp2 - _hp) * 0.01f;
                float air = (_lp2 - _hp) * 2.4f * _env;
                _phase += f0; if (_phase > 1f) _phase -= 1f;
                float buzz = snore > 0.001f ? (_phase * 2f - 1f) * (0.5f + 0.5f * _lp1) * snore * 0.35f : 0f;
                float v = air + buzz;
                for (int c = 0; c < channels; c++) data[i + c] = v;
            }
        }
    }
}
