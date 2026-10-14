using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FieldMedic.Interaction;
using FieldMedic.Treatment;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Owner's Meshy AI IFAK models -> game prefabs.
    /// Drop folder: SourceAssets/Meshy/&lt;fixed name&gt;.glb|.gltf|.fbx (names: docs/MESHY_PROMPTS_V2.md, <see cref="MeshyModels.RealLength"/>).
    ///  - .fbx is copied as is (embedded textures are imported by Unity);
    ///  - .glb/.gltf is converted headless by Blender with tools/bk_export.py (no glTF package needed in the project),
    ///    materials then come from the exported JSON (<see cref="LicensedModelSetup"/>);
    ///  - a prefab Resources/Meshy/&lt;name&gt;.prefab is built: real scale (longest side), longest axis = Z,
    ///    thinnest = Y, centred, BoxCollider + GrabPoint. <see cref="MeshyModels.Apply"/> swaps it into the IFAK item
    ///    at runtime; missing files keep the procedural models.
    /// Runs from the menu, automatically after any asset import when the drop folder changed, and before APK builds.
    /// </summary>
    public class MeshyImporter : AssetPostprocessor
    {
        public static string SourceDir => Path.Combine(ProjectRoot, "SourceAssets", "Meshy");
        public const string ModelRoot = "Assets/Art/Meshy";
        public const string PrefabRoot = "Assets/Resources/Meshy";
        public const string Blender = @"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe";
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static bool _running;

        public static readonly string[] Extensions = { ".glb", ".gltf", ".fbx" };

        /// <summary>
        /// Scenery the owner may generate in Meshy when BlenderKit has nothing usable (docs/MESHY_PROMPTS_V2.md,
        /// "Extra prompts"): file name -> (category, bk_export --size spec, rigged). They become prefabs with colliders
        /// under Assets/Art/Meshy/&lt;category&gt;/&lt;name&gt;/ exactly like the BlenderKit models.
        /// </summary>
        public static readonly Dictionary<string, (string category, string size, bool rigged)> Scenery =
            new Dictionary<string, (string, string, bool)>
            {
                { "env_sandbag_wall", ("cover", "L=3.0", false) },
                { "env_sandbag_pile", ("cover", "L=1.6", false) },
                { "env_shell_crater", ("cover", "L=6.0", false) },
                { "env_burnt_car", ("vehicles", "L=4.2", false) },
                { "veh_bmp2_wreck", ("vehicles", "L=6.7", false) },
                { "veh_btr80_burnt", ("vehicles", "L=7.7", false) },
                { "veh_t72", ("vehicles", "L=9.5", false) },
                { "air_su25", ("aircraft", "L=15.5", false) },
                { "air_mi24", ("aircraft", "L=17.5", false) },
                { "air_mi8", ("aircraft", "L=18.2", false) },
                { "sol_kz_soldier", ("soldiers", "H=1.78", true) },
                { "sol_enemy_soldier", ("soldiers", "H=1.78", true) },
                { "prop_spent_casings", ("props", "L=0.25", false) },
            };

        static IEnumerable<string> AllNames()
        {
            foreach (var k in MeshyModels.RealLength.Keys) yield return k;
            foreach (var k in Scenery.Keys) yield return k;
        }

        [MenuItem("Field Medic/Meshy/Import IFAK models")]
        public static void ImportAllMenu()
        {
            var r = ImportFrom(SourceDir, ModelRoot, PrefabRoot, true);
            Debug.Log("[FMVR] Meshy import: " + r.Count + " model(s)");
        }

        /// <summary>Batch / build hook: imports only new or changed files.</summary>
        public static void ImportAll() => ImportFrom(SourceDir, ModelRoot, PrefabRoot, false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_running || Application.isPlaying || !Directory.Exists(SourceDir)) return;
            if (!HasChanges(SourceDir, ModelRoot)) return;
            EditorApplication.delayCall += () => { if (!_running) ImportAll(); };
        }

        public static string FindSource(string dir, string name)
        {
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (System.Array.IndexOf(Extensions, ext) < 0) continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(f), name, System.StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        static string Stamp(string src) { var fi = new FileInfo(src); return fi.Name + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; }

        static string StampPath(string modelRoot, string name) => Path.Combine(ProjectRoot, modelRoot, name, name + ".src.txt");

        public static bool HasChanges(string sourceDir, string modelRoot)
        {
            foreach (var name in AllNames())
            {
                var src = FindSource(sourceDir, name);
                if (src == null) continue;
                var sp = StampPath(modelRoot, name);
                if (!File.Exists(sp) || File.ReadAllText(sp) != Stamp(src)) return true;
            }
            return false;
        }

        /// <summary>Imports every known file name found in sourceDir. Returns the prefabs that were (re)built.</summary>
        public static List<GameObject> ImportFrom(string sourceDir, string modelRoot, string prefabRoot, bool force)
        {
            var built = new List<GameObject>();
            if (!Directory.Exists(sourceDir)) return built;
            _running = true;
            try
            {
                foreach (var name in AllNames())
                {
                    var src = FindSource(sourceDir, name);
                    if (src == null) continue;
                    var sp = StampPath(modelRoot, name);
                    if (!force && File.Exists(sp) && File.ReadAllText(sp) == Stamp(src)) continue;
                    try
                    {
                        var p = Scenery.TryGetValue(name, out var sc)
                            ? ImportScenery(src, name, sc.category, sc.size, sc.rigged, modelRoot)
                            : ImportOne(src, name, MeshyModels.RealLength[name], modelRoot, prefabRoot);
                        if (p != null)
                        {
                            built.Add(p);
                            File.WriteAllText(sp, Stamp(src));
                        }
                    }
                    catch (System.Exception e) { Debug.LogError("[FMVR] Meshy import failed for " + src + ": " + e.Message); }
                }
            }
            finally { _running = false; }
            if (built.Count > 0) MeshyModels.ClearCache();
            return built;
        }

        public static GameObject ImportOne(string src, string name, float realLength, string modelRoot, string prefabRoot)
        {
            string dirAsset = modelRoot + "/" + name;
            string dirAbs = Path.Combine(ProjectRoot, dirAsset);
            Directory.CreateDirectory(dirAbs);
            string fbxAsset = dirAsset + "/" + name + ".fbx";
            var ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext == ".fbx")
            {
                File.Copy(src, Path.Combine(dirAbs, name + ".fbx"), true);
                AssetDatabase.ImportAsset(fbxAsset, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                RunBlender(src, dirAbs, name, "--max-faces 12000 --tex 1024 --pivot center --no-lods");
                AssetDatabase.Refresh();
                LicensedModelSetup.SetupModel(fbxAsset); // URP materials from <name>.materials.json (+ a plain prefab)
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
            if (model == null) throw new System.Exception("model did not import: " + fbxAsset);
            return BuildPrefab(model, name, realLength, prefabRoot);
        }

        /// <summary>Scenery: always through Blender (size, pivot at the bottom, LODs), then the shared prefab setup.</summary>
        public static GameObject ImportScenery(string src, string name, string category, string size, bool rigged, string modelRoot)
        {
            string dirAsset = modelRoot + "/" + category + "/" + name;
            string dirAbs = Path.Combine(ProjectRoot, dirAsset);
            Directory.CreateDirectory(dirAbs);
            Directory.CreateDirectory(Path.Combine(ProjectRoot, modelRoot, name)); // stamp folder
            int faces = category == "vehicles" || category == "aircraft" ? 30000 : 15000;
            RunBlender(src, dirAbs, name, "--max-faces " + faces + " --tex 1024 --size " + size +
                (rigged ? " --rigged --pivot keep" : " --pivot bottom"));
            AssetDatabase.Refresh();
            return LicensedModelSetup.SetupModel(dirAsset + "/" + name + ".fbx");
        }

        static void RunBlender(string src, string outAbs, string name, string extra)
        {
            if (!File.Exists(Blender)) throw new FileNotFoundException("Blender is needed to convert " + src, Blender);
            var script = Path.Combine(ProjectRoot, "tools", "bk_export.py");
            string q = "\"";
            var psi = new ProcessStartInfo(Blender,
                "-b --factory-startup --disable-autoexec --python " + q + script + q + " -- --in " + q + src + q +
                " --out " + q + outAbs + q + " --name " + name + " " + extra)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var proc = Process.Start(psi))
            {
                proc.ErrorDataReceived += (a, b) => { };
                proc.BeginErrorReadLine();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10 * 60 * 1000);
                if (proc.ExitCode != 0 || !File.Exists(Path.Combine(outAbs, name + ".fbx")))
                    throw new System.Exception("Blender conversion failed:\n" + Tail(output, 1500));
            }
        }

        static string Tail(string s, int n) => s.Length <= n ? s : s.Substring(s.Length - n);

        /// <summary>Normalised item prefab: longest axis Z, thinnest Y, real size, centred, collider + grab point.</summary>
        public static GameObject BuildPrefab(GameObject model, string name, float realLength, string prefabRoot)
        {
            EnsureFolder(prefabRoot);
            var root = new GameObject(name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

                var b = LocalBounds(root.transform);
                // axes sorted by extent: longest -> Z, thinnest -> Y
                var size = b.size;
                int longest = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
                int thinnest = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);
                if (thinnest == longest) thinnest = (longest + 1) % 3;
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                var rot = Quaternion.Inverse(Quaternion.LookRotation(axes[longest], axes[thinnest]));
                inst.transform.localRotation = rot * inst.transform.localRotation;
                b = LocalBounds(root.transform);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float k = maxDim > 1e-6f ? realLength / maxDim : 1f;
                inst.transform.localScale *= k;
                b = LocalBounds(root.transform);
                inst.transform.localPosition -= b.center;
                b = LocalBounds(root.transform);

                var box = root.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = Vector3.Max(b.size, Vector3.one * 0.008f);
                var gp = new GameObject("GrabPoint").AddComponent<GrabPoint>();
                gp.transform.SetParent(root.transform, false);
                gp.snap = false;
                gp.radius = Mathf.Clamp(realLength * 0.6f, 0.05f, 0.12f);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                string path = prefabRoot + "/" + name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[FMVR] Meshy prefab {path}: {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3} m");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static Bounds LocalBounds(Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Add(root, mf.transform, mf.sharedMesh, ref b, ref any);
            foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(root, s.transform, s.sharedMesh, ref b, ref any);
            return b;
        }

        static void Add(Transform root, Transform t, Mesh m, ref Bounds b, ref bool any)
        {
            if (m == null) return;
            var mat = root.worldToLocalMatrix * t.localToWorldMatrix;
            var mb = m.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = mat.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }

        public static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
