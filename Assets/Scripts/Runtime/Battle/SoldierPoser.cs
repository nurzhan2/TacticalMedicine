using FieldMedic.Casualty;
using UnityEngine;

namespace FieldMedic.Battle
{
    public enum SoldierPose { None = 0, Run = 1, Aim = 2, KneelAim = 3 }

    /// <summary>
    /// Procedural pose layer for the battle soldiers, applied after the Animator: a running gait (foot targets on
    /// a stride cycle + two-bone IK, hip bob, forward lean, rifle carried at port arms), a standing shouldered aim
    /// and a kneeling aim that keeps the kneel clip's legs but puts the rifle to the shoulder. The rifle (skinned to
    /// the right hand) is aligned by rotating the hand so its grip→muzzle axis points where the soldier aims; the
    /// left hand is IK'd onto the handguard. Shared with the editor baker (crowd meshes, impostor sheet), so the
    /// distant layers move exactly like the near soldiers. Blends in/out over 0.25 s.
    /// </summary>
    public sealed class SoldierPoser : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("Rifle grip→muzzle axis in right-hand space (baked by the battle asset builder).")]
        public Vector3 rifleAxisLocal = Vector3.forward;
        [Tooltip("Muzzle point in right-hand space.")]
        public Vector3 muzzleLocal = new Vector3(0f, 0f, 0.8f);
        [Tooltip("Run stride length per full cycle (m).")]
        public float strideLength = 2.3f;

        [System.NonSerialized] public SoldierPose pose = SoldierPose.None;
        [System.NonSerialized] public float speed = 3.6f;
        [System.NonSerialized] public float aimPitch;   // degrees, + = up

        public Rig rig;
        float _w, _phase;
        SoldierPose _last = SoldierPose.None;
        Quaternion[] _cur;

        public Vector3 Muzzle => rig != null && rig.rHand != null ? rig.rHand.TransformPoint(muzzleLocal) : transform.position + Vector3.up * 1.4f + transform.forward * 0.7f;
        public float Weight => _w;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            rig = Rig.Capture(animator, transform);
            if (rig != null) _cur = new Quaternion[rig.bones.Length];
            _phase = Random.value * Mathf.PI * 2f;
        }

        void LateUpdate()
        {
            if (rig == null) return;
            float dt = Time.deltaTime;
            float target = pose != SoldierPose.None ? 1f : 0f;
            _w = Mathf.MoveTowards(_w, target, dt * 4f);
            if (pose != SoldierPose.None) _last = pose;
            if (_w <= 0.001f) return;
            _phase += dt * speed / Mathf.Max(0.5f, strideLength) * Mathf.PI * 2f;
            if (_phase > Mathf.PI * 200f) _phase -= Mathf.PI * 200f;
            for (int i = 0; i < rig.bones.Length; i++) _cur[i] = rig.bones[i] != null ? rig.bones[i].localRotation : Quaternion.identity;
            Vector3 hipsCur = rig.hips.localPosition;
            Apply(rig, _last, _phase, transform, rifleAxisLocal, aimPitch);
            if (_w < 0.999f)
            {
                for (int i = 0; i < rig.bones.Length; i++)
                    if (rig.bones[i] != null) rig.bones[i].localRotation = Quaternion.Slerp(_cur[i], rig.bones[i].localRotation, _w);
                rig.hips.localPosition = Vector3.Lerp(hipsCur, rig.hips.localPosition, _w);
            }
        }

        /// <summary>Bones the pose layer drives, with their bind (T-pose) local rotations.</summary>
        public sealed class Rig
        {
            public Transform root, hips, spine, chest, neck, head;
            public Transform lUp, lLow, lFoot, rUp, rLow, rFoot;
            public Transform lArm, lFore, lHand, rArm, rFore, rHand;
            public Transform[] bones;
            public Quaternion[] bind;
            public Vector3 hipsBindLocal;
            public float hipsHeight;
            public Quaternion lFootRel, rFootRel, headRel, lHandRel;
            public int legStart; // bones[legStart..] are legs + hips (skipped by the kneel aim)

            public static Rig Capture(Animator a, Transform root)
            {
                if (a == null || !a.isHuman) return null;
                var r = new Rig { root = root };
                Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
                r.hips = B(HumanBodyBones.Hips); r.spine = B(HumanBodyBones.Spine); r.chest = B(HumanBodyBones.Chest) ?? r.spine;
                r.neck = B(HumanBodyBones.Neck); r.head = B(HumanBodyBones.Head);
                r.lUp = B(HumanBodyBones.LeftUpperLeg); r.lLow = B(HumanBodyBones.LeftLowerLeg); r.lFoot = B(HumanBodyBones.LeftFoot);
                r.rUp = B(HumanBodyBones.RightUpperLeg); r.rLow = B(HumanBodyBones.RightLowerLeg); r.rFoot = B(HumanBodyBones.RightFoot);
                r.lArm = B(HumanBodyBones.LeftUpperArm); r.lFore = B(HumanBodyBones.LeftLowerArm); r.lHand = B(HumanBodyBones.LeftHand);
                r.rArm = B(HumanBodyBones.RightUpperArm); r.rFore = B(HumanBodyBones.RightLowerArm); r.rHand = B(HumanBodyBones.RightHand);
                if (r.hips == null || r.rHand == null || r.lFoot == null) return null;
                // upper body first (kneel aim applies only these), then legs + hips
                r.bones = new[] { r.spine, r.chest, r.neck, r.head, r.lArm, r.lFore, r.lHand, r.rArm, r.rFore, r.rHand, r.hips, r.lUp, r.lLow, r.lFoot, r.rUp, r.rLow, r.rFoot };
                r.legStart = 10;
                r.bind = new Quaternion[r.bones.Length];
                for (int i = 0; i < r.bones.Length; i++) r.bind[i] = r.bones[i] != null ? r.bones[i].localRotation : Quaternion.identity;
                r.hipsBindLocal = r.hips.localPosition;
                var inv = Quaternion.Inverse(root.rotation);
                r.hipsHeight = Vector3.Dot(r.hips.position - root.position, root.up);
                r.lFootRel = inv * r.lFoot.rotation; r.rFootRel = inv * r.rFoot.rotation;
                r.headRel = r.head != null ? inv * r.head.rotation : Quaternion.identity;
                r.lHandRel = inv * r.lHand.rotation;
                return r;
            }

            public void ResetToBind(bool upperOnly)
            {
                int n = upperOnly ? legStart : bones.Length;
                for (int i = 0; i < n; i++) if (bones[i] != null) bones[i].localRotation = bind[i];
                if (!upperOnly) hips.localPosition = hipsBindLocal;
            }
        }

        static void Rotate(Transform t, Vector3 axis, float deg) { if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation; }

        /// <summary>Poses the rig (world-space construction from the bind pose). Pure function of its inputs.</summary>
        public static void Apply(Rig r, SoldierPose p, float phase, Transform root, Vector3 rifleAxisLocal, float aimPitch = 0f)
        {
            if (r == null || p == SoldierPose.None) return;
            bool kneel = p == SoldierPose.KneelAim;
            r.ResetToBind(kneel);
            Vector3 F = root.forward, U = root.up, R = root.right, O = root.position;
            Quaternion rootRot = root.rotation;

            if (!kneel)
            {
                // ---- hips + torso
                float hipH = p == SoldierPose.Run ? r.hipsHeight * 0.9f + 0.035f * Mathf.Cos(2f * phase) : r.hipsHeight * 0.975f;
                Vector3 hp = r.hips.position;
                r.hips.position = hp - U * Vector3.Dot(hp - O, U) + U * hipH + (p == SoldierPose.Run ? F * 0.06f : Vector3.zero);
                if (p == SoldierPose.Run)
                {
                    Rotate(r.spine, R, 11f);
                    Rotate(r.chest, R, 5f);
                    Rotate(r.spine, U, 6f * Mathf.Sin(phase));
                }
                else
                {
                    Rotate(r.spine, U, 18f);
                    Rotate(r.chest, R, 4f);
                }

                // ---- legs: foot targets + two-bone IK, knees forward
                for (int s = 0; s < 2; s++)
                {
                    bool left = s == 0;
                    Transform up = left ? r.lUp : r.rUp, low = left ? r.lLow : r.rLow, foot = left ? r.lFoot : r.rFoot;
                    float side = left ? -1f : 1f;
                    Vector3 target;
                    if (p == SoldierPose.Run)
                    {
                        float ph = phase + (left ? 0f : Mathf.PI);
                        float fx = 0.42f * Mathf.Sin(ph);
                        float c = Mathf.Cos(ph);
                        float lift = c > 0f ? 0.3f * Mathf.Pow(c, 1.3f) : 0f;
                        target = O + R * (side * 0.1f) + F * (fx + 0.06f) + U * (0.1f + lift);
                    }
                    else target = O + R * (side * 0.17f) + F * (left ? 0.24f : -0.16f) + U * 0.1f;
                    Vector3 hint = up.position + F * 0.6f - U * 0.4f;
                    TwoBoneIK.Solve(up, low, foot, target, hint, 1f);
                    float toe = p == SoldierPose.Run ? (left ? 1f : -1f) * 0f : 0f;
                    foot.rotation = rootRot * (left ? r.lFootRel : r.rFootRel) * Quaternion.Euler(toe, 0f, 0f);
                }
            }

            // ---- arms + rifle
            Vector3 D;
            if (p == SoldierPose.Run) D = (F * 0.55f + U * 0.5f - R * 0.4f).normalized;
            else D = Quaternion.AngleAxis(-aimPitch, R) * F;
            Vector3 shoulder = r.rArm.position;
            Vector3 grip = p == SoldierPose.Run
                ? shoulder + F * 0.22f - U * 0.28f - R * 0.1f
                : shoulder + D * 0.16f - U * 0.07f - R * 0.06f;
            TwoBoneIK.Solve(r.rArm, r.rFore, r.rHand, grip, r.rArm.position + R * 0.35f - U * 0.45f, 1f);
            Vector3 axisNow = r.rHand.rotation * rifleAxisLocal;
            r.rHand.rotation = Quaternion.FromToRotation(axisNow, D) * r.rHand.rotation;
            Vector3 support = r.rHand.position + D * (p == SoldierPose.Run ? 0.3f : 0.34f) - U * 0.03f;
            TwoBoneIK.Solve(r.lArm, r.lFore, r.lHand, support, r.lArm.position - U * 0.45f + F * 0.1f - R * 0.1f, 1f);
            r.lHand.rotation = Quaternion.LookRotation(D, U) * Quaternion.Inverse(Quaternion.LookRotation(F, U)) * rootRot * r.lHandRel;

            // ---- head looks along the aim
            if (r.head != null)
                r.head.rotation = Quaternion.AngleAxis(p == SoldierPose.Run ? 4f : -aimPitch * 0.7f, R) * rootRot * r.headRel;
        }
    }
}
