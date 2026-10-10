using System.Collections.Generic;

namespace FieldMedic.Environment
{
    public enum LightMode { Off = 0, Red = 1, White = 2 }

    public enum ThreatCue { None, BulletWhiz, ParapetImpact, Hit }

    /// <summary>One continuous exposure episode for the debrief.</summary>
    public struct ExposureRecord
    {
        public float start;
        public float duration;
        public string reason;   // key: "head_above_parapet", "white_light_drone", "standing_drone"
        public float peakRisk;
    }

    /// <summary>
    /// Pure risk model (no Unity objects): the player's situation each tick → accumulated risk 0..1 with warning
    /// cues at 0.35 (whiz) and 0.65 (impact on the parapet), and a hit at 1. Risk decays when the player is safe.
    /// Rates are per second and tuned so that ~3 s of head above the parapet under fire is lethal, while a short
    /// peek gets a warning first (training value: feel the cue, duck).
    /// </summary>
    public sealed class ThreatModel
    {
        public float exposedUnderFireRate = 0.34f;
        public float exposedQuietRate = 0.03f;      // sniper risk even in a quiet phase
        public float whiteLightDroneRate = 0.22f;
        public float redLightDroneRate = 0.015f;
        public float standingDroneRate = 0.05f;     // standing tall in the open trench under a recon drone
        public float whiteLightRate = 0.04f;        // white light at night with no drone: spotted later
        public float decayRate = 0.12f;

        public const float WhizThreshold = 0.35f;
        public const float ImpactThreshold = 0.65f;

        public float Risk { get; private set; }
        public bool IsHit { get; private set; }
        public string HitReason { get; private set; }
        public float Time { get; private set; }
        public readonly List<ExposureRecord> Exposures = new List<ExposureRecord>();

        bool _whizDone, _impactDone;
        int _open = -1;

        public struct Situation
        {
            public bool headAboveParapet;
            public bool underFire;
            public bool droneOverhead;
            public LightMode light;
            public bool standing;
            public bool inCover;
        }

        public void Reset()
        {
            Risk = 0f; IsHit = false; HitReason = null; Time = 0f;
            Exposures.Clear(); _whizDone = _impactDone = false; _open = -1;
        }

        /// <summary>Advances the model; returns the cue to play this tick (at most one).</summary>
        public ThreatCue Tick(Situation s, float dt)
        {
            Time += dt;
            if (IsHit) return ThreatCue.None;

            float rate = 0f;
            string reason = null;
            if (s.headAboveParapet)
            {
                rate += s.underFire ? exposedUnderFireRate : exposedQuietRate;
                reason = "head_above_parapet";
            }
            if (s.droneOverhead && !s.inCover)
            {
                if (s.light == LightMode.White) { rate += whiteLightDroneRate; reason ??= "white_light_drone"; }
                else if (s.light == LightMode.Red) rate += redLightDroneRate;
                if (s.standing) { rate += standingDroneRate; reason ??= "standing_drone"; }
            }
            else if (s.light == LightMode.White && !s.inCover)
            {
                rate += whiteLightRate;
                reason ??= "white_light";
            }

            if (rate > 0f) Risk += rate * dt;
            else Risk -= decayRate * dt;
            if (Risk < 0f) Risk = 0f;

            TrackExposure(reason, rate > 0.02f, dt);

            if (Risk >= 1f)
            {
                Risk = 1f;
                IsHit = true;
                HitReason = reason ?? "exposure";
                CloseExposure();
                return ThreatCue.Hit;
            }
            if (Risk < WhizThreshold * 0.5f) { _whizDone = false; _impactDone = false; }
            if (!_impactDone && Risk >= ImpactThreshold) { _impactDone = _whizDone = true; return ThreatCue.ParapetImpact; }
            if (!_whizDone && Risk >= WhizThreshold) { _whizDone = true; return ThreatCue.BulletWhiz; }
            return ThreatCue.None;
        }

        void TrackExposure(string reason, bool exposed, float dt)
        {
            if (exposed && reason != null)
            {
                if (_open < 0 || Exposures[_open].reason != reason)
                {
                    CloseExposure();
                    Exposures.Add(new ExposureRecord { start = Time - dt, reason = reason });
                    _open = Exposures.Count - 1;
                }
                var e = Exposures[_open];
                e.duration += dt;
                if (Risk > e.peakRisk) e.peakRisk = Risk;
                Exposures[_open] = e;
            }
            else CloseExposure();
        }

        void CloseExposure() => _open = -1;

        /// <summary>Explanation for the run-end screen / debrief (RU or KZ).</summary>
        public static string Explain(string reason, bool kazakh)
        {
            switch (reason)
            {
                case "head_above_parapet":
                    return kazakh
                        ? "Сіз бруствер үстінен басыңызды шығардыңыз — снайпер/пулемет. Оқ астында окоп түбінде жұмыс істеңіз."
                        : "Голова над бруствером под огнём — снайпер/пулемёт. Под огнём работайте ниже бруствера, на дне окопа.";
                case "white_light_drone":
                    return kazakh
                        ? "Дрон астында ақ шам — сізді байқады. Түнде тек қызыл шам, минимум жарықта."
                        : "Белый свет под дроном — вас засекли. Ночью только красный налобник на минимуме, под пончо.";
                case "standing_drone":
                    return kazakh
                        ? "Дрон үстінде тік тұрдыңыз — ВОГ тасталды. «Әуе!» — окоп түбіне."
                        : "Стояли в полный рост под разведдроном — сброс ВОГ. «Воздух!» — все на дно окопа.";
                case "white_light":
                    return kazakh ? "Ақ жарық алыстан көрінеді — позиция ашылды." : "Белый свет виден издалека — позиция демаскирована.";
                default:
                    return kazakh ? "Сіз жараландыңыз." : "Вы ранены.";
            }
        }
    }
}
