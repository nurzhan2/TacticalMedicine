using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// The medic's own voice, as a loudness level (w8 AVPU check: "Ерлан, слышишь меня?"). No speech recognition —
    /// RMS of the headset microphone, smoothed, mapped from −52 dBFS (0) to −16 dBFS (1). One instance, created on
    /// demand. Android asks for the RECORD_AUDIO permission once; when it is refused (or there is no microphone) the
    /// level stays 0 and the shake-the-shoulder pain stimulus still works. Tests drive <see cref="Override"/>.
    /// </summary>
    public sealed class PlayerVoice : MonoBehaviour
    {
        /// <summary>Tests / bots / the desktop simulator: forced level 0..1 (null = microphone).</summary>
        public static float? Override;
        /// <summary>The editor never opens the microphone unless asked (batch runs, no permission prompts).</summary>
        public static bool EnableInEditor;
        public const float TalkThreshold = 0.35f;

        static PlayerVoice _instance;
        public static float Level => Override ?? (_instance != null ? _instance._level : 0f);
        public static bool Talking => Level >= TalkThreshold;
        public static bool MicrophoneRunning => _instance != null && _instance._device != null;

        const int Rate = 16000;
        readonly float[] _buf = new float[512];   // ~32 ms window, reused (no per-frame allocation)
        string _device;
        AudioClip _clip;
        float _level, _retry;
        bool _asked;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("PlayerVoice");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlayerVoice>();
        }

        void OnDestroy() { if (_instance == this) _instance = null; StopMic(); }

        void Update()
        {
            if (_clip == null)
            {
                _retry -= Time.unscaledDeltaTime;
                if (_retry <= 0f) { _retry = 3f; TryStart(); }
                _level = Mathf.MoveTowards(_level, 0f, Time.unscaledDeltaTime * 2f);
                return;
            }
            int pos = Microphone.GetPosition(_device);
            if (pos < _buf.Length) { return; } // just wrapped: wait for a full window
            _clip.GetData(_buf, pos - _buf.Length);
            float sum = 0f;
            for (int i = 0; i < _buf.Length; i++) sum += _buf[i] * _buf[i];
            float rms = Mathf.Sqrt(sum / _buf.Length);
            float db = 20f * Mathf.Log10(Mathf.Max(1e-6f, rms));
            float target = Mathf.InverseLerp(-52f, -16f, db);
            // fast attack, slow release (speech has gaps between syllables)
            _level = target > _level ? Mathf.Lerp(_level, target, 0.6f) : Mathf.MoveTowards(_level, target, Time.unscaledDeltaTime * 1.5f);
        }

        void TryStart()
        {
#if UNITY_EDITOR
            if (!EnableInEditor || Application.isBatchMode) return;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!_asked) { _asked = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); }
                return;
            }
#endif
            if (Microphone.devices == null || Microphone.devices.Length == 0) return;
            _device = Microphone.devices[0];
            _clip = Microphone.Start(_device, true, 1, Rate);
            if (_clip == null) { _device = null; return; }
            Debug.Log("[FMVR] voice: microphone " + _device);
        }

        void StopMic()
        {
            if (_device != null && Microphone.IsRecording(_device)) Microphone.End(_device);
            _device = null;
            _clip = null;
        }

        void OnApplicationPause(bool paused) { if (paused) StopMic(); }
    }
}
