using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Ерлан's voice: words, pain vocalisations scaled by pain, and a breathing loop that tells the trainee about the
    /// airway (snoring when obstructed, gasping in tension pneumothorax, heavy fast breathing in shock). RESEARCH §6.
    /// w10d: his words are real recordings (ElevenLabs, Resources/Erlan/&lt;id&gt;_ru, played in both UI languages,
    /// subtitles per language); wordless pain sounds are groans only, quiet and short (owner: no screaming);
    /// leaning in to his face asks «слышишь меня?» (AVPU) and then «где болит?».
    /// </summary>
    public sealed class CasualtyVoice : MonoBehaviour
    {
        public VoiceBank bank;
        public AudioSource voice;
        public AudioSource breath;
        [Tooltip("Seconds between unsolicited lines, scaled by state.")]
        public Vector2 chatterInterval = new Vector2(4f, 8f);
        public bool muted;

        [Header("w10d: pain sounds quiet and short (owner: no screaming)")]
        [Range(0f, 1f)] public float painVolume = 0.5f;
        public float painMaxSeconds = 2.2f;
        [Header("w10d: talking to Erlan - lean in to his face (AVPU, «где болит?»)")]
        public bool answerWhenLeanedIn = true;
        public float askDistance = 0.7f;
        public AudioSource playerVoice;

        float _cooldown = 2f;
        float _sinceScream = 99f;
        float _vocalStopAt = -1f, _vocalFade;
        float _leanTimer, _askCooldown;
        int _askStage;
        bool _awaitLeave;
        float _answerAt = -1f;
        System.Action _pendingAnswer;
        AudioClip _breathClip;
        readonly System.Random _rng = new System.Random();
        public string LastLine { get; private set; }
        /// <summary>Text of Erlan's last spoken line in the current UI language (RU/KZ), null for wordless sounds.</summary>
        public string Subtitle { get; private set; }
        public float SubtitleUntil { get; private set; }

        static readonly Dictionary<string, (string ru, string kz)> Texts = new Dictionary<string, (string ru, string kz)>
        {
            ["hit_leg"] = ("Нога! Ногу не чувствую!", "Аяғым! Аяғым!"),
            ["hit_mixed"] = ("Аяғым! Нога!", "Аяғым! Нога!"),
            ["wounded"] = ("Брат, я ранен!", "Бауырым, жараландым!"),
            ["blood_much"] = ("Кровь... много крови...", "Қан кетіп жатыр..."),
            ["cant_breathe"] = ("Не могу дышать... задыхаюсь...", "Тыныс ала алмаймын..."),
            ["cold"] = ("Холодно...", "Суық..."),
            ["water"] = ("Пить... дай пить...", "Су бер..."),
            ["take_off"] = ("Сними, сними! Больно!", "Шеш, шеш! Ауырады!"),
            ["hurts"] = ("Больно!", "Ауырады!"),
            ["help"] = ("Помоги!", "Көмектес!"),
            ["will_i_die"] = ("Я умру?", "Өлемін бе?"),
            ["tell_mom"] = ("Маме скажи...", "Анама айт..."),
            ["where_leg"] = ("Где нога?.. Где моя нога?", "Аяғым қайда?"),
            ["what_happened"] = ("Что было? Где мы?", "Не болды? Қайдамыз?"),
            ["cant_hear"] = ("Не слышу! Что?!", "Естімеймін!"),
            ["dizzy"] = ("Голова кружится...", "Басым айналады..."),
            ["dont_leave"] = ("Не бросай меня!", "Мені тастама!"),
            ["holding"] = ("Держусь... держусь...", "Шыдаймын..."),
            ["brother"] = ("Бауырым...", "Бауырым..."),
            ["thanks"] = ("Спасибо, брат...", "Рахмет, бауырым..."),
            ["drone_above"] = ("Птичка над нами. Тихо.", "Дрон! Үстімізде!"),
            ["radio_air"] = ("Воздух! Все на дно!", "Ауа! Жатыңдар!"),
            ["hear_you"] = ("Слышу... слышу тебя...", "Естимін... естіп тұрмын..."),
            ["where_hurts_leg"] = ("Нога... нога горит...", "Аяғым күйіп тұр..."),
            ["where_hurts_chest"] = ("Грудь... в груди жжёт...", "Кеудем күйіп тұр..."),
            ["yes"] = ("Да...", "Иә..."),
            ["look_at_me"] = ("Смотри на меня!", "Маған қара!"),
            ["breathe"] = ("Дыши! Дыши, брат!", "Дем ал! Дем ал, бауырым!"),
            ["hold_on"] = ("Держись, я рядом.", "Шыда, мен қасыңдамын."),
        };

        /// <summary>Line text in the current UI language, or null.</summary>
        public static string Text(string id) =>
            id != null && Texts.TryGetValue(id, out var t) ? (GameSettings.KZ ? t.kz : t.ru) : null;

        public static AudioSource MakeSource(GameObject go, bool loop)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f; // owner 21.10: voices louder than the battle
            s.maxDistance = 18f;
            s.dopplerLevel = 0f;
            return s;
        }

        public bool Say(string id, bool interrupt = false)
        {
            if (muted || bank == null || voice == null) return false;
            if (voice.isPlaying && !interrupt) return false;
            // w10d: Erlan's real recorded words (Russian) in both UI languages; subtitles per language
            var real = VoiceOver.ErlanClip(id);
            if (real != null)
            {
                voice.Stop();
                _vocalStopAt = -1f;
                voice.pitch = 1f;
                voice.volume = 1f;
                voice.clip = real;
                voice.Play();
                ClipLog.Record(real, false);
                LastLine = id;
                Subtitle = Text(id);
                SubtitleUntil = Time.time + real.length + 1.5f;
                return true;
            }
            if (!VoiceOver.CasualtyTtsWords) return SayAsSound(id, interrupt);
            var clip = bank.Get(id, GameSettings.CasualtyKazakhShare, _rng);
            if (clip == null) return false;
            voice.pitch = 1f + (float)(_rng.NextDouble() - 0.5) * 0.06f;
            voice.volume = 1f;
            voice.clip = clip;
            voice.Play();
            ClipLog.Record(clip, false);
            LastLine = id;
            return true;
        }

        /// <summary>w10c: no recording for the line - its intent as a real recorded pain sound (groans only since w10d).</summary>
        bool SayAsSound(string id, bool interrupt)
        {
            switch (id)
            {
                case "hit_leg": case "hit_mixed": case "wounded": case "hurts": case "take_off":
                    return Vocalise(PainPool, 1f, interrupt);
                case "cant_breathe": case "breathe":
                    return Vocalise(bank.gasp != null && bank.gasp.Length > 0 ? bank.gasp : bank.groan, 0.9f, interrupt);
                case "help": case "blood_much": case "will_i_die": case "dont_leave":
                    return Vocalise(bank.groan, 0.75f, interrupt);
                default: // quiet lines: a low groan or silence
                    return Chance(0.5) && Vocalise(bank.groan, 0.45f, interrupt);
            }
        }

        /// <summary>w10d: the wordless pain pool - groans only (the freesound scream recordings are never played).</summary>
        AudioClip[] PainPool => bank != null ? bank.groan : null;

        /// <summary>Wordless sound (groan, cough, gasp): quiet (<see cref="painVolume"/>) and short (faded out after
        /// <see cref="painMaxSeconds"/>). A scream pool is mapped to groans.</summary>
        public bool Vocalise(AudioClip[] pool, float volume = 1f, bool interrupt = false)
        {
            if (muted || voice == null) return false;
            if (voice.isPlaying && !interrupt) return false;
            if (bank != null && pool != null && pool == bank.scream) pool = PainPool;
            var clip = VoiceBank.Pick(pool, _rng);
            if (clip == null) return false;
            voice.Stop();
            voice.pitch = 0.95f + (float)_rng.NextDouble() * 0.1f;
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume) * painVolume;
            voice.Play();
            _vocalFade = voice.volume;
            _vocalStopAt = clip.length > painMaxSeconds ? Time.time + painMaxSeconds : -1f;
            ClipLog.Record(clip, false);
            LastLine = clip.name;
            Subtitle = null;
            return true;
        }

        void UpdateVocalCut(float dt)
        {
            if (_vocalStopAt < 0f || voice == null) return;
            if (!voice.isPlaying) { _vocalStopAt = -1f; return; }
            if (Time.time < _vocalStopAt) return;
            voice.volume = Mathf.MoveTowards(voice.volume, 0f, dt * Mathf.Max(0.5f, _vocalFade) * 4f); // ~0.25 s fade
            if (voice.volume <= 0.001f) { voice.Stop(); voice.volume = 1f; _vocalStopAt = -1f; }
        }

        // ------------------------------------------------------------------ talking to Erlan (w10d)
        /// <summary>AVPU «Ты меня слышишь?»: A → «Слышу... слышу тебя...», V → «Да...», P → a low groan, U → silence.</summary>
        public bool AnswerAvpu(CasualtyController c)
        {
            if (c == null || c.Patient == null || c.Patient.outcome == Outcome.Died) return false;
            switch (c.Patient.AVPU)
            {
                case Consciousness.Alert: return Say("hear_you", true);
                case Consciousness.Voice: return Say("yes", true);
                case Consciousness.Pain: return Vocalise(PainPool, 0.5f, true);
                default: return false;
            }
        }

        /// <summary>«Где болит?»: chest wound / pneumothorax → «Грудь... в груди жжёт...», leg → «Нога... нога горит...».</summary>
        public bool AnswerWhereHurts(CasualtyController c)
        {
            var id = WhereHurtsLine(c);
            return id != null && Say(id, true);
        }

        /// <summary>Line id Erlan answers to «где болит?» (null when he cannot answer).</summary>
        public static string WhereHurtsLine(CasualtyController c)
        {
            if (c == null || c.Patient == null) return null;
            var p = c.Patient;
            if (p.outcome == Outcome.Died || p.AVPU > Consciousness.Voice) return null;
            bool breathless = p.pneumo == PneumoState.Open || p.pneumo == PneumoState.Tension;
            bool chest = breathless || p.wounds.Any(w => w.IsChest);
            bool leg = p.wounds.Any(w => BodyRegions.LimbId(w.region)?.StartsWith("leg") == true);
            if (chest && (breathless || !leg)) return "where_hurts_chest";
            if (leg) return "where_hurts_leg";
            return "hurts";
        }

        /// <summary>The player's own recorded line (2D): «Смотри на меня!», «Дыши! Дыши, брат!», «Держись, я рядом.».
        /// Returns the clip length (0 when nothing played).</summary>
        public float PlayerSays(string id)
        {
            if (muted) return 0f;
            var clip = VoiceOver.ErlanClip(id);
            if (clip == null) return 0f;
            if (playerVoice == null)
            {
                playerVoice = gameObject.AddComponent<AudioSource>();
                playerVoice.playOnAwake = false;
                playerVoice.spatialBlend = 0f;
                playerVoice.volume = 0.8f;
            }
            playerVoice.clip = clip;
            playerVoice.Play();
            ClipLog.Record(clip, false);
            return clip.length;
        }

        /// <summary>Lean in to his face (head within <see cref="askDistance"/> for 0.5 s): first the AVPU exchange
        /// («Смотри на меня!» → «Слышу...»/«Да...»/groan), next time «где болит?» → leg/chest answer.</summary>
        void UpdateLeanIn(CasualtyController c, float dt)
        {
            _askCooldown -= dt;
            if (_pendingAnswer != null && Time.time >= _answerAt) { var a = _pendingAnswer; _pendingAnswer = null; a(); }
            if (!answerWhenLeanedIn || muted || _pendingAnswer != null || c.rig == null) return;
            var cam = Camera.main;
            var head = c.rig.Bone(HumanBodyBones.Head);
            if (cam == null || head == null) return;
            bool near = Vector3.Distance(cam.transform.position, head.position) < askDistance;
            if (!near) _awaitLeave = false;
            _leanTimer = near && !_awaitLeave ? _leanTimer + dt : 0f;
            if (_leanTimer < 0.5f || _askCooldown > 0f) return;
            _leanTimer = 0f;
            _askCooldown = 6f;
            _awaitLeave = true; // one question per lean-in (working at his head must not make him chatter)
            if (_askStage++ % 2 == 0)
            {
                float t = PlayerSays("look_at_me");
                _pendingAnswer = () => AnswerAvpu(c);
                _answerAt = Time.time + t + 0.35f;
            }
            else
            {
                _pendingAnswer = () => AnswerWhereHurts(c);
                _answerAt = Time.time + 0.2f;
            }
            _cooldown = Mathf.Max(_cooldown, 4f);
        }

        /// <summary>Immediate reaction to being hit (fall).</summary>
        public void OnHit(CasualtyController c)
        {
            bool leg = c.Patient.wounds.Any(w => BodyRegions.LimbId(w.region)?.StartsWith("leg") == true);
            if (!Say(leg ? (_rng.NextDouble() < 0.5 ? "hit_mixed" : "hit_leg") : "wounded", true))
                Vocalise(PainPool, 1f, true);
            _cooldown = 2.5f;
            _sinceScream = 0f;
        }

        /// <summary>Acute pain spike (handling, tourniquet twist).</summary>
        public void OnPainSpike(CasualtyController c, bool tourniquet)
        {
            if (_sinceScream < 1.2f) return;
            _sinceScream = 0f;
            if (tourniquet && _rng.NextDouble() < 0.6) Say("take_off", true);
            else if (_rng.NextDouble() < 0.5) Say("hurts", true);
            else Vocalise(PainPool, 1f, true);
            _cooldown = Mathf.Max(_cooldown, 2f);
        }

        public void Tick(CasualtyController c, float dt)
        {
            _sinceScream += dt;
            var p = c.Patient;
            UpdateBreath(c);
            UpdateVocalCut(dt);
            if (Subtitle != null && Time.time > SubtitleUntil) Subtitle = null;
            if (!c.IsDown || p.outcome == Outcome.Died) return;
            UpdateLeanIn(c, dt);
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
                    if (pain >= PainModel.ScreamThreshold) { if (Chance(0.4)) Vocalise(PainPool); else Say(Pick("hurts", "help", "blood_much")); }
                    else if (pain >= PainModel.GroanThreshold) { if (Chance(0.55)) Vocalise(bank?.groan, 0.8f); else Say(Pick("blood_much", "wounded", "will_i_die", "dont_leave", "brother")); }
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
                if (want != null) { breath.clip = want; breath.Play(); ClipLog.Record(want, false); }
            }
            breath.volume = muted ? 0f : vol;
            breath.pitch = pitch;
        }

        bool Chance(double p) => _rng.NextDouble() < p;
        string Pick(params string[] ids) => ids[_rng.Next(ids.Length)];
        static AudioClip First(AudioClip[] a) => a != null && a.Length > 0 ? a[0] : null;
    }
}
