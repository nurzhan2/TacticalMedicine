using System;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Environment;
using FieldMedic.Startup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w0b: turns the authored Battlefield.unity into the scenes the APK ships —
    /// <c>Main_Core</c> (terrain, rig, Erlan, medic gear, HUD/menu, scenarios, TACEVAC; loaded by Boot) and the
    /// additive <c>Main_Cover</c> (ruins, sandbag line, wrecks, cover points), <c>Main_Scenery</c> (far field,
    /// props, grass, smoke columns) and <c>Main_Battle</c> (BattleDirector + all battle layers) which
    /// <see cref="GameplayScenes"/> streams in one by one. Serialized references that cross a scene border are
    /// replaced by <see cref="SceneLinker"/> links. Also sanitizes the rig (no passthrough, opaque clear, near/far)
    /// in the source scene, and bakes occlusion for the scene set.
    /// -executeMethod FieldMedic.EditorTools.SceneBuilders.SceneSplitter.SplitBatch
    /// </summary>
    public static class SceneSplitter
    {
        public const string Dir = "Assets/Scenes/";
        public const string CorePath = Dir + GameplayScenes.CoreScene + ".unity";

        /// <summary>Group (see <see cref="W0bDiag.GroupOf"/>) → additive scene. Everything else stays in the core.</summary>
        public static readonly (string scene, string[] groups)[] Additive =
        {
            // cover in three parts (one 1.1 M-triangle scene took 444 ms to activate); the line + cover points come
            // last because CoverPoint.cover links back into the ruins and wrecks
            ("Main_Ruins", new[] { "Battlefield/Ruins" }),
            ("Main_Wrecks", new[] { "Battlefield/ObstaclesAndWrecks", "Battlefield/Combined_Debris", "Battlefield/Combined_Rocks" }),
            ("Main_Cover", new[]
            {
                "Battlefield/CoverLine", "Battlefield/CoverPoints", "Battlefield/Combined_Sandbags",
                "Battlefield/Combined_Concertina", "Battlefield/Combined_WireFrames",
            }),
            ("Main_Scenery", new[]
            {
                "Battlefield/FarField", "Battlefield/Props", "Battlefield/Environment/GrassTufts", "Battlefield/SmokeColumns",
            }),
            ("Main_Battle", new[] { "Battle" }),
        };

        static int Order(string scene) => Array.FindIndex(Additive, a => a.scene == scene);

        public static string[] AdditivePaths => Additive.Select(a => Dir + a.scene + ".unity").ToArray();
        public static string[] AllPaths => new[] { CorePath }.Concat(AdditivePaths).ToArray();

        public static void SplitBatch()
        {
            bool ok = false;
            try { ok = Split(); }
            catch (Exception e) { Debug.LogError("[FMVR] SceneSplitter: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/Scenes/Split Battlefield (w0b)")]
        public static bool Split()
        {
            var scene = EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            int fixes = Sanitize(scene);
            if (fixes > 0) EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FMVR] SceneSplitter: source sanitized ({fixes} fixes)");

            // ---- assign every root-level group to a scene
            var target = new Dictionary<Transform, string>(); // group transform → scene name
            var groupNames = new Dictionary<string, string>();
            foreach (var (sc, groups) in Additive) foreach (var g in groups) groupNames[g] = sc;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var g = W0bDiag.GroupOf(t);
                if (groupNames.TryGetValue(g, out var sc) && Path(t) == g) target[t] = sc;
            }
            var missing = groupNames.Keys.Where(g => !target.Keys.Any(t => Path(t) == g)).ToList();
            if (missing.Count > 0) Debug.LogWarning("[FMVR] SceneSplitter: groups not found: " + string.Join(", ", missing));

            string SceneOf(Transform t)
            {
                for (var p = t; p != null; p = p.parent) if (target.TryGetValue(p, out var s)) return s;
                return GameplayScenes.CoreScene;
            }

            // ---- collect cross-scene references before anything moves
            var links = new Dictionary<string, List<SceneLinker.Link>>();
            int errors = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                string from = SceneOf(c.transform);
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    if (o == null || EditorUtility.IsPersistent(o)) continue;
                    Transform tt = o is GameObject go ? go.transform : o is Component cc ? cc.transform : null;
                    if (tt == null) continue;
                    string to = SceneOf(tt);
                    if (to == from) continue;
                    if (from == GameplayScenes.CoreScene)
                    {
                        // the core is loaded first and must not depend on streamed scenes
                        Debug.LogError($"[FMVR] SceneSplitter: core → {to} reference {Path(c.transform)} {c.GetType().Name}.{it.propertyPath} → {Path(tt)}");
                        errors++;
                        continue;
                    }
                    if (to != GameplayScenes.CoreScene && Order(to) >= Order(from))
                    {
                        Debug.LogError($"[FMVR] SceneSplitter: {from} → {to} reference {c.GetType().Name}.{it.propertyPath} (additive scenes may only reference the core or earlier additive scenes)");
                        errors++;
                        continue;
                    }
                    string type = o is GameObject ? "GameObject" : o.GetType().FullName;
                    if (!links.TryGetValue(from, out var list)) links[from] = list = new List<SceneLinker.Link>();
                    list.Add(new SceneLinker.Link { source = c, field = it.propertyPath, targetPath = Path(tt), targetType = type });
                    it.objectReferenceValue = null;
                    changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (errors > 0) return false;

            // ---- move groups into new scenes (same hierarchy path, same world pose)
            var created = new Dictionary<string, Scene>();
            foreach (var (sc, _) in Additive)
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(s, Dir + sc + ".unity"); // Unity allows one untitled scene at a time
                created[sc] = s;
            }
            var parents = new Dictionary<string, Transform>(); // "scene|path" → new parent
            foreach (var kv in target.OrderBy(k => Path(k.Key)))
            {
                var t = kv.Key;
                var s = created[kv.Value];
                Transform newParent = null;
                if (t.parent != null) newParent = EnsurePath(s, t.parent, parents);
                t.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(t.gameObject, s);
                if (newParent != null) t.SetParent(newParent, true);
            }
            foreach (var (sc, _) in Additive)
            {
                var s = created[sc];
                if (links.TryGetValue(sc, out var list))
                {
                    var go = new GameObject("~SceneLinker");
                    SceneManager.MoveGameObjectToScene(go, s);
                    go.AddComponent<SceneLinker>().links = list.ToArray();
                    Debug.Log($"[FMVR] SceneSplitter: {sc} gets {list.Count} cross-scene links");
                }
                if (!EditorSceneManager.SaveScene(s, Dir + sc + ".unity")) { Debug.LogError("[FMVR] cannot save " + sc); return false; }
            }

            // ---- the core: loader + rig grounding
            var loader = new GameObject("GameplayScenes");
            SceneManager.MoveGameObjectToScene(loader, scene);
            loader.AddComponent<GameplayScenes>().additive = Additive.Select(a => a.scene).ToArray();
            var rig = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<OVRCameraRig>() != null);
            if (rig != null && rig.GetComponent<RigGrounding>() == null) rig.AddComponent<RigGrounding>();
            if (!EditorSceneManager.SaveScene(scene, CorePath)) { Debug.LogError("[FMVR] cannot save core"); return false; }

            BakeOcclusion();
            SetBuildScenes(full: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[FMVR] SceneSplitter: done → " + string.Join(", ", AllPaths));
            return true;
        }

        /// <summary>Occlusion data for the whole scene set (multi-scene bake), so it stays valid when they load together.</summary>
        public static void BakeOcclusion()
        {
            EditorSceneManager.OpenScene(CorePath, OpenSceneMode.Single);
            foreach (var p in AdditivePaths) EditorSceneManager.OpenScene(p, OpenSceneMode.Additive);
            StaticOcclusionCulling.smallestOccluder = 2.5f;
            StaticOcclusionCulling.smallestHole = 0.3f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            var t0 = DateTime.Now;
            bool ok = StaticOcclusionCulling.Compute();
            Debug.Log($"[FMVR] SceneSplitter: multi-scene occlusion bake {(ok ? "ok" : "FAILED")} in {(DateTime.Now - t0).TotalSeconds:F0} s");
            EditorSceneManager.SaveOpenScenes();
        }

        /// <summary>Build scenes: Boot + core (+ additive scenes for the full build). The authored Battlefield and the
        /// legacy V1 trench (Main) stay out of the APK.</summary>
        public static void SetBuildScenes(bool full)
        {
            var list = new List<string> { BootSceneBuilder.ScenePath, CorePath };
            if (full) list.AddRange(AdditivePaths);
            EditorBuildSettings.scenes = list.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            Debug.Log("[FMVR] build scenes: " + string.Join(", ", list));
        }

        /// <summary>Black-screen guard rails on the rig/cameras of a scene. Returns the number of changes.</summary>
        public static int Sanitize(Scene scene)
        {
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var pp in root.GetComponentsInChildren<FieldMedic.Comfort.ProximityPassthrough>(true))
                {
                    if (pp.fadeQuad != null) Object.DestroyImmediate(pp.fadeQuad.gameObject);
                    Object.DestroyImmediate(pp); n++;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name == "PassthroughFade").ToList())
                { Object.DestroyImmediate(t.gameObject); n++; }
                foreach (var l in root.GetComponentsInChildren<OVRPassthroughLayer>(true)) { Object.DestroyImmediate(l); n++; }
                foreach (var m in root.GetComponentsInChildren<OVRManager>(true))
                {
                    if (m.isInsightPassthroughEnabled) { m.isInsightPassthroughEnabled = false; EditorUtility.SetDirty(m); n++; }
                    var so = new SerializedObject(m);
                    var p = so.FindProperty("_trackingOriginType");
                    if (p != null && p.intValue != (int)OVRManager.TrackingOrigin.FloorLevel) { p.intValue = (int)OVRManager.TrackingOrigin.FloorLevel; so.ApplyModifiedPropertiesWithoutUndo(); n++; }
                }
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.targetTexture != null) continue;
                    var bg = cam.backgroundColor;
                    if (bg.a < 1f) { bg.a = 1f; cam.backgroundColor = bg; n++; }
                    if (cam.name == "CenterEyeAnchor")
                    {
                        if (cam.nearClipPlane != 0.05f) { cam.nearClipPlane = 0.05f; n++; }
                        if (cam.farClipPlane < 1500f) { cam.farClipPlane = 1500f; n++; }
                        if (cam.cullingMask != ~0) { cam.cullingMask = ~0; n++; }
                        if (!cam.CompareTag("MainCamera")) { cam.tag = "MainCamera"; n++; }
                    }
                    else if (cam.enabled && cam.GetComponentInParent<OVRCameraRig>() != null)
                    {
                        cam.enabled = false; n++;
                    }
                    if (cam.name == "LeftEyeAnchor" && cam.CompareTag("MainCamera")) { cam.tag = "Untagged"; n++; }
                }
            }
            return n;
        }

        static Transform EnsurePath(Scene s, Transform original, Dictionary<string, Transform> made)
        {
            string key = s.name + "|" + Path(original);
            if (made.TryGetValue(key, out var t) && t != null) return t;
            Transform parent = original.parent != null ? EnsurePath(s, original.parent, made) : null;
            var go = new GameObject(original.name);
            if (parent == null) SceneManager.MoveGameObjectToScene(go, s);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = original.localPosition;
            go.transform.localRotation = original.localRotation;
            go.transform.localScale = original.localScale;
            go.isStatic = original.gameObject.isStatic;
            go.layer = original.gameObject.layer;
            made[key] = go.transform;
            return go.transform;
        }

        public static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }
    }
}
