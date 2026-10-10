using System;

namespace FieldMedic.Medical
{
    /// <summary>Hemostatic gauze packing: the gauze is fed into the wound channel by finger push strokes.</summary>
    [Serializable]
    public class PackingProcedure
    {
        public const float GauzeLengthM = 3.66f;      // Combat Gauze z-fold, 12 ft
        public const float StrokeLengthM = 0.16f;     // gauze pushed in by one finger stroke
        public const float MinStrokeDepthCm = 2f;

        public float requiredM;
        public float consumedM;
        public int strokes;
        public bool started;

        public PackingProcedure(WoundType type) { requiredM = RequiredLength(type); }

        /// Wound channel volume expressed as gauze length (game-tuned).
        public static float RequiredLength(WoundType t) => t switch
        {
            WoundType.Junctional => 1.6f,
            WoundType.ArterialLimb => 1.2f,
            WoundType.TraumaticAmputation => 1.4f,
            WoundType.Venous => 0.5f,
            _ => 0.3f,
        };

        public float Fill => requiredM <= 0f ? 1f : Math.Min(1f, consumedM / requiredM);
        public bool Complete => Fill >= 1f;
        public float RemainingGauzeM => Math.Max(0f, GauzeLengthM - consumedM);

        /// Gauze end placed into the wound (on the bleeding point).
        public void Start() => started = true;

        /// One push stroke with the fingertip `depthCm` deep into the wound. Returns gauze consumed (m).
        public float Stroke(float depthCm)
        {
            if (!started || Complete || depthCm < MinStrokeDepthCm || RemainingGauzeM <= 0f) return 0f;
            float m = Math.Min(StrokeLengthM * Math.Min(1.5f, depthCm / 3f), RemainingGauzeM);
            consumedM += m;
            strokes++;
            return m;
        }
    }

    /// <summary>Israeli (emergency) bandage: pad on wound → wrap → reverse through the pressure bar → wraps → clip.</summary>
    [Serializable]
    public class BandageProcedure
    {
        public enum Step { None, PadPlaced, Wrapping, PressureBar, Clipped }
        public const int WrapsBeforeBar = 1;
        public const int WrapsAfterBar = 2;

        public Step step;
        public float wrapDegrees;          // accumulated around the limb axis
        public float wrapsAtBar;

        public float Wraps => Math.Abs(wrapDegrees) / 360f;
        public float WrapsSinceBar => step >= Step.PressureBar ? Wraps - wrapsAtBar : 0f;

        /// 0..1 pressure applied on the pad.
        public float Pressure => step switch
        {
            Step.None => 0f,
            Step.PadPlaced => 0.15f,
            Step.Wrapping => Math.Min(0.45f, 0.15f + 0.3f * Wraps),
            _ => Math.Min(1f, 0.6f + 0.2f * WrapsSinceBar),
        };

        public bool PlacePad()
        {
            if (step != Step.None) return false;
            step = Step.PadPlaced;
            return true;
        }

        public void Wrap(float degrees)
        {
            if (step == Step.None || step == Step.Clipped) return;
            wrapDegrees += Math.Abs(degrees);
            if (step == Step.PadPlaced && Wraps > 0.25f) step = Step.Wrapping;
        }

        /// Reverse the bandage through the pressure bar (wrist twist at the bar).
        public bool TwistBar()
        {
            if (step != Step.Wrapping || Wraps < WrapsBeforeBar) return false;
            step = Step.PressureBar;
            wrapsAtBar = Wraps;
            return true;
        }

        public bool CanClip => step == Step.PressureBar && WrapsSinceBar >= WrapsAfterBar;

        public bool Clip()
        {
            if (!CanClip) return false;
            step = Step.Clipped;
            return true;
        }

        public bool Complete => step == Step.Clipped;
    }

    /// <summary>Vented chest seal: tear package → peel backing → wipe → place (end of exhale) → press edges.</summary>
    [Serializable]
    public class ChestSealProcedure
    {
        public enum Step { Packaged, Torn, Peeled, Placed, Sealed }
        public const int WipesNeeded = 2;
        public const int EdgePressesNeeded = 3;

        public Step step;
        public int wipes;
        public int edgePresses;
        public bool placedOnExhale;
        public bool vented = true;
        public int burps;

        public bool Tear() { if (step != Step.Packaged) return false; step = Step.Torn; return true; }
        public bool Peel() { if (step != Step.Torn) return false; step = Step.Peeled; return true; }
        public void Wipe() { if (step < Step.Placed) wipes++; }
        public bool AreaDry => wipes >= WipesNeeded;

        /// Place on the wound. A seal on wet, bloody skin does not stick (needs wiping first).
        public bool Place(bool exhaling)
        {
            if (step != Step.Peeled || !AreaDry) return false;
            step = Step.Placed;
            placedOnExhale = exhaling;
            return true;
        }

        public bool PressEdge()
        {
            if (step != Step.Placed) return false;
            edgePresses++;
            if (edgePresses >= EdgePressesNeeded) step = Step.Sealed;
            return step == Step.Sealed;
        }

        /// Lift an edge to let trapped air out ("burp"), then press it down again.
        public bool Burp()
        {
            if (step != Step.Sealed) return false;
            burps++;
            step = Step.Placed;
            edgePresses = EdgePressesNeeded - 1;
            return true;
        }

        public bool Complete => step == Step.Sealed;
    }

    public enum NeedleSite { SecondIcsMcl, FifthIcsAal, MedialToNippleLine, Other }

    /// <summary>Needle decompression rules (RESEARCH §3): site, perpendicular entry over the rib, depth, not through a seal.</summary>
    public static class NeedleRules
    {
        public const float MaxAngleFromPerpendicular = 30f;
        public const float MinDepthCm = 4f;   // chest wall of a fit soldier with gear: 3.25" needle needed

        public static bool SiteValid(NeedleSite s) => s is NeedleSite.SecondIcsMcl or NeedleSite.FifthIcsAal;

        public static string Check(NeedleSite site, float angleFromPerpendicular, float depthCm, bool throughSeal)
        {
            if (throughSeal) return "needle_through_seal";
            if (site == NeedleSite.MedialToNippleLine) return "needle_wrong_site";
            if (!SiteValid(site)) return "needle_wrong_site";
            if (angleFromPerpendicular > MaxAngleFromPerpendicular) return "needle_angle";
            if (depthCm < MinDepthCm) return "needle_shallow";
            return "ok";
        }
    }

    /// <summary>Nasopharyngeal airway: measure, lubricate, bevel to septum, insert perpendicular to the face.</summary>
    [Serializable]
    public class NpaProcedure
    {
        public const float FullDepthCm = 12f;
        public const float TurbinateDepthCm = 5f;
        public const float MaxAngleDeg = 30f;

        public bool measured;
        public bool lubed;
        public bool bevelToSeptum = true;
        public float depthCm;
        public bool resistance;
        public float rotationSinceResistance;

        public bool Inserted => depthCm >= FullDepthCm - 0.01f;

        /// Advance by `cm` with the tube `angleDeg` away from straight back (perpendicular to the face).
        /// Upward (toward the forehead) insertion hits the turbinates; rotating the tube helps it pass.
        public float Advance(float cm, float angleDeg)
        {
            if (cm <= 0f || Inserted) return 0f;
            // redirected (perpendicular again) and rotated: the tip slips past the turbinate
            if (resistance && rotationSinceResistance >= 40f && angleDeg <= MaxAngleDeg)
            {
                resistance = false;
                rotationSinceResistance = 0f;
            }
            float limit = FullDepthCm;
            if (angleDeg > MaxAngleDeg) limit = TurbinateDepthCm;
            else if (!lubed) limit = TurbinateDepthCm + 2f; // a dry tube sticks in the nasal passage
            if (resistance) limit = Math.Min(limit, depthCm);
            float before = depthCm;
            depthCm = Math.Min(limit, depthCm + cm);
            if (limit < FullDepthCm && depthCm >= limit - 1e-3f) resistance = true;
            return depthCm - before;
        }

        public void Rotate(float degrees)
        {
            if (resistance) rotationSinceResistance += Math.Abs(degrees);
        }

        public bool GoodTechnique => measured && lubed && bevelToSeptum;
    }
}
