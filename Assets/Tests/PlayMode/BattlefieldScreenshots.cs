using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Environment;
using FieldMedic.Util;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Renders docs/screens/v2_w2_*.jpg from the real Battlefield scene in play mode (runtime lighting, smoke
    /// simulated, flag waving) from the scene's camera bookmarks. Run explicitly (GPU needed, no -nographics):
    /// -testFilter FieldMedic.Tests.BattlefieldScreenshots
    /// </summary>
    [Category("Screenshots")]
    public class BattlefieldScreenshots
    {
        const string Dir = "docs/screens";

        [UnityTest]
        public IEnumerator Capture_Battlefield_Bookmarks()
        {
            // render with the Quest quality level (Mobile URP: forward, 1 hard cascade, no SSAO) — the editor's
            // default "PC" level uses Forward+/SSAO/soft cascades whose first offscreen frame is garbage
            // (Mobile asset + HDR for the capture only: its LDR path shows colour speckle noise on the Iris Xe /
            // D3D11 editor GPU; the Quest renders natively — see PROGRESS w2 "verify on device")
            int quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            bool hdr = rp != null && rp.supportsHDR;
            if (rp != null) rp.supportsHDR = true;
            try { yield return CaptureAll(); }
            finally
            {
                if (rp != null) rp.supportsHDR = hdr;
                QualitySettings.SetQualityLevel(quality, true);
            }
        }

        IEnumerator CaptureAll()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            AudioListener.volume = 0f;
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.autoAmbience = false;
            var erlan = GameObject.Find("Erlan");
            if (erlan != null) erlan.SetActive(false); // w4 places him; w2 shows the terrain
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;

            var marks = Object.FindObjectsByType<ScreenshotBookmark>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b.name.StartsWith("v2_w2_")).OrderBy(b => b.name).ToArray();
            Assert.IsNotEmpty(marks, "no v2_w2 bookmarks");
            foreach (var b in marks)
            {
                Shot(b.name, b.transform, b.fieldOfView);
                yield return null;
            }
        }

        public static void Shot(string name, Transform pose, float fov, int w = 1600, int h = 900)
        {
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.stereoTargetEye = StereoTargetEyeMask.None; }
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 450f;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(85));
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }
    }
}
