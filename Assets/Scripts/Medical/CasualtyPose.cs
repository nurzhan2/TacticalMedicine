namespace FieldMedic.Medical
{
    /// Animator states of the casualty. The int value is the Animator "State" parameter — do not reorder.
    public enum CasualtyPose
    {
        CrouchIdle = 0,
        LookAround = 1,
        HitFall_Explosion = 2,
        HitFall_Shot = 3,
        HitFall_Shoulder = 4,
        KnockedOut = 5,
        LyingWrithing = 6,
        LyingMoaning = 7,
        LyingBreathless = 8,
        LyingCough = 9,
        LyingDying = 10,
        Unconscious = 11,
        RecoverySide = 12,
        Carried = 13,
        SittingDazed = 14,
    }

    public enum HitKind { Explosion, Shot, Shoulder, KnockedOut }

    /// Pure mapping physiology/pain -> body language. Kept engine-agnostic so it is covered by EditMode tests.
    /// Behaviour table from RESEARCH §6: <15% loss — restless, screams; 15–30% — anxious, fast breathing;
    /// 30–40% — confused; >40% — lethargic, agonal breathing; chest — coughing / air hunger; U — limp.
    public static class CasualtyPoseMapper
    {
        public static bool IsFall(CasualtyPose p) =>
            p is CasualtyPose.HitFall_Explosion or CasualtyPose.HitFall_Shot or CasualtyPose.HitFall_Shoulder or CasualtyPose.KnockedOut;

        public static bool IsLying(CasualtyPose p) => p is CasualtyPose.LyingWrithing or CasualtyPose.LyingMoaning
            or CasualtyPose.LyingBreathless or CasualtyPose.LyingCough or CasualtyPose.LyingDying
            or CasualtyPose.Unconscious or CasualtyPose.RecoverySide;

        public static CasualtyPose FallFor(HitKind k) => k switch
        {
            HitKind.Shot => CasualtyPose.HitFall_Shot,
            HitKind.Shoulder => CasualtyPose.HitFall_Shoulder,
            HitKind.KnockedOut => CasualtyPose.KnockedOut,
            _ => CasualtyPose.HitFall_Explosion,
        };

        /// Pose for a casualty who is down. `pain` = expressed pain 0..1 (PainModel.level).
        public static CasualtyPose LyingPose(PatientPhysiology p, float pain, bool recoveryPosition)
        {
            if (p.outcome == Outcome.Died) return CasualtyPose.LyingDying;
            var avpu = p.AVPU;
            if (avpu == Consciousness.Unresponsive) return recoveryPosition ? CasualtyPose.RecoverySide : CasualtyPose.Unconscious;
            if (recoveryPosition) return CasualtyPose.RecoverySide;
            // class IV / pre-arrest: lethargic, agonal — limp if only pain-responsive
            if (p.LossFraction > 0.40f || p.SystolicBP < 60f)
                return avpu == Consciousness.Pain ? CasualtyPose.Unconscious : CasualtyPose.LyingBreathless;
            if (p.pneumo == PneumoState.Tension || p.SpO2 < 85f) return CasualtyPose.LyingBreathless;
            if (p.pneumo == PneumoState.Open) return CasualtyPose.LyingCough;
            if (avpu == Consciousness.Pain) return pain > 0.5f ? CasualtyPose.LyingMoaning : CasualtyPose.LyingBreathless;
            if (pain >= 0.6f) return CasualtyPose.LyingWrithing;
            if (avpu == Consciousness.Voice || p.HemorrhageClass >= 3) return CasualtyPose.LyingBreathless;
            return pain >= 0.25f ? CasualtyPose.LyingMoaning : CasualtyPose.LyingBreathless;
        }
    }
}
