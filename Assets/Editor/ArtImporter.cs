using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Treatment;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w9 art pipeline (docs/ART_IMPORT.md): SourceAssets/Incoming/&lt;item_id&gt;/&lt;model&gt;.fbx|glb|gltf (+ textures) →
    /// Assets/Art/Items/&lt;item_id&gt;/ with Quest import settings (ASTC, ≤ 1024 items / ≤ 2048 characters, no mesh
    /// colliders) → validated against the sub-part names the item logic needs (<see cref="ArtModels.ByKind"/>) →
    /// Assets/Resources/Art/&lt;item_id&gt;.prefab that <see cref="ArtModels.Apply"/> swaps into the item's Visual child.
    /// Missing sub-parts: nothing in the game changes, the report lists what is missing (logs/art_import.txt).
    /// Menu FieldMedic/Art/Import Incoming; batch -executeMethod FieldMedic.EditorTools.ArtImporter.ImportIncomingBatch
    /// </summary>
    public static class ArtImporter
    {
        public const string ModelRoot = "Assets/Art/Items";
        public const string PrefabRoot = "Assets/Resources/Art";
        public static readonly string[] ModelExt = { ".fbx", ".glb", ".gltf" };
        public static readonly string[] TextureExt = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd" };
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

        public static ArtModels.Spec? SpecFor(string id)
        {
            foreach (var s in ArtModels.All()) if (s.id == id) return s;
            return null;
        }

        public static bool IsCharacter(string id) => id.StartsWith("char_") || id.StartsWith("erlan");

        public static List<Result> ImportFrom(string incoming, string modelRoot, string prefabRoot)
        {
            var results = new List<Result>();
            if (Directory.Exists(incoming))
                foreach (var dir in Directory.GetDirectories(incoming).OrderBy(d => d))
                {
                    var res = new Result { id = Path.GetFileName(dir).ToLowerInvariant() };
                    results.Add(res);
                    try { ImportOne(dir, res, modelRoot, prefabRoot); }
                    catch (System.Exception e) { res.ok = false; res.notes.Add("error: " + e.Message); }
                }
            ArtModels.ClearCache();
            WriteReport(results);
            return results;
        }

        static void ImportOne(string dir, Result res, string modelRoot, string prefabRoot)
        {
            var spec = SpecFor(res.id);
            if (spec == null) { res.notes.Add("unknown item id (see docs/ART_IMPORT.md): " + string.Join(", ", ArtModels.All().Select(s => s.id))); return; }
            var src = Directory.GetFiles(dir).FirstOrDefault(f => ModelExt.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (src == null) { res.notes.Add("no .fbx/.glb/.gltf in the folder"); return; }
            string dirAsset = modelRoot + "/" + res.id;
            string dirAbs = Path.Combine(ProjectRoot, dirAsset);
            Directory.CreateDirectory(Path.Combine(dirAbs, "Textures"));
            string fbxAsset = dirAsset + "/" + res.id + ".fbx";
            if (Path.GetExtension(src).ToLowerInvariant() == ".fbx")
            {
                File.Copy(src, Path.Combine(dirAbs, res.id + ".fbx"), true);
                foreach (var t in Directory.GetFiles(dir).Where(f => TextureExt.Contains(Path.GetExtension(f).ToLowerInvariant())))
                    File.Copy(t, Path.Combine(dirAbs, "Textures", Path.GetFileName(t)), true);
            }
            else
            {
                Glb2Fbx(src, Path.Combine(dirAbs, res.id + ".fbx"), Path.Combine(dirAbs, "Textures"));
                res.notes.Add("converted from " + Path.GetExtension(src));
            }
            AssetDatabase.Refresh();
            ApplyTextureSettings(dirAsset, IsCharacter(res.id) ? 2048 : 1024);
            ApplyModelSettings(fbxAsset);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
            if (model == null) { res.notes.Add("model did not import"); return; }
            BuildPrefab(model, spec.Value, prefabRoot, res);
        }

        static void Glb2Fbx(string src, string outFbx, string texDir)
        {
            if (!File.Exists(MeshyImporter.Blender)) throw new FileNotFoundException("Blender is needed to convert " + src, MeshyImporter.Blender);
            string q = "\"";
            var script = Path.Combine(ProjectRoot, "tools", "art_glb2fbx.py");
            var psi = new ProcessStartInfo(MeshyImporter.Blender,
                "-b --factory-startup --disable-autoexec --python " + q + script + q + " -- --in " + q + src + q + " --out " + q + outFbx + q + " --tex " + q + texDir + q)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (a, b) => { };
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10 * 60 * 1000);
                if (p.ExitCode != 0 || !File.Exists(outFbx)) throw new System.Exception("Blender conversion failed: " + (output.Length > 800 ? output.Substring(output.Length - 800) : output));
            }
        }

        /// <summary>Quest: ASTC, size cap, normal maps by name, mipmaps, no read/write.</summary>
        public static void ApplyTextureSettings(string folder, int maxSize)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n.Contains("normal") || n.EndsWith("_n") || n.EndsWith("_nrm")) ti.textureType = TextureImporterType.NormalMap;
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
        public static void ApplyModelSettings(string fbxAsset)
        {
            if (!(AssetImporter.GetAtPath(fbxAsset) is ModelImporter mi)) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.addCollider = false;
            mi.importBlendShapes = false;
            mi.SaveAndReimport();
        }

        /// <summary>Validated, size-checked prefab with box colliders. Missing sub-parts: no prefab, report only.</summary>
        public static GameObject BuildPrefab(GameObject model, ArtModels.Spec spec, string prefabRoot, Result res)
        {
            var root = new GameObject(spec.id);
            try
            {
                var inst = PrefabUtility.IsPartOfPrefabAsset(model) ? (GameObject)PrefabUtility.InstantiatePrefab(model) : Object.Instantiate(model);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                res.missing.AddRange(ArtModels.MissingParts(inst.transform, spec));
                if (res.missing.Count > 0)
                {
                    res.ok = false;
                    res.notes.Add("rename the objects in the model and import again; the game keeps the current look");
                    return null;
                }
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

                var b = MeshyImporter.LocalBounds(root.transform);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float ratio = longest / Mathf.Max(1e-4f, spec.length);
                if (ratio > 30f) { inst.transform.localScale *= 0.01f; res.notes.Add($"model was {longest:F1} units long — looks like centimetres, scaled ×0.01"); }
                else if (ratio < 0.033f) { inst.transform.localScale *= 100f; res.notes.Add($"model was {longest:F4} units long — scaled ×100"); }
                else if (ratio > 2f || ratio < 0.5f) res.notes.Add($"warning: {longest:F3} m long, a real one is {spec.length:F2} m");
                b = MeshyImporter.LocalBounds(root.transform);
                if (b.center.magnitude > spec.length * 0.25f) res.notes.Add($"warning: pivot {b.center.magnitude:F3} m from the model centre (see the pivot rules)");

                // simple shapes only: one box around the item; the kit gets one box per main part
                if (spec.id == ArtModels.Pouch.id)
                {
                    foreach (var part in new[] { "Body", "Lid|Flap", "QuickPouch" })
                    {
                        var t = ArtModels.FindPart(inst.transform, part);
                        if (t == null) continue;
                        var pb = MeshyImporter.LocalBounds(t);
                        if (pb.size == Vector3.zero) continue;
                        var box = t.gameObject.AddComponent<BoxCollider>();
                        box.center = pb.center;
                        box.size = Vector3.Max(pb.size, Vector3.one * 0.005f);
                    }
                }
                else
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = Vector3.Max(b.size, Vector3.one * 0.008f);
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                MeshyImporter.EnsureFolder(prefabRoot);
                string path = prefabRoot + "/" + spec.id + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                res.ok = prefab != null;
                res.prefab = path;
                res.notes.Add($"{b.size.x:F3} × {b.size.y:F3} × {b.size.z:F3} m");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
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
