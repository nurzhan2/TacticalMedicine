using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// World-space limb geometry of the current (animated/IK'd) pose, in the same "segment units" as
    /// <see cref="LimbGeometry"/>: 0 = hip/shoulder, 1 = knee/elbow, 2 = ankle/wrist, ~2.6 = toes/fingertips.
    /// </summary>
    public static class LimbMath
    {
        public static readonly Limb[] All = { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg };

        public static string Id(Limb l) => l switch
        {
            Limb.LeftArm => "armL", Limb.RightArm => "armR", Limb.LeftLeg => "legL", _ => "legR",
        };

        public static Limb FromId(string id) => id switch
        {
            "armL" => Limb.LeftArm, "armR" => Limb.RightArm, "legL" => Limb.LeftLeg, _ => Limb.RightLeg,
        };

        public static bool IsLeg(Limb l) => l == Limb.LeftLeg || l == Limb.RightLeg;

        /// <summary>The four joint points of a limb: root, mid, tip (ankle/wrist), end (toes/fingertips).</summary>
        public static bool Points(CasualtyRig rig, Limb limb, out Vector3 root, out Vector3 mid, out Vector3 tip, out Vector3 end)
        {
            root = mid = tip = end = Vector3.zero;
            if (rig == null) return false;
            rig.LimbChain(limb, out var r, out var m, out var t);
            if (r == null || m == null || t == null) return false;
            root = r.position; mid = m.position; tip = t.position;
            Transform endBone = limb switch
            {
                Limb.LeftLeg => rig.Bone(HumanBodyBones.LeftToes),
                Limb.RightLeg => rig.Bone(HumanBodyBones.RightToes),
                Limb.LeftArm => rig.Bone(HumanBodyBones.LeftMiddleProximal),
                _ => rig.Bone(HumanBodyBones.RightMiddleProximal),
            };
            Vector3 dir = endBone != null ? endBone.position - tip : (tip - mid).normalized * 0.12f;
            if (dir.sqrMagnitude < 1e-6f) dir = (tip - mid).normalized * 0.12f;
            // past the toe joint / knuckles to the actual end of the foot / fingers
            end = tip + dir * (IsLeg(limb) ? 1.35f : 1.9f);
            return true;
        }

        public static Vector3 PointAt(CasualtyRig rig, Limb limb, float pos)
        {
            if (!Points(rig, limb, out var a, out var b, out var c, out var d)) return rig != null ? rig.transform.position : Vector3.zero;
            if (pos <= 1f) return Vector3.LerpUnclamped(a, b, pos);
            if (pos <= 2f) return Vector3.Lerp(b, c, pos - 1f);
            return Vector3.Lerp(c, d, Mathf.Clamp01((pos - 2f) / 0.6f));
        }

        /// <summary>Limb direction (proximal → distal) at a position.</summary>
        public static Vector3 AxisAt(CasualtyRig rig, Limb limb, float pos)
        {
            if (!Points(rig, limb, out var a, out var b, out var c, out var d)) return Vector3.down;
            Vector3 v = pos < 1f ? b - a : pos < 2f ? c - b : d - c;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.down;
        }

        /// <summary>Approximate limb radius (flesh + uniform) at a position (m).</summary>
        /// <summary>w10c: per-model thickness (the owner's soldier wears a thicker uniform than the old body);
        /// set from <see cref="CasualtyRig.legRadiusScale"/> / <see cref="CasualtyRig.armRadiusScale"/>.</summary>
        public static float LegScale = 1f, ArmScale = 1f;

        public static float RadiusAt(Limb limb, float pos) => RadiusAtBase(limb, pos) * (IsLeg(limb) ? LegScale : ArmScale);

        /// <summary>The old Mixamo body's radius profile (scale 1).</summary>
        public static float RadiusAtBase(Limb limb, float pos)
        {
            bool leg = IsLeg(limb);
            if (leg)
            {
                if (pos < 1f) return Mathf.Lerp(0.088f, 0.058f, pos);
                if (pos < 2f) return Mathf.Lerp(0.058f, 0.042f, pos - 1f);
                return 0.045f;
            }
            if (pos < 1f) return Mathf.Lerp(0.058f, 0.045f, pos);
            if (pos < 2f) return Mathf.Lerp(0.045f, 0.033f, pos - 1f);
            return 0.03f;
        }

        /// <summary>Projects a world point onto the limb skeleton. Returns distance to the axis.</summary>
        public static float Project(CasualtyRig rig, Limb limb, Vector3 p, out float pos, out Vector3 axisPoint)
        {
            pos = 0f; axisPoint = p;
            if (!Points(rig, limb, out var a, out var b, out var c, out var d)) return float.MaxValue;
            float best = float.MaxValue;
            Seg(p, a, b, 0f, 1f, ref best, ref pos, ref axisPoint);
            Seg(p, b, c, 1f, 1f, ref best, ref pos, ref axisPoint);
            Seg(p, c, d, 2f, 0.6f, ref best, ref pos, ref axisPoint);
            return best;
        }

        static void Seg(Vector3 p, Vector3 s0, Vector3 s1, float offset, float span, ref float best, ref float pos, ref Vector3 axisPoint)
        {
            Vector3 v = s1 - s0;
            float t = v.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s0, v) / v.sqrMagnitude);
            Vector3 q = s0 + v * t;
            float dist = Vector3.Distance(p, q);
            if (dist < best) { best = dist; pos = offset + t * span; axisPoint = q; }
        }

        /// <summary>Nearest limb to a point (distance to its axis minus its radius ≈ distance to the skin).</summary>
        public static bool Nearest(CasualtyRig rig, Vector3 p, out Limb limb, out float pos, out float skinDistance)
        {
            limb = Limb.RightLeg; pos = 0f; skinDistance = float.MaxValue;
            bool any = false;
            foreach (var l in All)
            {
                float d = Project(rig, l, p, out float lp, out _);
                if (d == float.MaxValue) continue;
                float skin = d - RadiusAt(l, lp);
                if (skin < skinDistance) { skinDistance = skin; limb = l; pos = lp; any = true; }
            }
            return any;
        }

        /// <summary>
        /// "Up" direction around the limb at a position: away from the ground (where the windlass / buckle sit
        /// so the rescuer can reach them). Perpendicular to the limb axis.
        /// </summary>
        public static Vector3 UpAround(CasualtyRig rig, Limb limb, float pos)
        {
            Vector3 axis = AxisAt(rig, limb, pos);
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (up.sqrMagnitude < 0.04f) up = Vector3.ProjectOnPlane(rig.BodyForward, axis);
            return up.normalized;
        }

        /// <summary>Medical wound position along its limb from where the wound site sits on the body.</summary>
        public static float WoundLimbPosition(CasualtyRig rig, BodyRegion region, Vector3 worldPos)
        {
            string id = BodyRegions.LimbId(region);
            if (id == null) return LimbGeometry.DefaultWoundPosition(region);
            Project(rig, FromId(id), worldPos, out float pos, out _);
            // w10e: the soldier mesh has no rigid skin on the thigh, so the site falls back to an outer vertex that can
            // project almost onto the hip (0.05) — a pre-applied / "high and tight" band at 0.12–0.2 was then distal to
            // the wound and never stopped it (S1, S7, S8). Keep the wound inside its own segment.
            int seg = BodyRegions.LimbSegment(region);
            if (seg >= 0) pos = Mathf.Clamp(pos, seg + 0.3f, seg + 0.9f);
            return pos;
        }
    }
}
