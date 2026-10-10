using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    [Serializable]
    public sealed class DebriefMistake
    {
        public string code;
        public string text;          // what was wrong
        public string consequence;   // what would have happened in reality
        public int points;
        public float time = -1f;
    }

    [Serializable]
    public sealed class DebriefEntry
    {
        public float time;
        public string clock;
        public string text;
        public Severity severity;
    }

    /// <summary>The after-action review shown on the dugout board (GDD §1 pillar 4, §3).</summary>
    [Serializable]
    public sealed class DebriefReport
    {
        public string scenarioId;
        public string title;
        public bool survived;
        public string outcomeText;     // "Ерлан доставлен на этап эвакуации" / cause of death / "Вы ранены"
        public string limbOutcome;
        public int score;
        public int bestScore;
        public bool newBest;
        public float bleedControlSeconds = -1f;  // incident → all life threats controlled
        public float durationSeconds;
        public float lossPercent;
        public string endReason;
        public readonly List<DebriefEntry> timeline = new();
        public readonly List<DebriefMistake> mistakes = new();

        /// Grade label for the big number on the board.
        public string Grade(bool kz) => !survived ? (kz ? "ҚАЗА" : "ГИБЕЛЬ")
            : score >= 90 ? (kz ? "ӨТЕ ЖАҚСЫ" : "ОТЛИЧНО")
            : score >= 75 ? (kz ? "ЖАҚСЫ" : "ХОРОШО")
            : score >= 50 ? (kz ? "ҚАНАҒАТТАНАРЛЫҚ" : "УДОВЛЕТВОРИТЕЛЬНО")
            : (kz ? "НАШАР" : "ПЛОХО");
    }

    public static class DebriefBuilder
    {
        /// <param name="clock">formats scenario seconds to the wall clock ("01:43")</param>
        /// <param name="incidentAt">patient seconds of the injury (bleed-control timing)</param>
        public static DebriefReport Build(ScenarioDefinition def, CareProtocol protocol, string endReason, Func<float, string> clock,
            float incidentAt = 0f, string playerHitText = null, bool kz = false)
        {
            var p = protocol.patient;
            int score = protocol.Score();
            bool playerHit = endReason == "player_hit";
            if (playerHit) score = Math.Min(score, 20);
            var r = new DebriefReport
            {
                scenarioId = def?.id,
                title = def?.Title(kz),
                endReason = endReason,
                survived = p.outcome != Outcome.Died && !playerHit,
                score = score,
                durationSeconds = p.secondsSinceStart,
                lossPercent = p.LossFraction * 100f,
            };
            r.outcomeText = playerHit ? (playerHitText ?? (kz ? "Сіз жараландыңыз — Ерлан көмексіз қалды." : "Вы ранены — Ерлан остался без помощи."))
                : p.outcome == Outcome.Died ? (kz ? "Ерлан қаза тапты: " : "Ерлан погиб: ") + p.causeOfDeath
                : endReason == "evacuated" ? (kz ? "Ерлан эвакуацияланды. Тірі." : "Ерлан передан на эвакуацию живым.")
                : (kz ? "Сценарий аяқталды." : "Сценарий завершён.");

            // limbs: tourniquet time or traumatic amputation
            var limbs = new List<string>();
            foreach (var w in p.wounds.Where(w => w.type == WoundType.TraumaticAmputation))
                limbs.Add(LimbName(w.LimbId, kz) + ": " + TourniquetConversion.LimbOutcome(0f, true, false, kz));
            foreach (var kv in p.tourniquetSeconds)
                if (!p.wounds.Any(w => w.type == WoundType.TraumaticAmputation && w.LimbId == kv.Key))
                    limbs.Add(LimbName(kv.Key, kz) + ": " + TourniquetConversion.LimbOutcome(kv.Value + (endReason == "evacuated" ? 3600f : 0f), false, false, kz));
            if (p.tourniquetConverted) limbs.Add(kz ? "Жгут ауыстырылды — аяқ сақталады" : "Жгут конвертирован — конечность сохранена");
            r.limbOutcome = limbs.Count == 0 ? (kz ? "Аяқ-қолдар сақталды" : "Конечности сохранены") : string.Join("; ", limbs);

            // timeline: protocol log (skip noise)
            foreach (var e in protocol.log)
            {
                if (string.IsNullOrEmpty(e.text) || e.code is "noop" or "ended" or "pulse" or "limb_lift") continue;
                r.timeline.Add(new DebriefEntry { time = e.time, clock = clock(e.time), text = e.text, severity = e.severity });
            }
            // first moment all life threats were controlled
            r.bleedControlSeconds = FirstControlTime(protocol, incidentAt);

            // mistakes: one row per penalty code with explanation and consequence
            foreach (var kv in protocol.penalties)
            {
                if (!CareProtocol.Penalty.TryGetValue(kv.Key, out var pen)) continue;
                var first = protocol.log.FirstOrDefault(e => e.code == kv.Key);
                r.mistakes.Add(new DebriefMistake
                {
                    code = kv.Key, text = pen.text, points = pen.points, time = first != null ? first.time : -1f,
                    consequence = CareProtocol.Consequence.TryGetValue(kv.Key, out var c) ? c : "",
                });
            }
            r.mistakes.Sort((a, b) => b.points.CompareTo(a.points));
            return r;
        }

        static float FirstControlTime(CareProtocol protocol, float incidentAt)
        {
            foreach (var e in protocol.log)
                if (e.code is "tq_ok" or "hold_done" or "dressing_ok" or "sealed" or "tq_retightened") return Math.Max(0f, e.time - incidentAt);
            return -1f;
        }

        public static string LimbName(string limb, bool kz) => limb switch
        {
            "legR" => kz ? "Оң аяқ" : "Правая нога",
            "legL" => kz ? "Сол аяқ" : "Левая нога",
            "armR" => kz ? "Оң қол" : "Правая рука",
            "armL" => kz ? "Сол қол" : "Левая рука",
            _ => limb,
        };

        /// Best score bookkeeping (storage is the caller's: PlayerPrefs in Unity).
        public static bool UpdateBest(DebriefReport r, int previousBest)
        {
            r.bestScore = Math.Max(previousBest, r.survived ? r.score : 0);
            r.newBest = r.survived && r.score > previousBest;
            return r.newBest;
        }
    }
}
