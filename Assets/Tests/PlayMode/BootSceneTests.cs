using System.Collections;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>w0 launch fix: the APK's first scene shows the title at once and hands over to the Battlefield (V2 main flow) without errors.</summary>
    public class BootSceneTests
    {
        [UnityTest]
        public IEnumerator Boot_Shows_Title_Then_Loads_Battlefield()
        {
            DesktopSimulator.XRActiveOverride = true; // headset code path
            try
            {
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Boot.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                AudioListener.volume = 0f;
                var boot = Object.FindAnyObjectByType<BootLoader>();
                Assert.IsNotNull(boot, "BootLoader missing in Boot.unity");
                Assert.IsNotNull(boot.status, "status text");
                Assert.IsNotNull(boot.head, "head (CenterEyeAnchor)");
                Assert.IsNotNull(GameObject.Find("Title"), "title text");
                Assert.AreEqual(BattlefieldLayout.SceneName, boot.nextScene);

                float t = 0f;
                while (SceneManager.GetActiveScene().name != BattlefieldLayout.SceneName && t < 120f) { t += Time.unscaledDeltaTime; yield return null; }
                Assert.AreEqual(BattlefieldLayout.SceneName, SceneManager.GetActiveScene().name, "the Battlefield was not loaded by the boot scene");
                yield return null;
                yield return null;
                Assert.IsNull(Object.FindAnyObjectByType<BootLoader>(), "boot scene must be unloaded");
                Assert.Greater(BootLoader.LastLoadSeconds, 0f);
                Debug.Log($"[FMVR] boot test: Battlefield active after {t:F1} s, async load {BootLoader.LastLoadSeconds:F1} s (editor)");
            }
            finally
            {
                DesktopSimulator.XRActiveOverride = null;
            }
        }
    }
}
