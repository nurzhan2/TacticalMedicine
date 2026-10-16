using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Ерлан's voice: words (RU/KZ mix from <see cref="VoiceBank"/>), pain vocalisations scaled by pain,
    /// and a breathing loop that tells the trainee about the airway (snoring when obstructed, gasping in tension
    /// pneumothorax, heavy fast breathing in shock). Behaviour per RESEARCH §6.
    /// </summary>
    public sealed class CasualtyVoice : MonoBehaviour
    {
        public VoiceBank bank;
        public AudioSource voice;
        public AudioSource breath;
        [Tooltip("Seconds between unsolicited lines, scaled by state.")]
        public Vector2 chatterInterval = new Vector2(4f, 8f);
        public bool muted;

        float _cooldown = 2f;
        float _sinceScream = 99f;
        AudioClip _breathClip;
        readonly System.Random _rng = new System.Random();
        public string LastLine { get; private set; }

        public static AudioSource MakeSource(GameObject go, bool loop)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 0.6f;
            s.maxDistance = 18f;
            s.dopplerLevel = 0f;
            return s;
        }

        public bool Say(string id, bool interrupt = false)
        {
            if (muted || bank == null || voice == null) return false;
            if (voice.isPlaying && !interrupt) return false;
            var clip = bank.Get(id, GameSettings.CasualtyKazakhShare, _rng);
            if (clip == null) return false;
            voice.pitch = 1f + (float)(_rng.NextDouble() - 0.5) * 0.06f;
            voice.clip = clip;
            voice.Play();
            LastLine = id;
            return true;
        }

        public bool Vocalise(AudioClip[] pool, float volume = 1f, bool interrupt = false)
        {
            if (muted || voice == null) return false;
            if (voice.isPlaying && !interrupt) return false;
            var clip = VoiceBank.Pick(pool, _rng);
            if (clip == null) return false;
            voice.pitch = 0.95f + (float)_rng.NextDouble() * 0.1f;
            voice.PlayOneShot(clip, volume);
            LastLine = clip.name;
            return true;
        }

        /// <summary>Immediate reaction to being hit (fall).</summary>
        public void OnHit(CasualtyController c)
        {
            bool leg = c.Patient.wounds.Any(w => BodyRegions.LimbId(w.region)?.StartsWith("leg") == true);
            if (!Say(leg ? (_rng.NextDouble() < 0.5 ? "hit_mixed" : "hit_leg") : "wounded", true))
                Vocalise(bank != null ? bank.scream : null, 1f, true);
            _cooldown = 2.5f;
            _sinceScream = 0f;
        }

        /// <summary>Acute pain spike (handling, tourniquet twist).</summary>
        public void OnPainSpike(CasualtyController c, bool tourniquet)
        {
            if (_sinceScream < 1.2f) return;
            _sinceScream = 0f;
            var avpu = c.Patient.AVPU;
            if (avpu == Consciousness.Unresponsive) return;
            if (avpu == Consciousness.Pain) { Vocalise(bank != null ? bank.groan : null, 0.6f, true); _cooldown = Mathf.Max(_cooldown, 2f); return; }
            if (tourniquet && _rng.NextDouble() < 0.6) Say("take_off", true);
            else if (_rng.NextDouble() < 0.5) Say("hurts", true);
            else Vocalise(bank != null ? bank.scream : null, 1f, true);
            _cooldown = Mathf.Max(_cooldown, 2f);
        }

        public void Tick(CasualtyController c, float dt)
        {
            _sinceScream += dt;
            var p = c.Patient;
            UpdateBreath(c);
            if (!c.IsDown || p.outcome == Outcome.Died) return;
            _cooldown -= dt;
            if (_cooldown > 0f || (voice != null && voice.isPlaying)) return;

            var avpu = p.AVPU;
            float pain = c.Pain.level;
            float next = Random.Range(chatterInterval.x, chatterInterval.y);
            switch (avpu)
            {
                case Consciousness.Alert:
                    if (p.pneumo is PneumoState.Open or PneumoState.Tension && Chance(0.5)) { Say(Chance(0.5) ? "cant_breathe" : "help"); break; }
                    if (p.pneumo == PneumoState.Open && Chance(0.5)) { Vocalise(bank?.cough); break; }
                    if (pain >= PainModel.ScreamThreshold) { if (Chance(0.6)) Vocalise(bank?.scream); else Say(Pick("hurts", "help", "blood_much")); }
                    else if (pain >= PainModel.GroanThreshold) { if (Chance(0.55)) Vocalise(bank?.groan); else Say(Pick("blood_much", "wounded", "will_i_die", "dont_leave", "brother")); }
                    else Say(p.HemorrhageClass >= 2 ? Pick("cold", "water", "holding") : Pick("holding", "brother"));
                    break;
                case Consciousness.Voice:
                    next *= 1.6f;
                    if (pain >= PainModel.GroanThreshold && Chance(0.5)) Vocalise(bank?.groan, 0.7f);
                    else Say(p.LossFraction > 0.33f ? Pick("tell_mom", "cold", "will_i_die", "dizzy") : Pick("cold", "water", "dizzy", "holding"));
                    break;
                case Consciousness.Pain:
                    next *= 2f;
                    if (pain > 0.3f) Vocalise(bank?.groan, 0.5f);
                    break;
            }
            _cooldown = next;
        }

        void UpdateBreath(CasualtyController c)
        {
            if (breath == null || bank == null) return;
            var p = c.Patient;
            AudioClip want = null;
            float vol = 0.5f, pitch = 1f;
            if (p.outcome != Outcome.Died && c.IsDown)
            {
                if (p.airwayObstructed && !p.airwayAdjunct) { want = First(bank.snore); vol = 0.8f; }
                else if (p.pneumo == PneumoState.Tension || p.SpO2 < 88f) { want = First(bank.gasp); vol = 0.7f; pitch = 1.05f; }
                else if (p.RespRate > 20f || p.HeartRate > 105f) { want = First(bank.breathHeavy); vol = Mathf.Lerp(0.35f, 0.75f, (p.RespRate - 20f) / 20f); pitch = Mathf.Lerp(0.95f, 1.15f, (p.RespRate - 20f) / 20f); }
            }
            if (want != _breathClip)
            {
                _breathClip = want;
                breath.Stop();
                if (want != null) { breath.clip = want; breath.Play(); }
            }
            breath.volume = muted ? 0f : vol;
            breath.pitch = pitch;
        }

        bool Chance(double p) => _rng.NextDouble() < p;
        string Pick(params string[] ids) => ids[_rng.Next(ids.Length)];
        static AudioClip First(AudioClip[] a) => a != null && a.Length > 0 ? a[0] : null;
    }
}
