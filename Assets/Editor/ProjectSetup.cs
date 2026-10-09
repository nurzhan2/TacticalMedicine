using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// One-shot project configuration for Quest 3S (Android, IL2CPP ARM64, Vulkan, Linear, URP mobile,
    /// OpenXR + Meta Quest feature group, hand tracking, passthrough "Supported"), then Meta's
    /// Project Setup Tool "fix all" for Android. Idempotent — safe to re-run.
    /// Batch: -executeMethod FieldMedic.EditorTools.ProjectSetup.Apply
    /// </summary>
    public static class ProjectSetup
    {
        public const string AppId = "kz.qorsa.fieldmedic";
        public const string ProductName = "Бауыр — Field Medic VR";
        public const string Company = "Qorsa Studio";
        public const string MobileRpPath = "Assets/Settings/Mobile_RPAsset.asset";

        static readonly StringBuilder Report = new StringBuilder();
        static void Log(string s) { Report.AppendLine(s); Debug.Log("[FMVR] " + s); }

        [MenuItem("FieldMedic/Setup/Apply Project Settings")]
        public static void Apply()
        {
            Report.Clear();
            try
            {
                SwitchToAndroid();
                ApplyPlayerSettings();
                ApplyRendering();
                ApplyOvrConfig();
                ApplyOpenXR();
                ApplyPhysics();
                RunMetaProjectSetupFixAll();
                // re-assert our choices in case a recommended fix touched them
                ApplyPlayerSettings();
                ApplyRendering();
                AssetDatabase.SaveAssets();
                LogFinalState();
                Log("ProjectSetup.Apply done");
            }
            catch (Exception e)
            {
                Debug.LogError("[FMVR] ProjectSetup failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/project_setup_report.txt", Report.ToString());
            // Meta's setup processors keep editor-update hooks alive and -quit then never fires
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                Log("Switch build target to Android: " + ok);
            }
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        static void ApplyPlayerSettings()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(android, AppId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetMobileMTRendering(android, true);
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);

            // Input System + legacy: some Meta helper scripts still use UnityEngine.Input.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 2) { p.intValue = 2; so.ApplyModifiedPropertiesWithoutUndo(); Log("activeInputHandler = Both"); }
            }
        }

        static void ApplyRendering()
        {
            var rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileRpPath);
            if (rp == null) { Debug.LogWarning("[FMVR] Mobile_RPAsset not found"); return; }

            GraphicsSettings.defaultRenderPipeline = rp;

            // URP asset knobs via serialized fields (some have no public setters)
            var so = new SerializedObject(rp);
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", false);
            SetFloat(so, "m_RenderScale", 1f);
            SetInt(so, "m_MainLightRenderingMode", 1);          // per pixel
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 1024);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", false);
            SetFloat(so, "m_ShadowDistance", 15f);
            SetInt(so, "m_ShadowCascadeCount", 1);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // per pixel (few small flashes)
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 4);
            SetBool(so, "m_RequireDepthTexture", false);
            SetBool(so, "m_RequireOpaqueTexture", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);

            // Quality: make sure the "Mobile" level uses the mobile asset and is Android's default
            var names = QualitySettings.names;
            int mobile = Array.FindIndex(names, n => n.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0);
            if (mobile < 0) mobile = 0;
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs != null)
            {
                var qso = new SerializedObject(qs);
                var levels = qso.FindProperty("m_QualitySettings");
                if (levels != null && mobile < levels.arraySize)
                {
                    var lvl = levels.GetArrayElementAtIndex(mobile);
                    var crp = lvl.FindPropertyRelative("customRenderPipeline");
                    if (crp != null) crp.objectReferenceValue = rp;
                    var aa = lvl.FindPropertyRelative("antiAliasing");
                    if (aa != null) aa.intValue = 4;
                    var sd = lvl.FindPropertyRelative("shadowDistance");
                    if (sd != null) sd.floatValue = 15f;
                    var vs = lvl.FindPropertyRelative("vSyncCount");
                    if (vs != null) vs.intValue = 0;
                }
                var perPlatform = qso.FindProperty("m_PerPlatformDefaultQuality");
                if (perPlatform != null)
                {
                    for (int i = 0; i < perPlatform.arraySize; i++)
                    {
                        var e = perPlatform.GetArrayElementAtIndex(i);
                        var key = e.FindPropertyRelative("first");
                        var val = e.FindPropertyRelative("second");
                        if (key != null && val != null && (key.stringValue == "Android" || key.stringValue == "MetaQuest"))
                            val.intValue = mobile;
                    }
                }
                qso.ApplyModifiedPropertiesWithoutUndo();
            }
            Log($"URP: default + quality[{mobile}:{names[mobile]}] = {rp.name}, MSAA4, HDR off, scale 1, main-light hard shadows 15 m");
        }

        static void ApplyOvrConfig()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(cfg);
            Log("OVRProjectConfig: hand tracking = ControllersAndHands, passthrough = Supported");
        }

        static void ApplyOpenXR()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (standalone != null) RemoveMissingFeatures(standalone);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) { Debug.LogWarning("[FMVR] No OpenXR settings for Android"); return; }
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            RemoveMissingFeatures(settings);
            string[] want =
            {
                "MetaQuestFeature",                    // Meta Quest support (feature group)
                "MetaXRFeature",                       // Meta XR core (OVRPlugin over OpenXR)
                "OculusTouchControllerProfile",
                "MetaQuestTouchPlusControllerProfile", // Quest 3/3S controllers
            };
            foreach (var f in settings.GetFeatures())
            {
                if (f == null) continue;
                string t = f.GetType().Name;
                if (want.Contains(t) && !f.enabled)
                {
                    f.enabled = true;
                    EditorUtility.SetDirty(f);
                    Log("OpenXR Android: enabled " + t);
                }
            }
            EditorUtility.SetDirty(settings);
            Log("OpenXR Android enabled: " + string.Join(", ", settings.GetFeatures().Where(f => f != null && f.enabled).Select(f => f.GetType().Name)));
        }

        /// <summary>
        /// The collision matrix inherited from the template is in a format 6.3 does not read back, which
        /// leaves every layer pair ignored (objects fall through the floor). Re-assert "everything collides".
        /// </summary>
        static void ApplyPhysics()
        {
            int fixedPairs = 0;
            for (int a = 0; a < 32; a++)
            for (int b = a; b < 32; b++)
            {
                if (!Physics.GetIgnoreLayerCollision(a, b)) continue;
                Physics.IgnoreLayerCollision(a, b, false);
                fixedPairs++;
            }
            var dm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset").FirstOrDefault();
            if (dm != null) EditorUtility.SetDirty(dm);
            Log($"Physics: layer collision matrix reset to all-collide ({fixedPairs} pairs re-enabled)");
        }

        /// <summary>Drops feature entries whose script no longer exists (they NRE the OpenXR validator on every load).</summary>
        static void RemoveMissingFeatures(OpenXRSettings settings)
        {
            var so = new SerializedObject(settings);
            var arr = so.FindProperty("features");
            if (arr == null || !arr.isArray) return;
            int removed = 0;
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue is UnityEngine.XR.OpenXR.Features.OpenXRFeature f && f != null) continue;
                arr.DeleteArrayElementAtIndex(i);
                removed++;
            }
            if (removed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                Log($"OpenXR {settings.name}: removed {removed} missing feature entr{(removed == 1 ? "y" : "ies")}");
            }
        }

        /// <summary>Meta Project Setup Tool: fix every auto-fixable task for Android, blocking.</summary>
        static void RunMetaProjectSetupFixAll()
        {
            try
            {
                var t = typeof(OVRProjectSetup);
                var fix = t.GetMethod("FixTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var logEnum = t.GetNestedType("LogMessages", BindingFlags.NonPublic | BindingFlags.Public);
                if (fix == null || logEnum == null)
                {
                    Log("OVRProjectSetup.FixTasks not found — skipped (API changed)");
                    return;
                }
                var prm = fix.GetParameters();
                var args = new object[prm.Length];
                for (int i = 0; i < prm.Length; i++)
                {
                    switch (prm[i].Name)
                    {
                        case "buildTargetGroup": args[i] = BuildTargetGroup.Android; break;
                        case "logMessages": args[i] = Enum.Parse(logEnum, "Changed"); break;
                        case "blocking": args[i] = true; break;
                        case "filter": args[i] = MakeTaskFilter(prm[i].ParameterType); break;
                        default: args[i] = prm[i].HasDefaultValue ? prm[i].DefaultValue : null; break;
                    }
                }
                for (int pass = 0; pass < 2; pass++) fix.Invoke(null, args);
                Log("Meta Project Setup Tool: FixTasks(Android, blocking) x2 executed — see [OVRProjectSetup] lines in the log");
            }
            catch (Exception e)
            {
                Log("Meta Project Setup fix-all failed (continuing): " + (e.InnerException ?? e).Message);
            }

        }

        /// <summary>
        /// Builds Func&lt;IEnumerable&lt;OVRConfigurationTask&gt;, List&lt;OVRConfigurationTask&gt;&gt; (internal type) that skips
        /// tasks we must not auto-fix overnight: the Meta XR Simulator installer (downloads an MSI on a slow link).
        /// </summary>
        static object MakeTaskFilter(Type funcType)
        {
            try
            {
                var taskType = funcType.GetGenericArguments()[0].GetGenericArguments()[0];
                var m = typeof(ProjectSetup).GetMethod(nameof(FilterTasks), BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(taskType);
                return Delegate.CreateDelegate(funcType, m);
            }
            catch (Exception e)
            {
                Log("UPST task filter unavailable: " + e.Message);
                return null;
            }
        }

        static System.Collections.Generic.List<T> FilterTasks<T>(System.Collections.Generic.IEnumerable<T> tasks)
        {
            var msg = typeof(T).GetMethod("GetLogMessage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var keep = new System.Collections.Generic.List<T>();
            foreach (var t in tasks)
            {
                string text = "";
                try { text = msg?.Invoke(t, new object[] { BuildTargetGroup.Android }) as string ?? ""; } catch { }
                if (text.IndexOf("Simulator", StringComparison.OrdinalIgnoreCase) >= 0) { Log("UPST skip: " + text); continue; }
                keep.Add(t);
            }
            return keep;
        }

        static void LogFinalState()
        {
            var a = NamedBuildTarget.Android;
            Log($"Final: target={EditorUserBuildSettings.activeBuildTarget} id={PlayerSettings.GetApplicationIdentifier(a)} " +
                $"backend={PlayerSettings.GetScriptingBackend(a)} arch={PlayerSettings.Android.targetArchitectures} " +
                $"minSdk={PlayerSettings.Android.minSdkVersion} targetSdk={(int)PlayerSettings.Android.targetSdkVersion} " +
                $"color={PlayerSettings.colorSpace} gfx={string.Join("/", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))} " +
                $"product='{PlayerSettings.productName}' company='{PlayerSettings.companyName}'");
        }

        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning("[FMVR] URP field missing: " + name); }
        static void SetFloat(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Debug.LogWarning("[FMVR] URP field missing: " + name); }
        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("[FMVR] URP field missing: " + name); }
    }
}
