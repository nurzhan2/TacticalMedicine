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

            // GDD_V2 §4: Kazakhstan flag on the left sleeve, Satbayev military department patch on the right one
            // (on top of the sleeve = back of the hand side, texture top toward the fingers)
            if (_rig == null) return; // the training ghost glove has no sleeve patch
            bool left = side < 0f;
            var patch = FieldMedic.UI.Insignia.Patch(transform, left ? FieldMedic.UI.Insignia.KzFlag : FieldMedic.UI.Insignia.Satbayev,
                transform.TransformPoint(new Vector3(0f, 0.0352f, -0.1f)), transform.up, transform.forward,
                left ? new Vector2(0.044f, 0.022f) : new Vector2(0.028f, 0.035f));
            _patch = patch.GetComponent<Renderer>();
        }

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
            float l = finger == 4 ? 0.026f : FingerLen[finger] * Seg[2];
            return d.TransformPoint(new Vector3(0f, 0f, l));
        }
    }
}
