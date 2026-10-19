using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w0b black-screen guard rails, run before every APK build and by EditMode tests. Fails on: more than one display
    /// camera (or none) per scene set, a camera not under OVRCameraRig/CenterEyeAnchor, transparent clear colour,
    /// near/far out of range, passthrough layers / passthrough enabled on OVRManager, missing TMP/UI shaders,
    /// a Boot scene with heavy or battlefield dependencies, URP settings that need extra passes, and an APK over budget.
    /// Report: logs/scene_audit.txt. -executeMethod FieldMedic.EditorTools.QuestSceneAudit.RunBatch
    /// </summary>
    public static class QuestSceneAudit
    {
        public const float BootBudgetMB = 5f;
        public const float ApkBudgetMB = 150f;
        public const float ApkHardLimitMB = 200f;
        public static readonly string[] RequiredShaders =
            { "TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field", "FieldMedic/BloodDecal", "FieldMedic/UiCard", "Universal Render Pipeline/Unlit" };
        // (GUI/Text Shader for TextMesh comes with the built-in font material; the editor copy is DontSave and breaks the build)
        public static readonly string[] BootForbidden = { "Assets/_Licensed/", "Assets/Art/Battlefield/", "Assets/Art/Characters/", "Assets/Audio/", "Assets/ThirdParty/" };

        // ------------------------------------------------------------------ pure rules (EditMode tests)
        public struct CamInfo
        {
            public string name, path, tag;
            public bool enabled, hasTarget;
            public Color background;
            public CameraClearFlags clear;
            public float near, far;
            public int cullingMask;
        }

        public static List<string> CameraRules(string sceneSet, IEnumerable<CamInfo> cams)
        {
            var p = new List<string>();
            var display = cams.Where(c => c.enabled && !c.hasTarget).ToList();
            if (display.Count == 0) p.Add($"{sceneSet}: no enabled camera renders to the display");
            if (display.Count > 1) p.Add($"{sceneSet}: {display.Count} cameras render to the display ({string.Join(", ", display.Select(c => c.path))})");
            foreach (var c in display)
            {
                if (!c.path.EndsWith("TrackingSpace/CenterEyeAnchor")) p.Add($"{sceneSet}: display camera {c.path} is not OVRCameraRig/TrackingSpace/CenterEyeAnchor");
                if (c.tag != "MainCamera") p.Add($"{sceneSet}: display camera {c.path} is not tagged MainCamera");
                if ((c.clear == CameraClearFlags.SolidColor || c.clear == CameraClearFlags.Skybox) && c.background.a < 0.999f)
                    p.Add($"{sceneSet}: camera {c.path} clear colour alpha {c.background.a:F2} (alpha 0 = passthrough hole → black without passthrough)");
                if (c.near > 0.051f || c.near < 0.009f) p.Add($"{sceneSet}: camera {c.path} near {c.near} (want 0.01..0.05)");
                if (c.far < 1500f) p.Add($"{sceneSet}: camera {c.path} far {c.far} < 1500");
                if (c.cullingMask != ~0 && (c.cullingMask & 1) == 0) p.Add($"{sceneSet}: camera {c.path} culling mask excludes Default");
            }
            return p;
        }

        public static List<string> BootDependencyRules(IEnumerable<(string path, long bytes)> deps)
        {
            var p = new List<string>();
            var list = deps.Where(d => !d.path.EndsWith(".cs") && !d.path.EndsWith(".asmdef") && !d.path.EndsWith(".unity")).ToList();
            float mb = list.Sum(d => d.bytes) / (1024f * 1024f);
            if (mb > BootBudgetMB) p.Add($"Boot dependencies {mb:F1} MB > {BootBudgetMB} MB: " + string.Join(", ", list.OrderByDescending(d => d.bytes).Take(5).Select(d => d.path)));
            foreach (var d in list)
                if (BootForbidden.Any(f => d.path.StartsWith(f))) p.Add("Boot depends on battlefield asset " + d.path);
            return p;
        }

        public static List<string> ApkRules(string name, float mb, bool full)
        {
            var p = new List<string>();
            if (mb > ApkHardLimitMB) p.Add($"{name}: APK {mb:F0} MB > hard limit {ApkHardLimitMB} MB");
            else if (full && mb > ApkBudgetMB) p.Add($"{name}: APK {mb:F0} MB > budget {ApkBudgetMB} MB (warning)");
            return p;
        }

        /// <summary>
        /// w9 owner request: «САТПАЕВ ЛУЧШЕ ВСЕХ» in the sky of every gameplay scene. The root's forward must point at the
        /// spawn eye (≤ 15°), the text must sit 25–35° above the horizon in the start view direction (± 40° yaw: clear of the ruin walls), the
        /// TMP child must read away from the viewer (not mirrored) and the whole thing must be inside the far plane.
        /// </summary>
        public static List<string> SkyTextRules(string sceneSet, Transform sky, Vector3 spawnEye, float spawnYaw, float farPlane)
        {
            var p = new List<string>();
            if (sky == null) { p.Add($"{sceneSet}: {FieldMedic.Environment.SkyText.ObjectName} missing (owner: sky text in every gameplay scene)"); return p; }
            Vector3 toSpawn = spawnEye - sky.position;
            float face = Vector3.Angle(sky.forward, toSpawn);
            if (face > 15f) p.Add($"{sceneSet}: sky text forward is {face:F0}° off the spawn point");
            float elev = FieldMedic.Environment.SkyText.ElevationFrom(spawnEye, sky.position);
            if (elev < 25f || elev > 35f) p.Add($"{sceneSet}: sky text at {elev:F0}° elevation (want 25–35°)");
            Vector3 flat = sky.position - spawnEye; flat.y = 0f;
            float yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : spawnYaw;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, spawnYaw)) > 40f) p.Add($"{sceneSet}: sky text at yaw {yaw:F0}°, the player starts facing {spawnYaw:F0}°");
            var tmp = sky.GetComponentInChildren<TMPro.TextMeshPro>(true);
            if (tmp == null) p.Add($"{sceneSet}: sky text has no TextMeshPro");
            else
            {
                if (Vector3.Dot(tmp.transform.forward, sky.position - spawnEye) <= 0f) p.Add($"{sceneSet}: sky text would read mirrored from the spawn");
                if (Vector3.Dot(tmp.transform.up, Vector3.up) < 0.5f) p.Add($"{sceneSet}: sky text is upside down / sideways");
                if (FieldMedic.Environment.SkyText.Normalise(tmp.text) != FieldMedic.Environment.SkyText.Phrase) p.Add($"{sceneSet}: sky text reads '{tmp.text}'");
            }
            if (toSpawn.magnitude > farPlane * 0.9f) p.Add($"{sceneSet}: sky text {toSpawn.magnitude:F0} m away, beyond the far plane {farPlane:F0} m");
            return p;
        }

        // ------------------------------------------------------------------ scene checks
        public static List<CamInfo> Cameras(IEnumerable<Scene> scenes)
        {
            var r = new List<CamInfo>();
            foreach (var s in scenes)
            foreach (var root in s.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Camera>(true))
                r.Add(new CamInfo
                {
                    name = c.name, path = SceneBuilders.SceneSplitter.Path(c.transform), tag = c.tag,
                    enabled = c.enabled && c.gameObject.activeInHierarchy, hasTarget = c.targetTexture != null,
                    background = c.backgroundColor, clear = c.clearFlags, near = c.nearClipPlane, far = c.farClipPlane, cullingMask = c.cullingMask,
                });
            return r;
        }

        public static List<string> PassthroughRules(string sceneSet, IEnumerable<Scene> scenes)
        {
            var p = new List<string>();
            foreach (var s in scenes)
            foreach (var root in s.GetRootGameObjects())
            {
                foreach (var l in root.GetComponentsInChildren<OVRPassthroughLayer>(true)) p.Add($"{sceneSet}: OVRPassthroughLayer on {SceneBuilders.SceneSplitter.Path(l.transform)}");
                foreach (var l in root.GetComponentsInChildren<FieldMedic.Comfort.ProximityPassthrough>(true)) p.Add($"{sceneSet}: ProximityPassthrough on {SceneBuilders.SceneSplitter.Path(l.transform)} (fades the scene to black)");
                foreach (var m in root.GetComponentsInChildren<OVRManager>(true))
                    if (m.isInsightPassthroughEnabled) p.Add($"{sceneSet}: OVRManager passthrough enabled");
            }
            return p;
        }

        public static List<string> ShaderRules()
        {
            var p = new List<string>();
            var always = AlwaysIncluded();
            foreach (var n in RequiredShaders)
            {
                var sh = Shader.Find(n);
                if (sh == null) { p.Add("shader missing: " + n); continue; }
                if ((sh.hideFlags & HideFlags.DontSave) != 0) { p.Add("editor-only shader would be built: " + n); continue; }
                if (!always.Contains(sh)) p.Add("shader not in Always Included (stripped if only created at runtime): " + n);
            }
            return p;
        }

        public static HashSet<Shader> AlwaysIncluded()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var set = new HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Shader s) set.Add(s);
            return set;
        }

        /// <summary>Adds the required runtime shaders to Always Included (TMP/UI created from code would be stripped).</summary>
        public static int EnsureAlwaysIncluded()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            int added = 0;
            for (int i = arr.arraySize - 1; i >= 0; i--)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Shader bad && (bad.hideFlags & HideFlags.DontSave) != 0)
                {
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    arr.DeleteArrayElementAtIndex(i);
                    added++;
                }
            if (added > 0) so.ApplyModifiedPropertiesWithoutUndo();
            var have = AlwaysIncluded();
            foreach (var n in RequiredShaders)
            {
                var sh = Shader.Find(n);
                if (sh == null || have.Contains(sh)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                added++;
            }
            if (added > 0) { so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
            return added;
        }

        public static List<string> UrpRules()
        {
            var p = new List<string>();
            var rp = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            var urp = rp as UniversalRenderPipelineAsset;
            if (urp == null) { p.Add("no URP asset active for Android"); return p; }
            if (urp.supportsHDR) p.Add($"URP {urp.name}: HDR on");
            if (Mathf.Abs(urp.renderScale - 1f) > 0.01f) p.Add($"URP {urp.name}: render scale {urp.renderScale}");
            if (urp.supportsCameraDepthTexture) p.Add($"URP {urp.name}: depth texture on (extra pass)");
            if (urp.supportsCameraOpaqueTexture) p.Add($"URP {urp.name}: opaque texture on (extra pass)");
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; list != null && i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData rd)
                    foreach (var f in rd.rendererFeatures)
                        if (f != null && f.isActive) p.Add($"URP renderer {rd.name}: active renderer feature {f.name}");
            return p;
        }

        // ------------------------------------------------------------------ run
        public static void RunBatch()
        {
            bool ok = Run(SceneBuilders.SceneSplitter.AllPaths, out _);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
        }

        /// <summary>Audits Boot + the given gameplay scene set (loaded together like at runtime).</summary>
        public static bool Run(string[] gameplay, out string report)
        {
            var problems = new List<string>();
            var notes = new List<string>();
            EnsureAlwaysIncluded();

            var boot = EditorSceneManager.OpenScene(SceneBuilders.BootSceneBuilder.ScenePath, OpenSceneMode.Single);
            problems.AddRange(CameraRules("Boot", Cameras(new[] { boot })));
            problems.AddRange(PassthroughRules("Boot", new[] { boot }));
            var bootDeps = AssetDatabase.GetDependencies(SceneBuilders.BootSceneBuilder.ScenePath, true)
                .Select(d => (d, File.Exists(d) ? new FileInfo(d).Length : 0L)).ToList();
            problems.AddRange(BootDependencyRules(bootDeps));
            notes.Add($"Boot dependencies: {bootDeps.Where(d => !d.d.EndsWith(".cs")).Sum(d => d.Item2) / 1048576f:F2} MB in {bootDeps.Count} files");

            var opened = new List<Scene>();
            for (int i = 0; i < gameplay.Length; i++)
                if (File.Exists(gameplay[i])) opened.Add(EditorSceneManager.OpenScene(gameplay[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive));
            string set = string.Join("+", opened.Select(s => s.name));
            problems.AddRange(CameraRules(set, Cameras(opened)));
            problems.AddRange(PassthroughRules(set, opened));
            foreach (var s in opened.Skip(1))
                if (Cameras(new[] { s }).Count > 0) problems.Add($"additive scene {s.name} has its own camera");
            if (opened.Count > 0)
            {
                var rig = opened.SelectMany(s => s.GetRootGameObjects()).SelectMany(g => g.GetComponentsInChildren<OVRCameraRig>(true)).FirstOrDefault();
                var sky = opened.SelectMany(s => s.GetRootGameObjects()).FirstOrDefault(g => g.name == FieldMedic.Environment.SkyText.ObjectName);
                var cam = rig != null ? rig.GetComponentsInChildren<Camera>(true).FirstOrDefault() : null;
                if (rig == null) problems.Add($"{set}: no OVRCameraRig (spawn) for the sky text check");
                else
                {
                    problems.AddRange(SkyTextRules(set, sky != null ? sky.transform : null, rig.transform.position + Vector3.up * FieldMedic.Environment.BattlefieldLayout.StandEyeHeight,
                        rig.transform.eulerAngles.y, cam != null ? cam.farClipPlane : 1500f));
                    if (sky != null) notes.Add($"sky text at {sky.transform.position:F0}, {FieldMedic.Environment.SkyText.ElevationFrom(rig.transform.position + Vector3.up * 1.65f, sky.transform.position):F0}° above the horizon");
                }
            }

            problems.AddRange(ShaderRules());
            problems.AddRange(UrpRules());

            var sb = new StringBuilder();
            sb.AppendLine("QuestSceneAudit " + System.DateTime.Now.ToString("dd.MM HH:mm"));
            foreach (var n in notes) sb.AppendLine("note: " + n);
            foreach (var pr in problems) sb.AppendLine("PROBLEM: " + pr);
            sb.AppendLine(problems.Count == 0 ? "RESULT OK" : $"RESULT FAILED ({problems.Count})");
            report = sb.ToString();
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/scene_audit.txt", report);
            Debug.Log("[FMVR] QuestSceneAudit: " + (problems.Count == 0 ? "OK" : "FAILED\n" + string.Join("\n", problems)));
            return problems.Count == 0;
        }
    }
}
