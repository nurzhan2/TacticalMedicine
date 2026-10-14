using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Checks the FINAL merged AndroidManifest of a Quest build for everything that makes an APK fail to install or
    /// start in VR (w0 launch fix). <see cref="BuildTools.BuildAndroid"/> runs it on the Gradle merger output after
    /// every build and fails the build on problems; the report goes to logs/manifest_check.txt.
    /// </summary>
    public static class QuestManifestAudit
    {
        const string A = "http://schemas.android.com/apk/res/android";

        public const string MergedManifestPath =
            "Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/build/intermediates/merged_manifests/release/processReleaseManifest/AndroidManifest.xml";

        /// <summary>uses-feature entries that may be required="true" (every Quest has them).</summary>
        public static readonly string[] AllowedRequiredFeatures =
        {
            "android.hardware.vr.headtracking",
            "android.hardware.vulkan.version",
            "android.hardware.vulkan.level",
        };

        public static readonly string[] RequiredDevices = { "quest3", "quest3s" };

        /// <summary>Returns the list of problems (empty = OK). <paramref name="notes"/> gets informational lines.</summary>
        public static List<string> Audit(string xml, List<string> notes = null)
        {
            var problems = new List<string>();
            notes ??= new List<string>();
            var doc = new XmlDocument();
            try { doc.LoadXml(xml); }
            catch (XmlException e) { problems.Add("manifest is not valid XML: " + e.Message); return problems; }
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", A);
            string Attr(XmlNode n, string name) => (n as XmlElement)?.GetAttribute(name, A) ?? "";

            // uses-sdk
            var sdk = doc.SelectSingleNode("/manifest/uses-sdk");
            if (sdk != null && int.TryParse(Attr(sdk, "minSdkVersion"), out int min))
            {
                if (min < 32) problems.Add($"minSdkVersion {min} < 32 (Quest needs Android 12L+)");
                notes.Add($"minSdkVersion {min}, targetSdkVersion {Attr(sdk, "targetSdkVersion")}");
            }

            // launcher activity with MAIN + LAUNCHER + VR category
            XmlElement launcher = null;
            foreach (XmlElement act in doc.SelectNodes("/manifest/application/activity", ns))
            foreach (XmlElement f in act.SelectNodes("intent-filter", ns))
            {
                bool main = f.SelectNodes("action", ns).Cast<XmlElement>().Any(e => Attr(e, "name") == "android.intent.action.MAIN");
                var cats = f.SelectNodes("category", ns).Cast<XmlElement>().Select(e => Attr(e, "name")).ToList();
                if (!main || !cats.Contains("android.intent.category.LAUNCHER")) continue;
                launcher = act;
                if (!cats.Contains("com.oculus.intent.category.VR"))
                    problems.Add("launcher intent-filter has no com.oculus.intent.category.VR (app starts as a 2D panel)");
            }
            if (launcher == null) problems.Add("no activity with MAIN + LAUNCHER intent-filter (app has no icon / cannot start)");
            else
            {
                notes.Add("launcher activity: " + Attr(launcher, "name"));
                if (Attr(launcher, "exported") != "true") problems.Add("launcher activity is not android:exported=\"true\" (install fails on Android 12+)");
            }

            // supported devices
            var devMeta = doc.SelectNodes("/manifest/application/meta-data", ns).Cast<XmlElement>()
                .FirstOrDefault(e => Attr(e, "name") == "com.oculus.supportedDevices");
            if (devMeta == null) problems.Add("no com.oculus.supportedDevices meta-data");
            else
            {
                var devs = Attr(devMeta, "value").Split('|');
                notes.Add("supportedDevices: " + Attr(devMeta, "value"));
                foreach (var d in RequiredDevices)
                    if (!devs.Contains(d)) problems.Add($"supportedDevices does not contain {d}");
            }

            // hardware features
            foreach (XmlElement f in doc.SelectNodes("/manifest/uses-feature", ns))
            {
                string name = Attr(f, "name");
                bool required = Attr(f, "required") != "false"; // default is required
                if (required && !string.IsNullOrEmpty(name) && !AllowedRequiredFeatures.Contains(name))
                    problems.Add($"uses-feature {name} is required=\"true\" (install fails if the Quest lacks it)");
            }

            // shared / native libraries
            foreach (XmlElement l in doc.SelectNodes("/manifest/application/uses-library|/manifest/application/uses-native-library", ns))
                if (Attr(l, "required") != "false")
                    problems.Add($"{l.Name} {Attr(l, "name")} is required (install fails when missing)");

            // Horizon OS supplement dependency (Meta Platform SDK, unused here)
            foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                if (n.LocalName == "uses-horizonos-supplement")
                    problems.Add("horizonos:uses-horizonos-supplement " + (n as XmlElement)?.GetAttribute("name") + " present (unused Platform SDK dependency)");

            // permissions that cause prompts
            var perms = doc.SelectNodes("/manifest/uses-permission", ns).Cast<XmlElement>().Select(e => Attr(e, "name")).ToList();
            foreach (var p in QuestManifestCleanup.Removed)
                if (perms.Contains(p)) problems.Add("permission " + p + " still present (prompt at first launch)");
            notes.Add("permissions: " + string.Join(", ", perms));
            return problems;
        }

        /// <summary>Audits the merged manifest of the last build, writes logs/manifest_check.txt. True = OK.</summary>
        public static bool AuditLastBuild(out string report)
        {
            var notes = new List<string>();
            List<string> problems;
            if (!File.Exists(MergedManifestPath)) problems = new List<string> { "merged manifest not found: " + MergedManifestPath };
            else
            {
                var xml = File.ReadAllText(MergedManifestPath);
                problems = Audit(xml, notes);
                Directory.CreateDirectory("logs");
                File.WriteAllText("logs/merged_manifest.xml", xml);
            }
            report = "Quest manifest audit " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" +
                     (problems.Count == 0 ? "RESULT: OK\n" : "RESULT: " + problems.Count + " PROBLEM(S)\n") +
                     string.Concat(problems.Select(p => "  PROBLEM " + p + "\n")) +
                     string.Concat(notes.Select(n => "  " + n + "\n"));
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/manifest_check.txt", report);
            foreach (var line in report.Split('\n')) if (line.Length > 0) Debug.Log("[FMVR] " + line.Trim());
            return problems.Count == 0;
        }
    }
}
