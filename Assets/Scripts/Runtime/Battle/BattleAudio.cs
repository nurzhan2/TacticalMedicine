using FieldMedic.Audio;
using FieldMedic.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Sound of the battle layers: a pool of 3D one-shot voices (gunfire near/far, cannon, rockets, bullet cracks,
    /// impacts) with voice stealing by audibility, plus two 2D beds that never stop (distant artillery rumble and a
    /// battle ambience). Far sounds arrive late (distance / 343 m/s). Separate from <see cref="CombatAmbience"/> so
    /// the dense near fire does not starve its scripted scenario sounds; muffle after a near blast applies here too.
    /// </summary>
    public sealed class BattleAudio : MonoBehaviour
    {
        public static BattleAudio Instance { get; private set; }

        [Header("Clips")]
        public AudioClip[] rifleBurst;     // near enemy/friendly rifles
        public AudioClip[] mgBurst;        // machine guns
        public AudioClip[] distantFire;    // far gunfire layer
        public AudioClip[] bulletCrack;    // supersonic crack past the head
        public AudioClip[] bulletWhiz;
        public AudioClip[] impactDirt;
        public AudioClip[] impactHard;     // ricochet off concrete/brick/steel
        public AudioClip[] tankCannon;
        public AudioClip[] tankEngine;
        public AudioClip[] rocketLaunch;
        public AudioClip[] bombLarge;
        public AudioClip[] jetFlyby;
        public AudioClip[] heliLoop;
        public AudioClip[] smokeHiss;
        public AudioClip[] pinPull;
        public AudioClip[] artilleryBed;
        public AudioClip[] battleBed;
        public AudioClip[] breathing;

        [Header("Routing")]
        public AudioMixerGroup sfxGroup, ambienceGroup;
        public Transform listener;
        [Range(0f, 1f)] public float bedVolume = 0.32f;
        [Range(0f, 1f)] public float battleBedVolume = 0.22f;

        const int Voices = 22;
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly AudioLowPassFilter[] _filters = new AudioLowPassFilter[Voices + 3];
        readonly float[] _priority = new float[Voices];
        AudioSource _bedA, _bedB, _breath;
        int _rr;
        public int Played { get; private set; }
        public int Stolen { get; private set; }

        Vector3 ListenerPos => listener != null ? listener.position : transform.position;

        void Awake()
        {
            Instance = this;
            if (listener == null && Camera.main != null) listener = Camera.main.transform;
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("BattleVoice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.dopplerLevel = 0f;
                s.outputAudioMixerGroup = sfxGroup;
                _voices[i] = s;
                _filters[i] = go.AddComponent<AudioLowPassFilter>();
                _filters[i].enabled = false;
            }
            _bedA = Bed("ArtilleryBed", out _filters[Voices]);
            _bedB = Bed("BattleBed", out _filters[Voices + 1]);
            _breath = Bed("Breathing", out _filters[Voices + 2]);
            _breath.volume = 0f;
        }

        AudioSource Bed(string name, out AudioLowPassFilter f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.outputAudioMixerGroup = ambienceGroup;
            f = go.AddComponent<AudioLowPassFilter>();
            f.enabled = false;
            return s;
        }

        void Start()
        {
            StartBed(_bedA, artilleryBed, bedVolume);
            StartBed(_bedB, battleBed, battleBedVolume);
        }

        static void StartBed(AudioSource s, AudioClip[] bank, float vol)
        {
            var c = Pick(bank);
            if (c == null) return;
            s.clip = c;
            s.volume = vol;
            s.time = Random.Range(0f, c.length * 0.8f);
            s.Play();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static AudioClip Pick(AudioClip[] bank) => bank == null || bank.Length == 0 ? null : bank[Random.Range(0, bank.Length)];

        public bool BedsPlaying => _bedA.isPlaying || _bedB.isPlaying;

        /// <summary>3D one-shot. Voice stealing: the least audible playing voice is replaced when all are busy;
        /// a new sound quieter than everything playing is dropped.</summary>
        public AudioSource Play(AudioClip clip, Vector3 pos, float volume, float minDist = 3f, float maxDist = 600f, float pitch = 1f, float delay = 0f)
        {
            if (clip == null || volume <= 0.001f) return null;
            float d = Vector3.Distance(pos, ListenerPos);
            float audibility = volume * minDist / Mathf.Max(minDist, d);
            int slot = -1;
            float worst = float.MaxValue;
            for (int k = 0; k < Voices; k++)
            {
                int i = (_rr + k) % Voices;
                var v = _voices[i];
                if (!v.isPlaying) { slot = i; break; }
                if (_priority[i] < worst) { worst = _priority[i]; slot = i; }
            }
            if (slot < 0) return null;
            if (_voices[slot].isPlaying)
            {
                if (worst > audibility) return null;
                Stolen++;
            }
            _rr = (slot + 1) % Voices;
            var s = _voices[slot];
            s.Stop();
            s.transform.position = pos;
            s.clip = clip;
            s.volume = volume;
            s.pitch = pitch;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            _priority[slot] = audibility;
            if (delay > 0f) s.PlayDelayed(delay); else s.Play();
            Played++;
            return s;
        }

        /// <summary>A far sound: placed ~70 m away in its true direction (keeps 3D cues without 500 m rolloff),
        /// loudness by real distance, delayed by the speed of sound.</summary>
        public void PlayFar(AudioClip clip, Vector3 realPos, float loudness, float pitch = 1f)
        {
            Vector3 lp = ListenerPos;
            Vector3 d = realPos - lp;
            float dist = d.magnitude;
            if (dist < 1f) { Play(clip, realPos, loudness, 4f, 500f, pitch); return; }
            float vol = Mathf.Clamp01(loudness * 120f / Mathf.Max(120f, dist));
            Play(clip, lp + d / dist * Mathf.Min(70f, dist), vol, 30f, 1500f, pitch, EnvMath.SoundDelay(dist));
        }

        /// <summary>Gunfire at a position: near (&lt; 90 m) as a real 3D source, far as the distant layer.</summary>
        public void Gunfire(Vector3 pos, bool mg, float loudness = 1f)
        {
            float d = Vector3.Distance(pos, ListenerPos);
            if (d < 90f) Play(Pick(mg ? mgBurst : rifleBurst), pos, loudness * (mg ? 1f : 0.85f), 6f, 400f, Random.Range(0.93f, 1.07f), EnvMath.SoundDelay(d));
            else PlayFar(Pick(distantFire), pos, loudness * 0.7f, Random.Range(0.9f, 1.05f));
        }

        /// <summary>0..1 suppression → heavy breathing loop volume (2D).</summary>
        public void SetBreathing(float k)
        {
            if (_breath == null) return;
            float target = Mathf.Clamp01(k) * 0.55f;
            if (target > 0.02f && !_breath.isPlaying)
            {
                var c = Pick(breathing);
                if (c == null) return;
                _breath.clip = c;
                _breath.Play();
            }
            _breath.volume = Mathf.MoveTowards(_breath.volume, target, Time.deltaTime * 0.6f);
            if (_breath.volume <= 0.005f && target <= 0.02f && _breath.isPlaying) _breath.Stop();
        }

        void Update()
        {
            // share the near-blast muffle with the combat ambience (same low-pass curve)
            var amb = CombatAmbience.Instance;
            float m = amb != null ? amb.MuffleAmount : 0f;
            bool on = m > 0.001f;
            float cutoff = Mathf.Lerp(22000f, 650f, Mathf.Pow(m, 0.6f));
            for (int i = 0; i < _filters.Length; i++)
            {
                var f = _filters[i];
                if (f == null) continue;
                if (f.enabled != on) f.enabled = on;
                if (on) f.cutoffFrequency = cutoff;
            }
        }
    }
}
