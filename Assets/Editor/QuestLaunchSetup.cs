using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Enforces the Android/XR settings a Quest 3/3S launch depends on (w0 launch fix). Called at the start of every
    /// <see cref="BuildTools.BuildAndroid"/>; batch: -executeMethod FieldMedic.EditorTools.QuestLaunchSetup.ApplyBatch.
    /// Every change is logged with [FMVR].
    /// </summary>
    public static class QuestLaunchSetup
    {
        /// <summary>OpenXR features (Android) that must be on.</summary>
        public static readonly string[] RequiredFeatures =
        {
            "MetaQuestFeature",                 // Unity "Meta Quest Support" (Quest loader, VR intent, devices)
            "MetaXRFeature",                    // Meta XR Core SDK over OpenXR (OVRManager/OVRInput/OVRHand)
            "OculusTouchControllerProfile",     // Quest 3/3S controllers
            "MetaQuestTouchPlusControllerProfile",
        };

        /// <summary>OpenXR features (Android) switched off: unused here and the riskiest for a black first frame
        /// (Unity Space Warp needs motion-vector passes we do not render; subsampled layout + dynamic resolution
        /// on URP has a history of corrupt/black eye buffers). FFR still works through MetaXRFoveationFeature.</summary>
        public static readonly string[] DisabledFeatures =
        {
            "SpaceWarpFeature",
            "MetaXRSubsampledLayout",
            "MockRuntime",
            "ConformanceAutomationFeature",
            "RuntimeDebuggerOpenXRFeature",
        };

        public static void ApplyBatch()
        {
            Apply();
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Apply()
        {
            var changes = new List<string>();
            void Set<T>(string what, T current, T wanted, System.Action<T> setter)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                setter(wanted);
                changes.Add($"{what}: {current} -> {wanted}");
            }

            var nbt = UnityEditor.Build.NamedBuildTarget.Android;
            Set("scripting backend", PlayerSettings.GetScriptingBackend(nbt), ScriptingImplementation.IL2CPP, v => PlayerSettings.SetScriptingBackend(nbt, v));
            Set("architectures", PlayerSettings.Android.targetArchitectures, AndroidArchitecture.ARM64, v => PlayerSettings.Android.targetArchitectures = v);
            Set("minSdk", PlayerSettings.Android.minSdkVersion, AndroidSdkVersions.AndroidApiLevel32, v => PlayerSettings.Android.minSdkVersion = v);
            // Meta XR SDK v207 requires GameActivity on Unity 2023.2+ (OVRProjectSetup "AndroidCompatibility" rule)
            Set("application entry", PlayerSettings.Android.applicationEntry, AndroidApplicationEntry.GameActivity, v => PlayerSettings.Android.applicationEntry = v);
            Set("auto graphics API", PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android), false, v => PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, v));
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length == 0 || apis[0] != GraphicsDeviceType.Vulkan)
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
                changes.Add("graphics APIs: " + string.Join(",", apis) + " -> Vulkan");
            }
            Set("Android TV", PlayerSettings.Android.androidTVCompatibility, false, v => PlayerSettings.Android.androidTVCompatibility = v);

            // first scene
            // first scene: Boot (renders immediately, loads Main async), then Main
            var before = string.Join(",", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
            SceneBuilders.MainSceneBuilder.SetBuildScenes();
            var after = string.Join(",", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
            if (before != after) changes.Add($"build scenes: [{before}] -> [{after}]");

            // OpenXR features
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) Debug.LogError("[FMVR] QuestLaunchSetup: no OpenXR settings for Android");
            else
            {
                foreach (var f in settings.GetFeatures())
                {
                    string n = f.GetType().Name;
                    if (RequiredFeatures.Contains(n) && !f.enabled) { f.enabled = true; changes.Add("OpenXR " + n + " -> on"); }
                    if (DisabledFeatures.Contains(n) && f.enabled) { f.enabled = false; changes.Add("OpenXR " + n + " -> off"); }
                    if (n == "MetaQuestFeature") EnsureQuestTargets(f, changes);
                    EditorUtility.SetDirty(f);
                }
                foreach (var n in RequiredFeatures)
                    if (settings.GetFeatures().All(f => f.GetType().Name != n)) Debug.LogError("[FMVR] QuestLaunchSetup: OpenXR feature missing: " + n);
                EditorUtility.SetDirty(settings);
            }

            // Meta project config: target devices (manifest supportedDevices)
            var cfg = OVRProjectConfig.CachedProjectConfig;
            var want = new List<OVRProjectConfig.DeviceType>
                { OVRProjectConfig.DeviceType.Quest2, OVRProjectConfig.DeviceType.QuestPro, OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
            if (cfg != null && !want.All(cfg.targetDeviceTypes.Contains))
            {
                changes.Add("OVR target devices: " + string.Join(",", cfg.targetDeviceTypes) + " -> " + string.Join(",", want));
                cfg.targetDeviceTypes = want;
                OVRProjectConfig.CommitProjectConfig(cfg);
            }

            // w0b black screen: no passthrough anywhere, OS loading screen black (not passthrough-contextual),
            // no Unity splash (the license allows it; it only delays the first frame)
            if (cfg != null)
            {
                bool dirty = false;
                if (cfg.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None)
                { changes.Add("OVR passthrough support: " + cfg.insightPassthroughSupport + " -> None"); cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.None; dirty = true; }
                if (cfg.systemLoadingScreenBackground != OVRProjectConfig.SystemLoadingScreenBackground.Black)
                { changes.Add("OS loading screen: " + cfg.systemLoadingScreenBackground + " -> Black"); cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.Black; dirty = true; }
                if (dirty) OVRProjectConfig.CommitProjectConfig(cfg);
            }
            Set("Unity splash", PlayerSettings.SplashScreen.show, false, v => PlayerSettings.SplashScreen.show = v);
            Set("Unity splash logo", PlayerSettings.SplashScreen.showUnityLogo, false, v => PlayerSettings.SplashScreen.showUnityLogo = v);
            // licensed models are Read/Write off (memory); their MeshColliders get prebaked collision data
            Set("prebake collision meshes", PlayerSettings.bakeCollisionMeshes, true, v => PlayerSettings.bakeCollisionMeshes = v);
            Set("static batching", PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.Android), true, v => PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.Android, v));

            Debug.Log("[FMVR] QuestLaunchSetup: " + (changes.Count == 0 ? "all launch settings already OK" : string.Join("; ", changes)));
        }

        static void EnsureQuestTargets(Object mq, List<string> changes)
        {
            // targetDevices is a private serialized list; make sure Quest 3 (eureka) and Quest 3S are enabled
            var so = new SerializedObject(mq);
            var list = so.FindProperty("targetDevices");
            if (list == null || !list.isArray) return;
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                var name = e.FindPropertyRelative("manifestName");
                var en = e.FindPropertyRelative("enabled");
                if (name == null || en == null) continue;
                if ((name.stringValue == "eureka" || name.stringValue == "quest3s" || name.stringValue == "quest3") && !en.boolValue)
                {
                    en.boolValue = true;
                    changes.Add("Meta Quest Support target " + name.stringValue + " -> on");
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
