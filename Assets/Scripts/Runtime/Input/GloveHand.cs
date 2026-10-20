using System.Collections.Generic;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// The player's tactical glove: a procedural skinned mesh (palm, cuff with strap, 4 fingers × 3 phalanges,
    /// thumb × 3) in ONE draw call, whose fingers curl with the controller: grip closes middle/ring/little,
    /// trigger closes the index, the thumb follows both; holding an item keeps the hand wrapped around it.
    /// Same frame as the old box glove (x across the palm, fingers along +z, palm facing −y).
    /// Hidden while OVR hand tracking shows the real tracked hand mesh.
    /// w10b: the player's hands (those under a <see cref="HandRig"/>) are the owner's soldier's gloved hands + 14 cm of
    /// camo sleeve (Resources/FPHands/FPHands_L|R.fbx, cut out of Soldier_KZ by tools/blender/fp_hands_extract.py),
    /// aligned to the same frame and driven by the same curls; the procedural glove stays as the fallback
    /// (load failure, <see cref="UseSoldierHands"/> off, the training ghost hand, the old tests).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GloveHand : MonoBehaviour
    {
        [Tooltip("+1 right hand, −1 left hand (mirrors the thumb side).")]
        public float side = 1f;
        public Material material;
        [Range(0f, 1f)] public float gripCurl, indexCurl, thumbCurl;
        [Tooltip("Drive curls from the hand input (off in tests that set curls directly).")]
        public bool driveFromInput = true;

        public SkinnedMeshRenderer Renderer { get; private set; }
        public int BoneCount => _bones.Count;

        /// <summary>Use the soldier's FP hands for the player's hands (false = always the procedural glove).</summary>
        public static bool UseSoldierHands = true;
        /// <summary>Tests: build the soldier hand even without a HandRig parent.</summary>
        [System.NonSerialized] public bool soldierWithoutRig;
        /// <summary>True when this hand shows the soldier mesh (false = procedural glove).</summary>
        public bool IsSoldierHand { get; private set; }

        public const string SoldierResDir = "FPHands/";
        /// <summary>Where the soldier's wrist (Bip01 Hand bone origin) sits in this (glove) frame: the old glove's cuff,
        /// so the palm centre lands on the old palm centre (0, 0, 0.036) and the middle knuckle at z = 0.08.</summary>
        public static Vector3 SoldierWristOffset = new Vector3(0f, 0f, -0.016f);
        /// <summary>Extra rotation of the soldier hand in the glove frame (euler, degrees) if the device shows a tilt.</summary>
        public static Vector3 SoldierRotationOffset = Vector3.zero;
        // glove finger order (index, middle, ring, little, thumb) → Biped finger number
        static readonly string[] BipedFinger = { "Finger1", "Finger2", "Finger3", "Finger4", "Finger0" };
        // soldier: curl angles per phalanx (the rest pose is already slightly bent)
        static readonly float[] SoldierCurl = { 62f, 80f, 52f };
        static readonly float[] SoldierThumb = { 22f, 30f, 38f };
        readonly Vector3[][] _axis = new Vector3[5][]; // soldier: bone-local curl axis (sign included)
        float[] _tipLen;

        HandRig _rig;
        HandGrabber _grabber;
        readonly List<Transform> _bones = new List<Transform>();
        readonly Transform[][] _fingers = new Transform[5][]; // 0..3 index..little, 4 thumb
        readonly Quaternion[][] _rest = new Quaternion[5][];

        // index, middle, ring, little: x offset at the knuckle (right hand), length, radius
        static readonly float[] FingerX = { -0.029f, -0.009f, 0.011f, 0.029f };
        static readonly float[] FingerLen = { 0.074f, 0.082f, 0.076f, 0.06f };
        static readonly float[] FingerR = { 0.0092f, 0.0095f, 0.009f, 0.0082f };
        static readonly float[] Seg = { 0.45f, 0.3f, 0.25f };

        void Awake()
        {
            _rig = GetComponentInParent<HandRig>();
            _grabber = GetComponentInParent<HandGrabber>();
        }

        // built lazily (Start, or earlier by BloodOnHands.Awake) so code that adds the component can set side first
        void Start() { if (Renderer == null) Build(); }

        public void Build()
        {
            if (Renderer != null) return;
            if (_rig == null) _rig = GetComponentInParent<HandRig>(); // Build() may run before Awake (BloodOnHands)
            if (_grabber == null) _grabber = GetComponentInParent<HandGrabber>();
            if (UseSoldierHands && (_rig != null || soldierWithoutRig) && TryBuildSoldier())
            {
                if (_rig != null) AddPatch(0.046f, -0.095f);
                return;
            }
            BuildProcedural();
        }

        void BuildProcedural()
        {
            var parts = new List<(Mesh mesh, Transform bone, Matrix4x4 m)>();
            // palm: rounded slab (sphere + box core kept inside it), knuckle ridge, thumb-base (thenar) pad
            parts.Add((Prim(PrimitiveType.Sphere), transform, Matrix4x4.TRS(new Vector3(0f, 0f, 0.036f), Quaternion.identity, new Vector3(0.094f, 0.034f, 0.112f))));
            parts.Add((Prim(PrimitiveType.Cube), transform, Matrix4x4.TRS(new Vector3(0f, 0f, 0.04f), Quaternion.identity, new Vector3(0.066f, 0.024f, 0.066f))));
            parts.Add((Prim(PrimitiveType.Capsule), transform, Matrix4x4.TRS(new Vector3(0f, 0.002f, 0.074f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.024f, 0.038f, 0.022f))));
            parts.Add((Prim(PrimitiveType.Sphere), transform, Matrix4x4.TRS(new Vector3(-0.026f * side, -0.008f, 0.022f), Quaternion.Euler(0f, 30f * side, 0f), new Vector3(0.034f, 0.026f, 0.05f))));
            // cuff + velcro strap
            parts.Add((Prim(PrimitiveType.Cylinder), transform, Matrix4x4.TRS(new Vector3(0f, 0f, -0.022f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.066f, 0.028f, 0.045f))));
            parts.Add((Prim(PrimitiveType.Cylinder), transform, Matrix4x4.TRS(new Vector3(0f, 0f, -0.01f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.008f, 0.049f))));
            // a hand-span of sleeve so the glove doesn't end in an open disc (the arm itself is not tracked)
            parts.Add((Prim(PrimitiveType.Cylinder), transform, Matrix4x4.TRS(new Vector3(0f, 0.002f, -0.085f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.08f, 0.045f, 0.064f))));
            parts.Add((Prim(PrimitiveType.Sphere), transform, Matrix4x4.TRS(new Vector3(0f, 0.002f, -0.13f), Quaternion.identity, new Vector3(0.08f, 0.064f, 0.03f))));
            _bones.Clear();
            _bones.Add(transform);

            for (int f = 0; f < 4; f++)
            {
                float len = FingerLen[f], r = FingerR[f];
                var chain = new Transform[3];
                Transform parent = transform;
                Vector3 at = new Vector3(FingerX[f] * side, -0.003f, 0.08f - (f == 3 ? 0.008f : 0f));
                Quaternion splay = Quaternion.Euler(10f, (f - 1.5f) * 4f * side, 0f);
                for (int s = 0; s < 3; s++)
                {
                    var b = new GameObject($"F{f}_{s}").transform;
                    b.SetParent(parent, false);
                    b.localPosition = s == 0 ? at : new Vector3(0f, 0f, len * Seg[s - 1]);
                    b.localRotation = s == 0 ? splay : Quaternion.Euler(6f, 0f, 0f);
                    chain[s] = b;
                    _bones.Add(b);
                    float l = len * Seg[s], rr = r * (1f - s * 0.08f);
                    parts.Add((Prim(PrimitiveType.Capsule), b, Matrix4x4.TRS(new Vector3(0f, 0f, l * 0.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(rr * 2f, (l + rr * 1.2f) * 0.5f, rr * 2f))));
                    if (s == 0) // knuckle padding on the back of the glove
                        parts.Add((Prim(PrimitiveType.Cube), b, Matrix4x4.TRS(new Vector3(0f, rr * 0.75f, l * 0.35f), Quaternion.identity, new Vector3(rr * 1.7f, rr * 0.6f, l * 0.55f))));
                    parent = b;
                }
                _fingers[f] = chain;
            }
            {
                var chain = new Transform[3];
                Transform parent = transform;
                float[] tl = { 0.04f, 0.032f, 0.026f };
                for (int s = 0; s < 3; s++)
                {
                    var b = new GameObject($"T_{s}").transform;
                    b.SetParent(parent, false);
                    b.localPosition = s == 0 ? new Vector3(-0.034f * side, -0.008f, 0.012f) : new Vector3(0f, 0f, tl[s - 1]);
                    b.localRotation = s == 0 ? Quaternion.Euler(14f, -42f * side, 30f * side) : Quaternion.Euler(4f, 0f, 0f);
                    chain[s] = b;
                    _bones.Add(b);
                    float r = 0.0115f - s * 0.0012f;
                    parts.Add((Prim(PrimitiveType.Capsule), b, Matrix4x4.TRS(new Vector3(0f, 0f, tl[s] * 0.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(r * 2f, (tl[s] + r * 1.2f) * 0.5f, r * 2f))));
                    parent = b;
                }
                _fingers[4] = chain;
            }
            for (int f = 0; f < 5; f++)
            {
                _rest[f] = new Quaternion[3];
                for (int s = 0; s < 3; s++) _rest[f][s] = _fingers[f][s].localRotation;
            }

            // bake: every part rigidly skinned to its bone
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var tris = new List<int>(); var weights = new List<BoneWeight>();
            var toRoot = transform.worldToLocalMatrix;
            foreach (var (mesh, bone, m) in parts)
            {
                int bi = _bones.IndexOf(bone);
                var M = toRoot * bone.localToWorldMatrix * m;
                var N = M.inverse.transpose;
                int baseV = verts.Count;
                var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(M.MultiplyPoint3x4(mv[i]));
                    norms.Add(N.MultiplyVector(mn[i]).normalized);
                    uvs.Add(mu.Length > i ? mu[i] : Vector2.zero);
                    weights.Add(new BoneWeight { boneIndex0 = bi, weight0 = 1f });
                }
                foreach (var t in mesh.triangles) tris.Add(baseV + t);
            }
            var glove = new Mesh { name = side > 0 ? "Glove_R" : "Glove_L" };
            glove.SetVertices(verts); glove.SetNormals(norms); glove.SetUVs(0, uvs); glove.SetTriangles(tris, 0);
            glove.boneWeights = weights.ToArray();
            var bind = new Matrix4x4[_bones.Count];
            for (int i = 0; i < _bones.Count; i++) bind[i] = _bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
            glove.bindposes = bind;
            glove.RecalculateBounds();

            var go = new GameObject("GloveMesh");
            go.transform.SetParent(transform, false);
            Renderer = go.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = glove;
            Renderer.bones = _bones.ToArray();
            Renderer.rootBone = transform;
            Renderer.updateWhenOffscreen = true;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.sharedMaterial = material != null ? material : FieldMedic.UI.WorldUI.Lit(new Color(0.12f, 0.12f, 0.1f), 0.2f);

            if (_rig == null) return; // the training ghost glove has no sleeve patch
            AddPatch(0.0352f, -0.1f);
        }

        /// <summary>GDD_V2 §4: Kazakhstan flag on the left sleeve, Satbayev military department patch on the right one
        /// (on top of the sleeve = back of the hand side, texture top toward the fingers).</summary>
        void AddPatch(float y, float z)
        {
            bool left = side < 0f;
            var patch = FieldMedic.UI.Insignia.Patch(transform, left ? FieldMedic.UI.Insignia.KzFlag : FieldMedic.UI.Insignia.Satbayev,
                transform.TransformPoint(new Vector3(0f, y, z)), transform.up, transform.forward,
                left ? new Vector2(0.044f, 0.022f) : new Vector2(0.028f, 0.035f));
            _patch = patch.GetComponent<Renderer>();
        }

        // ------------------------------------------------------------------ soldier hands (w10b)
        /// <summary>"Bip01 L Hand" == "Bip01_L_Hand" == "bip01.l.hand" == "Armature:Bip01 L Hand" (ignores case, spaces,
        /// underscores, dots, dashes and a namespace prefix).</summary>
        static string Norm(string n)
        {
            int c = n.LastIndexOf(':');
            if (c >= 0) n = n.Substring(c + 1);
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (char ch in n) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        static Transform FindDeep(Transform t, string key)
        {
            if (Norm(t.name) == key) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), key); if (r != null) return r; }
            return null;
        }

        /// <summary>Bone by (normalised) name: the renderer's bone list first, then the whole instance hierarchy.</summary>
        static Transform FindBone(Transform root, SkinnedMeshRenderer smr, string name)
        {
            string key = Norm(name);
            if (smr != null && smr.bones != null)
                foreach (var b in smr.bones) if (b != null && Norm(b.name) == key) return b;
            return FindDeep(root, key);
        }

        static string Dump(Transform t, int depth = 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(' ', depth * 2).Append(t.name);
            foreach (var c in t.GetComponents<Component>()) if (!(c is Transform)) sb.Append(" [").Append(c.GetType().Name).Append(']');
            sb.Append('\n');
            for (int i = 0; i < t.childCount && depth < 8; i++) sb.Append(Dump(t.GetChild(i), depth + 1));
            return sb.ToString();
        }

        bool TryBuildSoldier()
        {
            string s = side < 0f ? "L" : "R";
            GameObject src = null;
            try { src = Resources.Load<GameObject>(SoldierResDir + "FPHands_" + s); }
            catch (System.Exception e) { Debug.LogWarning("[FMVR] FP hands load failed: " + e.Message); }
            if (src == null) { Debug.LogWarning("[FMVR] FP hands: Resources/" + SoldierResDir + "FPHands_" + s + " missing, procedural glove"); return false; }
            var inst = Instantiate(src, transform, false);
            inst.name = "SoldierHand_" + s;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            string pre = "Bip01 " + s + " ";
            var hand = FindBone(inst.transform, smr, pre + "Hand");
            var chains = new Transform[5][];
            bool ok = smr != null && hand != null;
            for (int f = 0; ok && f < 5; f++)
            {
                chains[f] = new Transform[3];
                for (int k = 0; k < 3; k++)
                {
                    // Bip01 R Finger1, Finger11, Finger12 (thumb: Finger0, Finger01, Finger02)
                    chains[f][k] = FindBone(inst.transform, smr, pre + BipedFinger[f] + (k == 0 ? "" : k.ToString()));
                    if (chains[f][k] == null) ok = false;
                }
            }
            if (!ok)
            {
                Debug.LogWarning("[FMVR] FP hands: " + (smr == null ? "no SkinnedMeshRenderer (model Rig must be Generic, see FPHandsImport)" : "bones not found")
                                 + " in FPHands_" + s + ", procedural glove. Hierarchy:\n" + Dump(inst.transform)
                                 + (smr != null && smr.bones != null ? "smr.bones: " + string.Join(", ", System.Array.ConvertAll(smr.bones, b => b != null ? b.name : "null")) : ""));
                DestroyNow(inst);
                return false;
            }

            // measure the hand frame on the rest pose: forward = wrist → middle knuckle, x = across the knuckles
            // (index → little for the right hand, mirrored for the left), up = back of the hand
            float d = Vector3.Distance(chains[1][0].position, hand.position);
            if (d < 1e-5f) { DestroyNow(inst); return false; }
            if (d < 0.05f || d > 0.2f) inst.transform.localScale *= 0.0956f / d; // wrong unit scale on import
            Vector3 fwd = (chains[1][0].position - hand.position).normalized;
            Vector3 x = Vector3.ProjectOnPlane((chains[3][0].position - chains[0][0].position) * side, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, x);
            Quaternion want = transform.rotation * Quaternion.Euler(SoldierRotationOffset);
            inst.transform.rotation = want * Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * inst.transform.rotation;
            inst.transform.position += transform.TransformPoint(SoldierWristOffset) - hand.position;

            // curl axes (bone-local) with the sign that folds the fingers toward the palm (−up)
            Vector3 X = want * Vector3.right, U = want * Vector3.up;
            Vector3 palmTarget = (chains[3][0].position + chains[1][0].position) * 0.5f - U * 0.025f;
            _tipLen = new float[5];
            for (int f = 0; f < 5; f++)
            {
                _axis[f] = new Vector3[3];
                _rest[f] = new Quaternion[3];
                for (int k = 0; k < 3; k++)
                {
                    var b = chains[f][k];
                    _rest[f][k] = b.localRotation;
                    Vector3 seg = k < 2 ? chains[f][k + 1].position - b.position : b.position - chains[f][1].position;
                    Vector3 axisW, toward;
                    if (f < 4) { axisW = X; toward = -U; }
                    else
                    {
                        // thumb: swing across the palm toward the middle/little knuckles
                        toward = (palmTarget - b.position).normalized;
                        axisW = Vector3.Cross(seg, toward);
                        axisW = axisW.sqrMagnitude < 1e-10f ? X : axisW.normalized;
                    }
                    // sign found empirically on the transform itself (robust to the importer's axis/mirror conventions)
                    Vector3 la = b.InverseTransformDirection(axisW).normalized;
                    Vector3 probeL = b.InverseTransformPoint(b.position + seg);
                    b.localRotation = _rest[f][k] * Quaternion.AngleAxis(20f, la);
                    Vector3 moved = b.TransformPoint(probeL) - b.position;
                    b.localRotation = _rest[f][k];
                    float sign = Vector3.Dot(moved.normalized, toward) >= Vector3.Dot(seg.normalized, toward) ? 1f : -1f;
                    _axis[f][k] = la * sign;
                }
                _tipLen[f] = Vector3.Distance(chains[f][2].position, chains[f][1].position) * 0.85f;
            }

            for (int f = 0; f < 5; f++) _fingers[f] = chains[f];
            _bones.Clear();
            _bones.Add(hand);
            for (int f = 0; f < 5; f++) _bones.AddRange(chains[f]);
            var mat = Resources.Load<Material>(SoldierResDir + "FPHands_Body");
            if (mat != null) smr.sharedMaterial = mat;
            else if (material != null) smr.sharedMaterial = material;
            smr.gameObject.SetActive(true);
            smr.enabled = true;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.skinnedMotionVectors = false;
            Renderer = smr;
            IsSoldierHand = true;
            return true;
        }

        static void DestroyNow(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        Renderer _patch;

        static readonly Dictionary<PrimitiveType, Mesh> _prims = new Dictionary<PrimitiveType, Mesh>();

        static Mesh Prim(PrimitiveType t)
        {
            if (_prims.TryGetValue(t, out var m) && m != null) return m;
            var g = GameObject.CreatePrimitive(t);
            m = g.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
            return _prims[t] = m;
        }

        void LateUpdate()
        {
            if (Renderer == null) return;
            var input = _rig != null ? _rig.Input : null;
            bool tracked = _rig != null && _rig.ActiveMode == HandRig.Mode.OVR && _rig.ovrHand != null && _rig.ovrHand.IsTracked;
            if (Renderer.enabled == tracked) { Renderer.enabled = !tracked; if (_patch != null) _patch.enabled = !tracked; }
            if (driveFromInput && input != null)
            {
                bool holding = _grabber != null && _grabber.Held != null;
                float k = 1f - Mathf.Exp(-Time.deltaTime * 18f);
                float g = Mathf.Max(input.Grip, holding ? 0.65f : 0f);
                float t = Mathf.Max(input.Trigger, holding ? 0.55f : 0f, input.Grip * 0.35f);
                gripCurl = Mathf.Lerp(gripCurl, g, k);
                indexCurl = Mathf.Lerp(indexCurl, t, k);
                thumbCurl = Mathf.Lerp(thumbCurl, Mathf.Max(g, t) * 0.85f, k);
            }
            Pose();
        }

        /// <summary>Apply the curls to the bones (also callable from tests).</summary>
        public void Pose()
        {
            if (IsSoldierHand)
            {
                for (int f = 0; f < 5; f++)
                {
                    float c = f == 4 ? thumbCurl : f == 0 ? indexCurl : gripCurl;
                    if (f == 3) c = Mathf.Min(1f, c * 1.08f);
                    var max = f == 4 ? SoldierThumb : SoldierCurl;
                    for (int k = 0; k < 3; k++)
                        _fingers[f][k].localRotation = _rest[f][k] * Quaternion.AngleAxis(c * max[k], _axis[f][k]);
                }
                return;
            }
            for (int f = 0; f < 4; f++)
            {
                float c = f == 0 ? indexCurl : gripCurl;
                if (f == 3) c = Mathf.Min(1f, c * 1.08f);
                _fingers[f][0].localRotation = _rest[f][0] * Quaternion.Euler(c * 68f, 0f, 0f);
                _fingers[f][1].localRotation = _rest[f][1] * Quaternion.Euler(c * 88f, 0f, 0f);
                _fingers[f][2].localRotation = _rest[f][2] * Quaternion.Euler(c * 58f, 0f, 0f);
            }
            float th = thumbCurl;
            _fingers[4][0].localRotation = _rest[4][0] * Quaternion.Euler(th * 18f, th * 32f * side, 0f);
            _fingers[4][1].localRotation = _rest[4][1] * Quaternion.Euler(th * 35f, 0f, 0f);
            _fingers[4][2].localRotation = _rest[4][2] * Quaternion.Euler(th * 45f, 0f, 0f);
        }

        /// <summary>World position of a fingertip (0..3 fingers, 4 thumb) — for tests and touch effects.</summary>
        public Vector3 Tip(int finger)
        {
            var d = _fingers[finger][2];
            if (IsSoldierHand)
                return d.position + (d.position - _fingers[finger][1].position).normalized * _tipLen[finger];
            float l = finger == 4 ? 0.026f : FingerLen[finger] * Seg[2];
            return d.TransformPoint(new Vector3(0f, 0f, l));
        }
    }
}
