using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.UI
{
    /// <summary>
    /// First scene of the APK (Assets/Scenes/Boot.unity, a few objects): shows the title + a loading line in VR right
    /// after the Unity splash and loads Main asynchronously, so the headset never sits on a black frame while the big
    /// battlefield scene loads (V1 went straight into Main, which took many seconds → "the app did not open").
    /// The panel lazily follows the head so it is in view however the player stands.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        public string nextScene = "Main";
        public Transform head;
        public Transform panel;
        public TextMesh status;
        public float distance = 2.2f;

        /// <summary>Seconds the last async load of <see cref="nextScene"/> took (tests / logs).</summary>
        public static float LastLoadSeconds { get; private set; } = -1f;

        IEnumerator Start()
        {
            // a couple of frames so the compositor gets the title before the loading starts
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            var op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError("[FMVR] boot: cannot load scene " + nextScene);
                if (status != null) status.text = "Ошибка загрузки / Жүктеу қатесі";
                yield break;
            }
            string scene = nextScene;
            op.completed += _ =>
            {
                LastLoadSeconds = Time.realtimeSinceStartup - t0;
                Debug.Log($"[FMVR] boot: {scene} loaded in {LastLoadSeconds:F1} s");
            };
            while (!op.isDone)
            {
                if (status != null)
                    status.text = $"Загрузка / Жүктелуде… {Mathf.RoundToInt(Mathf.Clamp01(op.progress / 0.9f) * 100f)}%";
                yield return null;
            }
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
