using System;

namespace FieldMedic.Medical
{
    public enum WoundType
    {
        ArterialLimb,        // pulsatile bright red, life threat in minutes
        TraumaticAmputation, // partial/complete limb loss, massive bleed
        Junctional,          // groin / axilla / neck: no tourniquet possible
        Venous,              // steady dark flow, pressure dressing is enough
        Capillary,           // minor ooze
        PenetratingChest,    // open pneumothorax ("sucking chest wound")
    }

    public enum BodyRegion
    {
        Head, Neck, ChestL, ChestR, Abdomen, Pelvis,
        GroinL, GroinR, AxillaL, AxillaR,
        UpperArmL, UpperArmR, ForearmL, ForearmR, HandL, HandR,
        ThighL, ThighR, ShinL, ShinR, FootL, FootR,
    }

    public enum BleedControl
    {
        None,
        DirectPressure,   // hands on wound, stops working when released
        Packed,           // hemostatic gauze inside wound, needs 3 min hold
        PackedAndHeld,    // 3 min pressure completed
        PressureDressing, // emergency bandage over wound or packing
        Tourniquet,       // proximal occlusion
        OcclusiveSeal,    // chest seal
    }

    [Serializable]
    public class Wound
    {
        public string id;
        public WoundType type;
        public BodyRegion region;
        public bool hiddenUnderClothing = true;
        public bool discovered;
        public BleedControl control = BleedControl.None;
        public bool packed;
        public float packingHoldSeconds;
        public float bloodLostMl;
        /// Position along the limb in LimbGeometry units (0 hip/shoulder .. 2 ankle/wrist); negative = default for region.
        public float limbPosition = -1f;
        /// Clothing over the wound cut open (shears) — needed for packing / skin-level care.
        public bool exposed;
        /// Bleed multiplier from tourniquets proximal to this wound (1 none, above 1 = venous congestion, 0 occluded).
        public float tourniquetFactor = 1f;
        public bool sealVented;
        /// On the back / buttock: invisible and unreachable while he lies supine — found by a log roll.
        public bool posterior;
        /// Bleeding held back by vasospasm / a clot until this scenario second (delayed onset, amputation rebleed).
        public float onsetDelaySeconds;
        /// Fraction of the bleed that gets through before the onset (spasm leaks a little).
        public float preOnsetFactor = 0.08f;
        public bool onsetFired;
        [NonSerialized] public bool handsPressing; // set by interaction each frame, cleared by physiology tick

        public const float PackingHoldRequired = 180f;

        /// Base bleed rate at normal perfusion, ml/min (game-tuned, order of magnitude from trauma literature).
        public float BaseRateMlPerMin => type switch
        {
            WoundType.ArterialLimb => 600f,
            WoundType.TraumaticAmputation => 700f,
            WoundType.Junctional => 500f,
            WoundType.Venous => 40f,
            WoundType.Capillary => 8f,
            WoundType.PenetratingChest => 40f,
            _ => 0f,
        };

        public float LimbPos => limbPosition >= 0f ? limbPosition : LimbGeometry.DefaultWoundPosition(region);
        public string LimbId => BodyRegions.LimbId(region) ?? JunctionLimb(region);

        static string JunctionLimb(BodyRegion r) => r switch
        {
            BodyRegion.GroinL => "legL", BodyRegion.GroinR => "legR",
            BodyRegion.AxillaL => "armL", BodyRegion.AxillaR => "armR",
            _ => null,
        };

        /// Fraction of bleeding that remains (control method x tourniquet effect).
        public float ResidualFactor => ControlResidual * (control == BleedControl.Tourniquet ? 1f : tourniquetFactor);

        float ControlResidual => control switch
        {
            BleedControl.None => handsPressing ? DirectPressureResidual : 1f,
            BleedControl.DirectPressure => DirectPressureResidual,
            BleedControl.Packed => handsPressing ? 0.1f : 0.45f,
            BleedControl.PackedAndHeld => 0.08f,
            BleedControl.PressureDressing => PressureDressingResidual(),
            BleedControl.Tourniquet => 0f,
            BleedControl.OcclusiveSeal => 0.1f,
            _ => 1f,
        };

        float DirectPressureResidual => type is WoundType.Venous or WoundType.Capillary ? 0.05f : 0.35f;

        float PressureDressingResidual()
        {
            // A dressing over properly held packing holds; a dressing alone on an artery barely helps.
            if (packed && packingHoldSeconds >= PackingHoldRequired) return 0f;
            return type switch
            {
                WoundType.Venous or WoundType.Capillary => 0f,
                WoundType.ArterialLimb or WoundType.TraumaticAmputation => 0.6f,
                WoundType.Junctional => packed ? 0.25f : 0.7f,
                _ => 0.3f,
            };
        }

        public bool IsLifeThreatening =>
            type is WoundType.ArterialLimb or WoundType.TraumaticAmputation or WoundType.Junctional;

        public bool IsControlled => ResidualFactor <= 0.01f;
        /// Still bleeding when tourniquets are ignored (used to judge where a band is needed).
        public bool ControlResidualOpen => ControlResidual > 0.01f && control != BleedControl.Tourniquet;

        public bool IsLimb => BodyRegions.IsLimb(region);
        public bool IsJunctionalRegion => BodyRegions.IsJunctional(region);
        public bool IsChest => region is BodyRegion.ChestL or BodyRegion.ChestR;
    }

    public static class BodyRegions
    {
        public static bool IsLimb(BodyRegion r) => r is BodyRegion.UpperArmL or BodyRegion.UpperArmR
            or BodyRegion.ForearmL or BodyRegion.ForearmR or BodyRegion.HandL or BodyRegion.HandR
            or BodyRegion.ThighL or BodyRegion.ThighR or BodyRegion.ShinL or BodyRegion.ShinR
            or BodyRegion.FootL or BodyRegion.FootR;

        public static bool IsJunctional(BodyRegion r) => r is BodyRegion.GroinL or BodyRegion.GroinR
            or BodyRegion.AxillaL or BodyRegion.AxillaR or BodyRegion.Neck;

        /// Proximal-to-distal index within a limb (0 = closest to torso). -1 if not a limb.
        public static int LimbSegment(BodyRegion r) => r switch
        {
            BodyRegion.UpperArmL or BodyRegion.UpperArmR or BodyRegion.ThighL or BodyRegion.ThighR => 0,
            BodyRegion.ForearmL or BodyRegion.ForearmR or BodyRegion.ShinL or BodyRegion.ShinR => 1,
            BodyRegion.HandL or BodyRegion.HandR or BodyRegion.FootL or BodyRegion.FootR => 2,
            _ => -1,
        };

        /// Same-limb id: "armL", "legR", etc. Null if not a limb.
        public static string LimbId(BodyRegion r) => r switch
        {
            BodyRegion.UpperArmL or BodyRegion.ForearmL or BodyRegion.HandL => "armL",
            BodyRegion.UpperArmR or BodyRegion.ForearmR or BodyRegion.HandR => "armR",
            BodyRegion.ThighL or BodyRegion.ShinL or BodyRegion.FootL => "legL",
            BodyRegion.ThighR or BodyRegion.ShinR or BodyRegion.FootR => "legR",
            _ => null,
        };

        public static bool IsLeftSide(BodyRegion r) => r.ToString().EndsWith("L");
    }
}
