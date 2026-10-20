using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Treatment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w9 art pipeline (docs/ART_IMPORT.md): SourceAssets/Incoming/&lt;folder&gt;/&lt;model&gt;.fbx|glb|gltf (+ Textures/) →
    /// Assets/Art/Items/&lt;folder&gt;/ with Quest import settings (ASTC, ≤ 1024 items / ≤ 2048 characters, no mesh
    /// colliders, URP Lit materials from the PBR maps) → one prefab per item of that folder, validated against the
    /// sub-part names the item logic needs (<see cref="ArtModels.ByKind"/>) → Assets/Resources/Art/&lt;item_id&gt;.prefab
    /// that <see cref="ArtModels.Apply"/> swaps into the item's Visual child.
    /// w9d: folders are matched to items through <see cref="ArtModels.Spec.source"/> (tq → tourniquet, npa → npa +
    /// lubricant, small → card/tape/wipes/...), multi-state files keep all states in the prefab (only the carried ones
    /// active), and deliveries whose FBX lost nested transforms are re-exported from a COPY of their .blend.
    /// Missing sub-parts: nothing in the game changes, the report lists what is missing (logs/art_import.txt).
    /// Menu FieldMedic/Art/Import Incoming; batch -executeMethod FieldMedic.EditorTools.ArtImporter.ImportIncomingBatch
    /// </summary>
    public static class ArtImporter
    {
        public const string ModelRoot = "Assets/Art/Items";
        public const string PrefabRoot = "Assets/Resources/Art";
        public static readonly string[] ModelExt = { ".fbx", ".glb", ".gltf" };
        public static readonly string[] TextureExt = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd" };
        /// <summary>Deliveries whose FBX was exported with "Apply Transform" (Blender bake_space_transform): nested
        /// children and empties land in wrong places (FMVR_IFAK: InnerPanel_Lower, LowerExtension, lower Slot_*).
        /// They are re-exported from a temporary copy of the .blend (tools/art_reexport_fbx.py).</summary>
        public static readonly HashSet<string> ReexportFromBlend = new HashSet<string> { "ifak" };
        /// <summary>Materials with real transparency in the base colour alpha (gel, clear plastic).</summary>
        public static readonly HashSet<string> Transparent = new HashSet<string> { "chestseal", "needle", "npa" };
        /// <summary>Thin single-sided sheets (foil blanket): both faces rendered.</summary>
        public static readonly HashSet<string> TwoSided = new HashSet<string> { "blanket" };
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string IncomingDir => Path.Combine(ProjectRoot, "SourceAssets", "Incoming");

        public sealed class Result
        {
            public string id;
            public bool ok;
            public string prefab;
            public readonly List<string> missing = new List<string>();
            public readonly List<string> notes = new List<string>();
            public override string ToString() =>
                $"{id}: {(ok ? "OK → " + prefab : "NOT APPLIED")}" +
                (missing.Count > 0 ? $"; missing sub-parts: {string.Join(", ", missing)}" : "") +
                (notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
        }

        [MenuItem("FieldMedic/Art/Import Incoming")]
        public static void ImportIncomingMenu()
        {
            var r = ImportFrom(IncomingDir, ModelRoot, PrefabRoot);
            EditorUtility.DisplayDialog("Art import", r.Count == 0 ? "SourceAssets/Incoming is empty." : string.Join("\n", r.Select(x => x.ToString())), "OK");
        }

        public static void ImportIncomingBatch()
        {
            bool ok = true;
            try { ok = ImportFrom(IncomingDir, ModelRoot, PrefabRoot).All(r => r.ok); }
            catch (System.Exception e) { Debug.LogError("[FMVR] art import: " + e); ok = false; }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static ArtModels.Spec SpecFor(string id) => ArtModels.ById(id);

        /// <summary>Items fed by one delivery folder (folder name = <see cref="ArtModels.Spec.Source"/>).</summary>
        public static List<ArtModels.Spec> SpecsForFolder(string folder) =>
            ArtModels.All().Where(s => string.Equals(s.Source, folder, System.StringComparison.OrdinalIgnoreCase)).ToList();

        public static bool IsCharacter(string id) => id.StartsWith("char_") || id.StartsWith("erlan");

        public static List<Result> ImportFrom(string incoming, string modelRoot, string prefabRoot)
        {
            var results = new List<Result>();
            if (Directory.Exists(incoming))
                foreach (var dir in Directory.GetDirectories(incoming).OrderBy(d => d))
                {
                    string folder = Path.GetFileName(dir).ToLowerInvariant();
                    var specs = SpecsForFolder(folder);
                    if (specs.Count == 0)
                    {
                        var res = new Result { id = folder };
                        res.notes.Add("unknown item id (see docs/ART_IMPORT.md): " + string.Join(", ", ArtModels.All().Select(s => s.Source).Distinct()));
                        results.Add(res);
                        continue;
                    }
                    var batch = specs.Select(s => new Result { id = s.id }).ToList();
                    results.AddRange(batch);
                    try { ImportFolder(dir, folder, specs, batch, modelRoot, prefabRoot); }
                    catch (System.Exception e) { foreach (var r in batch) { r.ok = false; r.notes.Add("error: " + e.Message); } }
                }
            ArtModels.ClearCache();
            WriteReport(results);
            return results;
        }

        static void ImportFolder(string dir, string folder, List<ArtModels.Spec> specs, List<Result> results, string modelRoot, string prefabRoot)
        {
            var src = Directory.GetFiles(dir).Where(f => ModelExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetExtension(f).ToLowerInvariant() == ".fbx" ? 0 : 1).FirstOrDefault();
            if (src == null) { foreach (var r in results) r.notes.Add("no .fbx/.glb/.gltf in the folder"); return; }
            string dirAsset = modelRoot + "/" + folder;
            string dirAbs = Path.Combine(ProjectRoot, dirAsset);
            string texAbs = Path.Combine(dirAbs, "Textures");
            Directory.CreateDirectory(texAbs);
            string fbxAsset = dirAsset + "/" + folder + ".fbx";
            string fbxAbs = Path.Combine(dirAbs, folder + ".fbx");
            var blend = Directory.GetFiles(dir, "*.blend").FirstOrDefault();
            if (ReexportFromBlend.Contains(folder) && blend != null && File.Exists(MeshyImporter.Blender))
            {
                RunBlender("art_reexport_fbx.py", q(blend) + " " + q(fbxAbs), fbxAbs);
                foreach (var r in results) r.notes.Add("FBX re-exported from a copy of the .blend (delivered FBX had broken nested transforms)");
            }
            else if (Path.GetExtension(src).ToLowerInvariant() == ".fbx") File.Copy(src, fbxAbs, true);
            else
            {
                RunBlender("art_glb2fbx.py", "--in " + q(src) + " --out " + q(fbxAbs) + " --tex " + q(texAbs), fbxAbs);
                foreach (var r in results) r.notes.Add("converted from " + Path.GetExtension(src));
            }
            PrepareTextures(dir, folder, texAbs);
            AssetDatabase.Refresh();
            ApplyTextureSettings(dirAsset, IsCharacter(folder) ? 2048 : 1024);
            ApplyModelSettings(fbxAsset, folder == ArtModels.Pouch.Source); // the kit combines its parts at runtime (draw calls)
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
            if (model == null) { foreach (var r in results) r.notes.Add("model did not import"); return; }
            var mats = BuildMaterials(fbxAsset, dirAsset, folder);
            for (int i = 0; i < specs.Count; i++) BuildPrefab(model, specs[i], prefabRoot, results[i], mats);
        }

        static string q(string s) => "\"" + s + "\"";

        static void RunBlender(string script, string args, string expectOut)
        {
            if (!File.Exists(MeshyImporter.Blender)) throw new FileNotFoundException("Blender is needed", MeshyImporter.Blender);
            var psi = new ProcessStartInfo(MeshyImporter.Blender,
                "-b --factory-startup --disable-autoexec --python " + q(Path.Combine(ProjectRoot, "tools", script)) + " -- " + args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (a, b) => { };
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10 * 60 * 1000);
                if (p.ExitCode != 0 || !File.Exists(expectOut)) throw new System.Exception("Blender " + script + " failed: " + (output.Length > 800 ? output.Substring(output.Length - 800) : output));
            }
        }

        /// <summary>BaseColor / Normal / packed Mask (R metallic, G AO, A smoothness) at ≤ 1024 via tools/art_pack_textures.py
        /// (Anaconda python); without python the raw maps are copied as they are.</summary>
        static void PrepareTextures(string dir, string folder, string texAbs)
        {
            var srcTex = Path.Combine(dir, "Textures");
            if (File.Exists(Path.Combine(texAbs, folder + "_BaseColor.png"))) return;
            if (Directory.Exists(srcTex))
            {
                try
                {
                    var tmp = Path.Combine(Path.GetTempPath(), "fmvr_tex_" + folder);
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    Directory.CreateDirectory(tmp);
                    var link = Path.Combine(tmp, folder);
                    Directory.CreateDirectory(Path.Combine(link, "Textures"));
                    foreach (var f in Directory.GetFiles(srcTex)) File.Copy(f, Path.Combine(link, "Textures", Path.GetFileName(f)));
                    var psi = new ProcessStartInfo("python", "-I " + q(Path.Combine(ProjectRoot, "tools", "art_pack_textures.py")) + " " + q(tmp) + " " + q(Path.GetDirectoryName(Path.GetDirectoryName(texAbs))) + " 1024")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(5 * 60 * 1000); }
                    Directory.Delete(tmp, true);
                }
                catch (System.Exception e) { Debug.LogWarning("[FMVR] texture packing failed (" + e.Message + "), copying the raw maps"); }
                if (File.Exists(Path.Combine(texAbs, folder + "_BaseColor.png"))) return;
                foreach (var t in Directory.GetFiles(srcTex).Where(f => TextureExt.Contains(Path.GetExtension(f).ToLowerInvariant())))
                    File.Copy(t, Path.Combine(texAbs, Path.GetFileName(t)), true);
            }
            foreach (var t in Directory.GetFiles(dir).Where(f => TextureExt.Contains(Path.GetExtension(f).ToLowerInvariant())))
                File.Copy(t, Path.Combine(texAbs, Path.GetFileName(t)), true);
        }

        /// <summary>Quest: ASTC, size cap, normal maps by name, linear masks, mipmaps, no read/write.</summary>
        public static void ApplyTextureSettings(string folder, int maxSize)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n.Contains("normal") || n.EndsWith("_n") || n.EndsWith("_nrm")) ti.textureType = TextureImporterType.NormalMap;
                else if (n.EndsWith("_mask") || n.Contains("roughness") || n.Contains("metallic") || n.Contains("ambientocclusion")) ti.sRGBTexture = false;
                ti.isReadable = false;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = Mathf.Min(ti.maxTextureSize, maxSize);
                var android = ti.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = maxSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
                ti.SaveAndReimport();
            }
        }

        /// <summary>Scale 1, no cameras/lights/animation, compressed mesh, no read/write, no colliders from the file.</summary>
        public static void ApplyModelSettings(string fbxAsset, bool readable = false)
        {
            if (!(AssetImporter.GetAtPath(fbxAsset) is ModelImporter mi)) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = readable;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.addCollider = false;
            mi.importBlendShapes = false;
            mi.SaveAndReimport();
        }

        static Texture2D Tex(string dirAsset, string folder, string suffix) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(dirAsset + "/Textures/" + folder + "_" + suffix + ".png");

        /// <summary>One URP Lit material per material name in the FBX, from the folder's packed maps.</summary>
        public static Dictionary<string, Material> BuildMaterials(string fbxAsset, string dirAsset, string folder)
        {
            var result = new Dictionary<string, Material>();
            var names = AssetDatabase.LoadAllAssetsAtPath(fbxAsset).OfType<Material>().Select(m => m.name).Distinct().ToList();
            var baseTex = Tex(dirAsset, folder, "BaseColor");
            if (baseTex == null) return result; // no packed maps: keep the FBX's own materials
            var nrm = Tex(dirAsset, folder, "Normal");
            var mask = Tex(dirAsset, folder, "Mask");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var name in names)
            {
                string path = dirAsset + "/" + Safe(name) + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh) { name = Safe(name) }; AssetDatabase.CreateAsset(m, path); }
                else m.shader = sh;
                m.enableInstancing = true;
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BaseMap", baseTex);
                m.SetTexture("_BumpMap", nrm);
                m.SetFloat("_BumpScale", 1f);
                Kw(m, "_NORMALMAP", nrm != null);
                m.SetTexture("_MetallicGlossMap", mask);
                Kw(m, "_METALLICSPECGLOSSMAP", mask != null);
                m.SetFloat("_Metallic", mask != null ? 1f : 0f);
                m.SetFloat("_Smoothness", mask != null ? 0.9f : 0.35f);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.SetTexture("_OcclusionMap", mask); // AO packed in G (URP samples .g)
                m.SetFloat("_OcclusionStrength", 1f);
                Kw(m, "_OCCLUSIONMAP", mask != null);
                m.SetFloat("_WorkflowMode", 1f);
                bool cut = name.ToLowerInvariant().Contains("mesh") && folder == "ifak";
                bool transparent = Transparent.Contains(folder);
                bool twoSided = cut || TwoSided.Contains(folder);
                m.SetFloat("_AlphaClip", cut ? 1f : 0f);
                m.SetFloat("_Cutoff", 0.5f);
                Kw(m, "_ALPHATEST_ON", cut);
                m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
                if (transparent)
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    Kw(m, "_SURFACE_TYPE_TRANSPARENT", true);
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    m.SetFloat("_Surface", 0f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1f);
                    Kw(m, "_SURFACE_TYPE_TRANSPARENT", false);
                    m.renderQueue = cut ? (int)RenderQueue.AlphaTest : -1;
                }
                m.SetColor("_EmissionColor", Color.black);
                Kw(m, "_EMISSION", false);
                EditorUtility.SetDirty(m);
                result[name] = m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static string Safe(string n) => new string(n.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
        static void Kw(Material m, string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }

        public static GameObject BuildPrefab(GameObject model, ArtModels.Spec spec, string prefabRoot, Result res) =>
            BuildPrefab(model, spec, prefabRoot, res, null);

        /// <summary>Validated, size-checked prefab with box colliders. Missing sub-parts: no prefab, report only.</summary>
        public static GameObject BuildPrefab(GameObject model, ArtModels.Spec spec, string prefabRoot, Result res, Dictionary<string, Material> mats)
        {
            var root = new GameObject(spec.id);
            try
            {
                var inst = PrefabUtility.IsPartOfPrefabAsset(model) ? (GameObject)PrefabUtility.InstantiatePrefab(model) : Object.Instantiate(model);
                if (PrefabUtility.IsPartOfPrefabInstance(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                // only this item's objects (a delivery may hold several items)
                if (spec.objects != null)
                    foreach (Transform c in inst.transform.Cast<Transform>().ToList())
                        if (!spec.objects.Any(o => string.Equals(o, c.name, System.StringComparison.OrdinalIgnoreCase))) Object.DestroyImmediate(c.gameObject);
                res.missing.AddRange(ArtModels.MissingParts(inst.transform, spec));
                if (res.missing.Count > 0)
                {
                    res.ok = false;
                    res.notes.Add("rename the objects in the model and import again; the game keeps the current look");
                    return null;
                }
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                if (mats != null && mats.Count > 0)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(m => m != null && mats.TryGetValue(m.name, out var nm) ? nm : m).ToArray();

                // states: everything not carried starts inactive (kept for the item's other states)
                var carried = new List<Transform>();
                foreach (Transform c in inst.transform)
                {
                    bool isCarried = spec.carried == null || spec.carried.Any(o => string.Equals(o, c.name, System.StringComparison.OrdinalIgnoreCase));
                    if (isCarried) carried.Add(c);
                }
                // re-pose side-by-side states onto the carried group (rotation about their own centre)
                if (spec.stack != null && spec.stack.Count > 0)
                {
                    var anchor = BoundsOf(inst.transform, carried.Where(c => !spec.stack.ContainsKey(c.name)));
                    foreach (var kv in spec.stack)
                    {
                        var t = ArtModels.FindPart(inst.transform, kv.Key);
                        if (t == null) continue;
                        var own = BoundsOf(inst.transform, new[] { t });
                        t.localRotation = Quaternion.Euler(kv.Value) * t.localRotation;
                        t.localPosition = Quaternion.Euler(kv.Value) * (t.localPosition - own.center) + own.center;
                        var moved = BoundsOf(inst.transform, new[] { t });
                        t.localPosition += anchor.center - moved.center;
                    }
                }
                foreach (Transform c in inst.transform) c.gameObject.SetActive(carried.Contains(c));

                inst.transform.localRotation = Quaternion.Euler(spec.euler);
                var b = BoundsOf(root.transform, carried);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float ratio = longest / Mathf.Max(1e-4f, spec.length);
                if (ratio > 30f) { inst.transform.localScale *= 0.01f; res.notes.Add($"model was {longest:F1} units long — looks like centimetres, scaled ×0.01"); }
                else if (ratio < 0.033f) { inst.transform.localScale *= 100f; res.notes.Add($"model was {longest:F4} units long — scaled ×100"); }
                else if (ratio > 2f || ratio < 0.5f) res.notes.Add($"warning: {longest:F3} m long, a real one is {spec.length:F2} m");
                b = BoundsOf(root.transform, carried);
                // pivot = centre of the carried look (items); the kit keeps its own origin (hinges, slots)
                if (spec.id != ArtModels.Pouch.id) { inst.transform.localPosition -= b.center; b = BoundsOf(root.transform, carried); }

                // simple shapes only: one box around the item; the kit gets one box per main part
                if (spec.id == ArtModels.Pouch.id)
                {
                    foreach (var part in new[] { "Body", "Lid|Flap", "QuickPouch" })
                    {
                        var t = ArtModels.FindPart(inst.transform, part);
                        if (t == null) continue;
                        var mf = t.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var box = t.gameObject.AddComponent<BoxCollider>();
                        box.center = mf.sharedMesh.bounds.center;
                        box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.005f);
                    }
                }
                else
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = Vector3.Max(b.size, Vector3.one * 0.008f);
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                MeshyImporter.EnsureFolder(prefabRoot);
                string path = prefabRoot + "/" + spec.id + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                res.ok = prefab != null;
                res.prefab = path;
                int tris = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => (int)f.sharedMesh.GetIndexCount(0) / 3 + (f.sharedMesh.subMeshCount > 1 ? (int)f.sharedMesh.GetIndexCount(1) / 3 : 0));
                res.notes.Add($"{b.size.x:F3} × {b.size.y:F3} × {b.size.z:F3} m, {tris} tris (all states)");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Bounds (in `space`, null = world) of the meshes under the given transforms (inactive ones included).</summary>
        public static Bounds BoundsOf(Transform space, IEnumerable<Transform> parts)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var p in parts)
                foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var m = (space != null ? space.worldToLocalMatrix : Matrix4x4.identity) * mf.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var pt = m.MultiplyPoint3x4(c);
                        if (!any) { b = new Bounds(pt, Vector3.zero); any = true; } else b.Encapsulate(pt);
                    }
                }
            return b;
        }

        static void WriteReport(List<Result> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Art import " + System.DateTime.Now.ToString("dd.MM HH:mm") + "  (SourceAssets/Incoming → Assets/Resources/Art)");
            if (results.Count == 0) sb.AppendLine("nothing in SourceAssets/Incoming");
            foreach (var r in results) sb.AppendLine(r.ToString());
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "logs"));
            File.WriteAllText(Path.Combine(ProjectRoot, "logs", "art_import.txt"), sb.ToString());
            foreach (var r in results)
                if (r.ok) Debug.Log("[FMVR] art import " + r);
                else Debug.LogWarning("[FMVR] art import " + r);
        }
    }
}
