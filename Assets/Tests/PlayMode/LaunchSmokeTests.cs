using System.Collections;
using System.Linq;
using FieldMedic.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w0 launch fix: starts the real Main scene on the headset code path (XRActive forced true, like the Quest
    /// player) and fails on any exception / error log during the first seconds (the test framework fails on
    /// unhandled error logs), on hands left in desktop mode, or on the desktop simulator moving the tracking space.
    /// </summary>
    public class LaunchSmokeTests
    {
        [UnityTest]
        public IEnumerator MainScene_Starts_On_Headset_Path_Without_Errors()
        {
            DesktopSimulator.XRActiveOverride = true;
            try
            {
                float t0 = Time.realtimeSinceStartup;
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Main.unity",
                    new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
                yield return null;
                float load = Time.realtimeSinceStartup - t0;
                AudioListener.volume = 0f;
                Debug.Log($"[FMVR] launch smoke: Main loaded in {load:F1} s (editor)");

                for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime) yield return null;

                var rigs = Object.FindObjectsByType<HandRig>(FindObjectsSortMode.None);
                Assert.IsNotEmpty(rigs, "no HandRig in Main");
                foreach (var r in rigs.Where(r => r.mode == HandRig.Mode.Auto && r.ovrAnchor != null))
                    Assert.AreEqual(HandRig.Mode.OVR, r.ActiveMode, r.name + " must use the headset input");

                var ts = GameObject.Find("TrackingSpace");
                Assert.IsNotNull(ts, "OVRCameraRig/TrackingSpace missing");
                Assert.AreEqual(0f, ts.transform.localPosition.y, 1e-3f, "tracking space must stay at floor level on the headset");
                Assert.Less(load, 20f, "Main scene load too slow");
            }
            finally
            {
                DesktopSimulator.XRActiveOverride = null;
            }
        }
    }
}
