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
    /// Renders Logs/pm_shots/v2_w2_*.jpg from the real Battlefield scene in play mode (runtime lighting, smoke
    /// simulated, flag waving) from the scene's camera bookmarks. The published docs/screens shots come from the
    /// edit-mode capture (BattlefieldBuilder.BuildAndShoot): on the dev laptop's Iris Xe the play-mode path shows
    /// colour speckle on the licensed URP/Lit materials (editor GPU artifact, verify on device). Run explicitly (GPU needed, no -nographics):
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
                Shot(b.name, b.transform, b.fieldOfView, 1600, 900, "Logs/pm_shots", true); // docs/screens come from the edit-mode capture
                yield return null;
            }
        }

        /// <summary>Profiler capture (editor play mode, static batching applied): render counters from the player's
        /// spots with the Quest pipeline asset → Logs/w2_render_stats.txt (+ appended to docs/PERF_V2.md).</summary>
        [UnityTest]
        public IEnumerator Profile_Render_Stats_From_Player_Spots()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            int quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true); // Mobile URP asset = what the Quest runs
            AudioListener.volume = 0f;
            var cam = Camera.main;
            Assert.IsNotNull(cam);
            var keepPos = cam.transform.localPosition; var keepRot = cam.transform.localRotation; float keepFov = cam.fieldOfView;
            var rt = new RenderTexture(1440, 1440, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.fieldOfView = 100f;
            var names = new[] { "Triangles Count", "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Shadow Casters Count" };
            var recs = names.Select(n => Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, n)).ToArray();
            var spots = new (string, Vector3)[]
            {
                ("player start (house A)", BattlefieldLayout.PlayerStart + Vector3.up * 1.6f),
                ("behind the sandbags (kneeling)", new Vector3(-3.2f, 1.1f, 5.8f)),
                ("Erlan in the open", BattlefieldLayout.ErlanFallPoint + Vector3.up * 1.6f),
                ("LZ", BattlefieldLayout.LandingZone + Vector3.up * 1.6f),
            };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("| Spot | worst heading | triangles | draw calls | batches | SetPass | shadow casters |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            var rig = cam.transform.root;
            foreach (var (name, eye) in spots)
            {
                long[] worst = new long[names.Length]; float worstYaw = 0f;
                for (int h = 0; h < 8; h++)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, h * 45f, 0f));
                    for (int f = 0; f < 4; f++) yield return null;
                    long tris = recs[0].LastValue;
                    if (tris > worst[0]) { for (int k = 0; k < names.Length; k++) worst[k] = recs[k].LastValue; worstYaw = h * 45f; }
                }
                sb.AppendLine($"| {name} | {worstYaw:F0}° | {worst[0]:N0} | {worst[1]} | {worst[2]} | {worst[3]} | {worst[4]} |");
                // breakdown of visible renderers by top-level group (diagnostics for the draw-call budget)
                cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, worstYaw, 0f));
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var groups = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(r => r.enabled && r.isVisible | true && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds))
                    .GroupBy(r => GroupName(r.transform)).Select(g => $"{g.Key}:{g.Count()}").OrderByDescending(x => int.Parse(x.Split(':')[1])).Take(14);
                Debug.Log($"[FMVR] render groups at {name}: " + string.Join(", ", groups));
            }
            foreach (var r in recs) r.Dispose();
            cam.targetTexture = null;
            cam.transform.localPosition = keepPos; cam.transform.localRotation = keepRot; cam.fieldOfView = keepFov;
            rt.Release();
            QualitySettings.SetQualityLevel(quality, true);
            string table = sb.ToString();
            Debug.Log("[FMVR] render stats\n" + table);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/w2_render_stats.txt", table);
            if (File.Exists("docs/PERF_V2.md"))
            {
                var doc = File.ReadAllText("docs/PERF_V2.md");
                int cut = doc.IndexOf("## Profiler capture", System.StringComparison.Ordinal);
                if (cut >= 0) doc = doc.Substring(0, cut);
                doc += "## Profiler capture (editor play mode, Mobile URP asset, 100° FOV, 1440² eye target, static batching on)\n" +
                       "Render counters via `ProfilerRecorder` (`BattlefieldScreenshots.Profile_Render_Stats_From_Player_Spots`), worst of 8 headings per spot. " +
                       "Editor counts are for one eye; the Quest renders both eyes with multiview in the same draw calls.\n\n" + table;
                File.WriteAllText("docs/PERF_V2.md", doc);
            }
            Assert.Greater(table.Length, 100);
        }

        static string GroupName(Transform t)
        {
            // Battlefield/<Group>/<object>... or a root object
            var chain = new System.Collections.Generic.List<Transform>();
            for (var x = t; x != null; x = x.parent) chain.Insert(0, x);
            if (chain[0].name == "Battlefield" && chain.Count > 2) return chain[1].name + "/" + chain[2].name.Split(' ')[0];
            return chain[0].name;
        }

        public static void Shot(string name, Transform pose, float fov, int w = 1600, int h = 900) => Shot(name, pose, fov, w, h, Dir, true);

        public static void Shot(string name, Transform pose, float fov, int w, int h, string dir, bool hdr)
        {
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.stereoTargetEye = StereoTargetEyeMask.None; }
            cam.fieldOfView = fov;
            cam.allowHDR = hdr; // the eye camera copied above has HDR off (Quest); captures need the HDR path, see PROGRESS
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 450f;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); }
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(85));
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }
    }
}
