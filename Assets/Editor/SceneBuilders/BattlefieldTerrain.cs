using System.Collections.Generic;
using System.IO;
using FieldMedic.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Ground of the V2 steppe: a 400×400 m mesh in 4 quadrant chunks whose grid is dense at the centre (0.5 m)
    /// and coarse at the edge (~10 m) — x = 50u + 150u⁵ — so craters around our position are smooth while the
    /// whole field stays ~51k triangles. Height = <see cref="BattlefieldLayout.GroundHeight"/>; vertex colours
    /// for <c>FieldMedic/GroundBlend</c>: r = bare dirt (tracks, trampled ground, crater ejecta), g = scorch,
    /// b = AO (crater bowls). Plus dry-grass tufts (Poly Haven clumps split per tuft, combined per sector).
    /// </summary>
    public static class BattlefieldTerrain
    {
        const int Segs = 160;                 // per axis
        const float A = 50f, B = 150f;        // x = A u + B u⁵  (u ∈ [-1, 1]): ~0.65 m out to 25 m, 10 m at the rim

        public static float Map(float u) => A * u + B * u * u * u * u * u;

        /// <summary>Vehicle track from the rear (LZ side) past the burnt APC out toward the front.</summary>
        public static readonly Vector3[] Track =
        {
            new Vector3(-70f, 0f, -200f), new Vector3(-32f, 0f, -96f), new Vector3(-9f, 0f, -52f), new Vector3(4f, 0f, -22f),
            new Vector3(10f, 0f, -6f), new Vector3(13f, 0f, 6f), new Vector3(19f, 0f, 38f), new Vector3(30f, 0f, 110f), new Vector3(46f, 0f, 200f),
        };

        public static float DirtAt(float x, float z, out float burn, out float ao)
        {
            BattlefieldLayout.CraterOffset(x, z, out burn);
            ao = 1f;
            float dirt = 0f;
            // track: two wheel ruts 1.9 m apart + a worn band
            float d = TrenchBuilder.DistToPolyline(new Vector3(x, 0f, z), Track);
            float ruts = Mathf.Max(Gauss(d - 0.95f, 0.35f), Gauss(d + 0.95f, 0.35f));
            dirt = Mathf.Max(dirt, Mathf.Max(ruts * 0.85f, Gauss(d, 1.1f) * 0.35f));
            // trampled ground around our position (A) and the second ruin
            dirt = Mathf.Max(dirt, Box(x, z, -8.5f, -4.5f, 5f, 7.5f, 2.5f) * 0.75f);
            dirt = Mathf.Max(dirt, Box(x, z, -22f, 4f, -12f, 12f, 2.5f) * 0.6f);
            // the drag line from Erlan to the doorway: churned by boots and the body
            var a = BattlefieldLayout.ErlanFallPoint; var b = BattlefieldLayout.DoorwayCentre + Vector3.right * 0.8f;
            dirt = Mathf.Max(dirt, Gauss(TrenchBuilder.DistToPolyline(new Vector3(x, 0f, z), new[] { a, b }), 0.6f) * 0.55f);
            // the LZ: rotor wash blew the grass flat
            float lz = Vector2.Distance(new Vector2(x, z), new Vector2(BattlefieldLayout.LandingZone.x, BattlefieldLayout.LandingZone.z));
            dirt = Mathf.Max(dirt, Mathf.Clamp01(1.3f - lz / 9f) * 0.7f);
            // crater ejecta + bowls
            foreach (var c in BattlefieldLayout.Craters)
            {
                float r = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y)) / c.z;
                if (r > 2.6f) continue;
                dirt = Mathf.Max(dirt, Mathf.Clamp01(2.4f - r) * 0.9f);
                if (r < 1f) ao = Mathf.Min(ao, Mathf.Lerp(0.55f, 1f, r));
            }
            // patchy bare ground everywhere on the steppe
            float patch = Mathf.PerlinNoise(x * 0.07f + 5f, z * 0.07f + 11f);
            dirt = Mathf.Max(dirt, Mathf.Clamp01((patch - 0.62f) * 3f) * 0.6f);
            return Mathf.Clamp01(dirt);
        }

        static float Gauss(float d, float w) => Mathf.Exp(-(d * d) / (w * w));

        static float Box(float x, float z, float x0, float z0, float x1, float z1, float fall)
        {
            float dx = Mathf.Max(0f, Mathf.Max(x0 - x, x - x1)), dz = Mathf.Max(0f, Mathf.Max(z0 - z, z - z1));
            return Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / fall);
        }

        public static Transform BuildGround(Transform parent, Material m, string meshDir)
        {
            var root = new GameObject("Ground").transform;
            root.SetParent(parent, false);
            int half = Segs / 2;
            for (int qx = 0; qx < 2; qx++)
            for (int qz = 0; qz < 2; qz++)
            {
                var v = new List<Vector3>();
                var c = new List<Color>();
                var t = new List<int>();
                int i0 = qx * half, j0 = qz * half;
                for (int j = 0; j <= half; j++)
                for (int i = 0; i <= half; i++)
                {
                    float x = Map((i0 + i) / (float)half - 1f), z = Map((j0 + j) / (float)half - 1f);
                    float y = BattlefieldLayout.GroundHeight(x, z);
                    v.Add(new Vector3(x, y, z));
                    float dirt = DirtAt(x, z, out float burn, out float ao);
                    c.Add(new Color(dirt, burn, ao, 1f));
                }
                int row = half + 1;
                for (int j = 0; j < half; j++)
                for (int i = 0; i < half; i++)
                {
                    int a = j * row + i, b = a + 1, d = a + row, e = d + 1;
                    t.Add(a); t.Add(d); t.Add(b);
                    t.Add(b); t.Add(d); t.Add(e);
                }
                var mesh = new Mesh { name = $"Ground_{qx}{qz}" };
                mesh.SetVertices(v);
                mesh.SetColors(c);
                mesh.SetTriangles(t, 0);
                // smooth analytic normals (finite differences on the height function, no seams between chunks)
                var n = new Vector3[v.Count];
                for (int k = 0; k < v.Count; k++)
                {
                    float x = v[k].x, z = v[k].z, e = 0.25f;
                    float hx = BattlefieldLayout.GroundHeight(x + e, z) - BattlefieldLayout.GroundHeight(x - e, z);
                    float hz = BattlefieldLayout.GroundHeight(x, z + e) - BattlefieldLayout.GroundHeight(x, z - e);
                    n[k] = new Vector3(-hx, 2f * e, -hz).normalized;
                }
                mesh.normals = n;
                mesh.RecalculateBounds();
                string path = $"{meshDir}/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
            return root;
        }

        // ------------------------------------------------------------------ grass tufts
        /// <summary>Splits the Poly Haven grass strip (several clumps side by side along x) into single tufts.</summary>
        public static List<Mesh> SplitTufts(Mesh strip, int bins, string meshDir, string tag)
        {
            var res = new List<Mesh>();
            if (strip == null) return res;
            var v = strip.vertices; var n = strip.normals; var uv = strip.uv; var tris = strip.triangles;
            var b = strip.bounds;
            float w = b.size.x / bins;
            for (int k = 0; k < bins; k++)
            {
                var map = new Dictionary<int, int>();
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var nt = new List<int>();
                float lo = b.min.x + k * w, hi = lo + w;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    float cx = (v[tris[i]].x + v[tris[i + 1]].x + v[tris[i + 2]].x) / 3f;
                    if (cx < lo || cx >= hi) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int src = tris[i + j];
                        if (!map.TryGetValue(src, out int dst))
                        {
                            dst = nv.Count; map[src] = dst;
                            nv.Add(v[src]); nn.Add(n.Length > 0 ? n[src] : Vector3.up); nuv.Add(uv.Length > 0 ? uv[src] : Vector2.zero);
                        }
                        nt.Add(dst);
                    }
                }
                if (nt.Count < 30) continue;
                // recentre on the tuft's own foot
                Vector3 c = Vector3.zero; foreach (var p in nv) c += p; c /= nv.Count;
                float minY = float.MaxValue; foreach (var p in nv) minY = Mathf.Min(minY, p.y);
                for (int i = 0; i < nv.Count; i++) nv[i] -= new Vector3(c.x, minY, c.z);
                var m = new Mesh { name = $"Tuft_{tag}_{k}" };
                m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nuv); m.SetTriangles(nt, 0);
                m.RecalculateBounds();
                res.Add(m);
            }
            return res;
        }

        public static void ScatterGrass(Transform parent, GameObject grassPrefab, string meshDir, System.Func<Vector3, bool> blocked)
        {
            if (grassPrefab == null) { Debug.LogWarning("[FMVR] grass prefab missing"); return; }
            Mesh lod0 = null, lod1 = null;
            Material mat = null;
            foreach (var mf in grassPrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.EndsWith("_LOD0") || lod0 == null && !mf.name.Contains("_LOD")) { lod0 = mf.sharedMesh; mat = mf.GetComponent<Renderer>().sharedMaterial; }
                if (mf.name.EndsWith("_LOD1")) lod1 = mf.sharedMesh;
            }
            if (lod1 == null) lod1 = lod0;
            var near = SplitTufts(lod0, 6, meshDir, "near");
            var far = SplitTufts(lod1, 6, meshDir, "far");
            if (near.Count == 0) { Debug.LogWarning("[FMVR] grass split failed"); return; }
            if (far.Count == 0) far = near;
            var twoSided = TwoSided(mat);

            const int sectors = 6;
            var groups = new Dictionary<int, List<CombineInstance>>();
            var rng = new System.Random(4242);
            int placed = 0;
            for (int attempt = 0; attempt < 9000 && placed < 340; attempt++)
            {
                float r = 2f + Mathf.Sqrt((float)rng.NextDouble()) * 40f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                // patchy: tufts gather where the noise says "grass", never on dirt/crater/LZ
                float dens = Mathf.PerlinNoise(x * 0.18f + 3f, z * 0.18f + 9f);
                float dirt = DirtAt(x, z, out float burn, out _);
                if (dens < 0.42f || dirt > 0.35f || burn > 0.2f) continue;
                var p = new Vector3(x, BattlefieldLayout.GroundHeight(x, z), z);
                if (blocked(p)) continue;
                bool isNear = r < 9f;
                var list = isNear ? near : far;
                var mesh = list[rng.Next(list.Count)];
                float s = 1.3f + (float)rng.NextDouble() * 1.6f;
                var trs = Matrix4x4.TRS(p - Vector3.up * 0.02f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(s, s * (0.9f + (float)rng.NextDouble() * 0.7f), s));
                int sector = Mathf.FloorToInt((a / (Mathf.PI * 2f)) * sectors) % sectors + (isNear ? 0 : sectors);
                if (!groups.TryGetValue(sector, out var g)) groups[sector] = g = new List<CombineInstance>();
                g.Add(new CombineInstance { mesh = mesh, transform = trs });
                placed++;
            }
            var root = new GameObject("GrassTufts").transform;
            root.SetParent(parent, false);
            int tris = 0;
            foreach (var kv in groups)
            {
                var m = new Mesh { name = "Grass_" + kv.Key, indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), true, true);
                m.RecalculateBounds();
                tris += m.triangles.Length / 3;
                string path = $"{meshDir}/{m.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                var go = new GameObject(m.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = twoSided;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
            Debug.Log($"[FMVR] grass: {placed} tufts in {groups.Count} meshes, {tris} tris");
        }

        /// <summary>Straw-coloured copy of the (green) Poly Haven grass albedo: late-autumn steppe grass.
        /// Derived from a CC0 texture, so it is committed under Assets/Art/Battlefield.</summary>
        static Texture2D DryGrassTexture(Texture2D src)
        {
            if (src == null) return null;
            string outPath = BattlefieldBuilder.ArtDir + "/grass_dry_albedo.png";
            if (!File.Exists(outPath))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(src)));
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                    float lum = r * 0.3f + g * 0.59f + b * 0.11f;
                    // straw: luminance ramp from dusty brown to pale khaki, a little of the old hue kept
                    var straw = Color.Lerp(new Color(0.36f, 0.3f, 0.2f), new Color(0.86f, 0.78f, 0.56f), Mathf.Clamp01(lum * 1.7f));
                    var c = Color.Lerp(straw, new Color(r, g, b), 0.18f);
                    px[i] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), px[i].a);
                }
                t.SetPixels32(px);
                t.Apply();
                File.WriteAllBytes(outPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(outPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
                ti.alphaIsTransparency = true;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static Material TwoSided(Material src)
        {
            string path = BattlefieldBuilder.OverrideDir + "/grass_two_sided.mat";
            Directory.CreateDirectory(BattlefieldBuilder.OverrideDir);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            m.shader = src.shader;
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            // dry steppe: bleach the green toward straw
            var dry = DryGrassTexture(src.GetTexture("_BaseMap") as Texture2D);
            if (dry != null) m.SetTexture("_BaseMap", dry);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.95f, 0.92f, 0.86f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
