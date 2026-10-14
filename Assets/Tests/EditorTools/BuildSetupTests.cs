using FieldMedic.Comfort;
using FieldMedic.EditorTools;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    public class BuildSetupTests
    {
        const string LibManifest =
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">\n" +
            "  <uses-permission android:name=\"com.oculus.permission.HAND_TRACKING\" />\n" +
            "  <uses-permission android:name=\"android.permission.RECORD_AUDIO\" />\n" +
            "  <uses-permission android:name=\"com.oculus.permission.USE_SCENE\" />\n" +
            "</manifest>";

        [Test]
        public void ManifestCleanup_RemovesUnusedPermissions_KeepsHandTracking()
        {
            var x = QuestManifestCleanup.CleanXml(LibManifest, false);
            StringAssert.Contains("HAND_TRACKING", x);
            StringAssert.DoesNotContain("RECORD_AUDIO", x);
            StringAssert.DoesNotContain("USE_SCENE", x);
            StringAssert.EndsWith("</manifest>", x);
        }

        [Test]
        public void ManifestCleanup_AppManifest_GetsMergerRemoveMarkers()
        {
            var x = QuestManifestCleanup.CleanXml("<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">\n</manifest>", true);
            StringAssert.Contains("xmlns:tools=\"http://schemas.android.com/tools\"", x);
            StringAssert.Contains("android.permission.RECORD_AUDIO\" tools:node=\"remove\"", x);
            StringAssert.Contains("com.oculus.permission.USE_SCENE\" tools:node=\"remove\"", x);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(x, "xmlns:tools=").Count);
        }

        const string GoodMerged =
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" xmlns:horizonos=\"http://schemas.horizonos/sdk\" package=\"kz.qorsa.fieldmedic\">\n" +
            "  <uses-sdk android:minSdkVersion=\"32\" android:targetSdkVersion=\"34\" />\n" +
            "  <uses-permission android:name=\"com.oculus.permission.HAND_TRACKING\" />\n" +
            "  <uses-feature android:name=\"android.hardware.vr.headtracking\" android:required=\"true\" android:version=\"1\" />\n" +
            "  <uses-feature android:name=\"oculus.software.handtracking\" android:required=\"false\" />\n" +
            "  <uses-feature android:name=\"android.hardware.vulkan.version\" android:required=\"true\" />\n" +
            "  <horizonos:uses-horizonos-sdk horizonos:minSdkVersion=\"60\" horizonos:targetSdkVersion=\"207\" />\n" +
            "  <application>\n" +
            "    <meta-data android:name=\"com.oculus.supportedDevices\" android:value=\"quest2|questpro|quest3|quest3s\" />\n" +
            "    <activity android:name=\"com.unity3d.player.UnityPlayerGameActivity\" android:exported=\"true\">\n" +
            "      <intent-filter>\n" +
            "        <category android:name=\"android.intent.category.LAUNCHER\" />\n" +
            "        <category android:name=\"com.oculus.intent.category.VR\" />\n" +
            "        <action android:name=\"android.intent.action.MAIN\" />\n" +
            "      </intent-filter>\n" +
            "    </activity>\n" +
            "    <uses-native-library android:name=\"libhzos.meta.so\" android:required=\"false\" />\n" +
            "  </application>\n" +
            "</manifest>";

        [Test]
        public void ManifestAudit_GoodQuestManifest_Passes()
        {
            var notes = new System.Collections.Generic.List<string>();
            var p = QuestManifestAudit.Audit(GoodMerged, notes);
            Assert.IsEmpty(p, string.Join("\n", p));
            Assert.That(notes, Has.Some.Contains("UnityPlayerGameActivity"));
        }

        [Test]
        public void ManifestAudit_Flags_LaunchBlockers()
        {
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("<category android:name=\"com.oculus.intent.category.VR\" />", "")),
                Has.Some.Contains("com.oculus.intent.category.VR"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("android.intent.category.LAUNCHER", "android.intent.category.DEFAULT")),
                Has.Some.Contains("MAIN + LAUNCHER"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("quest2|questpro|quest3|quest3s", "quest2|quest3")),
                Has.Some.Contains("quest3s"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("<application>",
                "<uses-feature android:name=\"android.hardware.camera\" />\n  <application>")), Has.Some.Contains("android.hardware.camera"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("<application>",
                "<horizonos:uses-horizonos-supplement name=\"horizonos-supplement-hzplatformclientcore\" minSupplementVersion=\"1\" />\n  <application>")),
                Has.Some.Contains("supplement"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("android:required=\"false\" />\n  </application>", "/>\n  </application>")),
                Has.Some.Contains("libhzos"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("android:minSdkVersion=\"32\"", "android:minSdkVersion=\"29\"")),
                Has.Some.Contains("minSdkVersion"));
            Assert.That(QuestManifestAudit.Audit(GoodMerged.Replace("android:exported=\"true\"", "")), Has.Some.Contains("exported"));
            Assert.That(QuestManifestAudit.Audit("<manifest"), Has.Some.Contains("XML"));
        }

        [Test]
        public void ManifestCleanup_Strips_PlatformSupplement_And_Fixes_Devices_Keeps_Launcher()
        {
            var lib = GoodMerged
                .Replace("quest2|questpro|quest3|quest3s", "quest3")
                .Replace("<application>", "<horizonos:uses-horizonos-supplement name=\"horizonos-supplement-hzplatformclientcore\" minSupplementVersion=\"1\" targetSupplementVersion=\"1\" />\n  <application>");
            var x = QuestManifestCleanup.CleanXml(lib, false);
            StringAssert.DoesNotContain("uses-horizonos-supplement", x);
            StringAssert.Contains("uses-horizonos-sdk", x);
            StringAssert.Contains("android:value=\"" + QuestManifestCleanup.SupportedDevices + "\"", x);
            Assert.IsEmpty(QuestManifestAudit.Audit(x));
            // attribute order value-before-name (Unity custom manifests) is fixed too
            var y = QuestManifestCleanup.CleanXml("<manifest><application><meta-data android:value=\"quest3\" android:name=\"com.oculus.supportedDevices\" /></application></manifest>", false);
            StringAssert.Contains(QuestManifestCleanup.SupportedDevices, y);
        }

        [Test]
        public void QuestPerformanceProfile_WithinBudget()
        {
            Assert.AreEqual(72f, QuestPerformance.RefreshRate);
            Assert.That(QuestPerformance.MinResolutionScale, Is.InRange(0.6f, 1f));
            Assert.That(QuestPerformance.MaxResolutionScale, Is.InRange(1f, 1.3f));
            Assert.AreEqual(OVRManager.FoveatedRenderingLevel.High, QuestPerformance.Foveation);
        }
    }
}
