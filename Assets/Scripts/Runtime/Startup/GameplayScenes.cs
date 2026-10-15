using System.Collections;
using System.Collections.Generic;
using FieldMedic.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.Startup
{
    /// <summary>
    /// Lives in Main_Core (w0b). The player already sees the world (terrain, Erlan, the menu) while the heavy parts —
    /// cover/ruins, scenery, the battle — stream in one additive scene after another with LoadSceneAsync, so no single
    /// frame has to integrate the whole battlefield (a multi-second stall = black screen on the Quest). Measures the
    /// load time and the longest frame of each additive activation (tests + fmvr_log). SAFE mode
    /// (<see cref="LaunchGuard"/>) keeps Main_Core only and shows a small white note.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GameplayScenes : MonoBehaviour
    {
        public const string CoreScene = "Main_Core";
        public static readonly string[] DefaultAdditive = { "Main_Cover", "Main_Scenery", "Main_Battle" };

        [Tooltip("Loaded additively in this order after the core is visible.")]
        public string[] additive = DefaultAdditive;
        [Tooltip("Frames rendered between two additive loads (lets async uploads finish).")]
        public int framesBetween = 3;

        public struct Timing { public string scene; public float loadSeconds; public float maxFrameMs; public float activationMs; }

        public static bool Loading { get; private set; }
        public static bool Ready { get; private set; }
        public static readonly List<Timing> Timings = new List<Timing>();
        static GameplayScenes _instance;

        void Awake()
        {
            _instance = this;
            Loading = true;
            Ready = false;
            LaunchGuard.Mark(LaunchGuard.Stage.Core);
            // big async-upload budget while streaming, small time slice so frames keep coming
            QualitySettings.asyncUploadBufferSize = Mathf.Max(QualitySettings.asyncUploadBufferSize, 64);
            QualitySettings.asyncUploadTimeSlice = 3;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            // occlusion was baked with all battlefield scenes together: while some are missing (streaming, SAFE mode)
            // their occluders would still hide core objects → off until everything is in
            SetOcclusion(false);
        }

        static void SetOcclusion(bool on)
        {
            foreach (var c in Camera.allCameras) c.useOcclusionCulling = on;
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            Loading = false;
        }

        IEnumerator Start()
        {
            Timings.Clear();
            // two frames of the bare core first: the player sees the world before anything heavy starts
            yield return null;
            yield return null;
            if (LaunchGuard.SafeMode)
            {
                Debug.LogWarning("[FMVR] SAFE mode: Main_Core only (" + LaunchGuard.SafeReason + ")");
                ShowSafeNote();
            }
            else
            {
                foreach (var name in additive)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (SceneManager.GetSceneByName(name).isLoaded) continue;
                    if (!Application.CanStreamedLevelBeLoaded(name)) { Debug.Log($"[FMVR] additive scene {name} not in this build, skipped"); continue; }
                    yield return LoadOne(name);
                    for (int i = 0; i < framesBetween; i++) yield return null;
                }
            }
            Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
            Loading = false;
            Ready = true;
            if (!LaunchGuard.SafeMode) SetOcclusion(true);
            // a few seconds of real gameplay frames before the crash flag is cleared
            yield return new WaitForSecondsRealtime(3f);
            LaunchGuard.Mark(LaunchGuard.Stage.Ready);
            Debug.Log("[FMVR] gameplay ready (" + (LaunchGuard.SafeMode ? "SAFE" : "FULL") + ")");
        }

        IEnumerator LoadOne(string name)
        {
            float t0 = Time.realtimeSinceStartup;
            float maxDt = 0f;
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive); }
            catch (System.Exception e) { Debug.LogError($"[FMVR] additive {name} failed to start: {e.Message}"); }
            if (op == null) yield break;
            op.allowSceneActivation = false;
            while (op.progress < 0.9f)
            {
                yield return null;
                maxDt = Mathf.Max(maxDt, Time.unscaledDeltaTime);
            }
            // activation (Awake/OnEnable of every object + integration) happens on the main thread: measure it alone
            float ta = Time.realtimeSinceStartup;
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            float act = (Time.realtimeSinceStartup - ta) * 1000f;
            yield return null; // Start() of the new objects
            float startFrame = Time.unscaledDeltaTime;
            maxDt = Mathf.Max(maxDt, startFrame);
            var t = new Timing { scene = name, loadSeconds = Time.realtimeSinceStartup - t0, maxFrameMs = maxDt * 1000f, activationMs = act };
            Timings.Add(t);
            Debug.Log($"[FMVR] additive {name}: loaded in {t.loadSeconds:F2} s, activation {t.activationMs:F0} ms, longest frame {t.maxFrameMs:F0} ms");
        }

        void ShowSafeNote()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var note = WorldUI.Text(cam.transform, "SafeModeNote",
                "БЕЗОПАСНЫЙ РЕЖИМ / ҚАУІПСІЗ РЕЖИМ\nпрошлый запуск не завершился — бой отключён\nSAFE MODE: battle scenes off",
                new Vector3(0f, -0.32f, 1.6f), 0.028f, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center);
            Destroy(note.gameObject, 25f);
        }

        /// <summary>Restart / launch a scenario: async reload of the core (the old frame keeps rendering until the new
        /// core activates), which brings the additive scenes back in. Replaces the old blocking LoadScene.</summary>
        public static AsyncOperation Reload()
        {
            var active = SceneManager.GetActiveScene();
            Ready = false;
            if (active.buildIndex >= 0) return SceneManager.LoadSceneAsync(active.buildIndex, LoadSceneMode.Single);
            return SceneManager.LoadSceneAsync(active.name, LoadSceneMode.Single);
        }
    }
}
