using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Quest APK builds. Batch: -executeMethod FieldMedic.EditorTools.BuildTools.BuildAndroid (FULL, the game) or
    /// w0b bisect set: BuildHello / BuildSafe / BuildFull / BuildFullDev / BuildW0bSet (HELLO + SAFE + FULL [+ DEV]).
    /// Every build: QuestLaunchSetup → QuestSceneAudit (fails on black-screen risks) → build → manifest audit →
    /// size budget → report of the 50 largest assets (logs/apk_report_*.txt) → copy to the Desktop.
    /// Exit code 1 build failed, 2 manifest audit, 3 scene audit, 4 APK over the hard limit.
    /// </summary>
    public static class BuildTools
    {
        public const string ApkPath = "Builds/FieldMedicVR.apk";
        public const string TestDir = "FMVR_TEST";

        public enum Variant { Hello, Safe, Full, FullDev }

        [MenuItem("FieldMedic/Build/Android APK (Quest)")]
        public static void BuildAndroid() => BatchExit(Build(Variant.Full, ApkPath, null));
        public static void BuildHello() => BatchExit(Build(Variant.Hello, "Builds/1_HELLO.apk", "1_HELLO.apk"));
        public static void BuildSafe() => BatchExit(Build(Variant.Safe, "Builds/2_SAFE.apk", "2_SAFE.apk"));
        public static void BuildFull() => BatchExit(Build(Variant.Full, "Builds/3_FULL.apk", "3_FULL.apk"));
        public static void BuildFullDev() => BatchExit(Build(Variant.FullDev, "Builds/3_FULL_DEV.apk", "3_FULL_DEV.apk"));

        /// <summary>FULL first (the game), then HELLO and SAFE (incremental, fast), then DEV unless -skipDev.</summary>
        public static void BuildW0bSet()
        {
            int code = Build(Variant.Full, "Builds/3_FULL.apk", "3_FULL.apk");
            if (code == 0 || code == 4) File.Copy("Builds/3_FULL.apk", ApkPath, true);
            int c1 = Build(Variant.Hello, "Builds/1_HELLO.apk", "1_HELLO.apk");
            int c2 = Build(Variant.Safe, "Builds/2_SAFE.apk", "2_SAFE.apk");
            int c3 = System.Environment.GetCommandLineArgs().Contains("-skipDev") ? 0 : Build(Variant.FullDev, "Builds/3_FULL_DEV.apk", "3_FULL_DEV.apk");
            Debug.Log($"[FMVR] w0b set: FULL {code} HELLO {c1} SAFE {c2} DEV {c3}");
            BatchExit(new[] { code, c1, c2, c3 }.FirstOrDefault(c => c != 0));
        }

        /// <summary>Rebuild only the game APKs (after content/import changes): FULL then FULL_DEV.</summary>
        public static void BuildFullAndDev()
        {
            int code = Build(Variant.Full, "Builds/3_FULL.apk", "3_FULL.apk");
            if (code == 0 || code == 4) File.Copy("Builds/3_FULL.apk", ApkPath, true);
            int c3 = Build(Variant.FullDev, "Builds/3_FULL_DEV.apk", "3_FULL_DEV.apk");
            Debug.Log($"[FMVR] w0b full+dev: FULL {code} DEV {c3}");
            BatchExit(code != 0 ? code : c3);
        }

        public const string DemoDir = "FMVR_DEMO";

        /// <summary>w7 presentation set: FULL (demo button, all w0b guards) + SAFE (core only, no battle) → Desktop\FMVR_DEMO.</summary>
        public static void BuildDemoSet()
        {
            int code = Build(Variant.Full, "Builds/FieldMedicVR_DEMO.apk", null);
            if (code == 0 || code == 4) { File.Copy("Builds/FieldMedicVR_DEMO.apk", ApkPath, true); CopyToDemo("Builds/FieldMedicVR_DEMO.apk"); }
            int c2 = Build(Variant.Safe, "Builds/FieldMedicVR_SAFE.apk", null);
            if (c2 == 0) CopyToDemo("Builds/FieldMedicVR_SAFE.apk");
            Debug.Log($"[FMVR] w7 demo set: DEMO {code} SAFE {c2}");
            BatchExit(code != 0 ? code : c2);
        }

        public static void BuildDemoSafeOnly()
        {
            int c2 = Build(Variant.Safe, "Builds/FieldMedicVR_SAFE.apk", null);
            if (c2 == 0) CopyToDemo("Builds/FieldMedicVR_SAFE.apk");
            BatchExit(c2);
        }

        static void CopyToDemo(string apk)
        {
            try
            {
                var desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                var dir = Path.Combine(desk, DemoDir);
                Directory.CreateDirectory(dir);
                File.Copy(apk, Path.Combine(dir, Path.GetFileName(apk)), true);
                Debug.Log("[FMVR] APK copied to " + Path.Combine(dir, Path.GetFileName(apk)));
            }
            catch (Exception e) { Debug.LogWarning("[FMVR] demo copy failed: " + e.Message); }
        }

        static void BatchExit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

        public static string[] ScenesFor(Variant v)
        {
            switch (v)
            {
                case Variant.Hello: return new[] { SceneBuilders.HelloSceneBuilder.ScenePath };
                case Variant.Safe: return new[] { SceneBuilders.BootSceneBuilder.ScenePath, SceneBuilders.SceneSplitter.CorePath };
                default: return new[] { SceneBuilders.BootSceneBuilder.ScenePath }.Concat(SceneBuilders.SceneSplitter.AllPaths).ToArray();
            }
        }

        /// <summary>
        /// 18.10 device log: every APK shipped WITHOUT libUnityOpenXR.so and XR never started (loader=none) because
        /// XR Management printed "No XR Manager settings found" in batch builds. Register the settings explicitly.
        /// </summary>
        public static bool EnsureXrSettings()
        {
            const string path = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            var per = AssetDatabase.LoadAssetAtPath<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>(path);
            if (per == null) { Debug.LogError("[FMVR] XR settings asset missing: " + path); return false; }
            EditorBuildSettings.AddConfigObject(UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey, per, true);
            var gs = per.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (gs == null)
            {
                // Root cause (18.10): the asset was written in the m_SettingsPerBuildTarget format, but the installed
                // XR Management 4.6.1 reads Keys/Values only -> empty map -> no XR in any APK. Re-register the sub-assets.
                var subs = AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.XR.Management.XRGeneralSettings>().ToList();
                var android = subs.FirstOrDefault(s => s.name == "Android Settings");
                var standalone = subs.FirstOrDefault(s => s.name == "Standalone Settings");
                if (android != null) { android.InitManagerOnStart = true; per.SetSettingsForBuildTarget(BuildTargetGroup.Android, android); EditorUtility.SetDirty(android); }
                if (standalone != null) per.SetSettingsForBuildTarget(BuildTargetGroup.Standalone, standalone);
                EditorUtility.SetDirty(per);
                AssetDatabase.SaveAssets();
                gs = per.SettingsForBuildTarget(BuildTargetGroup.Android);
                Debug.Log("[FMVR] XR settings map repaired (" + subs.Count + " general settings found)");
            }
            int n = gs != null && gs.Manager != null ? gs.Manager.activeLoaders.Count : 0;
            if (n == 0 && gs != null && gs.Manager != null)
            {
                var la = AssetDatabase.LoadAssetAtPath<UnityEngine.XR.Management.XRLoader>("Assets/XR/Loaders/OpenXRLoader.asset");
                Debug.Log("[FMVR] OpenXRLoader asset: " + (la != null ? la.GetType().FullName : "NULL"));
                bool ok = UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(gs.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
                if (gs.Manager.activeLoaders.Count == 0 && la != null) gs.Manager.TryAddLoader(la);
                EditorUtility.SetDirty(gs.Manager);
                AssetDatabase.SaveAssets();
                n = gs.Manager.activeLoaders.Count;
                Debug.Log("[FMVR] AssignLoader OpenXR: " + ok + " -> loaders " + n);
            }
            Debug.Log($"[FMVR] XR settings registered: android loaders={n} initOnStart={(gs != null && gs.InitManagerOnStart)}");
            if (n == 0 || !gs.InitManagerOnStart) { Debug.LogError("[FMVR] XR: no active Android loader / init on start off"); return false; }
            return true;
        }

        public static int Build(Variant v, string apk, string testName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(apk));
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = v == Variant.FullDev;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            QuestLaunchSetup.Apply();
            if (!File.Exists(SceneBuilders.BootSceneBuilder.ScenePath)) SceneBuilders.BootSceneBuilder.Build();
            if (v == Variant.Hello && !File.Exists(SceneBuilders.HelloSceneBuilder.ScenePath)) SceneBuilders.HelloSceneBuilder.Build();
            if (v != Variant.Hello)
            {
                // the authored Battlefield is newer than the shipped split → split again
                if (!File.Exists(SceneBuilders.SceneSplitter.CorePath) ||
                    File.GetLastWriteTimeUtc(FieldMedic.Environment.BattlefieldLayout.ScenePath) > File.GetLastWriteTimeUtc(SceneBuilders.SceneSplitter.CorePath).AddMinutes(1))
                    SceneBuilders.SceneSplitter.Split();
                if (v == Variant.Full) MeshyImporter.ImportAll(); // owner's Meshy IFAK files dropped since the last import
                if (!QuestSceneAudit.Run(v == Variant.Safe ? new[] { SceneBuilders.SceneSplitter.CorePath } : SceneBuilders.SceneSplitter.AllPaths, out var rep))
                {
                    Debug.LogError("[FMVR] Build " + v + ": scene audit FAILED, see logs/scene_audit.txt\n" + rep);
                    return 3;
                }
            }
            AssetDatabase.SaveAssets();
            if (!EnsureXrSettings()) return 6;
            var scenes = ScenesFor(v);
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC | (v == Variant.FullDev ? BuildOptions.Development : BuildOptions.None),
            };

            Debug.Log($"[FMVR] Build {v}: " + string.Join(", ", scenes));
            var t0 = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            EditorUserBuildSettings.development = false;
            var sum = report.summary;
            double mins = (DateTime.Now - t0).TotalMinutes;
            float mb = File.Exists(apk) ? new FileInfo(apk).Length / (1024f * 1024f) : 0f;
            Debug.Log($"[FMVR] Build {v} result={sum.result} apk={mb:F1} MB errors={sum.totalErrors} warnings={sum.totalWarnings} time={mins:F1} min");

            if (sum.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.Log("[FMVR] build error: " + m.content);
                return 1;
            }
            WriteReport(report, v, apk, mb);

            // the final merged manifest must still launch in VR (w0 launch fix)
            if (!QuestManifestAudit.AuditLastBuild(out _))
            {
                Debug.LogError("[FMVR] Build " + v + ": manifest audit FAILED, see logs/manifest_check.txt");
                return 2;
            }
            var sizeProblems = QuestSceneAudit.ApkRules(Path.GetFileName(apk), mb, v == Variant.Full);
            foreach (var p in sizeProblems) Debug.LogWarning("[FMVR] " + p);
            CopyToDesktop(apk, testName);
            return mb > QuestSceneAudit.ApkHardLimitMB && v != Variant.FullDev ? 4 : 0; // DEV carries debug symbols
        }

        /// <summary>Top 50 assets by packed size + per-type totals → logs/apk_report_&lt;variant&gt;.txt.</summary>
        static void WriteReport(BuildReport report, Variant v, string apk, float mb)
        {
            var byAsset = new Dictionary<string, ulong>();
            var byType = new Dictionary<string, ulong>();
            var texByFolder = new Dictionary<string, ulong>();
            var texList = new Dictionary<string, ulong>();
            foreach (var pa in report.packedAssets)
            foreach (var c in pa.contents)
            {
                string key = string.IsNullOrEmpty(c.sourceAssetPath) ? "(built-in) " + c.type.Name : c.sourceAssetPath;
                byAsset.TryGetValue(key, out var a); byAsset[key] = a + c.packedSize;
                string t = c.type != null ? c.type.Name : "?";
                byType.TryGetValue(t, out var b); byType[t] = b + c.packedSize;
                if (t == "Texture2D" || t == "Cubemap")
                {
                    var parts = key.Split('/');
                    string folder = parts.Length > 3 ? string.Join("/", parts.Take(4)) : key;
                    texByFolder.TryGetValue(folder, out var f); texByFolder[folder] = f + c.packedSize;
                    texList.TryGetValue(key, out var x); texList[key] = x + c.packedSize;
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"{v} {apk} {mb:F1} MB (APK, compressed) — {DateTime.Now:dd.MM HH:mm}");
            sb.AppendLine("== packed size by type (uncompressed in the data files)");
            foreach (var kv in byType.OrderByDescending(k => k.Value).Take(12)) sb.AppendLine($"{kv.Value / 1048576f,8:F1} MB  {kv.Key}");
            sb.AppendLine("== textures by folder");
            foreach (var kv in texByFolder.OrderByDescending(k => k.Value).Take(25)) sb.AppendLine($"{kv.Value / 1048576f,8:F1} MB  {kv.Key}");
            sb.AppendLine("== top 30 textures");
            foreach (var kv in texList.OrderByDescending(k => k.Value).Take(30)) sb.AppendLine($"{kv.Value / 1048576f,8:F2} MB  {kv.Key}");
            sb.AppendLine("== top 50 assets");
            foreach (var kv in byAsset.OrderByDescending(k => k.Value).Take(50)) sb.AppendLine($"{kv.Value / 1048576f,8:F2} MB  {kv.Key}");
            Directory.CreateDirectory("logs");
            File.WriteAllText($"logs/apk_report_{v.ToString().ToLowerInvariant()}.txt", sb.ToString());
            Debug.Log("[FMVR] build report: logs/apk_report_" + v.ToString().ToLowerInvariant() + ".txt");
        }

        /// <summary>Copies the APK to the owner's Desktop (and Desktop\FMVR_TEST\&lt;name&gt; for the bisect set).</summary>
        public static void CopyToDesktop(string apk = ApkPath, string testName = null)
        {
            try
            {
                var desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) return;
                string dst;
                if (testName != null)
                {
                    Directory.CreateDirectory(Path.Combine(desk, TestDir));
                    dst = Path.Combine(desk, TestDir, testName);
                }
                else dst = Path.Combine(desk, Path.GetFileName(apk));
                File.Copy(apk, dst, true);
                Debug.Log("[FMVR] APK copied to " + dst);
            }
            catch (Exception e) { Debug.LogWarning("[FMVR] Desktop copy failed: " + e.Message); }
        }
    }
}
