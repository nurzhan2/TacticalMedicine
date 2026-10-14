using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Cleans the generated Gradle project's manifests so the APK installs and launches on any Quest (w0 launch fix).
    /// Runs last (callbackOrder 10000), after the Meta manifest pre-processor and the Meta Platform SDK post-processor.
    /// - Strips permissions the trainer never uses: RECORD_AUDIO (pulled in by Meta SDK building blocks that reference
    ///   UnityEngine.Microphone → mic prompt), USE_SCENE / USE_ANCHOR_API (spatial-data prompt; boundary + passthrough
    ///   work without them).
    /// - Strips &lt;horizonos:uses-horizonos-supplement name="horizonos-supplement-hzplatformclientcore"&gt;, which the
    ///   Meta Platform SDK (part of com.meta.xr.sdk.all) injects unconditionally. It declares a dependency on an OS
    ///   supplement library the trainer never calls (no Platform SDK / app ID); headsets without that supplement may
    ///   refuse to install or start the app.
    /// - Forces com.oculus.supportedDevices to <see cref="SupportedDevices"/>, so a Quest 2 / Pro owner does not get
    ///   "app not supported".
    /// Removes nothing launch-critical: the MAIN/LAUNCHER/VR intent filter and the Vulkan/head-tracking features stay
    /// (verified after the build by <see cref="QuestManifestAudit"/>).
    /// </summary>
    public sealed class QuestManifestCleanup : IPostGenerateGradleAndroidProject
    {
        public static readonly string[] Removed =
        {
            "android.permission.RECORD_AUDIO",
            "com.oculus.permission.USE_SCENE",
            "com.oculus.permission.USE_ANCHOR_API",
        };

        public const string SupportedDevices = "quest2|questpro|quest3|quest3s";

        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path = <gradle>/unityLibrary
            Clean(Path.Combine(path, "src", "main", "AndroidManifest.xml"), false);
            Clean(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"), true);
        }

        /// <summary>Removes the permission lines and the Horizon OS supplement dependency, fixes the supported
        /// devices; in the app (launcher) manifest also adds tools:node="remove" markers so the Gradle manifest merger
        /// drops the permissions when they come from .aar libraries.</summary>
        public static string CleanXml(string xml, bool addRemoveMarkers)
        {
            foreach (var p in Removed)
                xml = Regex.Replace(xml, "\\s*<uses-permission[^>]*android:name=\"" + Regex.Escape(p) + "\"[^>]*/>", "");
            xml = Regex.Replace(xml, "\\s*<horizonos:uses-horizonos-supplement\\b[^>]*/>", "");
            xml = Regex.Replace(xml, "(<meta-data[^>]*android:name=\"com\\.oculus\\.supportedDevices\"[^>]*android:value=\")[^\"]*(\")",
                "${1}" + SupportedDevices + "${2}");
            xml = Regex.Replace(xml, "(<meta-data[^>]*android:value=\")[^\"]*(\"[^>]*android:name=\"com\\.oculus\\.supportedDevices\")",
                "${1}" + SupportedDevices + "${2}");
            if (!addRemoveMarkers) return xml;
            if (!xml.Contains("xmlns:tools="))
                xml = Regex.Replace(xml, "<manifest\\b", "<manifest xmlns:tools=\"http://schemas.android.com/tools\"", RegexOptions.None, System.TimeSpan.FromSeconds(1));
            var sb = new System.Text.StringBuilder();
            foreach (var p in Removed)
                sb.Append("\n  <uses-permission android:name=\"").Append(p).Append("\" tools:node=\"remove\" />");
            int i = xml.LastIndexOf("</manifest>", System.StringComparison.Ordinal);
            return i < 0 ? xml : xml.Substring(0, i) + sb + "\n" + xml.Substring(i);
        }

        static void Clean(string file, bool addRemoveMarkers)
        {
            if (!File.Exists(file)) { Debug.Log("[FMVR] manifest cleanup: no " + file); return; }
            var xml = File.ReadAllText(file);
            var cleaned = CleanXml(xml, addRemoveMarkers);
            if (cleaned != xml) File.WriteAllText(file, cleaned);
            Debug.Log($"[FMVR] manifest cleanup: {Path.GetFullPath(file)} ({xml.Length} → {cleaned.Length} chars)");
        }
    }
}
