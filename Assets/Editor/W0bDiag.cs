using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w0b black-screen hunt: dumps the Battlefield hierarchy (groups, triangles, scripts) and every serialized
    /// cross-group reference, so the scene can be split into Core + additive scenes without breaking links.
    /// -executeMethod FieldMedic.EditorTools.W0bDiag.Hierarchy → logs/w0b_hierarchy.txt
    /// </summary>
    public static class W0bDiag
    {
        public static string GroupOf(Transform t)
        {
            var chain = new List<Transform>();
            for (var p = t; p != null; p = p.parent) chain.Insert(0, p);
            if (chain.Count >= 2 && chain[0].name == "Battlefield")
            {
                if (chain.Count >= 3 && (chain[1].name == "Environment")) return chain[0].name + "/" + chain[1].name + "/" + chain[2].name;
                return chain[0].name + "/" + chain[1].name;
            }
            return chain[0].name;
        }

        public static void Hierarchy()
        {
            var scenePath = "Assets/Scenes/Battlefield.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var groups = new Dictionary<string, (int tris, int rends, int objs, HashSet<string> scripts)>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var g = GroupOf(t);
                if (!groups.TryGetValue(g, out var v)) v = (0, 0, 0, new HashSet<string>());
                v.objs++;
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && t.GetComponent<MeshRenderer>() != null) { v.tris += Tris(mf.sharedMesh); v.rends++; }
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) { v.tris += Tris(smr.sharedMesh); v.rends++; }
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                    if (mb != null) v.scripts.Add(mb.GetType().Name);
                    else v.scripts.Add("<missing>");
                groups[g] = v;
            }
            sb.AppendLine("== groups (objects, renderers, triangles, scripts)");
            foreach (var kv in groups.OrderBy(k => k.Key))
                sb.AppendLine($"{kv.Key,-60} objs {kv.Value.objs,5} rend {kv.Value.rends,4} tris {kv.Value.tris,8}  [{string.Join(",", kv.Value.scripts.OrderBy(s => s))}]");

            sb.AppendLine("\n== cross-group serialized references (from group -> to group : component.field)");
            var edges = new SortedSet<string>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                string from = GroupOf(c.transform);
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    if (o == null || EditorUtility.IsPersistent(o)) continue;
                    Transform tt = o is GameObject go ? go.transform : o is Component cc ? cc.transform : null;
                    if (tt == null) continue;
                    string to = GroupOf(tt);
                    if (to == from) continue;
                    edges.Add($"{from} -> {to} : {c.GetType().Name}.{it.propertyPath}");
                }
            }
            foreach (var e in edges) sb.AppendLine(e);

            sb.AppendLine("\n== static / scene settings");
            sb.AppendLine("cameras: " + string.Join(", ", Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(c => $"{c.name}(en={c.enabled && c.gameObject.activeInHierarchy},tag={c.tag},clear={c.clearFlags},bg={c.backgroundColor},near={c.nearClipPlane},far={c.farClipPlane},tex={(c.targetTexture != null)})")));
            sb.AppendLine("passthrough layers: " + Object.FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            sb.AppendLine($"fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogColor} dens {RenderSettings.fogDensity} start {RenderSettings.fogStartDistance} end {RenderSettings.fogEndDistance}");

            sb.AppendLine("\n== build scene dependencies (MB on disk, source files)");
            foreach (var s in new[] { "Assets/Scenes/Boot.unity", "Assets/Scenes/Battlefield.unity", "Assets/Scenes/Main.unity" })
                sb.AppendLine($"{s}: {DepMB(s):F1} MB");
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/w0b_hierarchy.txt", sb.ToString());
            Debug.Log("[FMVR] w0b hierarchy written, groups " + groups.Count + " edges " + edges.Count);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>w0b pipeline before the APKs: import overrides → shader variants → Boot (with the warm-up
        /// collection) → Hello → launch settings → scene audit. -executeMethod FieldMedic.EditorTools.W0bDiag.Prepare</summary>
        public static void Prepare()
        {
            bool ok = true;
            try
            {
                QuestLaunchSetup.Apply();
                AndroidAssetOptimizer.Apply(SceneBuilders.SceneSplitter.AllPaths.Concat(new[] { SceneBuilders.BootSceneBuilder.ScenePath }).ToArray());
                int v = SceneBuilders.ShaderWarmupBuilder.Collect();
                if (v == 0) Debug.LogWarning("[FMVR] Prepare: shader variant collection is empty");
                SceneBuilders.BootSceneBuilder.Build();
                SceneBuilders.HelloSceneBuilder.Build();
                QuestLaunchSetup.Apply();
                AssetDatabase.SaveAssets();
                ok = QuestSceneAudit.Run(SceneBuilders.SceneSplitter.AllPaths, out var rep);
                Debug.Log("[FMVR] Prepare audit: " + rep);
                Debug.Log($"[FMVR] Boot deps {DepMB(SceneBuilders.BootSceneBuilder.ScenePath):F2} MB, core {DepMB(SceneBuilders.SceneSplitter.CorePath):F1} MB");
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] Prepare: " + e); ok = false; }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
        }

        public static int Tris(Mesh m)
        {
            int n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3;
            return n;
        }

        public static float DepMB(string scene)
        {
            long b = 0;
            foreach (var d in AssetDatabase.GetDependencies(scene, true))
            {
                if (d.EndsWith(".cs") || d.EndsWith(".asmdef")) continue;
                try { b += new FileInfo(d).Length; } catch { }
            }
            return b / (1024f * 1024f);
        }
    }
}
