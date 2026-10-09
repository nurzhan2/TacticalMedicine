using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    public enum Phase { CareUnderFire, TacticalFieldCare, Evacuation }

    public enum MedAction
    {
        Gloves, CutClothing, BloodSweep,
        Tourniquet, HemostaticPacking, DirectPressure, PressureDressing,
        ChestSeal, NeedleDecompression, NasalAirway, RecoveryPosition,
        MarkTourniquetTime, ThermalBlanket, CasualtyCard, MoveToCover,
    }

    public enum Severity { Info, Ok, Warning, Error }

    public readonly struct Feedback
    {
        public readonly Severity severity;
        public readonly string code;    // stable id for analytics / scoring
        public readonly string text;    // shown to trainee (RU)
        public Feedback(Severity s, string c, string t) { severity = s; code = c; text = t; }
    }

    [Serializable]
    public class LoggedAction
    {
        public float time;
        public MedAction action;
        public string target;
        public Severity severity;
        public string code;
        public string text;
    }

    /// Checks trainee actions against TCCC/MARCH teaching and applies valid ones to the physiology.
    /// All clinical rules live here so a medical instructor can review them in one place.
    public class CareProtocol
    {
        public readonly PatientPhysiology patient;
        public Phase phase;
        public bool glovesOn;
        public bool tourniquetTimeMarked;
        public readonly List<LoggedAction> log = new();
        public readonly Dictionary<string, int> penalties = new();

        public static readonly Dictionary<string, (int points, string text)> Penalty = new()
        {
            ["no_gloves"]       = (5,  "Работа без перчаток: риск для медика и инфицирования раны."),
            ["fire_non_tq"]     = (10, "Под огнём допустим только жгут «высоко и туго» — остальное после укрытия."),
            ["tq_on_joint"]     = (10, "Жгут на суставе не пережимает артерию."),
            ["tq_below_wound"]  = (15, "Жгут ниже раны не останавливает кровотечение."),
            ["tq_junctional"]   = (10, "Узловая зона (пах, подмышка, шея): жгут невозможен — тампонада."),
            ["tq_unneeded"]     = (25, "Жгут на некритичное кровотечение: при долгой эвакуации — частая причина ненужных ампутаций."),
            ["tq_not_marked"]   = (10, "Не записано время наложения жгута."),
            ["march_order"]     = (15, "Нарушен порядок MARCH: сначала массивное кровотечение, затем дыхательные пути и дыхание."),
            ["wrong_tool"]      = (5,  "Неподходящее средство для этого типа раны."),
            ["needle_no_signs"] = (10, "Декомпрессия без признаков напряжённого пневмоторакса."),
            ["blanket_early"]   = (5,  "Термоодеяло — шаг H, после контроля кровотечения и дыхания."),
            ["missed_wound"]    = (15, "Пропущено ранение: нужен полный осмотр (blood sweep)."),
            ["big_blood_loss"]  = (10, "Потеряно более 30% объёма крови — действуйте быстрее."),
        };

        public CareProtocol(PatientPhysiology p, Phase start) { patient = p; phase = start; }

        bool CriticalBleedOpen => patient.wounds.Any(w => w.IsLifeThreatening && !w.IsControlled);

        public Feedback Perform(MedAction action, Wound target = null, BodyRegion? site = null)
        {
            if (target != null) target.discovered = true;
            var fb = Evaluate(action, target, site);
            log.Add(new LoggedAction
            {
                time = patient.secondsSinceStart, action = action,
                target = target != null ? target.region.ToString() : site?.ToString(),
                severity = fb.severity, code = fb.code, text = fb.text,
            });
            if (Penalty.ContainsKey(fb.code)) AddPenalty(fb.code);
            return fb;
        }

        Feedback Evaluate(MedAction a, Wound w, BodyRegion? site)
        {
            if (patient.outcome != Outcome.Ongoing) return new(Severity.Info, "ended", "Сценарий завершён.");

            if (phase == Phase.CareUnderFire && a is not (MedAction.Tourniquet or MedAction.MoveToCover))
                return new(Severity.Error, "fire_non_tq", Penalty["fire_non_tq"].text);

            switch (a)
            {
                case MedAction.MoveToCover:
                    phase = Phase.TacticalFieldCare;
                    return new(Severity.Ok, "cover", "Раненый в укрытии. Полный осмотр по MARCH.");

                case MedAction.Gloves:
                    glovesOn = true;
                    return new(Severity.Ok, "gloves", "Перчатки надеты.");

                case MedAction.BloodSweep:
                    foreach (var x in patient.wounds) x.discovered = true;
                    return new(Severity.Ok, "sweep", "Осмотр «прочёсом»: найдены все ранения.");

                case MedAction.Tourniquet:
                    return Tourniquet(site);

                case MedAction.HemostaticPacking:
                    if (w == null) return NoWound();
                    if (w.IsChest) return new(Severity.Error, "wrong_tool", "Рану груди не тампонируют — окклюзионная наклейка.");
                    if (!w.IsLifeThreatening && CriticalBleedOpen) AddPenalty("march_order");
                    GloveCheck();
                    w.packed = true; w.control = BleedControl.Packed; w.packingHoldSeconds = 0f;
                    return new(Severity.Ok, "packed", "Рана плотно затампонирована. Держите прямое давление 3 минуты.");

                case MedAction.PressureDressing:
                    if (w == null) return NoWound();
                    if (w.IsChest) return new(Severity.Error, "wrong_tool", "Повязка не герметична — нужна окклюзионная наклейка.");
                    GloveCheck();
                    w.control = BleedControl.PressureDressing;
                    if (w.IsControlled) return new(Severity.Ok, "dressing_ok", "Давящая повязка — кровотечение остановлено.");
                    return new(Severity.Error, "wrong_tool", w.IsJunctionalRegion
                        ? "Повязка без тампонады на узловой ране почти не помогает: сначала гемостатик и давление."
                        : "Повязка не останавливает артериальное кровотечение конечности — нужен жгут.");

                case MedAction.ChestSeal:
                    if (w == null || !w.IsChest) return new(Severity.Warning, "wrong_site", "Окклюзионная наклейка — для ран груди.");
                    if (CriticalBleedOpen) AddPenalty("march_order");
                    patient.SealChest(w);
                    return new(Severity.Ok, "sealed", "Рана груди загерметизирована. Следите за дыханием.");

                case MedAction.NeedleDecompression:
                    if (patient.pneumo != PneumoState.Tension)
                        return new(Severity.Error, "needle_no_signs", Penalty["needle_no_signs"].text);
                    patient.NeedleDecompress();
                    return new(Severity.Ok, "decompressed", "Свист воздуха — давление в груди сброшено.");

                case MedAction.NasalAirway:
                case MedAction.RecoveryPosition:
                    patient.InsertAirwayAdjunct();
                    return new(Severity.Ok, "airway", "Дыхательные пути обеспечены.");

                case MedAction.MarkTourniquetTime:
                    if (patient.tourniquetSeconds.Count == 0) return new(Severity.Warning, "no_tq", "Жгут не наложен.");
                    tourniquetTimeMarked = true;
                    return new(Severity.Ok, "tq_marked", "Время наложения жгута записано.");

                case MedAction.ThermalBlanket:
                    if (!patient.AllBleedingControlled || patient.pneumo == PneumoState.Tension)
                        return new(Severity.Error, "blanket_early", Penalty["blanket_early"].text);
                    patient.ApplyBlanket();
                    return new(Severity.Ok, "blanket", "Раненый укрыт: профилактика гипотермии.");

                default:
                    return new(Severity.Info, "noop", "");
            }
        }

        Feedback Tourniquet(BodyRegion? site)
        {
            if (site == null) return new(Severity.Warning, "no_site", "Укажите место наложения.");
            var s = site.Value;
            if (BodyRegions.IsJunctional(s)) return new(Severity.Error, "tq_junctional", Penalty["tq_junctional"].text);
            if (!BodyRegions.IsLimb(s)) return new(Severity.Warning, "wrong_site", "Жгут накладывают только на конечности.");
            if (s is BodyRegion.HandL or BodyRegion.HandR or BodyRegion.FootL or BodyRegion.FootR)
                return new(Severity.Error, "tq_on_joint", Penalty["tq_on_joint"].text);

            var limb = BodyRegions.LimbId(s);
            var bleeding = patient.wounds.Where(x => !x.IsControlled && BodyRegions.LimbId(x.region) == limb).ToList();
            if (bleeding.Count == 0)
            {
                bool left = BodyRegions.IsLeftSide(s);
                var junction = patient.wounds.FirstOrDefault(x => !x.IsControlled && x.IsJunctionalRegion
                                                                  && BodyRegions.IsLeftSide(x.region) == left);
                if (junction != null)
                    return new(Severity.Error, "tq_junctional",
                        "Рана в узловой зоне выше любого места для жгута — он её не остановит. Тампонада и давление.");
                return new(Severity.Warning, "no_bleed", "На этой конечности нет активного кровотечения.");
            }

            var target = bleeding.OrderBy(x => BodyRegions.LimbSegment(x.region)).First();
            if (BodyRegions.LimbSegment(s) > BodyRegions.LimbSegment(target.region))
                return new(Severity.Error, "tq_below_wound", Penalty["tq_below_wound"].text);

            foreach (var x in bleeding) { x.discovered = true; patient.ApplyTourniquet(x); }
            if (!target.IsLifeThreatening) return new(Severity.Warning, "tq_unneeded", Penalty["tq_unneeded"].text);
            if (phase != Phase.CareUnderFire) GloveCheck();
            return new(Severity.Ok, "tq_ok", phase == Phase.CareUnderFire
                ? "Жгут наложен — теперь срочно в укрытие!"
                : "Жгут наложен, кровотечение остановлено. Запишите время.");
        }

        /// Called every frame while the trainee's hands press on a wound.
        /// holdTimeScale lets training mode shorten the real 3-minute hold.
        public bool HoldPressure(Wound w, float dt, float holdTimeScale = 1f)
        {
            if (w == null) return false;
            w.handsPressing = true;
            w.discovered = true;
            if (!w.packed || w.control != BleedControl.Packed) return false;
            w.packingHoldSeconds += dt * holdTimeScale;
            if (w.packingHoldSeconds < Wound.PackingHoldRequired) return false;
            w.control = BleedControl.PackedAndHeld;
            log.Add(new LoggedAction { time = patient.secondsSinceStart, action = MedAction.DirectPressure,
                target = w.region.ToString(), severity = Severity.Ok, code = "hold_done",
                text = "Давление удержано 3 минуты. Закрепите давящей повязкой." });
            return true;
        }

        void AddPenalty(string code) => penalties[code] = penalties.TryGetValue(code, out var n) ? n + 1 : 1;
        void GloveCheck() { if (!glovesOn) penalties.TryAdd("no_gloves", 1); }
        static Feedback NoWound() => new(Severity.Warning, "no_wound", "Здесь нет раны. Проведите осмотр.");

        /// Final score 0..100 (0 if the casualty died). Each mistake type is counted once.
        public int Score()
        {
            if (patient.outcome == Outcome.Died) return 0;
            if (patient.tourniquetSeconds.Count > 0 && !tourniquetTimeMarked) penalties.TryAdd("tq_not_marked", 1);
            if (patient.LossFraction > 0.30f) penalties.TryAdd("big_blood_loss", 1);
            if (patient.wounds.Any(w => !w.discovered)) penalties.TryAdd("missed_wound", 1);
            int total = 100;
            foreach (var kv in penalties) total -= Penalty[kv.Key].points;
            return Math.Max(0, total);
        }
    }
}
