using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Strips permissions the trainer never uses from the generated Gradle project, so the first launch on the
    /// Quest does not ask for the microphone (RECORD_AUDIO is pulled in by Meta SDK building blocks that reference
    /// UnityEngine.Microphone) or for spatial data (USE_SCENE / USE_ANCHOR_API — boundary + passthrough work without
    /// them). Runs last (callbackOrder 10000), after the Meta manifest pre-processor.
    /// </summary>
    public sealed class QuestManifestCleanup : IPostGenerateGradleAndroidProject
    {
        public static readonly string[] Removed =
        {
            "android.permission.RECORD_AUDIO",
            "com.oculus.permission.USE_SCENE",
            "com.oculus.permission.USE_ANCHOR_API",
        };

        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path = <gradle>/unityLibrary
            Clean(Path.Combine(path, "src", "main", "AndroidManifest.xml"), false);
            Clean(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"), true);
        }

        /// <summary>Removes the permission lines; in the app (launcher) manifest also adds tools:node="remove"
        /// markers so the Gradle manifest merger drops them when they come from .aar libraries.</summary>
        public static string CleanXml(string xml, bool addRemoveMarkers)
        {
            foreach (var p in Removed)
                xml = Regex.Replace(xml, "\\s*<uses-permission[^>]*android:name=\"" + Regex.Escape(p) + "\"[^>]*/>", "");
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
