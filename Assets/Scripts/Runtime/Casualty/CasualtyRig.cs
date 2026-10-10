using System.Collections.Generic;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    public enum Limb { LeftArm = 0, RightArm = 1, LeftLeg = 2, RightLeg = 3 }

    /// <summary>
    /// Procedural layer on top of the casualty's Animator: humanoid bone map, body-region trigger colliders
    /// (→ <see cref="BodyRegion"/>), two-bone IK for the four limbs (driven by grabs in S3), breathing
    /// (rate/depth from physiology), pain flinches and head look-at toward the player while conscious.
    /// All procedural work runs in LateUpdate after the Animator, or via <see cref="ApplyProcedural"/> in tools/tests.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CasualtyRig : MonoBehaviour
    {
        public Animator animator;
        public SkinnedMeshRenderer bodyRenderer;
        [Tooltip("Player head; defaults to Camera.main.")]
        public Transform lookTarget;
        [Range(0f, 1f)] public float lookWeight;
        [Tooltip("Breaths per minute (from physiology).")]
        public float breathRate = 15f;
        [Range(0f, 1f)] public float breathDepth = 0.4f;
        [Tooltip("Irregular gasping (class IV shock / pre-arrest / obstructed airway).")]
        public bool agonal;
        public bool buildColliders = true;
        [Tooltip("Lift the animated model so no body part sinks below the floor (root y).")]
        public bool groundClamp = true;
        public int colliderLayer = 0;

        public readonly List<CasualtyBodyPart> Parts = new List<CasualtyBodyPart>();
        readonly Dictionary<HumanBodyBones, Transform> _bones = new Dictionary<HumanBodyBones, Transform>();
        readonly LimbIKState[] _ik = new LimbIKState[4];
        bool _initialized;
        float _breathPhase, _gaspTimer = 4f, _gaspAmp;
        float _flinch, _flinchVel, _flinchTarget;
        float _lookBlend;
        Mesh _bakeMesh;

        struct LimbIKState
        {
            public Vector3 target;
            public Vector3? hint;
            public float weight;
        }

        public bool Initialized => _initialized;
        public float BreathSignal { get; private set; }   // -1..1, chest rise (for chest-rise checks in S3)

        void Awake() => Init();

        public void Init()
        {
            if (_initialized) return;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            _bones.Clear();
            if (animator != null && animator.isHuman)
            {
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) _bones[b] = t;
                }
            }
            Parts.Clear();
            Parts.AddRange(GetComponentsInChildren<CasualtyBodyPart>(true));
            if (buildColliders && _bones.Count > 0 && Parts.Count == 0) BuildColliders();
            _initialized = true;
        }

        public Transform Bone(HumanBodyBones b) => _bones.TryGetValue(b, out var t) ? t : null;

        /// <summary>Chest bone (UpperChest if mapped, else Chest, else Spine).</summary>
        public Transform ChestBone => Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);

        /// <summary>Body right axis from shoulder positions (independent of bone local axes).</summary>
        public Vector3 BodyRight
        {
            get
            {
                var l = Bone(HumanBodyBones.LeftUpperArm); var r = Bone(HumanBodyBones.RightUpperArm);
                if (l == null || r == null) return transform.right;
                return (r.position - l.position).normalized;
            }
        }

        /// <summary>Body up axis (hips → neck).</summary>
        public Vector3 BodyUp
        {
            get
            {
                var h = Bone(HumanBodyBones.Hips); var n = Bone(HumanBodyBones.Neck);
                if (h == null || n == null) return transform.up;
                return (n.position - h.position).normalized;
            }
        }

        /// <summary>Front of the torso.</summary>
        public Vector3 BodyForward => Vector3.Cross(BodyRight, BodyUp).normalized;

        // ------------------------------------------------------------------ colliders
        struct PartDef
        {
            public HumanBodyBones bone, child;
            public BodyRegion region;
            public float radius;
            public float side;       // chest halves / groin / axilla offset along body right
            public float along;      // 0..1 position along bone for spheres
            public bool sphere;
            public PartDef(HumanBodyBones b, HumanBodyBones c, BodyRegion r, float rad, float side = 0f, bool sphere = false, float along = 0.5f)
            { bone = b; child = c; region = r; radius = rad; this.side = side; this.sphere = sphere; this.along = along; }
        }

        static readonly PartDef[] Defs =
        {
            new PartDef(HumanBodyBones.Head, HumanBodyBones.LastBone, BodyRegion.Head, 0.11f, sphere: true),
            new PartDef(HumanBodyBones.Neck, HumanBodyBones.Head, BodyRegion.Neck, 0.06f),
            new PartDef(HumanBodyBones.Chest, HumanBodyBones.Neck, BodyRegion.ChestL, 0.11f, side: -0.08f),
            new PartDef(HumanBodyBones.Chest, HumanBodyBones.Neck, BodyRegion.ChestR, 0.11f, side: 0.08f),
            new PartDef(HumanBodyBones.Spine, HumanBodyBones.Chest, BodyRegion.Abdomen, 0.14f),
            new PartDef(HumanBodyBones.Hips, HumanBodyBones.Spine, BodyRegion.Pelvis, 0.15f),
            new PartDef(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, BodyRegion.GroinL, 0.07f, side: 0.05f, sphere: true, along: 0.08f),
            new PartDef(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, BodyRegion.GroinR, 0.07f, side: -0.05f, sphere: true, along: 0.08f),
            new PartDef(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, BodyRegion.AxillaL, 0.06f, sphere: true, along: 0.1f),
            new PartDef(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, BodyRegion.AxillaR, 0.06f, sphere: true, along: 0.1f),
            new PartDef(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, BodyRegion.UpperArmL, 0.06f),
            new PartDef(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, BodyRegion.UpperArmR, 0.06f),
            new PartDef(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, BodyRegion.ForearmL, 0.05f),
            new PartDef(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, BodyRegion.ForearmR, 0.05f),
            new PartDef(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, BodyRegion.HandL, 0.045f),
            new PartDef(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, BodyRegion.HandR, 0.045f),
            new PartDef(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, BodyRegion.ThighL, 0.085f),
            new PartDef(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, BodyRegion.ThighR, 0.085f),
            new PartDef(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, BodyRegion.ShinL, 0.065f),
            new PartDef(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, BodyRegion.ShinR, 0.065f),
            new PartDef(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, BodyRegion.FootL, 0.055f),
            new PartDef(HumanBodyBones.RightFoot, HumanBodyBones.RightToes, BodyRegion.FootR, 0.055f),
        };

        void BuildColliders()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            Vector3 right = BodyRight;
            foreach (var d in Defs)
            {
                var bone = Bone(d.bone);
                if (bone == null && d.bone == HumanBodyBones.Chest) bone = Bone(HumanBodyBones.Spine);
                if (bone == null) continue;
                var child = d.child != HumanBodyBones.LastBone ? Bone(d.child) : null;
                Vector3 a = bone.position;
                Vector3 b = child != null ? child.position : a + BodyUp * 0.18f;
                var go = new GameObject("Part_" + d.region);
                go.layer = colliderLayer;
                go.transform.SetParent(bone, false);
                Vector3 centerW = Vector3.Lerp(a, b, d.sphere ? d.along : 0.5f) + right * d.side;
                if (d.region == BodyRegion.Head) centerW = a + BodyUp * 0.09f;
                Collider col;
                if (d.sphere)
                {
                    var s = go.AddComponent<SphereCollider>();
                    s.center = bone.InverseTransformPoint(centerW);
                    s.radius = d.radius / Mathf.Max(1e-4f, bone.lossyScale.x);
                    col = s;
                }
                else
                {
                    var c = go.AddComponent<CapsuleCollider>();
                    Vector3 axisLocal = bone.InverseTransformDirection(b - a);
                    c.direction = MaxAxis(axisLocal);
                    float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
                    c.center = bone.InverseTransformPoint(centerW);
                    c.radius = d.radius / scale;
                    c.height = ((b - a).magnitude + d.radius * 2f) / scale;
                    col = c;
                }
                col.isTrigger = true;
                var part = go.AddComponent<CasualtyBodyPart>();
                part.region = d.region;
                part.bone = bone;
                part.rig = this;
                Parts.Add(part);
            }
        }

        static int MaxAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return v.x > v.y && v.x > v.z ? 0 : v.y > v.z ? 1 : 2;
        }

        public CasualtyBodyPart Part(BodyRegion r)
        {
            foreach (var p in Parts) if (p.region == r) return p;
            return null;
        }

        // ------------------------------------------------------------------ surface sampling (wound placement)
        /// <summary>
        /// Finds the skin/clothing surface point of the current (skinned) pose: among mesh vertices close to
        /// the segment bone→child at `along`, takes the one furthest in direction `dirWorld`.
        /// </summary>
        public bool SurfacePoint(HumanBodyBones bone, HumanBodyBones child, float along, Vector3 dirWorld,
            out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            var bt = Bone(bone); var ct = Bone(child);
            if (bt == null || bodyRenderer == null) return false;
            Vector3 a = bt.position, b = ct != null ? ct.position : a + BodyUp * 0.15f;
            Vector3 c = Vector3.Lerp(a, b, along);
            Vector3 axis = (b - a).normalized;
            dirWorld = Vector3.ProjectOnPlane(dirWorld, axis).normalized;
            if (_bakeMesh == null) _bakeMesh = new Mesh();
            bodyRenderer.BakeMesh(_bakeMesh, true);
            var verts = _bakeMesh.vertices; var norms = _bakeMesh.normals;
            // only vertices skinned mostly to this bone (or its child): never the other leg / a pouch
            var allowed = new HashSet<int>();
            var smrBones = bodyRenderer.bones;
            for (int i = 0; i < smrBones.Length; i++)
                if (smrBones[i] == bt || (ct != null && smrBones[i] == ct)) allowed.Add(i);
            BoneWeight[] weights = null;
            var shared = bodyRenderer.sharedMesh;
            if (shared != null && shared.isReadable) weights = shared.boneWeights;
            if (weights != null && weights.Length != verts.Length) weights = null;
            var m = bodyRenderer.transform.localToWorldMatrix;
            float best = float.MinValue; int bi = -1;
            float slab = Mathf.Max(0.03f, (b - a).magnitude * 0.08f);
            for (int i = 0; i < verts.Length; i++)
            {
                // rigidly skinned to this bone only: the point then stays on the surface in every pose
                if (weights != null && (!allowed.Contains(weights[i].boneIndex0) || weights[i].weight0 < 0.8f)) continue;
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                Vector3 rel = w - c;
                float ax = Vector3.Dot(rel, axis);
                if (Mathf.Abs(ax) > slab) continue;
                Vector3 radial = rel - axis * ax;
                float t = Vector3.Dot(radial, dirWorld);
                if (t <= 0f || radial.magnitude > 0.2f) continue;
                // outermost vertex inside a narrow cylinder around the ray (the model has inner layers)
                float perp = (radial - dirWorld * t).magnitude;
                if (perp > 0.035f) continue;
                if (t > best) { best = t; bi = i; }
            }
            if (bi < 0) return false;
            point = m.MultiplyPoint3x4(verts[bi]);
            normal = m.MultiplyVector(norms[bi]).normalized;
            if (Vector3.Dot(normal, dirWorld) < 0.2f) normal = dirWorld;
            return true;
        }

        // ------------------------------------------------------------------ IK API (used by grabs in S3)
        public void SetLimbIK(Limb limb, Vector3 target, float weight, Vector3? hint = null)
        {
            _ik[(int)limb] = new LimbIKState { target = target, hint = hint, weight = Mathf.Clamp01(weight) };
        }

        public void ClearLimbIK(Limb limb) => _ik[(int)limb].weight = 0f;

        public void LimbChain(Limb limb, out Transform root, out Transform mid, out Transform tip)
        {
            switch (limb)
            {
                case Limb.LeftArm: root = Bone(HumanBodyBones.LeftUpperArm); mid = Bone(HumanBodyBones.LeftLowerArm); tip = Bone(HumanBodyBones.LeftHand); break;
                case Limb.RightArm: root = Bone(HumanBodyBones.RightUpperArm); mid = Bone(HumanBodyBones.RightLowerArm); tip = Bone(HumanBodyBones.RightHand); break;
                case Limb.LeftLeg: root = Bone(HumanBodyBones.LeftUpperLeg); mid = Bone(HumanBodyBones.LeftLowerLeg); tip = Bone(HumanBodyBones.LeftFoot); break;
                default: root = Bone(HumanBodyBones.RightUpperLeg); mid = Bone(HumanBodyBones.RightLowerLeg); tip = Bone(HumanBodyBones.RightFoot); break;
            }
        }

        // ------------------------------------------------------------------ pain flinch
        /// <summary>Short involuntary flinch (curl, head jerk). strength 0..1.</summary>
        public void Flinch(float strength) => _flinchVel += Mathf.Clamp01(strength) * 9f;

        float _nod, _nodVel;
        /// <summary>Head nods from a jolt (drag): degrees of impulse, springs back.</summary>
        public void HeadJolt(float degrees) => _nodVel += degrees * 6f;

        void ApplyNod(float dt)
        {
            _nodVel += (-80f * _nod - 9f * _nodVel) * dt;
            _nod += _nodVel * dt;
            if (Mathf.Abs(_nod) < 0.01f && Mathf.Abs(_nodVel) < 0.05f) return;
            var head = Bone(HumanBodyBones.Head);
            if (head != null) head.rotation = Quaternion.AngleAxis(Mathf.Clamp(_nod, -20f, 20f), BodyRight) * head.rotation;
        }

        // ------------------------------------------------------------------ per-frame
        void LateUpdate() => ApplyProcedural(Time.deltaTime);

        public void ApplyProcedural(float dt)
        {
            if (!_initialized || dt <= 0f) return;
            if (groundClamp) GroundClamp(dt);
            Breathe(dt);
            ApplyFlinch(dt);
            ApplyNod(dt);
            LookAt(dt);
            for (int i = 0; i < 4; i++)
            {
                if (_ik[i].weight <= 0f) continue;
                LimbChain((Limb)i, out var r, out var m, out var t);
                TwoBoneIK.Solve(r, m, t, _ik[i].target, _ik[i].hint, _ik[i].weight);
            }
        }

        // approximate flesh/gear thickness around each bone (m)
        static readonly (HumanBodyBones bone, float r)[] GroundProbes =
        {
            (HumanBodyBones.Hips, 0.13f), (HumanBodyBones.Spine, 0.13f), (HumanBodyBones.Chest, 0.15f),
            (HumanBodyBones.UpperChest, 0.15f), (HumanBodyBones.Head, 0.12f), (HumanBodyBones.Neck, 0.07f),
            (HumanBodyBones.LeftUpperLeg, 0.09f), (HumanBodyBones.RightUpperLeg, 0.09f),
            (HumanBodyBones.LeftLowerLeg, 0.07f), (HumanBodyBones.RightLowerLeg, 0.07f),
            (HumanBodyBones.LeftFoot, 0.06f), (HumanBodyBones.RightFoot, 0.06f),
            (HumanBodyBones.LeftUpperArm, 0.06f), (HumanBodyBones.RightUpperArm, 0.06f),
            (HumanBodyBones.LeftLowerArm, 0.05f), (HumanBodyBones.RightLowerArm, 0.05f),
            (HumanBodyBones.LeftHand, 0.04f), (HumanBodyBones.RightHand, 0.04f),
        };
        float _lift;

        /// <summary>Animator writes the pose relative to the model root; lift the model if any part is under the floor.</summary>
        void GroundClamp(float dt)
        {
            if (animator == null || animator.transform == transform) return;
            var model = animator.transform;
            float floor = transform.position.y;
            float currentLift = model.position.y - floor;
            float need = 0f;
            foreach (var (b, r) in GroundProbes)
            {
                var t = Bone(b);
                if (t == null) continue;
                // bone height as if the model were not lifted
                float y = t.position.y - currentLift - floor;
                need = Mathf.Max(need, r - y);
            }
            need = Mathf.Clamp(need, 0f, 0.35f);
            _lift = Mathf.MoveTowards(_lift, need, dt * 0.6f + Mathf.Abs(need - _lift) * dt * 6f);
            model.position = new Vector3(model.position.x, floor + _lift, model.position.z);
        }

        void Breathe(float dt)
        {
            float rate = Mathf.Max(0f, breathRate);
            if (rate <= 0.01f) { BreathSignal = 0f; return; }
            _breathPhase += dt * rate / 60f * Mathf.PI * 2f;
            // inhale shorter than exhale
            float s = Mathf.Sin(_breathPhase);
            float sig = s > 0 ? s : s * 0.8f;
            float amp = Mathf.Lerp(0.6f, 3.2f, breathDepth);
            if (agonal)
            {
                _gaspTimer -= dt;
                if (_gaspTimer <= 0f) { _gaspAmp = 1f; _gaspTimer = Random.Range(5f, 10f); }
                _gaspAmp = Mathf.MoveTowards(_gaspAmp, 0f, dt * 0.8f);
                sig = Mathf.Sin(Mathf.Clamp01(_gaspAmp) * Mathf.PI) * 1.6f;
                amp = 4f;
            }
            BreathSignal = sig;
            var chest = ChestBone;
            if (chest == null) return;
            Vector3 right = BodyRight;
            // chest rises: extend the thoracic spine a little, lift the clavicles
            chest.rotation = Quaternion.AngleAxis(-sig * amp, right) * chest.rotation;
            var spine = Bone(HumanBodyBones.Spine);
            if (spine != null) spine.rotation = Quaternion.AngleAxis(-sig * amp * 0.35f, right) * spine.rotation;
            Vector3 fwd = BodyForward;
            var ls = Bone(HumanBodyBones.LeftShoulder); var rs = Bone(HumanBodyBones.RightShoulder);
            if (ls != null) ls.rotation = Quaternion.AngleAxis(sig * amp * 0.5f, fwd) * ls.rotation;
            if (rs != null) rs.rotation = Quaternion.AngleAxis(-sig * amp * 0.5f, fwd) * rs.rotation;
        }

        void ApplyFlinch(float dt)
        {
            // critically-damped-ish spring toward 0
            _flinchVel += (-60f * _flinch - 10f * _flinchVel) * dt;
            _flinch += _flinchVel * dt;
            if (Mathf.Abs(_flinch) < 1e-4f && Mathf.Abs(_flinchVel) < 1e-3f) return;
            float f = Mathf.Clamp(_flinch, -1.5f, 1.5f);
            Vector3 right = BodyRight;
            var spine = Bone(HumanBodyBones.Spine);
            if (spine != null) spine.rotation = Quaternion.AngleAxis(f * 10f, right) * spine.rotation;
            var chest = ChestBone;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(f * 8f, right) * chest.rotation;
            var head = Bone(HumanBodyBones.Head);
            if (head != null) head.rotation = Quaternion.AngleAxis(f * 14f, right) * head.rotation;
            foreach (var hb in new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
            {
                var t = Bone(hb);
                if (t != null) t.rotation = Quaternion.AngleAxis(f * 12f, right) * t.rotation;
            }
        }

        void LookAt(float dt)
        {
            _lookBlend = Mathf.MoveTowards(_lookBlend, lookWeight, dt * 1.5f);
            if (_lookBlend <= 0.001f) return;
            if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
            if (lookTarget == null) return;
            var head = Bone(HumanBodyBones.Head);
            var neck = Bone(HumanBodyBones.Neck);
            if (head == null) return;
            // head "forward" = body forward carried by the animated head (independent of bone axes)
            Vector3 headFwd = head.rotation * Quaternion.Inverse(ChestBone != null ? ChestBone.rotation : transform.rotation) * BodyForward;
            Vector3 eye = head.position + BodyUp * 0.07f;
            Vector3 to = (lookTarget.position - eye).normalized;
            float ang = Vector3.Angle(headFwd, to);
            if (ang < 0.5f) return;
            float limited = Mathf.Min(ang, 65f) * _lookBlend;
            var full = Quaternion.FromToRotation(headFwd, to);
            full.ToAngleAxis(out float a, out Vector3 axis);
            if (neck != null) neck.rotation = Quaternion.AngleAxis(limited * 0.4f, axis) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(limited * 0.6f, axis) * head.rotation;
        }

        void OnDestroy()
        {
            if (_bakeMesh == null) return;
            if (Application.isPlaying) Destroy(_bakeMesh); else DestroyImmediate(_bakeMesh);
        }
    }
}
