using System;

namespace FieldMedic.Medical
{
    /// CAT application steps (GDD §5). The runtime advances them only through physical gestures.
    public enum TourniquetStage
    {
        InPouch, Held, BandAroundLimb, Positioned, StrapPulled, WindlassTwisted, WindlassLocked, StrapSecured, TimeMarked,
    }

    /// Position along a limb in "segment units": 0 = hip/shoulder joint, 1 = knee/elbow, 2 = ankle/wrist,
    /// up to ~2.6 = toes/fingertips. Lets tourniquets and wounds be compared without engine types.
    public static class LimbGeometry
    {
        public const float JointHalfWidth = 0.11f;   // ±11% of a segment around knee/elbow ≈ ±4.5 cm on a thigh
        public const float DistalJointHalfWidth = 0.14f;
        public const float MinPosition = 0.03f;      // above this is the groin/axilla crease: no room for a band

        public static bool IsLeg(string limbId) => limbId != null && limbId.StartsWith("leg");

        /// Segment length in metres (adult male): thigh 42 cm, shin 40 cm, upper arm 30 cm, forearm 26 cm.
        public static float SegmentLength(string limbId, float pos)
        {
            bool leg = IsLeg(limbId);
            if (pos < 1f) return leg ? 0.42f : 0.30f;
            if (pos < 2f) return leg ? 0.40f : 0.26f;
            return leg ? 0.22f : 0.18f;
        }

        /// Metres between two positions on the same limb (positive if b is distal to a).
        public static float Distance(string limbId, float a, float b)
        {
            float sign = b >= a ? 1f : -1f;
            float lo = Math.Min(a, b), hi = Math.Max(a, b), d = 0f;
            for (int seg = (int)Math.Floor(lo); seg <= (int)Math.Floor(hi); seg++)
            {
                float s0 = Math.Max(lo, seg), s1 = Math.Min(hi, seg + 1);
                if (s1 > s0) d += (s1 - s0) * SegmentLength(limbId, seg + 0.5f);
            }
            return d * sign;
        }

        public static bool OnJoint(float pos) =>
            Math.Abs(pos - 1f) < JointHalfWidth || Math.Abs(pos - 2f) < DistalJointHalfWidth || pos >= 2f;

        public static BodyRegion RegionAt(string limbId, float pos)
        {
            int seg = pos < 1f ? 0 : pos < 2f ? 1 : 2;
            return limbId switch
            {
                "legL" => seg == 0 ? BodyRegion.ThighL : seg == 1 ? BodyRegion.ShinL : BodyRegion.FootL,
                "legR" => seg == 0 ? BodyRegion.ThighR : seg == 1 ? BodyRegion.ShinR : BodyRegion.FootR,
                "armL" => seg == 0 ? BodyRegion.UpperArmL : seg == 1 ? BodyRegion.ForearmL : BodyRegion.HandL,
                _ => seg == 0 ? BodyRegion.UpperArmR : seg == 1 ? BodyRegion.ForearmR : BodyRegion.HandR,
            };
        }

        /// Default position of a wound inside its region (mid-segment; junctional = above any band site).
        public static float DefaultWoundPosition(BodyRegion r)
        {
            if (BodyRegions.IsJunctional(r)) return 0f;
            int seg = BodyRegions.LimbSegment(r);
            return seg < 0 ? -1f : seg + 0.45f;
        }
    }

    /// <summary>
    /// The physical state of one CAT (pure C#; the Unity layer feeds it gestures).
    /// Occlusion model: a strap pulled tight compresses veins but not arteries; the windlass adds the arterial
    /// pressure. Partial occlusion blocks venous return while arterial inflow continues → the wound bleeds MORE
    /// (venous congestion, a classic real-world failure of loose tourniquets). Full occlusion stops the bleed
    /// and the distal pulse. A band on a joint cannot compress the artery against bone.
    /// </summary>
    [Serializable]
    public class TourniquetState
    {
        public const float InitialSlackCm = 12f;     // loop as it comes out of the pouch
        public const float TightSlackCm = 1.5f;      // "three fingers do not fit under the band"
        public const float CmPerHalfTurn = 1.5f;     // band taken up by one windlass half-turn
        public const int MaxHalfTurns = 7;           // the windlass runs out of band after ~3.5 turns
        public const float LockToleranceDeg = 35f;   // rod close enough to the clip to snap in

        public string id = "CAT";
        public TourniquetStage stage = TourniquetStage.InPouch;
        public string limbId;
        public float limbPosition = -1f;
        public float slackCm = InitialSlackCm;
        public float windlassDegrees;               // signed, accumulated; either direction tightens
        public bool overClothing = true;
        public float appliedAtSeconds = -1f;        // first windlass half-turn (scenario clock)
        public float timeMarkedAtSeconds = -1f;
        public float strapPulledCm;

        public event Action<TourniquetState, TourniquetStage> StageChanged;

        public bool OnLimb => stage >= TourniquetStage.BandAroundLimb && limbId != null;
        public bool OnJoint => OnLimb && LimbGeometry.OnJoint(limbPosition);
        public bool Locked => stage >= TourniquetStage.WindlassLocked;
        public bool Secured => stage >= TourniquetStage.StrapSecured;
        public bool TimeMarked => stage >= TourniquetStage.TimeMarked;
        public float Turns => Math.Abs(windlassDegrees) / 360f;
        public int HalfTurns => (int)Math.Floor(Math.Abs(windlassDegrees) / 180f + 1e-4f);
        public BodyRegion Region => LimbGeometry.RegionAt(limbId, limbPosition);

        public static int RequiredHalfTurns(string limbId) => LimbGeometry.IsLeg(limbId) ? 4 : 2;

        /// Half-turns needed for full occlusion with the current slack.
        public float HalfTurnsNeeded => RequiredHalfTurns(limbId) + Math.Max(0f, slackCm - TightSlackCm) / CmPerHalfTurn;

        public float StrapTightness => Clamp01((InitialSlackCm - slackCm) / (InitialSlackCm - TightSlackCm));

        /// 0 = no pressure, 1 = full arterial occlusion.
        public float Occlusion
        {
            get
            {
                if (!OnLimb || stage < TourniquetStage.Positioned) return 0f;
                float strap = 0.45f * StrapTightness;
                float twist = Clamp01((float)(Math.Abs(windlassDegrees) / 180.0) / HalfTurnsNeeded);
                float occ = strap + (1f - strap) * twist;
                if (OnJoint) occ = Math.Min(occ, 0.5f); // bones of the joint shield the artery
                return Clamp01(occ);
            }
        }

        public bool FullOcclusion => Occlusion >= 0.999f;

        /// Multiplier applied to bleeding of wounds distal to this band.
        public static float BleedMultiplier(float occ)
        {
            if (occ < 0.1f) return 1f;
            if (occ < 0.55f)
            {
                float t = (occ - 0.1f) / 0.45f;
                return 1f + 0.4f * t * t * (3f - 2f * t); // veins shut, arteries still pump: congestion
            }
            if (occ >= 0.999f) return 0f;
            return 1.4f * (float)Math.Pow(1f - (occ - 0.55f) / 0.45f, 1.6f);
        }

        public float BleedFactor => BleedMultiplier(Occlusion);

        /// Pulse strength multiplier below the band (1 = unaffected).
        public float DistalPulseFactor => FullOcclusion ? 0f : (float)Math.Pow(1f - Occlusion, 0.7f);

        /// True when this band is proximal to a point on the same limb (it can affect it).
        public bool IsProximalTo(string limb, float pos) => OnLimb && limb == limbId && limbPosition < pos;

        // ------------------------------------------------------------------ steps
        void Set(TourniquetStage s)
        {
            if (stage == s) return;
            stage = s;
            StageChanged?.Invoke(this, s);
        }

        public void Pickup() { if (stage == TourniquetStage.InPouch) Set(TourniquetStage.Held); }

        /// Loop slid over the foot/hand or routed around the limb.
        public bool WrapAround(string limb, float pos)
        {
            if (stage > TourniquetStage.Held || limb == null) return false;
            limbId = limb;
            limbPosition = Math.Max(LimbGeometry.MinPosition, pos);
            slackCm = InitialSlackCm;
            Set(TourniquetStage.BandAroundLimb);
            return true;
        }

        /// Slide the loose band along the limb (only while it is still loose).
        public bool SlideTo(float pos)
        {
            if (!OnLimb || stage > TourniquetStage.Positioned || strapPulledCm > 3f) return false;
            limbPosition = Math.Clamp(pos, LimbGeometry.MinPosition, 2.55f);
            return true;
        }

        public bool ConfirmPosition()
        {
            if (stage != TourniquetStage.BandAroundLimb) return false;
            Set(TourniquetStage.Positioned);
            return true;
        }

        /// Pull the free end of the strap through the buckle. Returns the slack removed.
        public float PullStrap(float cm)
        {
            if (stage < TourniquetStage.Positioned || Locked || cm <= 0f) return 0f;
            strapPulledCm += cm;
            float before = slackCm;
            slackCm = Math.Max(TightSlackCm * 0.5f, slackCm - cm * 0.8f); // velcro + friction: not 1:1
            if (stage == TourniquetStage.Positioned && strapPulledCm >= 4f) Set(TourniquetStage.StrapPulled);
            return before - slackCm;
        }

        /// Wrist twist applied to the gripped windlass rod. Returns the degrees actually applied.
        public float Twist(float degrees)
        {
            if (stage < TourniquetStage.Positioned || Locked) return 0f;
            float max = MaxHalfTurns * 180f;
            float before = windlassDegrees;
            windlassDegrees = Math.Clamp(windlassDegrees + degrees, -max, max);
            if (HalfTurns >= 1 && stage < TourniquetStage.WindlassTwisted) Set(TourniquetStage.WindlassTwisted);
            return windlassDegrees - before;
        }

        /// Angle of the rod relative to the nearest clip-aligned orientation (0 = can lock).
        public float LockError
        {
            get
            {
                float m = Math.Abs(windlassDegrees) % 180f;
                return Math.Min(m, 180f - m);
            }
        }

        public bool TryLock()
        {
            if (stage < TourniquetStage.WindlassTwisted || Locked) return false;
            if (LockError > LockToleranceDeg) return false;
            float sign = windlassDegrees >= 0f ? 1f : -1f;
            windlassDegrees = sign * (float)Math.Round(Math.Abs(windlassDegrees) / 180f) * 180f;
            if (HalfTurns < 1) return false;
            Set(TourniquetStage.WindlassLocked);
            return true;
        }

        /// The rod was let go outside the clip: band tension spins it back to the last clip-aligned position.
        public float ReleaseWindlass()
        {
            if (Locked || stage < TourniquetStage.Positioned) return 0f;
            float sign = windlassDegrees >= 0f ? 1f : -1f;
            float aligned = sign * (float)Math.Floor(Math.Abs(windlassDegrees) / 180f + 1e-4f) * 180f;
            float lost = Math.Abs(windlassDegrees - aligned);
            windlassDegrees = aligned;
            if (HalfTurns < 1 && stage == TourniquetStage.WindlassTwisted) Set(TourniquetStage.StrapPulled);
            return lost;
        }

        public bool SecureStrap()
        {
            if (stage != TourniquetStage.WindlassLocked) return false;
            Set(TourniquetStage.StrapSecured);
            return true;
        }

        public bool MarkTime(float scenarioSeconds)
        {
            if (stage < TourniquetStage.WindlassLocked) return false;
            timeMarkedAtSeconds = scenarioSeconds;
            if (stage == TourniquetStage.StrapSecured) Set(TourniquetStage.TimeMarked);
            return true;
        }

        /// Rough handling (drag, roll) loosens a band. A locked + secured CAT barely moves.
        public float Slip(float severity01)
        {
            if (!OnLimb) return 0f;
            float s = Clamp01(severity01);
            float cm = Locked ? (Secured ? 0.15f : 0.6f) * s : 2.5f * s;
            slackCm = Math.Min(InitialSlackCm, slackCm + cm);
            if (!Locked) limbPosition = Math.Min(2.55f, limbPosition + 0.02f * s); // loose bands creep distally
            return cm;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
