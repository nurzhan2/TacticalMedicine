using System.Collections.Generic;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Erlan's face (w8 "living Erlan"). The character model wears a closed balaclava with night-vision goggles; here
    /// the NVG is flipped up onto the helmet (vertices of a runtime mesh copy rotated about the helmet mount) and the
    /// opening of the balaclava is filled with a sculpted skin face built at runtime in the head bone's bind space:
    /// eyes that look at the medic, upper/lower eyelids (open / drowsy / closed by AVPU, blinks, squeeze on pain),
    /// lips, and a fabric rim. Skin and lips are tinted by the physiology (pale → grey, lips blue, sweat sheen)
    /// through MaterialPropertyBlocks at ≤ 10 Hz. Also the nail beds of his fingers (fingerless gloves) for the
    /// capillary refill test: pressed → white, released → colour returns in N s by perfusion.
    /// </summary>
    [DefaultExecutionOrder(110)] // after CasualtyRig (100): eyes follow the head after the look-at
    public sealed class CasualtyFace : MonoBehaviour
    {
        public CasualtyController casualty;
        public bool flipNvgUp = true;
        [Tooltip("Diagnostics: build the skin face (off = only the mesh edit).")]
        public bool buildSkin = true;
        [Tooltip("Physiology-driven material updates per second (Quest budget: ≤ 10).")]
        public float tintRateHz = 10f;
        public Color skin = new Color(0.80f, 0.61f, 0.49f);
        public Color lips = new Color(0.66f, 0.36f, 0.34f);
        public Color palePallid = new Color(0.74f, 0.66f, 0.6f);
        public Color greyShock = new Color(0.55f, 0.55f, 0.53f);
        public Color cyanoticLips = new Color(0.42f, 0.38f, 0.58f);
        public Color nailPink = new Color(0.86f, 0.58f, 0.56f);
        public Color nailBlanched = new Color(0.95f, 0.92f, 0.88f);

        public bool Built { get; private set; }
        /// <summary>0 closed … 1 wide open (current, animated).</summary>
        public float EyesOpen { get; private set; }
        public float EyesOpenTarget { get; private set; }
        public bool LookingAtPlayer { get; private set; }
        public Transform LeftEye => _eyes[0];
        public Transform RightEye => _eyes[1];
        public Renderer FaceRenderer => _faceR;
        public Color CurrentSkin { get; private set; }
        public Color CurrentLips { get; private set; }
        public float CurrentSmoothness { get; private set; }
        public int TintUpdates { get; private set; }
        /// <summary>Nail beds (fingerless glove tips), index/middle/ring of both hands.</summary>
        public readonly List<NailBed> Nails = new List<NailBed>();
        /// <summary>Mouth point (head bind space → world) for the breathing check.</summary>
        public Vector3 MouthPosition => _head != null ? _head.TransformPoint(_mouthLocal) : transform.position;
        public Vector3 FaceForward => _head != null ? _head.TransformDirection(_fwdL) : transform.forward;

        Transform _head;
        Vector3 _cL, _rightL, _upL, _fwdL, _mouthLocal;
        readonly Transform[] _eyes = new Transform[2];
        readonly Transform[] _upperLids = new Transform[2];
        readonly Transform[] _lowerLids = new Transform[2];
        readonly Quaternion[] _eyeRest = new Quaternion[2];
        readonly Renderer[] _lidR = new Renderer[4];
        Renderer _faceR, _rimR, _lipR;
        MaterialPropertyBlock _mpb;
        float _tintTimer, _blinkTimer = 3f, _blink, _grimace, _openBoost, _openBoostLevel, _previewPallor = -1f, _previewCyan, _previewOpen = -1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothId = Shader.PropertyToID("_Smoothness");

        public sealed class NailBed
        {
            public Transform transform;
            public Renderer renderer;
            public Renderer tipRenderer;
            public string name;
            public float blanch;          // 0 pink … 1 white
            public float pressedFor;
            public bool pressed;
            public float releasedAt = -1f;
            public float refillSeconds;   // measured at release
            public Vector3 Position => transform.position;
        }

        void Start() { if (!Built) Build(); }

        // ------------------------------------------------------------------ build
        public void Build()
        {
            if (Built) return;
            if (casualty == null) casualty = GetComponent<CasualtyController>();
            var rig = casualty != null ? casualty.rig : GetComponent<CasualtyRig>();
            if (rig == null) return;
            rig.Init();
            _head = rig.Bone(HumanBodyBones.Head);
            var smr = rig.bodyRenderer;
            if (_head == null || smr == null || smr.sharedMesh == null) return;
            Built = true;
            _mpb = new MaterialPropertyBlock();

            // head-bind frame: mesh-space character axes carried into head-local space by the bind pose
            var mesh = smr.sharedMesh;
            int hi = System.Array.IndexOf(smr.bones, _head);
            Matrix4x4 bind = hi >= 0 && hi < mesh.bindposes.Length ? mesh.bindposes[hi] : (_head.worldToLocalMatrix * smr.transform.localToWorldMatrix);
            Vector3 meshFwd = smr.transform.InverseTransformDirection(casualty != null ? casualty.transform.forward : transform.forward);
            Vector3 meshUp = smr.transform.InverseTransformDirection(casualty != null ? casualty.transform.up : transform.up);
            _fwdL = bind.MultiplyVector(meshFwd).normalized;
            _upL = bind.MultiplyVector(meshUp).normalized;
            _rightL = Vector3.Cross(_upL, _fwdL).normalized;
            _cL = _upL * 0.06f;

            // NVG flipped up + the balaclava's front profile (no poke-through)
            var profile = new float[GX, GY];
            for (int i = 0; i < GX; i++) for (int j = 0; j < GY; j++) profile[i, j] = float.NegativeInfinity;
            if (mesh.isReadable) PrepareMesh(smr, mesh, bind, hi, profile);
            else SmoothProfile(profile);

            if (!buildSkin) return;
            BuildFace(profile);
            BuildEyes(profile);
            BuildNails(rig);
            EyesOpen = EyesOpenTarget = 1f;
            ApplyTint(true);
        }

        // grid over the face (head frame, metres): x −0.105…0.105, y −0.12…0.10 at 5 mm
        const float GX0 = -0.105f, GY0 = -0.12f, GS = 0.005f;
        const int GX = 43, GY = 45;
        static Vector2 Cell(int i, int j) => new Vector2(GX0 + i * GS, GY0 + j * GS);

        Vector3 ToFrame(Vector3 headLocal) { Vector3 d = headLocal - _cL; return new Vector3(Vector3.Dot(d, _rightL), Vector3.Dot(d, _upL), Vector3.Dot(d, _fwdL)); }
        Vector3 FromFrame(float x, float y, float z) => _cL + _rightL * x + _upL * y + _fwdL * z;

        void PrepareMesh(SkinnedMeshRenderer smr, Mesh src, Matrix4x4 bind, int headIndex, float[,] profile)
        {
            var verts = src.vertices;
            var weights = src.boneWeights;
            int n = verts.Length;
            var local = new Vector3[n];
            var headW = new bool[n];
            for (int i = 0; i < n; i++)
            {
                headW[i] = weights.Length == n && weights[i].boneIndex0 == headIndex && weights[i].weight0 > 0.6f;
                local[i] = ToFrame(bind.MultiplyPoint3x4(verts[i]));
            }
            // goggle verts: sub-mesh 1 triangles fully inside the eye band, in front of the face
            var nvg = new HashSet<int>();
            if (src.subMeshCount > 1)
            {
                var t = src.GetTriangles(1);
                for (int k = 0; k < t.Length; k += 3)
                {
                    bool inBox = true;
                    for (int q = 0; q < 3 && inBox; q++)
                    {
                        var p = local[t[k + q]];
                        if (!headW[t[k + q]] || Mathf.Abs(p.x) > 0.105f || p.y < -0.005f || p.y > 0.095f || p.z < 0.10f) inBox = false;
                    }
                    if (inBox) { nvg.Add(t[k]); nvg.Add(t[k + 1]); nvg.Add(t[k + 2]); }
                }
            }
            // front profile of everything else on the head (balaclava, helmet brim), max depth per cell
            for (int i = 0; i < n; i++)
            {
                if (!headW[i] || nvg.Contains(i)) continue;
                var p = local[i];
                if (p.z < 0.02f) continue;
                int gx = Mathf.RoundToInt((p.x - GX0) / GS), gy = Mathf.RoundToInt((p.y - GY0) / GS);
                if (gx < 0 || gx >= GX || gy < 0 || gy >= GY) continue;
                if (p.z > profile[gx, gy]) profile[gx, gy] = p.z;
            }
            SmoothProfile(profile);
            if (!flipNvgUp) return;
            // the NVG goes (pushed off by the medic), and the balaclava's face patch inside the opening goes too:
            // the sculpted face below takes its place, its edge blended into the remaining mask under a fabric rim
            var copy = Instantiate(src);
            copy.name = src.name + "_Face";
            for (int sm = 0; sm < copy.subMeshCount; sm++)
            {
                var t = copy.GetTriangles(sm);
                var keep = new List<int>(t.Length);
                for (int k = 0; k < t.Length; k += 3)
                {
                    int a = t[k], b = t[k + 1], c = t[k + 2];
                    bool drop = nvg.Contains(a) && nvg.Contains(b) && nvg.Contains(c);
                    if (!drop && headW[a] && headW[b] && headW[c])
                    {
                        // only triangles completely inside the opening AND close to / in front of the new face surface:
                        // the receding sides of the mask (seen from the side) stay
                        drop = Oval(local[a].x, local[a].y) < 1.04f && Oval(local[b].x, local[b].y) < 1.04f && Oval(local[c].x, local[c].y) < 1.04f
                            && (InFace(local[a]) || InFace(local[b]) || InFace(local[c]));
                    }
                    if (!drop) { keep.Add(a); keep.Add(b); keep.Add(c); }
                }
                copy.SetTriangles(keep, sm, false);
            }
            smr.sharedMesh = copy;
        }

        bool InFace(Vector3 p) => p.z > Surface(p.x, p.y) - 0.005f;

        // ------------------------------------------------------------------ face sculpt
        static float G(float x, float y, float cx, float cy, float sx, float sy)
        {
            float dx = (x - cx) / sx, dy = (y - cy) / sy;
            return Mathf.Exp(-(dx * dx + dy * dy));
        }

        /// Face opening of the balaclava (oval): brow to chin, cheek to cheek.
        const float OvalA = 0.066f, OvalB = 0.09f, OvalY = -0.006f;
        static float OvalHalfWidth(float y) => OvalA * Mathf.Lerp(1f, 0.62f, Mathf.Clamp01((-0.025f - y) / 0.07f));
        static float Oval(float x, float y) { float dx = x / OvalHalfWidth(y), dy = (y - OvalY) / OvalB; return dx * dx + dy * dy; }

        static readonly Vector2 EyeL = new Vector2(-0.031f, 0.031f), EyeR = new Vector2(0.031f, 0.031f);
        const float EyeRadius = 0.0118f;

        /// Rounded front of a face (no features).
        public static float AnalyticBase(float x, float y)
        {
            float yy = Mathf.Clamp(y, -0.115f, 0.1f);
            float zc = 0.148f + 0.004f * G(0, yy, 0, 0.045f, 1, 0.03f) - 0.03f * Mathf.Max(0f, (-yy - 0.075f) / 0.04f) - 0.02f * Mathf.Max(0f, (yy - 0.075f) / 0.03f);
            float a = 0.105f - 0.02f * Mathf.Max(0f, (-yy - 0.04f) / 0.08f);        // narrower jaw
            float k = Mathf.Clamp01(x * x / (a * a));
            return zc * Mathf.Sqrt(1f - 0.92f * k);
        }

        /// Nose, brows, sockets, cheekbones, lips, chin (offsets on top of the base surface).
        public static float Features(float x, float y)
        {
            float z = 0f;
            float ridge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.036f, -0.006f, y)) * (y > -0.014f ? 1f : Mathf.Exp(-(y + 0.014f) * (y + 0.014f) / 0.00002f));
            z += (0.004f + 0.02f * ridge) * Mathf.Exp(-x * x / (0.0085f * 0.0085f) * (1f - 0.5f * ridge)) * (y < 0.04f ? 1f : Mathf.Exp(-(y - 0.04f) * (y - 0.04f) / 0.0001f)); // nose
            z += 0.007f * (G(x, y, -0.013f, -0.011f, 0.007f, 0.006f) + G(x, y, 0.013f, -0.011f, 0.007f, 0.006f));   // alae
            z += 0.006f * (G(x, y, -0.03f, 0.047f, 0.022f, 0.007f) + G(x, y, 0.03f, 0.047f, 0.022f, 0.007f));       // brows
            z -= 0.013f * (G(x, y, EyeL.x, EyeL.y, 0.0185f, 0.0098f) + G(x, y, EyeR.x, EyeR.y, 0.0185f, 0.0098f)); // sockets
            z += 0.005f * (G(x, y, -0.047f, 0.004f, 0.018f, 0.014f) + G(x, y, 0.047f, 0.004f, 0.018f, 0.014f));    // cheekbones
            z += 0.0035f * G(x, y, 0f, -0.033f, 0.019f, 0.0045f);                                                     // upper lip
            z += 0.0045f * G(x, y, 0f, -0.047f, 0.018f, 0.0055f);                                                     // lower lip
            z -= 0.003f * G(x, y, 0f, -0.0405f, 0.021f, 0.0014f);                                                     // mouth line
            z += 0.006f * G(x, y, 0f, -0.085f, 0.018f, 0.014f);                                                       // chin
            z -= 0.0025f * G(x, y, 0f, -0.022f, 0.004f, 0.006f);                                                      // philtrum
            return z;
        }

        /// Analytic face only (no mask), e.g. for tests and the preview.
        public static float Sculpt(float x, float y) => AnalyticBase(x, y) + Features(x, y);

        float[,] _prof;   // smoothed front surface of the remaining mask (head frame), −∞ where none

        /// Mask surface at a point (bilinear over the smoothed grid), NaN where there is none / in the goggle band.
        float ProfAt(float x, float y)
        {
            if (_prof == null || InNvgBox(x, y)) return float.NaN;
            float fx = (x - GX0) / GS, fy = (y - GY0) / GS;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            if (ix < 0 || iy < 0 || ix >= GX - 1 || iy >= GY - 1) return float.NaN;
            float tx = fx - ix, ty = fy - iy;
            float a = _prof[ix, iy], b = _prof[ix + 1, iy], c = _prof[ix, iy + 1], d = _prof[ix + 1, iy + 1];
            if (float.IsNegativeInfinity(a) || float.IsNegativeInfinity(b) || float.IsNegativeInfinity(c) || float.IsNegativeInfinity(d))
            {
                float m = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
                return float.IsNegativeInfinity(m) ? float.NaN : m;
            }
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// The skin surface: the analytic face, never in front of the old mask (keeps its narrow V-chin and jaw),
        /// plus the features.
        public float Surface(float x, float y)
        {
            float b = AnalyticBase(x, y);
            float p = ProfAt(x, y);
            if (!float.IsNaN(p)) b = Mathf.Min(b, p - 0.002f);
            else if (InNvgBox(x, y))
            {
                // the old goggle band has no mask behind: continue the shape of the row just below it, fading upward
                float below = ProfAt(x, -0.0135f);
                float w = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-0.012f, 0.012f, y));
                if (!float.IsNaN(below)) b = Mathf.Lerp(b, Mathf.Min(b, below - 0.002f + (AnalyticBase(x, y) - AnalyticBase(x, -0.0135f))), w);
            }
            return b + Features(x, y);
        }

        /// Goggle band left open by the removed NVG (filled with balaclava fabric outside the face oval).
        static bool InNvgBox(float x, float y) => Mathf.Abs(x) <= 0.108f && y >= -0.012f && y <= 0.1f;

        void SmoothProfile(float[,] raw)
        {
            if (_prof != null) return;
            _prof = new float[GX, GY];
            for (int i = 0; i < GX; i++) for (int j = 0; j < GY; j++) _prof[i, j] = MaxAround(raw, i, j);
            var tmp = new float[GX, GY];                     // second pass: the decimated mask is lumpy
            for (int i = 0; i < GX; i++) for (int j = 0; j < GY; j++) tmp[i, j] = MaxAround(_prof, i, j);
            _prof = tmp;
        }

        void BuildFace(float[,] profile)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            var idx = new int[GX, GY];
            var oval = new float[GX, GY];
            for (int i = 0; i < GX; i++)
                for (int j = 0; j < GY; j++)
                {
                    idx[i, j] = -1;
                    var c = Cell(i, j);
                    float o = Oval(c.x, c.y);
                    oval[i, j] = o;
                    if (o > 1.0f && !InNvgBox(c.x, c.y)) continue;
                    float z = Surface(c.x, c.y);
                    // the old goggle band outside the face: balaclava fabric over the forehead/temples
                    if (o > 1f) z += 0.003f * Mathf.Clamp01((o - 1f) * 4f);
                    idx[i, j] = verts.Count;
                    verts.Add(FromFrame(c.x, c.y, z));
                    uvs.Add(new Vector2((c.x - GX0) / (GX * GS), (c.y - GY0) / (GY * GS)));
                }
            var skinT = new List<int>(); var fabricT = new List<int>();
            for (int i = 0; i < GX - 1; i++)
                for (int j = 0; j < GY - 1; j++)
                {
                    int a = idx[i, j], b = idx[i + 1, j], c = idx[i, j + 1], d = idx[i + 1, j + 1];
                    if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                    float o = (oval[i, j] + oval[i + 1, j] + oval[i, j + 1] + oval[i + 1, j + 1]) * 0.25f;
                    var list = o < 1.0f ? skinT : fabricT;
                    // winding: front faces toward +fwd (his right is the viewer's left)
                    list.Add(a); list.Add(b); list.Add(c);
                    list.Add(b); list.Add(d); list.Add(c);
                }
            _mouthLocal = FromFrame(0f, -0.04f, Surface(0f, -0.04f) + 0.004f);
            var m = new Mesh { name = "ErlanFace" };
            m.SetVertices(verts); m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(skinT, 0); m.SetTriangles(fabricT, 1);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            var go = new GameObject("ErlanFace");
            go.transform.SetParent(_head, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            _faceR = go.AddComponent<MeshRenderer>();
            _faceR.sharedMaterials = new[] { FaceSkinMat, FabricMat };
            _faceR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // lips: a smooth overlay following the sculpt (upper lip with a cupid's bow, fuller lower lip)
            var lv = new List<Vector3>(); var lt = new List<int>(); var luv = new List<Vector2>();
            const int LU = 28, LR = 5;
            for (int half = 0; half < 2; half++)
            {
                int b0 = lv.Count;
                for (int u = 0; u <= LU; u++)
                {
                    float uu = u / (float)LU * 2f - 1f, x = uu * 0.0235f, w = 1f - uu * uu;
                    float mouth = -0.0405f + 0.0012f * w;
                    float outer = half == 0
                        ? -0.0405f + 0.0098f * Mathf.Pow(w, 0.55f) - 0.0013f * Mathf.Exp(-uu * uu / 0.012f) + 0.0007f * Mathf.Exp(-(Mathf.Abs(uu) - 0.26f) * (Mathf.Abs(uu) - 0.26f) / 0.012f)
                        : -0.0405f - 0.0112f * Mathf.Pow(w, 0.5f);
                    for (int r = 0; r <= LR; r++)
                    {
                        float y = Mathf.Lerp(mouth, outer, r / (float)LR);
                        lv.Add(FromFrame(x, y, Surface(x, y) + 0.0006f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.15f + r / (float)LR * 0.85f)) + 0.0002f));
                        luv.Add(new Vector2(u / (float)LU, r / (float)LR));
                    }
                }
                for (int u = 0; u < LU; u++)
                    for (int r = 0; r < LR; r++)
                    {
                        int i0 = b0 + u * (LR + 1) + r, i1 = i0 + LR + 1, i2 = i0 + 1, i3 = i1 + 1;
                        if (half == 0) { lt.Add(i0); lt.Add(i1); lt.Add(i2); lt.Add(i1); lt.Add(i3); lt.Add(i2); }
                        else { lt.Add(i0); lt.Add(i2); lt.Add(i1); lt.Add(i1); lt.Add(i2); lt.Add(i3); }
                    }
            }
            var lm = new Mesh { name = "ErlanLips" };
            lm.SetVertices(lv); lm.SetUVs(0, luv); lm.SetTriangles(lt, 0); lm.RecalculateNormals(); lm.RecalculateBounds();
            var lgo = new GameObject("ErlanLips");
            lgo.transform.SetParent(_head, false);
            lgo.AddComponent<MeshFilter>().sharedMesh = lm;
            _lipR = lgo.AddComponent<MeshRenderer>();
            _lipR.sharedMaterial = LipMat;
            _lipR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // balaclava rim: a soft fabric band along the oval
            var rv = new List<Vector3>(); var rt = new List<int>();
            const int seg = 64;
            for (int s = 0; s <= seg; s++)
            {
                float t = s / (float)seg * Mathf.PI * 2f;
                for (int r = 0; r < 3; r++)
                {
                    float o = r == 0 ? 0.86f : r == 1 ? 0.95f : 1.06f;
                    float y = OvalY + Mathf.Cos(t) * OvalB * Mathf.Sqrt(o), x = Mathf.Sin(t) * OvalHalfWidth(y) * Mathf.Sqrt(o);
                    int gi = Mathf.Clamp(Mathf.RoundToInt((x - GX0) / GS), 0, GX - 1), gj = Mathf.Clamp(Mathf.RoundToInt((y - GY0) / GS), 0, GY - 1);
                    float prof = ProfAt(x, y);
                    float face = Surface(x, y);
                    float z = Mathf.Max(face, float.IsNaN(prof) ? face : prof) + (r == 1 ? 0.0045f : 0.0008f);
                    rv.Add(FromFrame(x, y, z));
                }
                if (s == seg) break;
                int b0 = s * 3;
                for (int r = 0; r < 2; r++)
                {
                    rt.Add(b0 + r); rt.Add(b0 + 3 + r); rt.Add(b0 + r + 1);
                    rt.Add(b0 + r + 1); rt.Add(b0 + 3 + r); rt.Add(b0 + 3 + r + 1);
                }
            }
            var rm = new Mesh { name = "BalaclavaRim" };
            rm.SetVertices(rv); rm.SetTriangles(rt, 0); rm.RecalculateNormals(); rm.RecalculateBounds();
            var rgo = new GameObject("BalaclavaRim");
            rgo.transform.SetParent(_head, false);
            rgo.AddComponent<MeshFilter>().sharedMesh = rm;
            _rimR = rgo.AddComponent<MeshRenderer>();
            _rimR.sharedMaterial = FabricMat;
            _rimR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// Front surface of the remaining mask around a cell: the mean of the valid cells in a 5x5 block
        /// (the decimated mask has few vertices; a mean avoids spikes), −∞ when nothing is there.
        static float MaxAround(float[,] p, int i, int j)
        {
            float sum = 0f; int n = 0;
            for (int a = -2; a <= 2; a++)
                for (int b = -2; b <= 2; b++)
                {
                    int x = i + a, y = j + b;
                    if (x < 0 || y < 0 || x >= GX || y >= GY || float.IsNegativeInfinity(p[x, y])) continue;
                    sum += p[x, y]; n++;
                }
            return n > 0 ? sum / n : float.NegativeInfinity;
        }

        // ------------------------------------------------------------------ eyes + lids
        void BuildEyes(float[,] profile)
        {
            var eyeMesh = UvSphere(EyeRadius, 20, 14, true, 1f);
            var upperLid = UvSphere(EyeRadius + 0.0011f, 20, 10, false, 0.56f);   // cap from the top pole to just below the equator
            var lowerLid = UvSphere(EyeRadius + 0.0009f, 20, 6, false, 0.3f);
            for (int e = 0; e < 2; e++)
            {
                var p2 = e == 0 ? EyeL : EyeR;
                float zs = Surface(p2.x, p2.y);
                var eye = new GameObject(e == 0 ? "EyeL" : "EyeR").transform;
                eye.SetParent(_head, false);
                eye.localPosition = FromFrame(p2.x, p2.y, zs - EyeRadius + 0.005f);
                eye.localRotation = Quaternion.LookRotation(_fwdL, _upL);
                _eyeRest[e] = eye.localRotation;
                var er = eye.gameObject.AddComponent<MeshRenderer>();
                eye.gameObject.AddComponent<MeshFilter>().sharedMesh = eyeMesh;
                er.sharedMaterial = EyeMat;
                er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _eyes[e] = eye;
                for (int l = 0; l < 2; l++)
                {
                    var lid = new GameObject(l == 0 ? "UpperLid" : "LowerLid").transform;
                    lid.SetParent(_head, false);
                    lid.localPosition = eye.localPosition;
                    lid.gameObject.AddComponent<MeshFilter>().sharedMesh = l == 0 ? upperLid : lowerLid;
                    var lr = lid.gameObject.AddComponent<MeshRenderer>();
                    lr.sharedMaterial = LidMat;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _lidR[e * 2 + l] = lr;
                    if (l == 0) _upperLids[e] = lid; else _lowerLids[e] = lid;
                }
            }
            PoseLids(1f);
        }

        /// <summary>
        /// Sphere (pole on +Y) or a cap of it: `capFraction` of the latitude range from the top pole. Eye: planar UV
        /// along +Z (iris in the middle of the texture).
        /// </summary>
        static Mesh UvSphere(float r, int lon, int lat, bool planarUv, float capFraction)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int a = 0; a <= lat; a++)
            {
                float th = Mathf.PI * capFraction * a / lat;
                for (int b = 0; b <= lon; b++)
                {
                    float ph = Mathf.PI * 2f * b / lon;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * r);
                    uv.Add(planarUv ? new Vector2(0.5f + 0.5f * d.x, 0.5f + 0.5f * d.y) : new Vector2(b / (float)lon, a / (float)lat));
                }
            }
            for (int a = 0; a < lat; a++)
                for (int b = 0; b < lon; b++)
                {
                    int i0 = a * (lon + 1) + b, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
            // planar UV must only show the iris on the front (+Z) half
            if (planarUv)
                for (int i = 0; i < v.Count; i++) if (v[i].z < 0f) uv[i] = new Vector2(0.03f, 0.5f);
            var m = new Mesh { name = planarUv ? "Eyeball" : "Lid" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// open: 1 wide open, 0 closed.
        void PoseLids(float open)
        {
            for (int e = 0; e < 2; e++)
            {
                if (_upperLids[e] == null) continue;
                // the upper cap is the top of the eye sphere (pole = head up); rotating it forward/down over the
                // cornea closes the eye. Lower lid: the cap turned upside down, rising a little.
                float upper = Mathf.Lerp(78f, -26f, open);
                float lower = Mathf.Lerp(-31f, -11f, open);
                Quaternion baseRot = Quaternion.LookRotation(_fwdL, _upL);
                _upperLids[e].localRotation = Quaternion.AngleAxis(upper, _rightL) * baseRot;
                _lowerLids[e].localRotation = Quaternion.AngleAxis(lower, _rightL) * Quaternion.AngleAxis(180f, _fwdL) * baseRot;
            }
        }

        // ------------------------------------------------------------------ nails (capillary refill)
        static readonly HumanBodyBones[] NailBones =
        {
            HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal,
        };

        void BuildNails(CasualtyRig rig)
        {
            var tipMesh = UvSphere(1f, 12, 8, false, 1f);
            foreach (var b in NailBones)
            {
                var bone = rig.Bone(b);
                if (bone == null || bone.parent == null) continue;
                // finger direction = from the intermediate joint to this distal joint
                Vector3 dir = (bone.position - bone.parent.position);
                float len = Mathf.Clamp(dir.magnitude, 0.015f, 0.03f);
                dir.Normalize();
                // back of the hand: away from the palm ≈ the hand's normal; use the hand bone's frame
                var handBone = rig.Bone(b <= HumanBodyBones.LeftRingDistal ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var mid = rig.Bone(b <= HumanBodyBones.LeftRingDistal ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                var idx = rig.Bone(b <= HumanBodyBones.LeftRingDistal ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                Vector3 across = idx != null && mid != null ? (idx.position - mid.position).normalized : Vector3.right;
                Vector3 handDir = mid != null && handBone != null ? (mid.position - handBone.position).normalized : dir;
                Vector3 back = Vector3.Cross(handDir, across).normalized;
                if (b > HumanBodyBones.LeftRingDistal) back = -back;
                // the fingertip outside the glove: a skin capsule-ish blob + a flat nail on the back
                var tip = new GameObject("FingerTip_" + b).transform;
                tip.SetParent(bone, false);
                tip.position = bone.position + dir * (len * 0.45f + 0.004f);
                tip.rotation = Quaternion.LookRotation(dir, back);
                tip.localScale = new Vector3(0.0108f, 0.0098f, 0.0175f) / Mathf.Max(1e-4f, bone.lossyScale.x);
                tip.gameObject.AddComponent<MeshFilter>().sharedMesh = tipMesh;
                var tr = tip.gameObject.AddComponent<MeshRenderer>();
                tr.sharedMaterial = SkinMat;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var nail = new GameObject("Nail").transform;
                nail.SetParent(tip, false);
                nail.localPosition = new Vector3(0f, 0.8f, 0.32f);
                nail.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                nail.localScale = new Vector3(0.72f, 0.16f, 0.62f);
                nail.gameObject.AddComponent<MeshFilter>().sharedMesh = tipMesh;
                var nr = nail.gameObject.AddComponent<MeshRenderer>();
                nr.sharedMaterial = NailMat;
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Nails.Add(new NailBed { transform = nail, renderer = nr, tipRenderer = tr, name = b.ToString() });
            }
        }

        // ------------------------------------------------------------------ materials (shared, tinted per renderer)
        static Material _skinMat, _lipMat, _eyeMat, _nailMat, _fabricMat, _faceSkinMat, _lidMat;
        static Material FaceSkinMat
        {
            get
            {
                if (_faceSkinMat != null) return _faceSkinMat;
                _faceSkinMat = FieldMedic.UI.WorldUI.Lit(new Color(0.80f, 0.61f, 0.49f), 0.32f);
                var tex = PaintFace(256);
                _faceSkinMat.SetTexture("_BaseMap", tex);
                _faceSkinMat.mainTexture = tex;
                return _faceSkinMat;
            }
        }
        static Material LidMat
        {
            get
            {
                if (_lidMat != null) return _lidMat;
                _lidMat = FieldMedic.UI.WorldUI.Lit(new Color(0.80f, 0.61f, 0.49f), 0.32f);
                var tex = new Texture2D(4, 32, TextureFormat.RGBA32, false) { name = "LidLash", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[4 * 32];
                for (int y = 0; y < 32; y++)
                {
                    float v = y / 31f;   // 0 at the cap pole … 1 at the lid edge (lashes)
                    Color c = Color.Lerp(Color.white, new Color(0.86f, 0.74f, 0.74f), Mathf.InverseLerp(0.6f, 0.85f, v));
                    if (v > 0.88f) c = new Color(0.12f, 0.09f, 0.08f);
                    for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                _lidMat.SetTexture("_BaseMap", tex);
                _lidMat.mainTexture = tex;
                return _lidMat;
            }
        }

        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xffffff) / 16777215f; } }

        /// Smooth value noise (bilinear between hashed lattice points, `cell` texels apart).
        static float VNoise(float x, float y, float cell)
        {
            float fx = x / cell, fy = y / cell;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), tx), b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// Skin detail painted in the face frame (white-based, tinted by _BaseColor): brows, stubble, eye bags,
        /// cheek/nose redness, dust and soot of the battlefield.
        static Texture2D PaintFace(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "ErlanFaceDetail", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int py = 0; py < n; py++)
                for (int pxi = 0; pxi < n; pxi++)
                {
                    float x = GX0 + (pxi + 0.5f) / n * GX * GS, y = GY0 + (py + 0.5f) / n * GY * GS;
                    float noise = (VNoise(pxi, py, 3f) - 0.5f) * 0.035f + (VNoise(pxi, py, 14f) - 0.5f) * 0.07f;
                    Color c = new Color(1f + noise, 1f + noise, 1f + noise);
                    // redness: cheeks, nose tip
                    float red = 0.6f * (G(x, y, -0.045f, -0.005f, 0.02f, 0.016f) + G(x, y, 0.045f, -0.005f, 0.02f, 0.016f)) + 0.5f * G(x, y, 0f, -0.006f, 0.009f, 0.01f);
                    c *= Color.Lerp(Color.white, new Color(1f, 0.86f, 0.84f), Mathf.Clamp01(red));
                    // eye bags / dark circles (fatigue, shock)
                    float bag = G(x, y, EyeL.x, EyeL.y - 0.014f, 0.014f, 0.006f) + G(x, y, EyeR.x, EyeR.y - 0.014f, 0.014f, 0.006f);
                    c *= Color.Lerp(Color.white, new Color(0.8f, 0.74f, 0.78f), Mathf.Clamp01(bag));
                    // stubble: jaw, chin, upper lip (not on the lips themselves)
                    float beard = Mathf.Clamp01(Mathf.InverseLerp(-0.012f, -0.035f, y) * (1f - G(x, y, 0f, -0.041f, 0.026f, 0.012f) * 1.2f));
                    beard = Mathf.Max(beard, Mathf.Clamp01(G(x, y, 0f, -0.026f, 0.02f, 0.004f) * 1.3f));
                    float grain = VNoise(pxi * 1.7f, py * 1.7f, 2f);
                    c *= Color.Lerp(Color.white, new Color(0.8f, 0.78f, 0.78f), beard * (0.55f + 0.35f * grain));
                    // eyebrows: thick dark arches
                    for (int e = 0; e < 2; e++)
                    {
                        float ex = e == 0 ? EyeL.x : EyeR.x, side = e == 0 ? -1f : 1f;
                        float u = (x - ex) * side / 0.021f;                       // −1 inner … +1 outer
                        if (u < -1.1f || u > 1.15f) continue;
                        float browY = EyeL.y + 0.0155f + 0.0035f * Mathf.Cos(u * 1.3f) - 0.0015f * u;
                        float thick = 0.0034f * (1.15f - 0.45f * Mathf.Clamp01(u));
                        float d = Mathf.Abs(y - browY) / thick;
                        float k = Mathf.SmoothStep(1f, 0.35f, d) * (0.7f + 0.3f * VNoise(pxi * 2f, py * 0.7f, 2f));
                        if (d < 1f) c = Color.Lerp(c, new Color(0.17f, 0.12f, 0.1f), 0.85f * k);
                    }
                    // battlefield dust and soot
                    float dirt = G(x, y, 0.04f, 0.06f, 0.03f, 0.012f) * 0.6f + G(x, y, -0.05f, -0.03f, 0.02f, 0.03f) * 0.45f + Mathf.Clamp01((VNoise(pxi + 40, py + 17, 22f) - 0.62f) * 2.2f) * 0.4f;
                    c *= Color.Lerp(Color.white, new Color(0.72f, 0.68f, 0.62f), Mathf.Clamp01(dirt));
                    c.a = 1f;
                    px[py * n + pxi] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Material SkinMat => _skinMat != null ? _skinMat : (_skinMat = FieldMedic.UI.WorldUI.Lit(new Color(0.80f, 0.61f, 0.49f), 0.32f));
        static Material LipMat => _lipMat != null ? _lipMat : (_lipMat = FieldMedic.UI.WorldUI.Lit(new Color(0.66f, 0.36f, 0.34f), 0.45f));
        static Material NailMat => _nailMat != null ? _nailMat : (_nailMat = FieldMedic.UI.WorldUI.Lit(new Color(0.86f, 0.58f, 0.56f), 0.7f));
        static Material FabricMat => _fabricMat != null ? _fabricMat : (_fabricMat = FieldMedic.UI.WorldUI.Lit(new Color(0.2f, 0.23f, 0.14f), 0.08f));
        static Material EyeMat
        {
            get
            {
                if (_eyeMat != null) return _eyeMat;
                _eyeMat = FieldMedic.UI.WorldUI.Lit(Color.white, 0.88f);
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "EyeIris", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                        Color c = new Color(0.93f, 0.89f, 0.86f);                                    // sclera
                        c = Color.Lerp(c, new Color(0.85f, 0.55f, 0.52f), Mathf.InverseLerp(0.55f, 1f, r) * 0.35f); // red-ish rim
                        if (r < 0.44f)
                        {
                            float rays = 0.85f + 0.15f * Mathf.Sin(Mathf.Atan2(dy, dx) * 23f);
                            c = Color.Lerp(new Color(0.18f, 0.11f, 0.06f), new Color(0.33f, 0.21f, 0.11f), r / 0.44f) * rays; // dark brown iris
                            c.a = 1f;
                        }
                        if (r < 0.17f) c = new Color(0.02f, 0.02f, 0.02f);                           // pupil
                        if (r > 0.42f && r < 0.46f) c = Color.Lerp(c, new Color(0.08f, 0.06f, 0.05f), 0.6f);  // limbal ring
                        px[y * N + x] = c;
                    }
                tex.SetPixels32(px);
                tex.Apply(true, true);
                _eyeMat.SetTexture("_BaseMap", tex);
                _eyeMat.mainTexture = tex;
                return _eyeMat;
            }
        }

        // ------------------------------------------------------------------ per frame
        public void Grimace(float strength) => _grimace = Mathf.Max(_grimace, Mathf.Clamp01(strength));

        /// <summary>A stimulus answered (AVPU): eyes open toward the medic for a few seconds.</summary>
        public void OnStimulus(AvpuResponse r, float seconds = 3.5f)
        {
            if (!r.eyesOpen) { if (r.withdraws) { _openBoostLevel = Mathf.Max(_openBoostLevel, 0.25f); _openBoost = 1.0f; } return; }
            _openBoostLevel = 1f;
            _openBoost = seconds;
        }

        /// <summary>Editor preview: fixed shock tint and eye opening (pallor 0..1, cyanosis 0..1, eyes 0..1).</summary>
        public void PreviewShock(float pallor, float cyanosis, float eyesOpen)
        {
            if (!Built) Build();
            _previewPallor = pallor; _previewCyan = cyanosis; _previewOpen = eyesOpen;
            EyesOpen = eyesOpen;
            PoseLids(eyesOpen);
            ApplyTint(true);
        }

        void LateUpdate()
        {
            if (!Built) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateEyes(dt);
            _tintTimer -= dt;
            if (_tintTimer <= 0f) { _tintTimer = 1f / Mathf.Max(1f, tintRateHz); ApplyTint(false); }
            UpdateNails(dt);
        }

        void UpdateEyes(float dt)
        {
            var p = casualty != null ? casualty.Patient : null;
            float target = 1f;
            bool dead = p != null && p.outcome == Outcome.Died;
            if (p != null)
            {
                target = dead ? 0.18f : p.AVPU switch
                {
                    Consciousness.Alert => 1f,
                    Consciousness.Voice => 0.3f,
                    Consciousness.Pain => 0.04f,
                    _ => 0.08f,
                };
            }
            if (_previewOpen >= 0f) target = _previewOpen;
            _openBoost -= dt;
            if (_openBoost > 0f && !dead) target = Mathf.Max(target, _openBoostLevel);
            else _openBoostLevel = 0f;
            _grimace = Mathf.MoveTowards(_grimace, 0f, dt * 0.9f);
            target *= 1f - 0.75f * _grimace;                                                  // squeeze on pain
            if (!dead && target > 0.2f)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f) { _blink = 1f; _blinkTimer = Random.Range(2.5f, 6f); }
            }
            _blink = Mathf.MoveTowards(_blink, 0f, dt * 7f);
            EyesOpenTarget = target;
            EyesOpen = Mathf.MoveTowards(EyesOpen, target * (1f - Mathf.Sin(_blink * Mathf.PI)), dt * 5f);
            PoseLids(EyesOpen);

            // gaze: toward the player's head when conscious and the eyes are open (clamped to the eye's range)
            var rig = casualty != null ? casualty.rig : null;
            Transform look = rig != null ? rig.lookTarget : null;
            if (look == null && Camera.main != null) look = Camera.main.transform;
            LookingAtPlayer = false;
            for (int e = 0; e < 2; e++)
            {
                var eye = _eyes[e];
                if (eye == null) continue;
                Quaternion rest = _head.rotation * _eyeRest[e];
                Quaternion want = rest;
                if (look != null && !dead && EyesOpen > 0.15f && p != null && p.AVPU <= Consciousness.Voice)
                {
                    Vector3 to = look.position - eye.position;
                    Vector3 fwd = rest * Vector3.forward;
                    float ang = Vector3.Angle(fwd, to);
                    Quaternion full = Quaternion.FromToRotation(fwd, to);
                    want = Quaternion.Slerp(Quaternion.identity, full, Mathf.Min(1f, 32f / Mathf.Max(0.01f, ang))) * rest;
                    LookingAtPlayer = ang < 40f;
                }
                eye.rotation = Quaternion.Slerp(eye.rotation, want, 1f - Mathf.Exp(-dt * 14f));
            }
        }

        void ApplyTint(bool force)
        {
            if (_faceR == null) return;
            float pallor = _previewPallor >= 0f ? _previewPallor : casualty != null ? casualty.Pallor : 0f;
            float cyan = _previewPallor >= 0f ? _previewCyan : casualty != null ? casualty.Cyanosis : 0f;
            float sweat = _previewPallor >= 0f ? pallor * 0.8f : casualty != null ? casualty.Sweat : 0f;
            // pale first, then grey (class III–IV), lips lose colour then turn blue
            Color s = Color.Lerp(skin, palePallid, Mathf.Clamp01(pallor * 1.4f));
            s = Color.Lerp(s, greyShock, Mathf.Clamp01((pallor - 0.55f) / 0.45f) * 0.8f);
            s = Color.Lerp(s, s * new Color(0.86f, 0.9f, 1.05f), cyan * 0.6f);
            Color l = Color.Lerp(lips, Color.Lerp(lips, palePallid, 0.6f), pallor);
            l = Color.Lerp(l, cyanoticLips, Mathf.Clamp01(cyan * 1.2f));
            float smooth = Mathf.Lerp(0.26f, 0.5f, sweat);
            if (!force && (s - CurrentSkin).maxColorComponent < 0.002f && (l - CurrentLips).maxColorComponent < 0.002f && Mathf.Abs(smooth - CurrentSmoothness) < 0.005f) return;
            CurrentSkin = s; CurrentLips = l; CurrentSmoothness = smooth;
            TintUpdates++;
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, s);
            _mpb.SetFloat(SmoothId, smooth);
            _faceR.SetPropertyBlock(_mpb, 0);
            foreach (var r in _lidR) if (r != null) r.SetPropertyBlock(_mpb);
            foreach (var n in Nails) if (n.tipRenderer != null) n.tipRenderer.SetPropertyBlock(_mpb);
            if (_lipR != null)
            {
                _mpb.SetColor(BaseColorId, l);
                _mpb.SetFloat(SmoothId, Mathf.Lerp(0.42f, 0.6f, sweat));
                _lipR.SetPropertyBlock(_mpb);
            }
        }

        // ------------------------------------------------------------------ capillary refill
        /// <summary>Base nail colour for the current perfusion (pink → pale → dusky).</summary>
        Color NailBase()
        {
            float pallor = casualty != null ? casualty.Pallor : 0f, cyan = casualty != null ? casualty.Cyanosis : 0f;
            Color c = Color.Lerp(nailPink, palePallid, pallor * 0.8f);
            return Color.Lerp(c, cyanoticLips, cyan * 0.7f);
        }

        void UpdateNails(float dt)
        {
            if (Nails.Count == 0) return;
            float refill = casualty != null && casualty.Patient != null ? VitalsAssessment.CapillaryRefillSeconds(casualty.Patient) : 1.5f;
            foreach (var n in Nails)
            {
                float before = n.blanch;
                if (n.pressed) n.blanch = Mathf.MoveTowards(n.blanch, 1f, dt * 5f);
                else if (n.blanch > 0f)
                    n.blanch = float.IsInfinity(refill) ? n.blanch : Mathf.MoveTowards(n.blanch, 0f, dt / Mathf.Max(0.3f, refill));
                if (Mathf.Abs(before - n.blanch) < 1e-4f && n.blanch <= 0f && !n.pressed) continue;
                _mpb.Clear();
                _mpb.SetColor(BaseColorId, Color.Lerp(NailBase(), nailBlanched, n.blanch));
                n.renderer.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>A fingertip presses/releases a nail bed (VitalsChecks drives this).</summary>
        public void PressNail(NailBed n, bool pressed)
        {
            if (n.pressed == pressed) return;
            n.pressed = pressed;
            if (!pressed) n.releasedAt = Time.time;
        }
    }
}
