using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.EditorTools;
using FieldMedic.EditorTools.SceneBuilders;
using FieldMedic.Startup;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>w0b black-screen guard rails: every QuestSceneAudit rule, the SAFE-mode decision, cross-scene links,
    /// texture budgets, and the real Boot / Main_* scenes.</summary>
    public class BlackScreenGuardTests
    {
        static QuestSceneAudit.CamInfo Cam(string path = "OVRCameraRig/TrackingSpace/CenterEyeAnchor", float a = 1f, bool enabled = true,
            float near = 0.05f, float far = 1500f, string tag = "MainCamera", bool target = false) =>
            new QuestSceneAudit.CamInfo
            {
                name = path.Split('/').Last(), path = path, tag = tag, enabled = enabled, hasTarget = target,
                background = new Color(0.5f, 0.5f, 0.5f, a), clear = CameraClearFlags.SolidColor, near = near, far = far, cullingMask = ~0,
            };

        // ------------------------------------------------------------------ camera rules
        [Test] public void Camera_Good_Rig_Passes() => Assert.IsEmpty(QuestSceneAudit.CameraRules("t", new[] { Cam(), Cam("OVRCameraRig/TrackingSpace/LeftEyeAnchor", enabled: false) }));
        [Test] public void Camera_Two_Display_Cameras_Fail() => Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(), Cam("Other/Cam") }).Any(p => p.Contains("2 cameras")));
        [Test] public void Camera_None_Fails() => Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(enabled: false) }).Any(p => p.Contains("no enabled camera")));
        [Test] public void Camera_RenderTexture_Camera_Is_Not_A_Display_Camera() => Assert.IsEmpty(QuestSceneAudit.CameraRules("t", new[] { Cam(), Cam("Minimap/Cam", target: true) }));
        [Test] public void Camera_Alpha0_Background_Fails() => Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(a: 0f) }).Any(p => p.Contains("alpha")));
        [Test] public void Camera_Not_Under_Rig_Fails() => Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam("Main Camera") }).Any(p => p.Contains("CenterEyeAnchor")));
        [Test] public void Camera_Untagged_Fails() => Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(tag: "Untagged") }).Any(p => p.Contains("MainCamera")));
        [Test] public void Camera_Near_Far_Out_Of_Range_Fail()
        {
            Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(near: 0.3f) }).Any(p => p.Contains("near")));
            Assert.IsTrue(QuestSceneAudit.CameraRules("t", new[] { Cam(far: 420f) }).Any(p => p.Contains("far")));
        }

        // ------------------------------------------------------------------ boot / apk budgets
        [Test] public void Boot_Over_Budget_Fails() =>
            Assert.IsTrue(QuestSceneAudit.BootDependencyRules(new[] { ("Assets/Big.png", 6L * 1024 * 1024) }).Any(p => p.Contains("MB >")));
        [Test] public void Boot_Battlefield_Dependency_Fails() =>
            Assert.IsTrue(QuestSceneAudit.BootDependencyRules(new[] { ("Assets/_Licensed/BlenderKit/x.fbx", 1000L) }).Any(p => p.Contains("battlefield asset")));
        [Test] public void Boot_Small_Passes() =>
            Assert.IsEmpty(QuestSceneAudit.BootDependencyRules(new[] { ("Assets/Shaders/Ui.shader", 4000L), ("Assets/Scripts/Runtime/UI/BootLoader.cs", 9000000L) }));
        [Test] public void Apk_Budgets()
        {
            Assert.IsEmpty(QuestSceneAudit.ApkRules("a", 120f, true));
            Assert.IsTrue(QuestSceneAudit.ApkRules("a", 170f, true).Single().Contains("budget"));
            Assert.IsEmpty(QuestSceneAudit.ApkRules("a", 170f, false));
            Assert.IsTrue(QuestSceneAudit.ApkRules("a", 230f, false).Single().Contains("hard limit"));
        }

        // ------------------------------------------------------------------ SAFE mode
        [Test] public void SafeMode_Decision()
        {
            Assert.IsFalse(LaunchGuard.Decide(LaunchGuard.Stage.None, 0, false, false, out _), "first launch is full");
            Assert.IsFalse(LaunchGuard.Decide(LaunchGuard.Stage.Ready, 0, false, false, out _), "previous launch reached gameplay");
            Assert.IsTrue(LaunchGuard.Decide(LaunchGuard.Stage.Started, 0, false, false, out _), "stuck before the core → safe");
            Assert.IsTrue(LaunchGuard.Decide(LaunchGuard.Stage.Core, 1, false, false, out var why), "stuck while streaming → safe");
            StringAssert.Contains("core", why);
            Assert.IsFalse(LaunchGuard.Decide(LaunchGuard.Stage.Core, 3, true, false, out _), "force full wins");
            Assert.IsTrue(LaunchGuard.Decide(LaunchGuard.Stage.Ready, 0, false, true, out _), "force safe wins");
        }

        [Test] public void SafeMode_State_RoundTrip()
        {
            LaunchGuard.Parse(LaunchGuard.Format(LaunchGuard.Stage.Core, 2), out var st, out var n);
            Assert.AreEqual(LaunchGuard.Stage.Core, st); Assert.AreEqual(2, n);
            LaunchGuard.Parse("garbage", out st, out n);
            Assert.AreEqual(LaunchGuard.Stage.None, st);
            LaunchGuard.Parse(null, out st, out _);
            Assert.AreEqual(LaunchGuard.Stage.None, st);
        }

        [Test] public void SafeMode_Files_Flow()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fmvr_guard_test");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                LaunchGuard.Begin(dir);                       // first launch
                Assert.IsFalse(LaunchGuard.SafeMode);
                LaunchGuard.Mark(LaunchGuard.Stage.Core);     // ...then "killed" while streaming
                LaunchGuard.Begin(dir);
                Assert.IsTrue(LaunchGuard.SafeMode, "a launch that never reached gameplay → next is SAFE");
                LaunchGuard.Mark(LaunchGuard.Stage.Core);
                LaunchGuard.Mark(LaunchGuard.Stage.Ready);
                LaunchGuard.Begin(dir);
                Assert.IsFalse(LaunchGuard.SafeMode, "after a good (safe) launch the next one is FULL again");
            }
            finally { LaunchGuard.SetForTests(false); Directory.Delete(dir, true); }
        }

        // ------------------------------------------------------------------ cross-scene links
        class Holder : MonoBehaviour { public Transform head; public Transform[] list = new Transform[2]; public List<Transform> items = new List<Transform> { null }; }

        [Test] public void SceneLinker_Assigns_Fields_Arrays_Lists()
        {
            var go = new GameObject("h");
            var t = new GameObject("t").transform;
            try
            {
                var h = go.AddComponent<Holder>();
                Assert.IsTrue(SceneLinker.Assign(h, "head", t));
                Assert.IsTrue(SceneLinker.Assign(h, "list.Array.data[1]", t));
                Assert.IsTrue(SceneLinker.Assign(h, "items.Array.data[0]", t));
                Assert.AreSame(t, h.head); Assert.AreSame(t, h.list[1]); Assert.AreSame(t, h.items[0]);
                Assert.IsFalse(SceneLinker.Assign(h, "list.Array.data[5]", t), "out of range");
                Assert.IsFalse(SceneLinker.Assign(h, "nope", t), "unknown field");
                Assert.IsFalse(SceneLinker.Assign(h, "head", go), "wrong type");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(t.gameObject); }
        }

        // ------------------------------------------------------------------ textures
        [Test] public void Texture_Budgets()
        {
            Assert.AreEqual(2048, AndroidAssetOptimizer.MaxSizeFor("Assets/ThirdParty/PolyHaven/Textures/dry_ground_01/dry_ground_01_diff.jpg", false));
            Assert.AreEqual(1024, AndroidAssetOptimizer.MaxSizeFor("Assets/ThirdParty/PolyHaven/Textures/dry_ground_01/dry_ground_01_nor_gl.jpg", true));
            Assert.AreEqual(2048, AndroidAssetOptimizer.MaxSizeFor("Assets/Art/Characters/Erlan/Textures/Erlan_Body1_Albedo.jpg", false));
            Assert.AreEqual(512, AndroidAssetOptimizer.MaxSizeFor("Assets/_Licensed/BlenderKit/ruins/brick_ruins_a/Textures/PX_Brick_Ruins_10_LOD0_normal.jpg", true));
            Assert.AreEqual(1024, AndroidAssetOptimizer.MaxSizeFor("Assets/_Licensed/BlenderKit/ruins/brick_ruins_a/Textures/PX_Brick_Ruins_10_LOD0_albedo.jpg", false));
            Assert.AreEqual(512, AndroidAssetOptimizer.MaxSizeFor("Assets/_Licensed/BlenderKit/props/fuel_drums/Textures/Oil_Drum_200L_normal.jpg", true));
            Assert.AreEqual(512, AndroidAssetOptimizer.MaxSizeFor("Assets/_Licensed/BlenderKit/aircraft/f16_jet/Textures/x.jpg", false));
            Assert.IsTrue(AndroidAssetOptimizer.NeedsReadable("Assets/Art/Characters/Erlan/Erlan.fbx"));
            Assert.IsFalse(AndroidAssetOptimizer.NeedsReadable("Assets/_Licensed/BlenderKit/ruins/bombed_apartment/bombed_apartment.fbx"));
        }

        // ------------------------------------------------------------------ the real scenes
        [Test] public void Real_Scenes_Pass_The_Audit()
        {
            Assume.That(File.Exists(SceneSplitter.CorePath), "split scenes not generated yet");
            var bootScene = EditorSceneManager.OpenScene(BootSceneBuilder.ScenePath, OpenSceneMode.Single);
            Assert.IsEmpty(QuestSceneAudit.CameraRules("Boot", QuestSceneAudit.Cameras(new[] { bootScene })));
            Assert.IsEmpty(QuestSceneAudit.PassthroughRules("Boot", new[] { bootScene }));
            var deps = AssetDatabase.GetDependencies(BootSceneBuilder.ScenePath, true).Select(d => (d, File.Exists(d) ? new FileInfo(d).Length : 0L));
            Assert.IsEmpty(QuestSceneAudit.BootDependencyRules(deps));
            var boot = Object.FindAnyObjectByType<FieldMedic.UI.BootLoader>();
            Assert.AreEqual(GameplayScenes.CoreScene, boot.nextScene);

            var scenes = new List<UnityEngine.SceneManagement.Scene>();
            for (int i = 0; i < SceneSplitter.AllPaths.Length; i++)
                scenes.Add(EditorSceneManager.OpenScene(SceneSplitter.AllPaths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive));
            Assert.IsEmpty(QuestSceneAudit.CameraRules("set", QuestSceneAudit.Cameras(scenes)));
            Assert.IsEmpty(QuestSceneAudit.PassthroughRules("set", scenes));
            Assert.IsNotNull(Object.FindAnyObjectByType<GameplayScenes>(), "Main_Core needs the additive loader");
            Assert.IsNotNull(Object.FindAnyObjectByType<RigGrounding>(), "rig grounding");
            foreach (var s in scenes.Skip(1)) Assert.IsEmpty(QuestSceneAudit.Cameras(new[] { s }), s.name + " must not have a camera");
            foreach (var l in Object.FindObjectsByType<SceneLinker>(FindObjectsSortMode.None))
                foreach (var link in l.links)
                    Assert.IsNotNull(SceneLinker.Find(link.targetPath, link.targetType), $"{l.gameObject.scene.name}: link target {link.targetPath} ({link.targetType})");
        }

        [Test] public void Urp_And_Shaders_Pass_The_Audit()
        {
            QuestSceneAudit.EnsureAlwaysIncluded();
            Assert.IsEmpty(QuestSceneAudit.ShaderRules());
            Assert.IsEmpty(QuestSceneAudit.UrpRules());
        }

        [Test] public void Build_Variants_Have_The_Right_Scenes()
        {
            Assert.AreEqual(new[] { HelloSceneBuilder.ScenePath }, BuildTools.ScenesFor(BuildTools.Variant.Hello));
            Assert.AreEqual(new[] { BootSceneBuilder.ScenePath, SceneSplitter.CorePath }, BuildTools.ScenesFor(BuildTools.Variant.Safe));
            var full = BuildTools.ScenesFor(BuildTools.Variant.Full);
            Assert.AreEqual(BootSceneBuilder.ScenePath, full[0], "Boot first");
            Assert.AreEqual(SceneSplitter.CorePath, full[1]);
            Assert.AreEqual(2 + SceneSplitter.Additive.Length, full.Length);
            CollectionAssert.DoesNotContain(full, "Assets/Scenes/Main.unity", "legacy V1 trench stays out of the APK");
            CollectionAssert.DoesNotContain(full, "Assets/Scenes/Battlefield.unity", "authored source stays out of the APK");
        }
    }
}
