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
