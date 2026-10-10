using System.Collections;
using System.Collections.Generic;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace FieldMedic.Audio
{
    /// <summary>
    /// Combat ambience director. A quiet night bed (crickets / rain) under a front that never sleeps: distant
    /// artillery and MLRS (flash first, sound after distance/343 s), machine-gun bursts with tracers, drones by
    /// sound (recon hum circling, FPV whine diving, Shahed "moped" high above). Scenario API for scripted near
    /// misses: <see cref="PlayDropGrenade"/>, <see cref="PlayIncomingShell"/>, <see cref="PlaySniperCrack"/>.
    /// Near blasts → tinnitus + low-pass muffle 3–5 s + comfort-limited shake + debris.
    /// Routing: AudioMixer groups when assigned (Assets/Audio/CombatMixer.mixer); muffle uses mixer snapshots if
    /// present and always an AudioLowPassFilter on the managed sources (works without the mixer too).
    /// </summary>
    public sealed class CombatAmbience : MonoBehaviour
    {
        public static CombatAmbience Instance { get; private set; }

        [Header("Clips")]
        public AudioClip[] nightBed;
        public AudioClip[] rainBed;
        public AudioClip[] artilleryOut;
        public AudioClip[] explosionFar;
        public AudioClip[] explosionNear;
        public AudioClip[] gunfireDistant;
        public AudioClip[] incomingWhistle;
        public AudioClip[] bulletWhiz;
        public AudioClip[] bulletImpact;
        public AudioClip[] debris;
        public AudioClip[] droneFpv;
        public AudioClip[] droneRecon;
        public AudioClip[] droneShahed;
        public AudioClip[] tinnitus;
        public AudioClip[] flarePop;

        [Header("Mixer (optional)")]
        public AudioMixer mixer;
        public AudioMixerGroup ambienceGroup, sfxGroup, voiceGroup, tinnitusGroup;
        public AudioMixerSnapshot normalSnapshot, muffledSnapshot;

        [Header("Behaviour")]
        public Transform listener;
        public bool autoAmbience = true;
        public float intensity = 1f;              // 0 = calm night, 1 = active front, 2 = heavy shelling
        public Vector2 artilleryInterval = new Vector2(5f, 16f);
        public Vector2 gunfireInterval = new Vector2(7f, 22f);
        public Vector2 droneInterval = new Vector2(40f, 90f);

        const int Pool = 14;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly List<AudioLowPassFilter> _filters = new List<AudioLowPassFilter>();
        readonly List<DroneAudio> _drones = new List<DroneAudio>();
        AudioSource _bed, _rain, _tinnitus;
        int _next;
        float _nextArt, _nextGun, _nextDrone;
        float _muffleUntil, _muffleAmount;

        public float MuffleAmount => _muffleAmount;
        public float LastNearBlastTime { get; private set; } = -99f;
        public int EventsPlayed { get; private set; }

        public bool DroneOverhead
        {
            get
            {
                if (listener == null) return false;
                foreach (var d in _drones) if (d.IsOverhead(listener.position)) return true;
                return false;
            }
        }

        void Awake()
        {
            Instance = this;
            if (listener == null && Camera.main != null) listener = Camera.main.transform;
            for (int i = 0; i < Pool; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.outputAudioMixerGroup = sfxGroup;
                _pool.Add(s);
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = 22000f;
                _filters.Add(lp);
            }
            _bed = MakeLoop("NightBed", ambienceGroup, 0.35f);
            _rain = MakeLoop("RainBed", ambienceGroup, 0.4f);
            _tinnitus = MakeLoop("Tinnitus", tinnitusGroup, 0f);
            _tinnitus.loop = true;
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Drone" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.outputAudioMixerGroup = sfxGroup;
                _filters.Add(go.AddComponent<AudioLowPassFilter>());
                _drones.Add(go.AddComponent<DroneAudio>());
            }
            float t = Time.time;
            _nextArt = t + Random.Range(2f, 6f);
            _nextGun = t + Random.Range(3f, 8f);
            _nextDrone = t + Random.Range(12f, 25f);
        }

        void Start()
        {
            if (nightBed != null && nightBed.Length > 0) { _bed.clip = Pick(nightBed); _bed.Play(); }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        AudioSource MakeLoop(string name, AudioMixerGroup g, float vol)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = vol;
            s.outputAudioMixerGroup = g;
            if (name != "Tinnitus") _filters.Add(go.AddComponent<AudioLowPassFilter>());
            return s;
        }

        static AudioClip Pick(AudioClip[] bank) => bank == null || bank.Length == 0 ? null : bank[Random.Range(0, bank.Length)];

        Vector3 ListenerPos => listener != null ? listener.position : transform.position;

        /// <summary>Plays a one-shot from the pool at a world position (3D).</summary>
        public AudioSource PlayAt(AudioClip clip, Vector3 pos, float volume, float minDist = 2f, float maxDist = 500f, float pitch = 1f, float delay = 0f)
        {
            if (clip == null) return null;
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.transform.position = pos;
            s.clip = clip;
            s.volume = volume;
            s.pitch = pitch;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.spatialBlend = 1f;
            if (delay > 0f) s.PlayDelayed(delay); else s.Play();
            EventsPlayed++;
            return s;
        }

        /// <summary>Distant sound: placed ~80 m away in the true direction, volume by real distance, delayed by d/343.</summary>
        public void PlayDistant(AudioClip clip, Vector3 realPos, float loudness)
        {
            float d = Vector3.Distance(realPos, ListenerPos);
            Vector3 dir = (realPos - ListenerPos).normalized;
            float vol = Mathf.Clamp01(loudness * 300f / Mathf.Max(300f, d));
            PlayAt(clip, ListenerPos + dir * 80f, vol, 40f, 2000f, Random.Range(0.85f, 1.05f), EnvMath.SoundDelay(d));
        }

        // ------------------------------------------------------------------ scenario API
        /// <summary>Recon drone drop: click above, short whistle of the falling VOG, blast at the point.</summary>
        public void PlayDropGrenade(Vector3 position)
        {
            StartCoroutine(DropRoutine(position));
        }

        IEnumerator DropRoutine(Vector3 p)
        {
            PlayAt(Pick(droneRecon), p + Vector3.up * 30f, 0.5f, 6f, 200f);
            yield return new WaitForSeconds(1.2f);
            PlayAt(Pick(incomingWhistle), p + Vector3.up * 6f, 0.35f, 3f, 60f, 1.6f);
            yield return new WaitForSeconds(1.1f);
            Detonate(p, 0.6f);
        }

        /// <summary>Incoming artillery/mortar: whistle grows for ~2 s, then the blast (near or far by distance).</summary>
        public void PlayIncomingShell(Vector3 position)
        {
            StartCoroutine(IncomingRoutine(position));
        }

        IEnumerator IncomingRoutine(Vector3 p)
        {
            var w = PlayAt(Pick(incomingWhistle), Vector3.Lerp(ListenerPos, p, 0.6f) + Vector3.up * 15f, 0.85f, 10f, 400f);
            float len = w != null && w.clip != null ? Mathf.Min(w.clip.length, 2.4f) : 2f;
            yield return new WaitForSeconds(len);
            if (w != null) w.Stop();
            Detonate(p, 1f);
        }

        /// <summary>Supersonic crack past the head, impact into the parapet, the rifle's thump arrives later.</summary>
        public void PlaySniperCrack()
        {
            var h = ListenerPos;
            PlayAt(Pick(bulletWhiz), h + new Vector3(Random.Range(-0.6f, 0.6f), 0.35f, 0.4f), 1f, 1f, 50f, 1.1f);
            var hitPos = new Vector3(h.x + Random.Range(-0.8f, 0.8f), 1.85f, h.z + 0.9f);
            BulletImpact(hitPos);
            PlayDistant(Pick(gunfireDistant), h + new Vector3(Random.Range(-100f, 100f), 0f, 600f), 0.6f);
        }

        /// <summary>Bullet whiz warning (threat system cue).</summary>
        public void PlayWhiz()
        {
            var h = ListenerPos;
            PlayAt(Pick(bulletWhiz), h + new Vector3(Random.Range(-1f, 1f), 0.5f, 0.6f), 0.9f, 1f, 50f, Random.Range(0.95f, 1.15f));
        }

        /// <summary>Round hits the parapet next to the player: thud + dirt puff.</summary>
        public void BulletImpact(Vector3 at)
        {
            PlayAt(Pick(bulletImpact), at, 1f, 1f, 60f);
            if (CombatVfx.Instance != null) CombatVfx.Instance.TrenchDust(at, 0.25f);
        }

        /// <summary>Blast at a point: visual + correct-delay sound; near blasts hit the player's senses.</summary>
        public void Detonate(Vector3 p, float charge)
        {
            float d = Vector3.Distance(p, ListenerPos);
            if (d < 120f)
            {
                CombatVfx.Instance?.ExplosionNear(p, charge);
                PlayAt(Pick(explosionNear), p, 1f, 8f, 600f, Random.Range(0.9f, 1.05f), EnvMath.SoundDelay(d));
                if (d < 40f) PlayAt(Pick(debris), ListenerPos + Vector3.up * 0.8f, Mathf.Clamp01(1.2f - d / 40f), 1f, 20f, 1f, EnvMath.SoundDelay(d) + 0.4f);
                if (d < 18f) NearBlast(d, charge);
            }
            else
            {
                CombatVfx.Instance?.ExplosionFar(p, charge);
                NightLightingDirector.Instance?.SkyFlash(p, Mathf.Clamp01(charge * 600f / d));
                PlayDistant(Pick(explosionFar), p, charge);
            }
        }

        /// <summary>Senses after a close blast: tinnitus loudness and muffle duration grow as distance shrinks.</summary>
        public void NearBlast(float distance, float charge)
        {
            float k = Mathf.Clamp01((18f - distance) / 14f) * Mathf.Clamp01(charge);
            if (k <= 0.05f) return;
            LastNearBlastTime = Time.time;
            _muffleAmount = Mathf.Max(_muffleAmount, k);
            _muffleUntil = Mathf.Max(_muffleUntil, Time.time + 3f + 2f * k);
            if (tinnitus != null && tinnitus.Length > 0)
            {
                _tinnitus.clip = Pick(tinnitus);
                _tinnitus.volume = 0.25f + 0.45f * k;
                _tinnitus.Play();
            }
            if (muffledSnapshot != null) muffledSnapshot.TransitionTo(0.05f);
            CameraShake.Add(EnvMath.ShakeAmplitude(distance, charge, ComfortSettings.ShakeLimit), 0.5f + 0.4f * k);
            CombatVfx.Instance?.RoofDust(new Vector3(-8.2f, 1.9f, -1.4f), k);
        }

        // ------------------------------------------------------------------ loop
        void Update()
        {
            float now = Time.time;
            UpdateMuffle(now, Time.deltaTime);
            if (!autoAmbience || intensity <= 0f) return;
            float busy = Mathf.Max(0.25f, intensity);

            if (now >= _nextArt)
            {
                _nextArt = now + Random.Range(artilleryInterval.x, artilleryInterval.y) / busy;
                var p = ListenerPos + Polar(Random.Range(-75f, 75f), Random.Range(900f, 5000f));
                if (Random.value < 0.4f) PlayDistant(Pick(artilleryOut), ListenerPos + Polar(Random.Range(150f, 210f), Random.Range(1500f, 4000f)), 0.9f);
                else if (Random.value < 0.15f) StartCoroutine(MlrsSeries(p));
                else Detonate(p, Random.Range(0.8f, 1.6f));
            }
            if (now >= _nextGun)
            {
                _nextGun = now + Random.Range(gunfireInterval.x, gunfireInterval.y) / busy;
                var p = ListenerPos + Polar(Random.Range(-70f, 70f), Random.Range(250f, 900f));
                PlayDistant(Pick(gunfireDistant), p, 0.55f);
                CombatVfx.Instance?.TracerBurst(p + Vector3.up * 1.2f, Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * (ListenerPos - p).normalized + Vector3.up * 0.03f, Random.Range(3, 7));
            }
            if (now >= _nextDrone)
            {
                _nextDrone = now + Random.Range(droneInterval.x, droneInterval.y) / busy;
                float r = Random.value;
                if (r < 0.5f) StartDrone(DroneKind.Recon, Random.Range(35f, 70f));
                else if (r < 0.8f) StartDrone(DroneKind.Shahed, Random.Range(45f, 70f));
                else StartDrone(DroneKind.Fpv, Random.Range(9f, 14f));
            }
        }

        public DroneAudio StartDrone(DroneKind kind, float life)
        {
            var clip = Pick(kind == DroneKind.Fpv ? droneFpv : kind == DroneKind.Recon ? droneRecon : droneShahed);
            if (clip == null) return null;
            foreach (var d in _drones)
                if (!d.Active) { d.Begin(kind, clip, ListenerPos, life); return d; }
            return null;
        }

        IEnumerator MlrsSeries(Vector3 p)
        {
            int n = Random.Range(6, 14);
            for (int i = 0; i < n; i++)
            {
                Detonate(p + new Vector3(Random.Range(-250f, 250f), 0f, Random.Range(-150f, 150f)), Random.Range(0.8f, 1.3f));
                yield return new WaitForSeconds(Random.Range(0.25f, 0.7f));
            }
        }

        void UpdateMuffle(float now, float dt)
        {
            if (now > _muffleUntil && _muffleAmount > 0f)
            {
                _muffleAmount = Mathf.MoveTowards(_muffleAmount, 0f, dt * 0.5f);
                if (_muffleAmount <= 0f && normalSnapshot != null) normalSnapshot.TransitionTo(1.5f);
            }
            float cutoff = Mathf.Lerp(22000f, 650f, Mathf.Pow(_muffleAmount, 0.6f));
            bool on = _muffleAmount > 0.001f; // filters cost DSP on Quest: only while muffled
            foreach (var f in _filters) if (f != null) { f.enabled = on; f.cutoffFrequency = cutoff; }
            if (_tinnitus.isPlaying)
            {
                float target = now > _muffleUntil ? 0f : _tinnitus.volume;
                _tinnitus.volume = Mathf.MoveTowards(_tinnitus.volume, target, dt * 0.15f);
                if (_tinnitus.volume <= 0.01f) _tinnitus.Stop();
            }
        }

        public void SetRain(bool on)
        {
            if (on && rainBed != null && rainBed.Length > 0) { _rain.clip = Pick(rainBed); _rain.Play(); }
            else _rain.Stop();
            CombatVfx.Instance?.SetRain(on);
        }

        static Vector3 Polar(float azimuthDeg, float dist)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
        }
    }
}
