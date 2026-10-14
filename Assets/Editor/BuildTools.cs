using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Quest APK build. Batch: -executeMethod FieldMedic.EditorTools.BuildTools.BuildAndroid
    /// Output: Builds/FieldMedicVR.apk (release, LZ4HC). Exit code 1 on failure.
    /// </summary>
    public static class BuildTools
    {
        public const string ApkPath = "Builds/FieldMedicVR.apk";

        [MenuItem("FieldMedic/Build/Android APK (Quest)")]
        public static void BuildAndroid()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/Main.unity" };
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            QuestLaunchSetup.Apply();
            AssetDatabase.SaveAssets();

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC,
            };

            Debug.Log("[FMVR] BuildAndroid: " + string.Join(", ", scenes));
            var t0 = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            double mins = (DateTime.Now - t0).TotalMinutes;
            Debug.Log($"[FMVR] BuildAndroid result={sum.result} size={sum.totalSize / (1024f * 1024f):F1} MB " +
                      $"errors={sum.totalErrors} warnings={sum.totalWarnings} time={mins:F1} min");

            if (sum.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.Log("[FMVR] build error: " + m.content);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // the final merged manifest must still launch in VR (w0 launch fix)
            if (!QuestManifestAudit.AuditLastBuild(out _))
            {
                Debug.LogError("[FMVR] BuildAndroid: manifest audit FAILED, see logs/manifest_check.txt");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            CopyToDesktop();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Copies the APK to the owner's Desktop (sideload with MQDH / SideQuest / adb).</summary>
        public static void CopyToDesktop()
        {
            try
            {
                var desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) return;
                var dst = Path.Combine(desk, Path.GetFileName(ApkPath));
                File.Copy(ApkPath, dst, true);
                Debug.Log("[FMVR] APK copied to " + dst);
            }
            catch (Exception e) { Debug.LogWarning("[FMVR] Desktop copy failed: " + e.Message); }
        }
    }
}
