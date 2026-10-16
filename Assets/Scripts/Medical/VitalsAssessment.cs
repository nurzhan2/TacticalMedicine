using System;

namespace FieldMedic.Medical
{
    public enum PulseQuality { Absent, Thready, Weak, Good }
    public enum Stimulus { Voice, Pain }
    public enum AvpuReaction { None, Groans, Mumbles, Talks }
    public enum AssessmentKind { Pulse, Breathing, Avpu, CapillaryRefill }

    /// What the fingertips feel at a pulse point.
    public readonly struct PulseReading
    {
        public readonly PulseSite site;
        public readonly int rate;          // beats/min, rounded like a trainee counts (to 2)
        public readonly float strength;    // 0..1 (haptic amplitude)
        public readonly PulseQuality quality;
        public PulseReading(PulseSite s, int r, float st, PulseQuality q) { site = s; rate = r; strength = st; quality = q; }
        public bool Present => quality != PulseQuality.Absent;
    }

    /// What the ear and the eyes get at his mouth/chest.
    public readonly struct BreathReading
    {
        public readonly int rate;          // breaths/min
        public readonly float depth;       // 0..1 chest excursion
        public readonly bool snoring;      // obstructed airway
        public readonly bool agonal;
        public BreathReading(int r, float d, bool s, bool a) { rate = r; depth = d; snoring = s; agonal = a; }
        public bool Fast => rate >= 24;
        public bool Shallow => depth < 0.32f;
    }

    /// How he answers a stimulus (AVPU): what the player hears and sees.
    public readonly struct AvpuResponse
    {
        public readonly AvpuReaction reaction;
        public readonly bool eyesOpen;     // opens/keeps his eyes open toward the medic
        public readonly bool withdraws;    // pulls away / flinches (pain)
        public AvpuResponse(AvpuReaction r, bool e, bool w) { reaction = r; eyesOpen = e; withdraws = w; }
    }

    /// One assessment the trainee did with the hands (pulse, breathing, AVPU, capillary refill) for the debrief.
    [Serializable]
    public sealed class AssessmentRecord
    {
        public AssessmentKind kind;
        public float time;         // patient seconds
        public string site;        // pulse site / stimulus
        public string ru, kz;      // result text ("128, слабый")
        public string Text(bool k) => k && !string.IsNullOrEmpty(kz) ? kz : ru;
    }

    /// <summary>
    /// The hands-on primary survey (w8): what a pulse check, a breathing check, an AVPU check and a capillary refill
    /// test tell the medic for the current physiology, with the RU/KZ result texts used by the training label and
    /// the debrief. Teaching rules of thumb (RESEARCH §2/§6): radial pulse gone below ~80 systolic, carotid below ~50;
    /// tension pneumothorax = fast and shallow; refill &gt; 2 s = poor perfusion.
    /// </summary>
    public static class VitalsAssessment
    {
        // ------------------------------------------------------------------ pulse
        public static PulseReading ReadPulse(PatientPhysiology p, PulseSite site)
        {
            float s = p.PulseStrength(site);
            int hr = (int)Math.Round(p.HeartRate / 2f) * 2;
            var q = s <= 0f ? PulseQuality.Absent : s < 0.35f ? PulseQuality.Thready : s < 0.62f ? PulseQuality.Weak : PulseQuality.Good;
            return new PulseReading(site, q == PulseQuality.Absent ? 0 : hr, s, q);
        }

        public static string SiteName(PulseSite s, bool kz) => s switch
        {
            PulseSite.Carotid => kz ? "мойында" : "на шее",
            PulseSite.RadialL or PulseSite.RadialR => kz ? "білекте" : "на запястье",
            PulseSite.PedalL or PulseSite.PedalR => kz ? "табанда" : "на стопе",
            _ => kz ? "шапта" : "в паху",
        };

        public static string QualityText(PulseQuality q, bool kz) => q switch
        {
            PulseQuality.Absent => kz ? "жоқ" : "нет",
            PulseQuality.Thready => kz ? "жіп тәрізді" : "нитевидный",
            PulseQuality.Weak => kz ? "әлсіз" : "слабый",
            _ => kz ? "жақсы толған" : "хорошего наполнения",
        };

        /// "128, слабый, частый" / "нет" — the value part (no site).
        public static string PulseValue(PulseReading r, bool kz)
        {
            if (!r.Present) return kz ? "пульс жоқ" : "пульса нет";
            string rate = r.rate > 100 ? (kz ? ", жиі" : ", частый") : r.rate < 55 ? (kz ? ", сирек" : ", редкий") : "";
            return r.rate + ", " + QualityText(r.quality, kz) + rate;
        }

        /// Short label next to the fingers in Training: "Пульс на шее: 128 — слабый, частый".
        public static string PulseLabel(PulseReading r, bool kz) =>
            (kz ? "Пульс " : "Пульс ") + SiteName(r.site, kz) + ": " + PulseValue(r, kz);

        // ------------------------------------------------------------------ breathing
        public static BreathReading ReadBreathing(PatientPhysiology p)
        {
            int rr = (int)Math.Round(p.RespRate);
            bool dead = p.outcome == Outcome.Died;
            return new BreathReading(dead ? 0 : rr, dead ? 0f : p.BreathDepth, !dead && p.AirwayCompromised, !dead && p.LossFraction > 0.42f);
        }

        public static string BreathValue(BreathReading b, bool kz)
        {
            if (b.rate <= 0) return kz ? "тыныс жоқ" : "дыхания нет";
            string what = b.snoring ? (kz ? "қорылдап, сирек" : "храп, редкое")
                : b.agonal ? (kz ? "агоналды, үзік" : "агональное, прерывистое")
                : b.Fast && b.Shallow ? (kz ? "жиі, үстірт" : "частое, поверхностное")
                : b.Fast ? (kz ? "жиі" : "частое")
                : b.rate < 10 ? (kz ? "сирек" : "редкое")
                : (kz ? "біркелкі" : "ровное");
            return b.rate + (kz ? "/мин, " : "/мин, ") + what;
        }

        public static string BreathLabel(BreathReading b, bool kz) => (kz ? "Тыныс: " : "Дыхание: ") + BreathValue(b, kz);

        // ------------------------------------------------------------------ capillary refill
        /// Seconds for the colour to return to a pressed nail bed: ~1.5 s healthy, &gt; 2 s poor perfusion,
        /// longer in the cold. Infinite when dead.
        public static float CapillaryRefillSeconds(PatientPhysiology p)
        {
            if (p.outcome == Outcome.Died) return float.PositiveInfinity;
            float perf = p.Perfusion;                                    // 0.15..1.1
            float s = 1.4f + 4.6f * Math.Clamp((1.02f - perf) / 0.7f, 0f, 1f);
            if (p.coreTempC < 35.5f) s += (35.5f - p.coreTempC) * 0.8f;  // cold peripheries
            return Math.Min(s, 8f);
        }

        public static string RefillValue(float seconds, bool kz)
        {
            if (float.IsInfinity(seconds)) return kz ? "толмайды" : "не восстанавливается";
            string v = seconds.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + (kz ? " с — " : " с — ");
            return v + (seconds <= 2f ? (kz ? "қалыпты" : "норма") : seconds <= 3f ? (kz ? "баяулаған" : "замедлено") : (kz ? "шок" : "шок"));
        }

        public static string RefillLabel(float seconds, bool kz) => (kz ? "Капиллярлық толу: " : "Капиллярное наполнение: ") + RefillValue(seconds, kz);

        // ------------------------------------------------------------------ AVPU
        public static AvpuResponse Respond(Consciousness c, Stimulus s)
        {
            switch (c)
            {
                case Consciousness.Alert: return new AvpuResponse(AvpuReaction.Talks, true, s == Stimulus.Pain);
                case Consciousness.Voice:
                    return s == Stimulus.Voice ? new AvpuResponse(AvpuReaction.Mumbles, true, false)
                                               : new AvpuResponse(AvpuReaction.Groans, true, true);
                case Consciousness.Pain:
                    return s == Stimulus.Voice ? new AvpuResponse(AvpuReaction.None, false, false)
                                               : new AvpuResponse(AvpuReaction.Groans, false, true);
                default: return new AvpuResponse(AvpuReaction.None, false, false);
            }
        }

        /// What the medic can conclude from one stimulus (a voice check on a P/U casualty only says "no answer to voice").
        public static string AvpuValue(Consciousness c, Stimulus s, bool kz)
        {
            if (s == Stimulus.Voice && c >= Consciousness.Pain)
                return kz ? "дауысқа жауап жоқ — ауырсынумен тексер" : "на голос не отвечает — проверь болью";
            return c switch
            {
                Consciousness.Alert => kz ? "есі бар, сөйлейді (A)" : "в сознании, отвечает (A)",
                Consciousness.Voice => kz ? "дауысқа жауап береді (V)" : "реагирует на голос (V)",
                Consciousness.Pain => kz ? "тек ауырсынуға жауап (P)" : "реагирует только на боль (P)",
                _ => kz ? "жауап бермейді (U)" : "не реагирует (U)",
            };
        }

        public static string AvpuLabel(Consciousness c, Stimulus s, bool kz) => "AVPU: " + AvpuValue(c, s, kz);

        // ------------------------------------------------------------------ debrief
        public static string KindName(AssessmentKind k, bool kz) => k switch
        {
            AssessmentKind.Pulse => kz ? "Пульс тексерілді" : "Пульс проверен",
            AssessmentKind.Breathing => kz ? "Тыныс тексерілді" : "Дыхание проверено",
            AssessmentKind.Avpu => kz ? "Сана (AVPU) тексерілді" : "Сознание (AVPU) проверено",
            _ => kz ? "Капиллярлық толу тексерілді" : "Капиллярное наполнение проверено",
        };

        /// "Пульс проверен в 0:42 — 128, слабый" (time from the injury, m:ss).
        public static string DebriefLine(AssessmentRecord r, float incidentAt, bool kz)
        {
            float t = Math.Max(0f, r.time - incidentAt);
            int m = (int)(t / 60f), s = (int)(t % 60f);
            string site = r.kind == AssessmentKind.Pulse && !string.IsNullOrEmpty(r.site) ? " (" + r.site + ")" : "";
            string clock = m + ":" + s.ToString("00");
            return KindName(r.kind, kz) + site + (kz ? " (" + clock + ")" : " в " + clock) + " — " + r.Text(kz);
        }
    }
}
