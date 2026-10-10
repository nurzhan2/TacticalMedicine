using UnityEngine;

namespace FieldMedic.Comfort
{
    /// <summary>
    /// Quest 3S runtime performance profile, bootstrapped automatically (no scene object needed):
    /// fixed foveated rendering High + dynamic, 72 Hz, sustained-high CPU/GPU levels, OVRManager dynamic
    /// resolution 0.7..1.2. Only touches the headset (Android player); in the editor it only logs the profile.
    /// </summary>
    public sealed class QuestPerformance : MonoBehaviour
    {
        public const float RefreshRate = 72f;
        public const float MinResolutionScale = 0.7f;
        public const float MaxResolutionScale = 1.2f;
        public const OVRManager.FoveatedRenderingLevel Foveation = OVRManager.FoveatedRenderingLevel.High;

        public static bool Applied { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<QuestPerformance>() != null) return;
            var go = new GameObject("~QuestPerformance");
            DontDestroyOnLoad(go);
            go.AddComponent<QuestPerformance>();
        }

        System.Collections.IEnumerator Start()
        {
            yield return null; // OVRManager.Awake + XR display are up after the first frame
#if UNITY_ANDROID && !UNITY_EDITOR
            Apply();
#else
            Debug.Log($"[FMVR] QuestPerformance (device only): FFR {Foveation} dynamic, {RefreshRate} Hz, dynres {MinResolutionScale}..{MaxResolutionScale}");
#endif
            // Scene reloads (every scenario run) re-create OVRManager → re-apply on each new instance.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => { if (this != null) StartCoroutine(ApplyNextFrame()); };
        }

        System.Collections.IEnumerator ApplyNextFrame()
        {
            yield return null;
#if UNITY_ANDROID && !UNITY_EDITOR
            Apply();
#endif
        }

        static void Apply()
        {
            try
            {
                OVRManager.foveatedRenderingLevel = Foveation;
                OVRManager.useDynamicFoveatedRendering = true;
                OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                if (OVRManager.display != null) OVRManager.display.displayFrequency = RefreshRate;
                var mgr = OVRManager.instance;
                if (mgr != null)
                {
                    mgr.quest3MinDynamicResolutionScale = mgr.minDynamicResolutionScale = MinResolutionScale;
                    mgr.quest3MaxDynamicResolutionScale = mgr.maxDynamicResolutionScale = MaxResolutionScale;
                    mgr.quest2MinDynamicResolutionScale = MinResolutionScale;
                    mgr.quest2MaxDynamicResolutionScale = MaxResolutionScale;
                    mgr.enableDynamicResolution = true;
                }
                Applied = true;
                Debug.Log($"[FMVR] QuestPerformance applied: FFR {OVRManager.foveatedRenderingLevel}, {RefreshRate} Hz, dynres {(mgr != null)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FMVR] QuestPerformance failed: " + e.Message);
            }
        }
    }
}
