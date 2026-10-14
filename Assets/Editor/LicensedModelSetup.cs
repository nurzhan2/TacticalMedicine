using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Import side of tools/bk_fetch.py + tools/bk_export.py. Every exported model folder holds
    /// &lt;slug&gt;.fbx, Textures/*.png and &lt;slug&gt;.materials.json. This class
    ///  - sets texture/model import settings (normal maps, linear MS maps, ASTC, 1K, Humanoid for soldiers),
    ///  - builds URP Lit materials from the JSON and remaps the FBX materials to them,
    ///  - makes a ready prefab next to the FBX: colliders (Cover tag/layer for ruins, cover and vehicles), static flags.
    /// Menu: Field Medic/Assets/Setup Imported Models  (batch: -executeMethod FieldMedic.EditorTools.LicensedModelSetup.SetupAll)
    /// </summary>
    public class LicensedModelSetup : AssetPostprocessor
    {
        public const string LicensedRoot = "Assets/_Licensed";
        public const string ThirdPartyRoot = "Assets/ThirdParty";
        public const string MeshyRoot = "Assets/Art/Meshy";
        public const string CoverTag = "Cover";
        public const string CoverLayer = "Cover";

        static readonly string[] Roots = { LicensedRoot, ThirdPartyRoot, MeshyRoot };

        public static bool IsManaged(string path)
        {
            path = path.Replace('\\', '/');
            foreach (var r in Roots) if (path.StartsWith(r + "/")) return true;
            return false;
        }

        /// <summary>Category = folder under BlenderKit/ (ruins, cover, vehicles, aircraft, soldiers, props), else "props".</summary>
        public static string CategoryOf(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "BlenderKit" || parts[i] == "PolyHaven" || (parts[i] == "Meshy" && i + 2 < parts.Length - 1)) return parts[i + 1];
            return path.Contains("/Meshy/") ? "meshy" : "props";
        }

        public static bool IsCoverCategory(string cat) => cat == "ruins" || cat == "cover" || cat == "vehicles";

        // ------------------------------------------------------------------ import settings
        void OnPreprocessTexture()
        {
            if (!IsManaged(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (n.EndsWith("_normal") || n.EndsWith("_nor_gl") || n.EndsWith("_nor"))
                ti.textureType = TextureImporterType.NormalMap;
            else if (n.EndsWith("_ms") || n.EndsWith("_rough") || n.EndsWith("_arm"))
                ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = n.Contains("hdri") || assetPath.EndsWith(".hdr") || assetPath.EndsWith(".exr") ? 2048 : 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = ti.maxTextureSize;
            android.format = ti.textureType == TextureImporterType.NormalMap ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            if (assetPath.EndsWith(".hdr") || assetPath.EndsWith(".exr")) android.format = TextureImporterFormat.ASTC_HDR_6x6;
            ti.SetPlatformTextureSettings(android);
        }

        void OnPreprocessModel()
        {
            if (!IsManaged(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            string cat = CategoryOf(assetPath);
            bool rigged = cat == "soldiers" || ReadOrEmpty(SidePath(assetPath, ".source.json")).Contains("\"rigged\": true");
            if (rigged)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.optimizeGameObjects = false;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                // static scenery gets lightmap UVs (w2 bakes lighting); fly-by aircraft and hand items do not need them
                mi.generateSecondaryUV = cat == "ruins" || cat == "cover" || cat == "vehicles";
            }
        }

        static string ReadOrEmpty(string p) => File.Exists(p) ? File.ReadAllText(p) : "";

        static string SidePath(string fbxPath, string ext) =>
            Path.Combine(Path.GetDirectoryName(fbxPath), Path.GetFileNameWithoutExtension(fbxPath) + ext).Replace('\\', '/');

        // ------------------------------------------------------------------ materials + prefabs
        [MenuItem("Field Medic/Assets/Setup Imported Models")]
        public static void SetupAll()
        {
            EnsureCoverTagAndLayer();
            int n = 0, fail = 0;
            var fbxs = new List<string>();
            foreach (var root in Roots)
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) && File.Exists(SidePath(p, ".materials.json"))) fbxs.Add(p);
                }
            }
            foreach (var p in fbxs)
            {
                try { if (SetupModel(p) != null) n++; else fail++; }
                catch (System.Exception e) { fail++; Debug.LogError("[FMVR] setup failed " + p + ": " + e); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FMVR] imported models set up: {n} ok, {fail} failed");
        }

        [System.Serializable]
        public class MatDesc
        {
            public float[] color;
            public float metallic, smoothness, normalScale = 1f;
            public bool cutout, transparent;
            public string albedo, ms, normal;
            public float[] emission;
        }

        /// <summary>Materials + remap + prefab for one exported FBX. Returns the prefab.</summary>
        public static GameObject SetupModel(string fbxPath)
        {
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string slug = Path.GetFileNameWithoutExtension(fbxPath);
            string cat = CategoryOf(fbxPath);
            var descs = ParseMaterials(File.ReadAllText(SidePath(fbxPath, ".materials.json")));
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            if (mi == null) return null;

            string matDir = dir + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materials");
            bool changed = false;
            var existing = mi.GetExternalObjectMap();
            foreach (var kv in descs)
            {
                var mat = BuildMaterial(matDir, dir, kv.Key, kv.Value);
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                if (!existing.TryGetValue(id, out var cur) || cur != mat)
                {
                    mi.AddRemap(id, mat);
                    changed = true;
                }
            }
            if (changed) mi.SaveAndReimport();
            return BuildPrefab(fbxPath, dir, slug, cat);
        }

        public static Dictionary<string, MatDesc> ParseMaterials(string json)
        {
            // JsonUtility cannot read a top-level dictionary: split the object into "name": {...} pairs
            var result = new Dictionary<string, MatDesc>();
            int i = json.IndexOf('{') + 1;
            while (i > 0 && i < json.Length)
            {
                int q0 = json.IndexOf('"', i);
                if (q0 < 0) break;
                int q1 = q0 + 1;
                var sb = new System.Text.StringBuilder();
                while (q1 < json.Length && json[q1] != '"')
                {
                    if (json[q1] == '\\' && q1 + 1 < json.Length) { q1++; sb.Append(json[q1] == 'u' ? '?' : json[q1]); if (json[q1] == 'u') q1 += 4; }
                    else sb.Append(json[q1]);
                    q1++;
                }
                int b0 = json.IndexOf('{', q1);
                if (b0 < 0) break;
                int depth = 0, b1 = b0;
                for (; b1 < json.Length; b1++)
                {
                    if (json[b1] == '{') depth++;
                    else if (json[b1] == '}' && --depth == 0) break;
                }
                result[sb.ToString()] = JsonUtility.FromJson<MatDesc>(json.Substring(b0, b1 - b0 + 1));
                i = b1 + 1;
            }
            return result;
        }

        static string SafeName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        static Texture2D Tex(string dir, string rel) =>
            string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + rel);

        public static Material BuildMaterial(string matDir, string dir, string name, MatDesc d)
        {
            string path = matDir + "/" + SafeName(name) + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(sh) { name = SafeName(name) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = sh;
            mat.enableInstancing = true;
            var c = d.color != null && d.color.Length >= 3
                ? new Color(d.color[0], d.color[1], d.color[2], d.color.Length > 3 ? d.color[3] : 1f) : Color.white;
            // Blender colours are linear; the URP colour property is sRGB in the inspector
            mat.SetColor("_BaseColor", c.gamma);
            var albedo = Tex(dir, d.albedo);
            mat.SetTexture("_BaseMap", albedo);
            var nrm = Tex(dir, d.normal);
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", nrm != null ? Mathf.Clamp(d.normalScale, 0.2f, 2f) : 1f);
            SetKeyword(mat, "_NORMALMAP", nrm != null);
            var ms = Tex(dir, d.ms);
            mat.SetTexture("_MetallicGlossMap", ms);
            SetKeyword(mat, "_METALLICSPECGLOSSMAP", ms != null);
            mat.SetFloat("_Metallic", ms != null ? 1f : Mathf.Clamp01(d.metallic));
            // with an MS map URP multiplies map.a by _Smoothness; cap the gloss a bit (scans are often too shiny on Quest)
            mat.SetFloat("_Smoothness", ms != null ? 0.85f : Mathf.Clamp(d.smoothness, 0f, 0.8f));
            mat.SetFloat("_WorkflowMode", 1f);
            bool cut = d.cutout && albedo != null;
            mat.SetFloat("_AlphaClip", cut ? 1f : 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            SetKeyword(mat, "_ALPHATEST_ON", cut);
            mat.SetFloat("_Cull", cut ? (float)CullMode.Off : (float)CullMode.Back);
            if (d.transparent && !cut)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                SetKeyword(mat, "_SURFACE_TYPE_TRANSPARENT", true);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                SetKeyword(mat, "_SURFACE_TYPE_TRANSPARENT", false);
                mat.renderQueue = cut ? (int)RenderQueue.AlphaTest : -1;
            }
            bool emit = d.emission != null && d.emission.Length >= 3 && (d.emission[0] + d.emission[1] + d.emission[2]) > 0.01f;
            mat.SetColor("_EmissionColor", emit ? new Color(d.emission[0], d.emission[1], d.emission[2]) : Color.black);
            SetKeyword(mat, "_EMISSION", emit);
            mat.globalIlluminationFlags = emit ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetKeyword(Material m, string k, bool on)
        {
            if (on) m.EnableKeyword(k); else m.DisableKeyword(k);
        }

        public static GameObject BuildPrefab(string fbxPath, string dir, string slug, string cat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = slug;
                ConfigureInstance(go, cat);
                string prefabPath = dir + "/" + slug + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Debug.Log($"[FMVR] prefab {prefabPath} ({cat}, tris {CountTris(go)})");
                return prefab;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Colliders, tags, static flags by category (shared with the Meshy importer).</summary>
        public static void ConfigureInstance(GameObject go, string cat)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bool cover = IsCoverCategory(cat);
            if (cover)
            {
                int layer = LayerMask.NameToLayer(CoverLayer);
                // collide with the most detailed mesh only (LOD0 or the single mesh)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    string n = mf.gameObject.name;
                    if (n.Contains("_LOD") && !n.EndsWith("_LOD0")) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (layer >= 0) t.gameObject.layer = layer;
                    if (TagExists(CoverTag)) t.gameObject.tag = CoverTag;
                }
            }
            else if (cat == "soldiers")
            {
                var b = Bounds(go, renderers);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.center = go.transform.InverseTransformPoint(b.center);
                cap.height = b.size.y;
                cap.radius = Mathf.Min(0.3f, Mathf.Max(b.size.x, b.size.z) * 0.25f);
            }
            else if (cat != "aircraft")
            {
                var b = Bounds(go, renderers);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                bc.size = b.size;
            }
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic;
            bool isStatic = cover || cat == "props";
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var f = flags;
                if (cover && cat != "vehicles") f |= StaticEditorFlags.OccluderStatic;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, isStatic ? f : 0);
            }
            foreach (var r in renderers)
            {
                // budget: only big scenery casts realtime shadows; small props and fly-by aircraft do not
                r.shadowCastingMode = cover || cat == "soldiers" ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
        }

        public static Bounds Bounds(GameObject go, Renderer[] rs)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in rs)
            {
                if (r.gameObject.name.Contains("_LOD") && !r.gameObject.name.EndsWith("_LOD0")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public static int CountTris(GameObject go)
        {
            int n = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.gameObject.name.Contains("_LOD") && !mf.gameObject.name.EndsWith("_LOD0")) continue;
                n += mf.sharedMesh.triangles.Length / 3;
            }
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh != null) n += s.sharedMesh.triangles.Length / 3;
            return n;
        }

        // ------------------------------------------------------------------ tag + layer
        static bool TagExists(string tag)
        {
            foreach (var t in UnityEditorInternal.InternalEditorUtility.tags) if (t == tag) return true;
            return false;
        }

        public static void EnsureCoverTagAndLayer()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tm.FindProperty("tags");
            bool has = false;
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == CoverTag) has = true;
            if (!has)
            {
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = CoverTag;
            }
            var layers = tm.FindProperty("layers");
            bool hasLayer = false;
            for (int i = 0; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == CoverLayer) hasLayer = true;
            if (!hasLayer)
            {
                for (int i = 8; i < layers.arraySize; i++)
                {
                    var l = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(l.stringValue)) { l.stringValue = CoverLayer; break; }
                }
            }
            tm.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
