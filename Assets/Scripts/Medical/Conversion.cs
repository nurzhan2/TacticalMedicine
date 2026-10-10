using System;

namespace FieldMedic.Medical
{
    /// <summary>
    /// Tourniquet conversion (prolonged field care, RESEARCH §7 S8 / TCCC): a band applied in a hurry under fire
    /// is re-assessed and, if the rules allow, replaced by a pressure dressing within 2 hours — that is what saves
    /// limbs on long evacuations. Rules: not in shock, no amputation, band on for less than 6 hours, the wound
    /// already dressed/packed under the loosened band, casualty can be watched. If bleeding resumes: re-tighten,
    /// write the time and never try again.
    /// </summary>
    public static class TourniquetConversion
    {
        public const float ReassessBySeconds = 2f * 3600f;
        public const float ForbiddenAfterSeconds = 6f * 3600f;

        /// Returns "ok" or a penalty code explaining why conversion must not be attempted.
        public static string Check(PatientPhysiology p, TourniquetState tq, float bandSeconds)
        {
            if (tq == null || !tq.OnLimb) return "no_tq";
            if (p.conversionFailed) return "conv_after_fail";
            foreach (var w in p.wounds)
                if (w.type == WoundType.TraumaticAmputation && w.LimbId == tq.limbId) return "conv_amputation";
            if (bandSeconds >= ForbiddenAfterSeconds) return "conv_too_late";
            if (InShock(p)) return "conv_shock";
            foreach (var w in p.wounds)
                if (tq.IsProximalTo(w.LimbId, w.LimbPos) && !DressedForConversion(w)) return "conv_no_dressing";
            return "ok";
        }

        /// Clinical shock by field signs: fast weak pulse, no radial pulse, altered mental state.
        public static bool InShock(PatientPhysiology p) =>
            p.HeartRate > 110f || !p.RadialPulsePresent || p.AVPU != Consciousness.Alert;

        /// A wound is ready for conversion when packing/dressing would hold it on its own.
        public static bool DressedForConversion(Wound w) =>
            w.control is BleedControl.PressureDressing or BleedControl.PackedAndHeld or BleedControl.Packed;

        /// Does the wound bleed again once the band is loose? Arterial bleeds under a plain dressing do.
        public static bool WillRebleed(Wound w)
        {
            if (w.type is WoundType.ArterialLimb or WoundType.TraumaticAmputation)
                return !(w.packed && w.packingHoldSeconds >= Wound.PackingHoldRequired);
            return false;
        }

        /// Limb outcome by band time (no occlusion of a living limb beyond these limits in the field).
        public static string LimbOutcome(float bandSeconds, bool amputated, bool converted, bool kazakh = false)
        {
            if (amputated) return kazakh ? "Аяқ-қол жарақаттан жоғалған (ампутация)" : "Конечность утрачена при ранении (травматическая ампутация)";
            if (converted || bandSeconds <= 0f) return kazakh ? "Аяқ-қол сақталады" : "Конечность сохранена";
            if (bandSeconds < 2f * 3600f) return kazakh ? "Сақталады (жгут 2 сағаттан аз)" : "Скорее всего сохранена (жгут < 2 ч)";
            if (bandSeconds < 6f * 3600f) return kazakh ? "Қауіп төнді: ишемия" : "Под угрозой: ишемия 2–6 ч, возможны осложнения";
            return kazakh ? "Жоғалту қаупі жоғары" : "Высокий риск ампутации (жгут > 6 ч)";
        }
    }
}
