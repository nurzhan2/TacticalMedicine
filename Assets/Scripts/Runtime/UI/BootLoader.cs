using System.Collections;
using FieldMedic.Startup;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.UI
{
    /// <summary>
    /// First scene of the APK (Assets/Scenes/Boot.unity, a few objects, &lt; 5 MB of dependencies): shows the title + a
    /// loading line in VR right away and keeps rendering every frame while it (1) warms the shader variants of the
    /// battlefield progressively (no first-frame shader/pipeline compile stall later) and (2) loads Main_Core with
    /// LoadSceneAsync in parallel. The heavy battle scenes are streamed in later by <see cref="GameplayScenes"/>.
    /// The panel lazily follows the head so it is in view however the player stands.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        public string nextScene = GameplayScenes.CoreScene;
        public Transform head;
        public Transform panel;
        public TextMesh status;
        public float distance = 2f;
        [Tooltip("Shader variants of Main_Core + battle, collected in the editor (Assets/Data/Shaders/Battlefield.shadervariants).")]
        public ShaderVariantCollection warmup;
        public int variantsPerFrame = 6;

        /// <summary>Seconds the last async load of <see cref="nextScene"/> took (tests / logs).</summary>
        public static float LastLoadSeconds { get; private set; } = -1f;
        public static float LastWarmupSeconds { get; private set; } = -1f;

        IEnumerator Start()
        {
            // a couple of frames so the compositor gets the title before the loading starts
            yield return null;
            yield return null;
            Debug.Log($"[FMVR] boot: title visible at {Time.realtimeSinceStartup:F2} s, mode {(LaunchGuard.SafeMode ? "SAFE" : "FULL")}");
            Application.backgroundLoadingPriority = ThreadPriority.Low; // keep the boot frames smooth
            float t0 = Time.realtimeSinceStartup;
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Single); }
            catch (System.Exception e) { Debug.LogError("[FMVR] boot: " + e.Message); }
            if (op == null)
            {
                Debug.LogError("[FMVR] boot: cannot load scene " + nextScene);
                if (status != null) status.text = "Ошибка загрузки / Жүктеу қатесі\nLOAD ERROR " + nextScene;
                yield break;
            }
            op.allowSceneActivation = false;
            string scene = nextScene;
            op.completed += _ =>
            {
                LastLoadSeconds = Time.realtimeSinceStartup - t0;
                Debug.Log($"[FMVR] boot: {scene} loaded in {LastLoadSeconds:F1} s");
            };

            // shader warm-up runs on the main thread a few variants per frame while the scene loads in the background
            float tw = Time.realtimeSinceStartup;
            bool warmDone = warmup == null || warmup.variantCount == 0;
            int total = warmup != null ? warmup.variantCount : 0;
            int warmed = 0;
            while (!warmDone || op.progress < 0.9f)
            {
                if (!warmDone)
                {
                    try { warmDone = warmup.WarmUpProgressively(variantsPerFrame); }
                    catch (System.Exception e) { Debug.LogWarning("[FMVR] boot: shader warm-up failed: " + e.Message); warmDone = true; }
                    warmed = Mathf.Min(total, warmed + variantsPerFrame);
                    if (warmDone)
                    {
                        LastWarmupSeconds = Time.realtimeSinceStartup - tw;
                        Debug.Log($"[FMVR] boot: {total} shader variants warmed in {LastWarmupSeconds:F1} s");
                    }
                }
                float load = Mathf.Clamp01(op.progress / 0.9f);
                float warm = total > 0 ? (float)warmed / total : 1f;
                SetStatus(Mathf.RoundToInt((load * 0.6f + warm * 0.4f) * 100f));
                yield return null;
            }
            SetStatus(100);
            yield return null;
            op.allowSceneActivation = true;
        }

        void SetStatus(int pct)
        {
            if (status != null) status.text = $"Загрузка / Жүктелуде… {pct}%\nLoading {pct}%";
        }

        void LateUpdate()
        {
            if (head == null || panel == null) return;
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 target = head.position + fwd * distance;
            float k = Time.frameCount < 3 ? 1f : 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            panel.position = Vector3.Lerp(panel.position, target, k);
            Vector3 look = panel.position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
    }
}
