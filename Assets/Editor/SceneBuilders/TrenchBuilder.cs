using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Procedural night front-line trench (editor-time mesh generation, saved as mesh assets):
    /// zig-zag fire trench ~21 m (fire bays + traverses), 1.75 m deep, ~1.2 m wide at the bottom with sloped soil
    /// walls, plank revetment + posts, sandbag parapet/parados, duckboards, puddles, a firing step, a covered
    /// dugout (log roof, earth mound, tarp door, candle), a communication trench branch, field cables, ammo crates,
    /// an anti-drone net, and a churned field with craters, spoil berms, burnt stumps and concertina wire.
    /// Textures: Poly Haven CC0 (see ATTRIBUTION.md), shader FieldMedic/EnvLit.
    /// Coordinates: floor y≈0, the player starts at the origin facing +Z (the enemy side).
    /// </summary>
    public static class TrenchBuilder
    {
        public const float G = 1.75f;                 // ground level = trench depth
        public const float HalfWidth = 0.6f;          // at the bottom
        const string ArtDir = "Assets/Art/Environment";
        const string TexDir = ArtDir + "/Textures";
        const string MeshDir = ArtDir + "/Generated";
        const string MatDir = ArtDir + "/Materials";

        public static readonly Vector3[] MainPath =
        {
            new Vector3(-9.6f, 0f, -1.5f), new Vector3(-6.2f, 0f, -1.5f), new Vector3(-4.6f, 0f, 0f),
            new Vector3(4.8f, 0f, 0f), new Vector3(6.4f, 0f, 1.5f), new Vector3(10.5f, 0f, 1.5f),
        };
        public static readonly Vector3[] BranchPath =
        {
            new Vector3(-2.65f, 0f, -0.72f), new Vector3(-3.2f, 0f, -3.2f), new Vector3(-2.4f, 0f, -6.6f), new Vector3(-3.1f, 0f, -11f),
        };
        public static readonly Vector3 DugoutCentre = new Vector3(-8.1f, 0f, -1.5f);
        const float DugoutEndX = -6.6f;

        static Random _rng;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static readonly List<Vector4> Craters = new List<Vector4>();   // x, z, radius, depth
        static Transform _root;

        // ------------------------------------------------------------------ entry
        public static GameObject Build()
        {
            _rng = new Random(1337);
            Mats.Clear();
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(MatDir);
            ConfigureTextures();
            MakeCraters();

            _root = new GameObject("Trench").transform;
            var main = SamplePath(MainPath, MainHalfWidth);
            var branch = SamplePath(BranchPath, s => 0.5f);

            BuildWalls("Main", main, true, BranchGap);
            BuildWalls("Branch", branch, false, null);
            BuildFloor("Main", main);
            BuildFloor("Branch", branch);
            BuildCap("MainWestCap", main[0], -1f);
            BuildCap("MainEastCap", main[main.Count - 1], 1f);
            BuildCap("BranchCap", branch[branch.Count - 1], 1f);
            BuildGround(new[] { main, branch });

            BuildRevetment(main);
            BuildSandbags(main, branch);
            BuildDuckboards(main, branch);
            BuildPuddles(main, branch);
            BuildFiringSteps(main);
            BuildDugout();
            BuildProps();
            BuildCables(main);
            BuildNet();
            BuildField();

            foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<MeshRenderer>() != null)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            AssetDatabase.SaveAssets();
            return _root.gameObject;
        }

        static float MainHalfWidth(float x) => Mathf.Lerp(0.95f, HalfWidth, Mathf.InverseLerp(DugoutEndX, DugoutEndX + 0.9f, x));

        static bool BranchGap(Vector3 p)
        {
            float d = DistToPolyline(p, BranchPath);
            return d < 0.5f + 0.05f && p.z < -0.3f && p.x < -1.5f && p.x > -4f;
        }

        // ------------------------------------------------------------------ textures & materials
        static void ConfigureTextures()
        {
            foreach (var path in Directory.GetFiles(TexDir))
            {
                if (path.EndsWith(".meta")) continue;
                var ti = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter;
                if (ti == null) continue;
                bool normal = path.Contains("_Normal");
                bool changed = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
                if (ti.anisoLevel != 2) { ti.anisoLevel = 2; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        static Material Env(string name, string tex, Color tint, float smooth, float fromAlpha = 0f, float wet = 0f,
            float variation = 0.3f, float ao = 0f, float bump = 1f)
        {
            if (Mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find("FieldMedic/EnvLit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            if (tex != null)
            {
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{tex}_Albedo.png") ??
                             AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{tex}_Albedo.jpg");
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{tex}_Normal.jpg");
                m.SetTexture("_BaseMap", albedo);
                m.SetTexture("_BumpMap", nrm);
            }
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_SmoothFromAlpha", fromAlpha);
            m.SetFloat("_Wetness", wet);
            m.SetFloat("_Variation", variation);
            m.SetFloat("_AOStrength", ao);
            m.SetFloat("_BumpScale", bump);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Material SoilWall => Env("Env_SoilWall", "Env_Soil", new Color(0.62f, 0.55f, 0.48f), 0.55f, 1f, 0.35f, 0.35f, 1f, 1.2f);
        static Material MudFloor => Env("Env_MudFloor", "Env_Mud", new Color(0.55f, 0.5f, 0.45f), 0.75f, 1f, 0.55f, 0.3f, 1f, 1.1f);
        static Material Field => Env("Env_Field", "Env_Field", new Color(0.26f, 0.21f, 0.17f), 0.25f, 0f, 0.4f, 0.4f, 1f, 1f);
        static Material Planks => Tiled(Env("Env_Planks", "Env_Planks", new Color(0.46f, 0.43f, 0.39f), 0.25f, 0f, 0.0f, 0.45f, 0f, 1f));
        static Material PlanksWet => Tiled(Env("Env_PlanksWet", "Env_Planks", new Color(0.34f, 0.31f, 0.27f), 0.35f, 0f, 0.35f, 0.45f, 0f, 1f));
        static Material Bark => Env("Env_Bark", "Env_Bark", new Color(0.55f, 0.5f, 0.45f), 0.2f, 0f, 0f, 0.3f, 0f, 1.2f);
        static Material BarkBurnt => Env("Env_BarkBurnt", "Env_Bark", new Color(0.16f, 0.14f, 0.13f), 0.15f, 0f, 0f, 0.4f, 0f, 1.3f);
        static Material Crate => Tiled(Env("Env_CrateOlive", "Env_Planks", new Color(0.3f, 0.34f, 0.23f), 0.3f, 0f, 0f, 0.3f, 0f, 1f));
        static Material Rust => Env("Env_Rust", "Env_Rust", new Color(0.55f, 0.6f, 0.5f), 0.35f, 0f, 0f, 0.2f, 0f, 1f);
        /// <summary>Unit-cube boards: one plank strip of the plank-wall texture per face, repeated along the board.</summary>
        static Material Tiled(Material m)
        {
            m.SetTextureScale("_BaseMap", new Vector2(1.6f, 0.2f));
            return m;
        }

        static Material Cable => Env("Env_Cable", null, new Color(0.04f, 0.045f, 0.04f), 0.5f);
        static Material Puddle => Env("Env_Puddle", null, new Color(0.035f, 0.03f, 0.025f), 0.96f, 0f, 0f, 0.1f);
        static Material Tarp => Env("Env_Tarp", "Env_Hessian", new Color(0.3f, 0.32f, 0.22f), 0.15f, 0f, 0f, 0.2f, 0f, 1.5f);
        static Material Bag(int i)
        {
            var tints = new[] { new Color(0.62f, 0.55f, 0.42f), new Color(0.5f, 0.46f, 0.36f), new Color(0.42f, 0.42f, 0.33f) };
            return Env("Env_Sandbag" + i, "Env_Hessian", tints[i], 0.12f, 0f, 0.15f, 0.5f, 0f, 1.6f);
        }

        static Material NetMaterial()
        {
            string texPath = TexDir + "/Env_Net.png";
            if (!File.Exists(texPath))
            {
                const int N = 128;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool line = x % 16 < 2 || y % 16 < 2;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, line ? 1f : 0f));
                }
                t.Apply();
                File.WriteAllBytes(texPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            string path = $"{MatDir}/Env_Net.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("FieldMedic/Particle");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetTextureScale("_MainTex", new Vector2(14f, 4f));
            m.SetColor("_TintColor", new Color(0.07f, 0.075f, 0.055f, 0.95f));
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ path sampling
        public struct Sample
        {
            public Vector3 c;    // centre on the floor
            public Vector3 n;    // left normal (XZ)
            public Vector3 t;    // tangent
            public float s;      // arc length
            public float hw;     // bottom half width
            public float miter;
            public int seg;
        }

        public static List<Sample> SamplePath(Vector3[] pts, Func<float, float> halfWidth, float step = 0.25f)
        {
            var list = new List<Sample>();
            float s = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                float len = Vector3.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                Vector3 t = (b - a).normalized;
                Vector3 nrm = new Vector3(-t.z, 0f, t.x);
                for (int k = 0; k < n; k++)
                {
                    float f = (float)k / n;
                    var smp = new Sample { c = Vector3.Lerp(a, b, f), t = t, n = nrm, s = s + len * f, miter = 1f, seg = i };
                    if (k == 0 && i > 0)
                    {
                        Vector3 tp = (pts[i] - pts[i - 1]).normalized;
                        Vector3 np = new Vector3(-tp.z, 0f, tp.x);
                        Vector3 avg = (np + nrm).normalized;
                        smp.miter = 1f / Mathf.Max(0.55f, Vector3.Dot(avg, nrm));
                        smp.n = avg;
                        smp.t = new Vector3(avg.z, 0f, -avg.x);
                    }
                    smp.hw = halfWidth(smp.c.x);
                    list.Add(smp);
                }
                s += len;
            }
            Vector3 last = pts[pts.Length - 1];
            Vector3 tl = (last - pts[pts.Length - 2]).normalized;
            list.Add(new Sample { c = last, t = tl, n = new Vector3(-tl.z, 0f, tl.x), s = s, miter = 1f, hw = halfWidth(last.x), seg = pts.Length - 2 });
            return list;
        }

        public static float DistToPolyline(Vector3 p, Vector3[] pts)
        {
            float best = float.MaxValue;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < pts.Length - 1; i++)
                best = Mathf.Min(best, Environment.EnvMath.DistanceToSegment(q, new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i + 1].x, pts[i + 1].z)));
            return best;
        }

        static float TopWidth(float hw, bool revetted) => hw + (revetted ? 0.06f : 0.22f);
        static float LipDist(float hw) => hw + 0.6f;

        // nearest trench sample (for ground snapping)
        static float NearestTrench(Vector3 p, List<Sample>[] paths, out Vector3 onAxis, out float hw)
        {
            float best = float.MaxValue; onAxis = p; hw = HalfWidth;
            var q = new Vector2(p.x, p.z);
            foreach (var path in paths)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = new Vector2(path[i].c.x, path[i].c.z), b = new Vector2(path[i + 1].c.x, path[i + 1].c.z);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    Vector2 c = a + ab * t;
                    float d = Vector2.Distance(q, c);
                    if (d < best) { best = d; onAxis = new Vector3(c.x, 0f, c.y); hw = Mathf.Lerp(path[i].hw, path[i + 1].hw, t); }
                }
            return best;
        }

        // ------------------------------------------------------------------ heights
        static void MakeCraters()
        {
            Craters.Clear();
            var r = new Random(7);
            float F(double a, double b) => (float)(a + r.NextDouble() * (b - a));
            for (int i = 0; i < 18; i++) Craters.Add(new Vector4(F(-30, 30), F(6, 45), F(1.2, 3.6), F(0.4, 1.1)));
            for (int i = 0; i < 5; i++) Craters.Add(new Vector4(F(-28, 28), F(-24, -7), F(1.0, 2.8), F(0.35, 0.9)));
            Craters.Add(new Vector4(3.4f, 4.6f, 1.7f, 0.85f));   // fresh hit right in front of the bay
            Craters.Add(new Vector4(-6.5f, -5.2f, 1.4f, 0.6f));  // behind the dugout
        }

        static float Perlin(float x, float z) => Mathf.PerlinNoise(x + 100f, z + 100f) - 0.5f;

        static float GroundHeight(float x, float z, float distToTrench, float hw, out float craterMask)
        {
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LipDist(hw) + 0.6f, 6f, distToTrench));
            float h = G + Perlin(x * 0.07f, z * 0.07f) * 0.6f * far + Perlin(x * 0.45f, z * 0.45f) * 0.14f * (0.4f + 0.6f * far)
                      + Perlin(x * 1.7f, z * 1.7f) * 0.04f;
            // spoil berm along the trench (dug-out earth thrown up both sides)
            float bd = (distToTrench - (hw + 0.95f)) / 0.7f;
            h += 0.32f * Mathf.Exp(-bd * bd);
            craterMask = 0f;
            foreach (var c in Craters)
            {
                float r = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y));
                if (r < c.z)
                {
                    float k = r / c.z;
                    h -= c.w * (1f - k * k);
                    craterMask = Mathf.Max(craterMask, 1f - k);
                }
                else if (r < c.z * 1.55f)
                {
                    float k = (r - c.z) / (c.z * 0.55f);
                    h += c.w * 0.35f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.3f);
                }
            }
            return h;
        }

        // ------------------------------------------------------------------ walls / floor / caps
        static void BuildWalls(string name, List<Sample> path, bool revetted, Func<Vector3, bool> gap)
        {
            const int R = 7;
            for (int side = -1; side <= 1; side += 2)
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>();
                var tris = new List<int>();
                int rows = R + 2;
                var skip = new bool[path.Count];
                for (int j = 0; j < path.Count; j++)
                {
                    var p = path[j];
                    if (gap != null) skip[j] = gap(p.c + p.n * side * p.hw);
                    float top = TopWidth(p.hw, revetted && !(p.c.x < DugoutEndX && name == "Main"));
                    for (int k = 0; k < rows; k++)
                    {
                        Vector3 v;
                        float y;
                        if (k <= R)
                        {
                            y = G * k / R;
                            float off = Mathf.Lerp(p.hw, top, y / G);
                            float nz = revetted ? 0.025f : 0.07f;
                            off += (Mathf.PerlinNoise(p.s * 0.9f + side * 31f, y * 1.3f) - 0.5f) * 2f * nz
                                   + (Mathf.PerlinNoise(p.s * 3.1f + side * 7f, y * 4f) - 0.5f) * nz * 0.6f;
                            if (k == 0) off = p.hw;
                            v = p.c + p.n * (side * off * p.miter) + Vector3.up * y;
                        }
                        else
                        {
                            Vector3 lip = p.c + p.n * (side * LipDist(p.hw) * p.miter);
                            float d = Vector2.Distance(new Vector2(lip.x, lip.z), new Vector2(p.c.x, p.c.z));
                            y = GroundHeight(lip.x, lip.z, d, p.hw, out _);
                            v = new Vector3(lip.x, y, lip.z);
                        }
                        verts.Add(v);
                        uvs.Add(new Vector2(p.s * 0.5f, y * 0.5f + (k > R ? 0.15f : 0f)));
                        float ao = Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(Mathf.Clamp01(y / G)));
                        float wet = Mathf.Clamp01(1f - y / 0.45f) * 0.9f;
                        cols.Add(new Color(ao, wet, 0f, 1f));
                    }
                }
                for (int j = 0; j < path.Count - 1; j++)
                {
                    if (skip[j] || skip[j + 1]) continue;
                    for (int k = 0; k < rows - 1; k++)
                    {
                        int a = j * rows + k, b = (j + 1) * rows + k;
                        Quad(tris, a, b, b + 1, a + 1, side < 0);
                    }
                }
                var mesh = SaveMesh($"{name}_Wall{(side < 0 ? "R" : "L")}", verts, uvs, cols, tris);
                var go = MeshObject($"{name}Wall_{(side < 0 ? "R" : "L")}", mesh, SoilWall, true);
                EnsureFacing(mesh, go, verts, path[path.Count / 2].c, path[path.Count / 2].n * side);
            }
        }

        static void BuildFloor(string name, List<Sample> path)
        {
            const int C = 6;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var p in path)
            {
                for (int k = 0; k <= C; k++)
                {
                    float u = (float)k / C * 2f - 1f;
                    var v = p.c + p.n * (u * (p.hw + 0.02f) * p.miter);
                    v.y = -0.045f * (1f - u * u) + Perlin(v.x * 2.3f, v.z * 2.3f) * 0.06f + Perlin(v.x * 6f, v.z * 6f) * 0.02f;
                    verts.Add(v);
                    uvs.Add(new Vector2(v.x * 0.6f, v.z * 0.6f));
                    cols.Add(new Color(Mathf.Lerp(0.6f, 0.85f, 1f - Mathf.Abs(u)), 1f, 0f, 1f));
                }
            }
            for (int j = 0; j < path.Count - 1; j++)
                for (int k = 0; k < C; k++)
                {
                    int a = j * (C + 1) + k, b = (j + 1) * (C + 1) + k;
                    Quad(tris, a, a + 1, b + 1, b, false);
                }
            var mesh = SaveMesh(name + "_Floor", verts, uvs, cols, tris);
            var go = MeshObject(name + "Floor", mesh, MudFloor, true);
            EnsureFacing(mesh, go, verts, path[path.Count / 2].c + Vector3.up, Vector3.down);
        }

        static void BuildCap(string name, Sample p, float dir)
        {
            const int C = 6, R = 7;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            Vector3 outward = p.t * dir;
            for (int k = 0; k <= R + 1; k++)
            for (int i = 0; i <= C; i++)
            {
                float u = (float)i / C * 2f - 1f;
                float y = k <= R ? G * k / R : G;
                float bulge = k <= R ? 0.18f * y / G : 0.75f;
                float w = Mathf.Lerp(p.hw, p.hw + 0.22f, y / G) + (k > R ? 0.4f : 0f);
                var v = p.c + p.n * (u * w) + outward * (bulge + (Mathf.PerlinNoise(u * 2f + 9f, y) - 0.5f) * 0.1f) + Vector3.up * y;
                if (k > R)
                {
                    float d = Vector2.Distance(new Vector2(v.x, v.z), new Vector2(p.c.x, p.c.z));
                    v.y = GroundHeight(v.x, v.z, d, p.hw, out _);
                }
                verts.Add(v);
                uvs.Add(new Vector2(u * 0.6f, y * 0.5f));
                cols.Add(new Color(Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(y / G)), Mathf.Clamp01(1f - y / 0.45f), 0f, 1f));
            }
            for (int k = 0; k <= R; k++)
                for (int i = 0; i < C; i++)
                {
                    int a = k * (C + 1) + i;
                    Quad(tris, a, a + 1, a + C + 2, a + C + 1, false);
                }
            var mesh = SaveMesh(name, verts, uvs, cols, tris);
            var go = MeshObject(name, mesh, SoilWall, true);
            EnsureFacing(mesh, go, verts, p.c, outward);
        }

        /// <summary>Flips the triangle winding if the mesh faces away from the trench (normal at the point nearest <paramref name="probe"/> must oppose <paramref name="outward"/>).</summary>
        static void EnsureFacing(Mesh mesh, GameObject go, List<Vector3> verts, Vector3 probe, Vector3 outward)
        {
            var normals = mesh.normals;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float d = (verts[i] - (probe + outward * 0.8f + Vector3.up * 0.8f)).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (Vector3.Dot(normals[best], outward) > 0f)
            {
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                mesh.triangles = t;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                EditorUtility.SetDirty(mesh);
                var mc = go.GetComponent<MeshCollider>();
                if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = mesh; }
            }
        }

        // ------------------------------------------------------------------ ground
        static void BuildGround(List<Sample>[] paths)
        {
            const float X0 = -34f, X1 = 34f, Z0 = -28f, Z1 = 50f, Step = 0.75f;
            int nx = Mathf.CeilToInt((X1 - X0) / Step) + 1, nz = Mathf.CeilToInt((Z1 - Z0) / Step) + 1;
            var verts = new List<Vector3>(nx * nz); var uvs = new List<Vector2>(); var cols = new List<Color>();
            var dist = new float[nx * nz];
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float x = X0 + ix * Step, z = Z0 + iz * Step;
                var p = new Vector3(x, 0f, z);
                float d = NearestTrench(p, paths, out var axis, out var hw);
                float lip = LipDist(hw);
                if (d < lip)
                {
                    var dirv = p - axis; dirv.y = 0f;
                    if (dirv.sqrMagnitude < 1e-6f) dirv = Vector3.forward;
                    p = axis + dirv.normalized * lip;
                    d = lip;
                }
                float h = GroundHeight(p.x, p.z, d, hw, out float crater);
                p.y = h;
                verts.Add(p);
                dist[verts.Count - 1] = d;
                uvs.Add(new Vector2(p.x * 0.28f, p.z * 0.28f));
                float wet = Mathf.Clamp01(crater * 2.2f - 0.9f);
                cols.Add(new Color(Mathf.Lerp(1f, 0.7f, crater), wet, 0f, 1f));
            }
            var tris = new List<int>();
            for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx + 1, d = a + nx;
                TryTri(tris, verts, paths, a, d, c);
                TryTri(tris, verts, paths, a, c, b);
            }
            var mesh = SaveMesh("Ground", verts, uvs, cols, tris);
            var go = MeshObject("Ground", mesh, Field, true);
            EnsureFacing(mesh, go, verts, new Vector3(0f, G, 20f), Vector3.down);

            // far frame (coarse, slightly lower) so the horizon is not a cliff edge
            var far = new List<(Vector3 c, Vector3 s)>
            {
                (new Vector3(0f, G - 0.25f, Z1 + 60f), new Vector3(400f, 1f, 122f)),
                (new Vector3(0f, G - 0.25f, Z0 - 60f), new Vector3(400f, 1f, 122f)),
                (new Vector3(X1 + 60f, G - 0.25f, (Z0 + Z1) * 0.5f), new Vector3(122f, 1f, Z1 - Z0 + 2f)),
                (new Vector3(X0 - 60f, G - 0.25f, (Z0 + Z1) * 0.5f), new Vector3(122f, 1f, Z1 - Z0 + 2f)),
            };
            foreach (var (c, s) in far)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Plane);
                q.name = "FarGround";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(_root, false);
                q.transform.position = c;
                q.transform.localScale = s * 0.1f;
                q.GetComponent<Renderer>().sharedMaterial = Env("Env_FieldFar", "Env_Field", new Color(0.2f, 0.18f, 0.16f), 0.2f, 0f, 0.2f, 0.6f);
            }
        }

        static void TryTri(List<int> tris, List<Vector3> v, List<Sample>[] paths, int a, int b, int c)
        {
            var centroid = (v[a] + v[b] + v[c]) / 3f;
            float d = NearestTrench(centroid, paths, out _, out float hw);
            if (d < LipDist(hw) - 0.08f) return;
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        // ------------------------------------------------------------------ revetment, sandbags, duckboards
        static void BuildRevetment(List<Sample> main)
        {
            var root = Group("Revetment");
            for (int seg = 0; seg < MainPath.Length - 1; seg++)
            {
                Vector3 a = MainPath[seg], b = MainPath[seg + 1];
                Vector3 t = (b - a).normalized, n = new Vector3(-t.z, 0f, t.x);
                float len = Vector3.Distance(a, b);
                bool dugout = seg == 0;
                for (int side = -1; side <= 1; side += 2)
                {
                    // posts
                    for (float s = 0.35f; s < len - 0.2f; s += 1.45f)
                    {
                        Vector3 c = a + t * s;
                        float hw = MainHalfWidth(c.x);
                        var post = Prim(PrimitiveType.Cylinder, "Post", root, c + n * side * (hw - 0.03f) + Vector3.up * (G * 0.5f + 0.05f),
                            new Vector3(0.11f, G * 0.5f + 0.08f, 0.11f), Bark);
                        post.transform.localRotation = Quaternion.Euler(Rnd(-2f, 2f), Rnd(0f, 360f), Rnd(-2f, 2f));
                    }
                    if (dugout) continue; // dugout walls: logs below
                    // boards
                    for (int row = 0; row < 8; row++)
                    {
                        float y = 0.12f + row * 0.205f;
                        float s = 0.05f + Rnd(0f, 0.6f) * (row % 2);
                        while (s < len - 0.15f)
                        {
                            float bl = Mathf.Min(Rnd(1.5f, 2.4f), len - 0.1f - s);
                            if (bl > 0.3f && _rng.NextDouble() > 0.06)
                            {
                                Vector3 c = a + t * (s + bl * 0.5f);
                                float hw = MainHalfWidth(c.x) + 0.06f * y / G - 0.012f + Rnd(-0.008f, 0.008f);
                                var board = Prim(PrimitiveType.Cube, "Board", root, c + n * side * hw + Vector3.up * y,
                                    new Vector3(bl, 0.19f, 0.03f), row < 2 ? PlanksWet : Planks);
                                board.transform.localRotation = Quaternion.LookRotation(n * side) * Quaternion.Euler(-side * 2f, 90f, Rnd(-1.5f, 1.5f));
                                board.transform.localScale = new Vector3(0.03f, 0.19f, bl);
                                board.transform.localRotation = Quaternion.LookRotation(t) * Quaternion.Euler(0f, 0f, Rnd(-1.2f, 1.2f));
                            }
                            s += bl + 0.012f;
                        }
                    }
                }
            }
        }

        static Mesh _bagMesh;
        static Mesh BagMesh()
        {
            if (_bagMesh != null) return _bagMesh;
            const int lon = 10, lat = 6;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float v = (float)i / lat;
                float phi = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, v);
                for (int j = 0; j <= lon; j++)
                {
                    float u = (float)j / lon;
                    float th = u * Mathf.PI * 2f;
                    float cx = Mathf.Cos(phi) * Mathf.Cos(th), cz = Mathf.Cos(phi) * Mathf.Sin(th), cy = Mathf.Sin(phi);
                    // superellipsoid-ish: boxier sides, flat top/bottom (filled sack)
                    float sx = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), 0.55f);
                    float sz = Mathf.Sign(cz) * Mathf.Pow(Mathf.Abs(cz), 0.7f);
                    float sy = Mathf.Sign(cy) * Mathf.Pow(Mathf.Abs(cy), 0.6f);
                    verts.Add(new Vector3(sx * 0.5f, sy * 0.5f, sz * 0.5f));
                    uvs.Add(new Vector2(u * 2f, v));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            _bagMesh = SaveMesh("Sandbag", verts, uvs, null, tris);
            return _bagMesh;
        }

        static void Sandbag(Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Sandbag");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(0.5f * Rnd(0.92f, 1.06f), 0.15f * Rnd(0.9f, 1.15f), 0.3f * Rnd(0.92f, 1.05f));
            go.AddComponent<MeshFilter>().sharedMesh = BagMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Bag(_rng.Next(3));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static void BuildSandbags(List<Sample> main, List<Sample> branch)
        {
            var root = Group("Sandbags");
            PlaceBags(root, MainPath, 1, 3, 0f, 99f);   // front parapet: 3 rows
            PlaceBags(root, MainPath, -1, 1, 0f, 99f);  // parados
            PlaceBags(root, BranchPath, 1, 1, 0.5f, 4.5f);
            PlaceBags(root, BranchPath, -1, 1, 0.5f, 4.5f);
        }

        static void PlaceBags(Transform root, Vector3[] pts, int side, int rows, float from, float to)
        {
            float acc = 0f;
            for (int seg = 0; seg < pts.Length - 1; seg++)
            {
                Vector3 a = pts[seg], b = pts[seg + 1];
                Vector3 t = (b - a).normalized, n = new Vector3(-t.z, 0f, t.x);
                float len = Vector3.Distance(a, b);
                for (int row = 0; row < rows; row++)
                {
                    float off = row % 2 == 0 ? 0f : 0.23f;
                    for (float s = 0.2f + off; s < len - 0.15f; s += 0.47f)
                    {
                        if (acc + s < from || acc + s > to) continue;
                        Vector3 c = a + t * s;
                        float hw = pts == MainPath ? MainHalfWidth(c.x) : 0.5f;
                        if (pts == MainPath && c.x < DugoutEndX + 0.2f) continue; // dugout has its own roof
                        // on the lip of the cut (the wall bevel below is not solid ground), stacked with overlap
                        float d = LipDist(hw) + 0.08f - row * 0.03f;
                        Vector3 p = c + n * side * d;
                        if (_rng.NextDouble() < 0.05 && row == rows - 1) continue; // gaps in the top row
                        float baseH = GroundHeight(p.x, p.z, LipDist(hw), hw, out _);
                        p.y = baseH + 0.03f + row * 0.115f;
                        var rot = Quaternion.LookRotation(n * side) * Quaternion.Euler(Rnd(-4f, 4f) - side * 3f, 90f + Rnd(-7f, 7f), Rnd(-5f, 5f));
                        Sandbag(root, p, rot);
                    }
                }
                acc += len;
            }
        }

        static void BuildDuckboards(List<Sample> main, List<Sample> branch)
        {
            var root = Group("Duckboards");
            foreach (var path in new[] { main, branch })
            {
                float next = path == main ? 3.8f : 0.9f;
                foreach (var p in path)
                {
                    if (p.s < next) continue;
                    next = p.s + 1.22f;
                    if (path == main && p.c.x < DugoutEndX + 0.4f) continue;
                    var db = new GameObject("Duckboard").transform;
                    db.SetParent(root, false);
                    db.localPosition = new Vector3(p.c.x, 0.035f + Rnd(-0.01f, 0.012f), p.c.z);
                    db.localRotation = Quaternion.LookRotation(p.t) * Quaternion.Euler(Rnd(-1.5f, 1.5f), 90f + Rnd(-3f, 3f), Rnd(-1.5f, 1.5f));
                    var m = _rng.NextDouble() < 0.5 ? PlanksWet : Planks;
                    for (int s = -1; s <= 1; s += 2)
                        Prim(PrimitiveType.Cube, "Rail", db, new Vector3(0f, 0f, s * 0.24f), new Vector3(1.15f, 0.05f, 0.06f), m);
                    for (float sx = -0.5f; sx <= 0.51f; sx += 0.2f)
                    {
                        if (_rng.NextDouble() < 0.07) continue; // missing slat
                        var sl = Prim(PrimitiveType.Cube, "Slat", db, new Vector3(sx + Rnd(-0.015f, 0.015f), 0.035f, Rnd(-0.02f, 0.02f)),
                            new Vector3(0.11f, 0.022f, 0.64f), m);
                        sl.transform.localRotation = Quaternion.Euler(0f, Rnd(-2f, 2f), 0f);
                    }
                }
            }
        }

        static void BuildPuddles(List<Sample> main, List<Sample> branch)
        {
            var root = Group("Puddles");
            void P(List<Sample> path, float s, float r)
            {
                foreach (var p in path)
                    if (p.s >= s)
                    {
                        var go = new GameObject("Puddle");
                        go.transform.SetParent(root, false);
                        go.transform.localPosition = new Vector3(p.c.x + Rnd(-0.1f, 0.1f), -0.012f, p.c.z) + p.n * Rnd(-0.15f, 0.15f);
                        go.AddComponent<MeshFilter>().sharedMesh = PuddleMesh(r, _rng.Next(1000));
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = Puddle;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        return;
                    }
            }
            P(main, 1.2f, 0.75f); P(main, 4.6f, 0.55f); P(main, 8.3f, 0.6f); P(main, 13.6f, 0.5f); P(main, 16.4f, 0.7f); P(main, 19.6f, 0.55f);
            P(branch, 1.8f, 0.6f); P(branch, 5.2f, 0.7f); P(branch, 8.6f, 0.5f);
        }

        static Mesh PuddleMesh(float radius, int seed)
        {
            const int N = 18;
            var verts = new List<Vector3> { Vector3.zero }; var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var tris = new List<int>();
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                float r = radius * (0.65f + 0.45f * Mathf.PerlinNoise(seed * 0.37f + Mathf.Cos(a) * 1.3f, Mathf.Sin(a) * 1.3f));
                verts.Add(new Vector3(Mathf.Cos(a) * r * 1.4f, 0f, Mathf.Sin(a) * r * 0.75f));
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int i = 0; i < N; i++) { tris.Add(0); tris.Add(1 + (i + 1) % N); tris.Add(1 + i); }
            return SaveMesh("Puddle_" + seed, verts, uvs, null, tris);
        }

        static void BuildFiringSteps(List<Sample> main)
        {
            var root = Group("FiringSteps");
            void Step(float x0, float x1, float z, float side)
            {
                float len = x1 - x0;
                var c = new Vector3((x0 + x1) * 0.5f, 0.2f, z + side * (HalfWidth - 0.17f));
                Prim(PrimitiveType.Cube, "FiringStepEarth", root, c, new Vector3(len, 0.4f, 0.34f), SoilWall).AddComponent<BoxCollider>();
                Prim(PrimitiveType.Cube, "FiringStepTop", root, c + Vector3.up * 0.215f, new Vector3(len + 0.04f, 0.03f, 0.36f), Planks);
                Prim(PrimitiveType.Cube, "FiringStepFace", root, c + new Vector3(0f, 0f, -side * 0.18f), new Vector3(len, 0.4f, 0.03f), PlanksWet);
            }
            Step(-4.1f, -2.9f, 0f, 1f);
            Step(7.4f, 9.4f, 1.5f, 1f);
        }

        // ------------------------------------------------------------------ dugout
        static void BuildDugout()
        {
            var root = Group("Dugout");
            float x0 = MainPath[0].x - 0.1f, x1 = DugoutEndX;
            float z = DugoutCentre.z, hw = 0.95f;
            float roofY = G + 0.08f;
            // log walls inside the chamber
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 8; i++)
                {
                    float y = 0.12f + i * 0.215f;
                    var log = Prim(PrimitiveType.Cylinder, "WallLog", root, new Vector3((x0 + x1) * 0.5f, y, z + side * (hw - 0.02f)),
                        new Vector3(0.21f, (x1 - x0) * 0.5f, 0.21f), Bark);
                    log.transform.localRotation = Quaternion.Euler(Rnd(-1f, 1f), 0f, 90f);
                }
            // beams and roof logs across the chamber
            for (int side = -1; side <= 1; side += 2)
            {
                var beam = Prim(PrimitiveType.Cylinder, "Beam", root, new Vector3((x0 + x1) * 0.5f, roofY - 0.1f, z + side * (hw - 0.08f)),
                    new Vector3(0.22f, (x1 - x0) * 0.5f + 0.25f, 0.22f), Bark);
                beam.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            for (float x = x0 + 0.1f; x <= x1 + 0.35f; x += 0.235f)
            {
                var log = Prim(PrimitiveType.Cylinder, "RoofLog", root, new Vector3(x, roofY + 0.11f + Rnd(-0.015f, 0.015f), z + Rnd(-0.08f, 0.08f)),
                    new Vector3(0.22f + Rnd(-0.02f, 0.03f), hw + 0.55f, 0.22f), Bark);
                log.transform.localRotation = Quaternion.Euler(90f, Rnd(-3f, 3f), 0f);
            }
            // earth cover over the log roof: heightfield (single-sided, so from inside only the logs are seen)
            BuildDugoutMound(root, roofY + 0.24f);
            for (int i = 0; i < 9; i++)
            {
                float x = Mathf.Lerp(x0 + 0.1f, x1 - 0.3f, i / 8f);
                float zz = z + (i % 2 == 0 ? -1f : 1f) * Rnd(0.9f, 1.3f);
                Sandbag(root, new Vector3(x, MoundHeight(x, zz, roofY + 0.24f) + 0.04f, zz), Quaternion.Euler(Rnd(-6f, 6f), Rnd(-20f, 20f), Rnd(-6f, 6f)));
            }
            for (int i = 0; i < 5; i++) // bags over the door
                Sandbag(root, new Vector3(x1 + 0.05f, roofY + 0.36f + (i / 3) * 0.11f, z - 0.75f + (i % 3) * 0.5f + (i / 3) * 0.25f), Quaternion.Euler(Rnd(-4f, 4f), 90f + Rnd(-8f, 8f), Rnd(-4f, 4f)));
            // doorway frame + tarp half pulled aside
            for (int side = -1; side <= 1; side += 2)
                Prim(PrimitiveType.Cylinder, "DoorPost", root, new Vector3(x1 + 0.05f, roofY * 0.5f, z + side * 0.62f), new Vector3(0.14f, roofY * 0.5f, 0.14f), Bark);
            var lintel = Prim(PrimitiveType.Cylinder, "Lintel", root, new Vector3(x1 + 0.05f, roofY - 0.05f, z), new Vector3(0.18f, 0.75f, 0.18f), Bark);
            lintel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tarp = Prim(PrimitiveType.Quad, "DoorTarp", root, new Vector3(x1 + 0.12f, roofY * 0.52f, z - 0.42f), new Vector3(0.55f, roofY - 0.1f, 1f), Tarp);
            tarp.transform.localRotation = Quaternion.Euler(0f, -90f + 12f, 2f);
            Object.DestroyImmediate(tarp.GetComponent<Collider>());
            var tarpBack = Object.Instantiate(tarp, root);
            tarpBack.name = "DoorTarpBack";
            tarpBack.transform.localRotation = tarp.transform.localRotation * Quaternion.Euler(0f, 180f, 0f);
            // floor boards
            for (float x = x0 + 0.15f; x < x1; x += 0.24f)
                Prim(PrimitiveType.Cube, "FloorBoard", root, new Vector3(x, 0.04f, z), new Vector3(0.22f, 0.03f, 2f * hw - 0.1f), PlanksWet);
            // bench + crate + candle (the only warm light: tiny, dim)
            Prim(PrimitiveType.Cube, "Bench", root, new Vector3(-8.6f, 0.42f, z - hw + 0.28f), new Vector3(1.8f, 0.05f, 0.42f), Planks);
            for (int i = -1; i <= 1; i += 2)
                Prim(PrimitiveType.Cube, "BenchLeg", root, new Vector3(-8.6f + i * 0.75f, 0.21f, z - hw + 0.28f), new Vector3(0.06f, 0.42f, 0.36f), Planks);
            var crate = Prim(PrimitiveType.Cube, "DugoutCrate", root, new Vector3(-9.1f, 0.22f, z + 0.5f), new Vector3(0.62f, 0.4f, 0.4f), Crate);
            crate.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
            var candle = Prim(PrimitiveType.Cylinder, "TrenchCandle", root, new Vector3(-9.0f, 0.47f, z + 0.48f), new Vector3(0.07f, 0.03f, 0.07f), Rust);
            var lgo = new GameObject("CandleLight");
            lgo.transform.SetParent(candle.transform.parent, false);
            lgo.transform.position = candle.transform.position + Vector3.up * 0.12f;
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.22f);
            l.intensity = 0.55f;
            l.range = 2.6f;
            l.shadows = LightShadows.None;
            lgo.AddComponent<Environment.CandleFlicker>();
        }

        const float MoundX0 = -12.2f, MoundX1 = -6.45f, MoundZ0 = -4.4f, MoundZ1 = 1.4f;

        /// <summary>Earth cover height: full over the dugout + its cut, blending to the field around.</summary>
        static float MoundHeight(float x, float z, float top)
        {
            float dz = Mathf.Max(0f, Mathf.Abs(z - DugoutCentre.z) - 1.75f);
            float dx = Mathf.Max(0f, -11.4f - x);
            float edge = Mathf.Sqrt(dx * dx + dz * dz);
            float w = 1f - Mathf.SmoothStep(0f, 1f, edge / 1.3f);
            float dome = top + 0.28f * Mathf.Clamp01(1f - Mathf.Abs(z - DugoutCentre.z) / 2.2f) * Mathf.Clamp01((x - MoundX0) / 3f);
            float ground = GroundHeight(x, z, 99f, HalfWidth, out _) - 0.05f;
            return Mathf.Lerp(ground, dome, w) + Perlin(x * 1.3f, z * 1.3f) * 0.08f;
        }

        static void BuildDugoutMound(Transform root, float top)
        {
            const float Step = 0.22f;
            int nx = Mathf.CeilToInt((MoundX1 - MoundX0) / Step) + 1, nz = Mathf.CeilToInt((MoundZ1 - MoundZ0) / Step) + 1;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float x = Mathf.Min(MoundX0 + ix * Step, MoundX1), z = MoundZ0 + iz * Step;
                verts.Add(new Vector3(x, MoundHeight(x, z, top), z));
                uvs.Add(new Vector2(x * 0.5f, z * 0.5f));
                cols.Add(new Color(1f, 0.1f, 0f, 1f));
            }
            for (int iz = 0; iz < nz - 1; iz++)
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    int a = iz * nx + ix;
                    Quad(tris, a, a + nx, a + nx + 1, a + 1, false);
                }
            var mesh = SaveMesh("DugoutMound", verts, uvs, cols, tris);
            var go = MeshObject("DugoutMound", mesh, SoilWall, false);
            go.transform.SetParent(root, true);
            EnsureFacing(mesh, go, verts, new Vector3(-9f, top, DugoutCentre.z), Vector3.down);

            // front face above the door (east edge, x = MoundX1) down to the lintel
            var fv = new List<Vector3>(); var fu = new List<Vector2>(); var fc = new List<Color>(); var ft = new List<int>();
            for (int iz = 0; iz < nz; iz++)
            {
                float z = MoundZ0 + iz * Step;
                fv.Add(new Vector3(MoundX1, MoundHeight(MoundX1, z, top), z));
                fv.Add(new Vector3(MoundX1 + 0.05f, top - 0.32f, z));
                fu.Add(new Vector2(z * 0.5f, 0.6f)); fu.Add(new Vector2(z * 0.5f, 0.2f));
                fc.Add(new Color(0.8f, 0.1f, 0f, 1f)); fc.Add(new Color(0.6f, 0.2f, 0f, 1f));
            }
            for (int iz = 0; iz < nz - 1; iz++)
            {
                int a = iz * 2;
                Quad(ft, a, a + 2, a + 3, a + 1, false);
            }
            var fmesh = SaveMesh("DugoutFront", fv, fu, fc, ft);
            var fgo = MeshObject("DugoutFront", fmesh, SoilWall, false);
            fgo.transform.SetParent(root, true);
            EnsureFacing(fmesh, fgo, fv, new Vector3(MoundX1 + 0.8f, top - 0.9f, DugoutCentre.z), Vector3.left);
        }

        // ------------------------------------------------------------------ props
        static void BuildProps()
        {
            var root = Group("Props");
            // ammo crates (wooden, olive) stacked against the back wall of the east bay and near the dugout
            Box(root, "AmmoCrate", new Vector3(7.6f, 0.17f, 1.5f - 0.38f), new Vector3(0.62f, 0.26f, 0.32f), 4f, Crate);
            Box(root, "AmmoCrate", new Vector3(7.65f, 0.43f, 1.5f - 0.38f), new Vector3(0.62f, 0.26f, 0.32f), -6f, Crate);
            Box(root, "AmmoCrate", new Vector3(8.35f, 0.17f, 1.5f - 0.36f), new Vector3(0.62f, 0.26f, 0.32f), 11f, Crate);
            Box(root, "AmmoCrate", new Vector3(-5.9f, 0.17f, -0.95f), new Vector3(0.62f, 0.26f, 0.32f), 40f, Crate);
            // metal ammo cans
            Box(root, "AmmoCan", new Vector3(-5.6f, 0.13f, -0.55f), new Vector3(0.3f, 0.19f, 0.15f), 25f, Rust);
            Box(root, "AmmoCan", new Vector3(8.9f, 0.13f, 1.12f), new Vector3(0.3f, 0.19f, 0.15f), -10f, Rust);
            // shovel leaning on the wall
            var handle = Prim(PrimitiveType.Cylinder, "ShovelHandle", root, new Vector3(-1.6f, 0.62f, -0.5f), new Vector3(0.04f, 0.55f, 0.04f), Planks);
            handle.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            var blade = Prim(PrimitiveType.Cube, "ShovelBlade", root, new Vector3(-1.6f, 0.08f, -0.33f), new Vector3(0.18f, 0.22f, 0.012f), Rust);
            blade.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
        }

        static void Box(Transform root, string name, Vector3 pos, Vector3 size, float yaw, Material m)
        {
            var b = Prim(PrimitiveType.Cube, name, root, pos, size, m);
            b.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        static void BuildCables(List<Sample> main)
        {
            var root = Group("Cables");
            // field telephone cable pinned to the posts of the back wall, sagging between them
            var pts = new List<Vector3>();
            for (float x = -4.5f; x <= 4.7f; x += 0.15f)
            {
                float f = Mathf.Repeat(x + 4.6f, 1.45f) / 1.45f;
                pts.Add(new Vector3(x, 1.52f - 0.09f * Mathf.Sin(f * Mathf.PI), -HalfWidth - 0.03f));
            }
            Tube(root, "FieldCable", pts, 0.006f);
            // second cable along the floor edge into the dugout
            var p2 = new List<Vector3>();
            for (float s = 0f; s <= 1f; s += 0.04f)
            {
                var a = new Vector3(-3.0f, 0.02f, -0.5f); var b = new Vector3(-6.4f, 0.03f, -1.95f);
                var p = Vector3.Lerp(a, b, s);
                p.y += Mathf.PerlinNoise(s * 6f, 3f) * 0.03f;
                p2.Add(p);
            }
            Tube(root, "FloorCable", p2, 0.007f);
        }

        static void Tube(Transform root, string name, List<Vector3> pts, float radius)
        {
            const int Sides = 4;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 t = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 n = Vector3.Cross(t, Vector3.up).normalized; Vector3 b = Vector3.Cross(t, n);
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * Mathf.PI * 2f / Sides;
                    verts.Add(pts[i] + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * radius);
                    uvs.Add(new Vector2(i * 0.1f, k / (float)Sides));
                }
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < Sides; k++)
                {
                    int a = i * Sides + k, b = i * Sides + (k + 1) % Sides;
                    Quad(tris, a, b, b + Sides, a + Sides, false);
                }
            var mesh = SaveMesh(name, verts, uvs, null, tris);
            MeshObject(name, mesh, Cable, false).transform.SetParent(root, true);
        }

        static void BuildNet()
        {
            var root = Group("AntiDroneNet");
            // over the east traverse + bay, sagging between poles on the berms
            const int NX = 22, NZ = 6;
            float x0 = 4.6f, x1 = 10.6f;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int iz = 0; iz <= NZ; iz++)
            for (int ix = 0; ix <= NX; ix++)
            {
                float fx = ix / (float)NX, fz = iz / (float)NZ;
                float x = Mathf.Lerp(x0, x1, fx);
                float zc = x < 6.4f ? Mathf.Lerp(0f, 1.5f, Mathf.InverseLerp(4.8f, 6.4f, x)) : 1.5f;
                float z = zc + Mathf.Lerp(-1.55f, 1.55f, fz);
                float sag = 0.35f * Mathf.Sin(fz * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(fx * Mathf.PI * 3f + 1f));
                verts.Add(new Vector3(x, G + 0.75f - sag + Perlin(x * 2f, z * 2f) * 0.08f, z));
                uvs.Add(new Vector2(fx, fz));
            }
            for (int iz = 0; iz < NZ; iz++)
                for (int ix = 0; ix < NX; ix++)
                {
                    int a = iz * (NX + 1) + ix;
                    Quad(tris, a, a + 1, a + NX + 2, a + NX + 1, false);
                }
            var mesh = SaveMesh("AntiDroneNet", verts, uvs, null, tris);
            var go = MeshObject("Net", mesh, NetMaterial(), false);
            go.transform.SetParent(root, true);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var p in new[] { new Vector3(4.6f, 0f, -1.55f), new Vector3(4.6f, 0f, 1.55f), new Vector3(10.6f, 0f, -0.05f), new Vector3(10.6f, 0f, 3.05f), new Vector3(7.6f, 0f, -0.05f), new Vector3(7.6f, 0f, 3.05f) })
            {
                float h = GroundHeight(p.x, p.z, 1.6f, HalfWidth, out _);
                var pole = Prim(PrimitiveType.Cylinder, "NetPole", root, new Vector3(p.x, h + 0.38f, p.z), new Vector3(0.06f, 0.45f, 0.06f), Bark);
                pole.transform.localRotation = Quaternion.Euler(Rnd(-5f, 5f), 0f, Rnd(-5f, 5f));
            }
        }

        // ------------------------------------------------------------------ field beyond the parapet
        static void BuildField()
        {
            var root = Group("Field");
            // burnt stumps and broken trunks (a former tree line)
            for (int i = 0; i < 16; i++)
            {
                float x = Rnd(-30f, 30f), z = i < 11 ? Rnd(14f, 42f) : Rnd(-22f, -8f);
                float h = _rng.NextDouble() < 0.6 ? Rnd(0.3f, 1.2f) : Rnd(2f, 4.5f);
                float r = Rnd(0.18f, 0.4f);
                float y = GroundHeight(x, z, 99f, HalfWidth, out _);
                var st = Prim(PrimitiveType.Cylinder, "BurntStump", root, new Vector3(x, y + h * 0.5f - 0.1f, z), new Vector3(r, h * 0.5f, r), BarkBurnt);
                st.transform.localRotation = Quaternion.Euler(Rnd(-8f, 8f), Rnd(0f, 360f), Rnd(-8f, 8f));
                Object.DestroyImmediate(st.GetComponent<Collider>());
                if (h > 2f && _rng.NextDouble() < 0.6)
                {
                    var br = Prim(PrimitiveType.Cylinder, "Branch", root, st.transform.position + Vector3.up * h * 0.3f, new Vector3(r * 0.35f, h * 0.22f, r * 0.35f), BarkBurnt);
                    br.transform.localRotation = Quaternion.Euler(Rnd(30f, 60f), Rnd(0f, 360f), 0f);
                    Object.DestroyImmediate(br.GetComponent<Collider>());
                }
            }
            for (int i = 0; i < 4; i++)
            {
                float x = Rnd(-25f, 25f), z = Rnd(10f, 35f);
                float y = GroundHeight(x, z, 99f, HalfWidth, out _);
                var log = Prim(PrimitiveType.Cylinder, "FallenTrunk", root, new Vector3(x, y + 0.12f, z), new Vector3(0.3f, Rnd(1.5f, 3f), 0.3f), BarkBurnt);
                log.transform.localRotation = Quaternion.Euler(90f, Rnd(0f, 180f), 0f);
                Object.DestroyImmediate(log.GetComponent<Collider>());
            }
            // concertina wire (line renderers) on stakes
            var wireMat = WireMaterial();
            foreach (var zLine in new[] { 11.5f, 14.2f })
            {
                var go = new GameObject("Concertina");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                var pts = new List<Vector3>();
                for (float x = -26f; x <= 26f; x += 0.045f)
                {
                    float a = x / 0.16f * Mathf.PI * 2f * 0.5f;
                    float baseY = GroundHeight(x, zLine, 99f, HalfWidth, out _);
                    pts.Add(new Vector3(x + Mathf.Sin(a) * 0.08f, baseY + 0.42f + Mathf.Cos(a) * 0.4f, zLine + Mathf.Sin(a) * 0.4f));
                }
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr.widthMultiplier = 0.01f;
                lr.sharedMaterial = wireMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                for (float x = -25f; x <= 25f; x += 3.1f)
                {
                    float y = GroundHeight(x, zLine, 99f, HalfWidth, out _);
                    var stake = Prim(PrimitiveType.Cylinder, "WireStake", root, new Vector3(x, y + 0.45f, zLine), new Vector3(0.035f, 0.5f, 0.035f), Rust);
                    stake.transform.localRotation = Quaternion.Euler(Rnd(-8f, 8f), 0f, Rnd(-8f, 8f));
                    Object.DestroyImmediate(stake.GetComponent<Collider>());
                }
            }
        }

        static Material WireMaterial()
        {
            string path = $"{MatDir}/Env_Wire.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("FieldMedic/Particle");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_TintColor", new Color(0.09f, 0.085f, 0.08f, 0.9f));
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ helpers
        static float Rnd(float a, float b) => (float)(a + _rng.NextDouble() * (b - a));

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root, false);
            return t;
        }

        static void Quad(List<int> tris, int a, int b, int c, int d, bool flip)
        {
            if (!flip) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c); }
        }

        static Mesh SaveMesh(string name, List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (cols != null) mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject MeshObject(string name, Mesh mesh, Material m, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            var c = go.GetComponent<Collider>();
            if (c != null && type != PrimitiveType.Cube) Object.DestroyImmediate(c); // keep box colliders for boards/crates only
            return go;
        }
    }
}
