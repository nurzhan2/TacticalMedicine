using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Input;
using FieldMedic.Startup;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w0b: the APK's real start path on the headset code path — Boot → Main_Core → Main_Cover → Main_Scenery →
    /// Main_Battle (GameplayScenes) — without error logs, with every cross-scene link resolved, exactly one display
    /// camera, the rig on the ground; logs Boot first frame, core activation, each additive activation and the
    /// longest frame (logs/w0b_timings.txt, docs/BLACKSCREEN_FIX.md).
    /// </summary>
    public class SceneStreamingTests
    {
        [UnityTest]
        public IEnumerator Boot_Core_Then_All_Additive_Scenes_Stream_Without_Errors()
        {
            DesktopSimulator.XRActiveOverride = true;
            LaunchGuard.SetForTests(false);
            var errors = new List<string>();
            void OnLog(string m, string st, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }
            Application.logMessageReceived += OnLog;
            var sb = new StringBuilder();
            float maxDt = 0f, maxDtAfterCore = 0f;
            try
            {
                float t0 = Time.realtimeSinceStartup;
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Boot.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                float bootFirst = Time.realtimeSinceStartup - t0;
                AudioListener.volume = 0f;
                Assert.IsNotNull(Object.FindAnyObjectByType<BootLoader>());

                float coreAt = -1f;
                float t = 0f;
                while (!GameplayScenes.Ready && t < 180f)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    t += dt;
                    maxDt = Mathf.Max(maxDt, dt);
                    if (coreAt < 0f && SceneManager.GetActiveScene().name == GameplayScenes.CoreScene) coreAt = Time.realtimeSinceStartup - t0;
                    else if (coreAt >= 0f) maxDtAfterCore = Mathf.Max(maxDtAfterCore, dt);
                }
                Assert.IsTrue(GameplayScenes.Ready, "additive scenes did not finish loading in 180 s");
                for (int i = 0; i < 30; i++) { yield return null; maxDtAfterCore = Mathf.Max(maxDtAfterCore, Time.unscaledDeltaTime); }

                foreach (var name in GameplayScenes.DefaultAdditive)
                    Assert.IsTrue(SceneManager.GetSceneByName(name).isLoaded, name + " not loaded");
                Assert.AreEqual(GameplayScenes.CoreScene, SceneManager.GetActiveScene().name, "core stays the active scene (lighting/fog)");
                var display = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled && c.targetTexture == null).ToList();
                Assert.AreEqual(1, display.Count, "display cameras: " + string.Join(", ", display.Select(c => c.name)));
                Assert.AreEqual("CenterEyeAnchor", display[0].name);
                Assert.IsTrue(display[0].useOcclusionCulling, "occlusion back on after streaming");
                foreach (var l in Object.FindObjectsByType<SceneLinker>(FindObjectsSortMode.None))
                    Assert.AreEqual(0, l.Failed, l.gameObject.scene.name + " has unresolved cross-scene links");
                var battle = Object.FindAnyObjectByType<FieldMedic.Battle.BattleDirector>();
                Assert.IsNotNull(battle, "battle director streamed in");
                Assert.IsNotNull(battle.head, "battle director got the player's head through the linker");

#if UNITY_EDITOR
                // w0b: the variants the battlefield really used (incl. battle FX) → Boot's warm-up collection
                for (int i = 0; i < 240; i++) yield return null;
                SaveTrackedVariants(sb);
#endif
                sb.AppendLine($"Boot first frame (editor, scene load + 1 frame): {bootFirst * 1000f:F0} ms");
                sb.AppendLine($"Main_Core active after: {coreAt:F2} s; BootLoader async load {BootLoader.LastLoadSeconds:F2} s, shader warm-up {BootLoader.LastWarmupSeconds:F2} s");
                foreach (var tm in GameplayScenes.Timings)
                    sb.AppendLine($"{tm.scene}: load {tm.loadSeconds:F2} s, activation {tm.activationMs:F0} ms, longest frame {tm.maxFrameMs:F0} ms");
                sb.AppendLine($"longest frame overall {maxDt * 1000f:F0} ms, after the core was visible {maxDtAfterCore * 1000f:F0} ms");
                sb.AppendLine($"error logs: {errors.Count}");
                Directory.CreateDirectory("logs");
                File.WriteAllText("logs/w0b_timings.txt", sb.ToString());
                Debug.Log("[FMVR] w0b streaming timings:\n" + sb);
                Assert.IsEmpty(errors, "error logs during start: " + string.Join(" | ", errors.Take(5)));
                Assert.AreEqual(3, GameplayScenes.Timings.Count);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                DesktopSimulator.XRActiveOverride = null;
            }
        }

#if UNITY_EDITOR
        static void SaveTrackedVariants(StringBuilder sb)
        {
            const string path = "Assets/Data/Shaders/Battlefield.shadervariants";
            var su = typeof(UnityEditor.ShaderUtil);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var count = su.GetMethod("GetCurrentShaderVariantCollectionVariantCount", flags);
            var save = su.GetMethod("SaveCurrentShaderVariantCollection", flags);
            if (count == null || save == null) return;
            int tracked = System.Convert.ToInt32(count.Invoke(null, null));
            var old = UnityEditor.AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
            int had = old != null ? old.variantCount : 0;
            if (tracked > had)
            {
                save.Invoke(null, new object[] { path });
                UnityEditor.AssetDatabase.ImportAsset(path);
            }
            sb.AppendLine($"shader variants tracked in play mode: {tracked} (collection had {had})");
        }
#endif

        [UnityTest]
        public IEnumerator Safe_Mode_Loads_The_Core_Only()
        {
            DesktopSimulator.XRActiveOverride = true;
            LaunchGuard.SetForTests(true, "test");
            try
            {
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Main_Core.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
#endif
                AudioListener.volume = 0f;
                float t = 0f;
                while (!GameplayScenes.Ready && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }
                Assert.IsTrue(GameplayScenes.Ready);
                foreach (var name in GameplayScenes.DefaultAdditive)
                    Assert.IsFalse(SceneManager.GetSceneByName(name).isLoaded, name + " must not load in SAFE mode");
                Assert.IsNotNull(GameObject.Find("SafeModeNote"), "SAFE note in front of the player");
                Assert.IsNotNull(Object.FindAnyObjectByType<FieldMedic.Casualty.CasualtyController>(), "Erlan is in the core");
            }
            finally
            {
                LaunchGuard.SetForTests(false);
                DesktopSimulator.XRActiveOverride = null;
            }
        }
    }
}
