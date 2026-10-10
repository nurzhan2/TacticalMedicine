using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Analytic two-bone IK (upper/lower limb). Works on world rotations after the Animator has written the pose,
    /// so it can be layered on top of any clip: a grabbed ankle pulls the leg, a held wrist the arm.
    /// The bend plane comes from the hint (knee/elbow direction) or, without a hint, from the current pose.
    /// </summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform root, Transform mid, Transform tip, Vector3 target, Vector3? hint, float weight)
        {
            if (weight <= 0f || root == null || mid == null || tip == null) return;
            Vector3 a = root.position, b = mid.position, c = tip.position;
            Vector3 t = Vector3.Lerp(c, target, Mathf.Clamp01(weight));

            float lab = (b - a).magnitude, lcb = (c - b).magnitude;
            if (lab < 1e-5f || lcb < 1e-5f) return;
            float lat = Mathf.Clamp((t - a).magnitude, 1e-4f, (lab + lcb) * 0.9995f);

            // current interior angles
            float acab0 = Angle(c - a, b - a);
            float babc0 = Angle(a - b, c - b);
            // desired interior angles (law of cosines)
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            Vector3 axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-10f && hint.HasValue) axis = Vector3.Cross(c - a, hint.Value - a);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(c - a, Vector3.up);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(c - a, Vector3.forward);
            axis.Normalize();

            mid.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis) * mid.rotation;
            root.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * root.rotation;
            // swing the whole chain so the tip lands on the target
            root.rotation = Quaternion.FromToRotation(tip.position - root.position, t - root.position) * root.rotation;

            // twist around the root->target line so the knee/elbow points toward the hint
            if (hint.HasValue)
            {
                Vector3 line = (t - root.position).normalized;
                Vector3 pb = Vector3.ProjectOnPlane(mid.position - root.position, line);
                Vector3 ph = Vector3.ProjectOnPlane(hint.Value - root.position, line);
                if (pb.sqrMagnitude > 1e-8f && ph.sqrMagnitude > 1e-8f)
                {
                    float ang = Vector3.SignedAngle(pb, ph, line);
                    root.rotation = Quaternion.AngleAxis(ang, line) * root.rotation;
                }
            }
        }

        static float Angle(Vector3 u, Vector3 v)
        {
            float d = u.magnitude * v.magnitude;
            if (d < 1e-10f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, v) / d, -1f, 1f));
        }
    }
}
