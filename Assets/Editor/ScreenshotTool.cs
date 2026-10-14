using System;
using System.IO;
using System.Linq;
using FieldMedic.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Renders every <see cref="ScreenshotBookmark"/> of a scene to docs/screens/&lt;name&gt;.png (1600x900).
    /// Batch (WITHOUT -nographics): -executeMethod FieldMedic.EditorTools.ScreenshotTool.Capture
    ///   optional args: -fmScene Assets/Scenes/X.unity   -fmShots name1,name2   -fmWidth 1600 -fmHeight 900
    /// </summary>
    public static class ScreenshotTool
    {
        public const string BookmarkRoot = "_CameraBookmarks";
        public const string OutDir = "docs/screens";

        [MenuItem("FieldMedic/Screenshots/Capture Main Scene")]
        public static void Capture()
        {
            string scenePath = Arg("-fmScene") ?? "Assets/Scenes/Main.unity";
            string shots = Arg("-fmShots");
            int w = int.TryParse(Arg("-fmWidth"), out var pw) ? pw : 1600;
            int h = int.TryParse(Arg("-fmHeight"), out var ph) ? ph : 900;
            JpgQuality = int.TryParse(Arg("-fmJpg"), out var q) ? q : 0;
            try
            {
                int n = CaptureScene(scenePath, shots?.Split(','), w, h);
                Debug.Log($"[FMVR] ScreenshotTool: {n} screenshot(s) from {scenePath}");
                if (Application.isBatchMode) EditorApplication.Exit(n == 0 ? 2 : 0);
            }
            catch (Exception e)
            {
                Debug.LogError("[FMVR] ScreenshotTool failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>&gt; 0 → screenshots are written as JPG with this quality (V2: keep pushes small).</summary>
        public static int JpgQuality;
        /// <summary>Renders per shot whose per-pixel median is saved (1 = single render).</summary>
        public static int MedianOf = 1;

        public static int CaptureScene(string scenePath, string[] only, int width, int height, Action prepare = null)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            prepare?.Invoke();
            DynamicGI.UpdateEnvironment();
            Directory.CreateDirectory(OutDir);

            var marks = Object.FindObjectsByType<ScreenshotBookmark>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => only == null || only.Contains(b.name)).OrderBy(b => b.name).ToArray();

            // scene cameras (the rig eye) must not render on top of the capture camera
            var sceneCams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var template = sceneCams.FirstOrDefault(c => c.CompareTag("MainCamera"));
            foreach (var c in sceneCams) c.enabled = false;

            int count = 0;
            foreach (var b in marks)
            {
                var path = Path.Combine(OutDir, b.name + (JpgQuality > 0 ? ".jpg" : ".png"));
                Render(b.transform, b.fieldOfView, template, width, height, path);
                Debug.Log("[FMVR] screenshot " + path);
                count++;
            }
            return count;
        }

        public static void Render(Transform pose, float fov, Camera template, int width, int height, string path)
        {
            var go = new GameObject("~ScreenshotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            if (template != null)
            {
                cam.clearFlags = template.clearFlags;
                cam.backgroundColor = template.backgroundColor;
                cam.nearClipPlane = template.nearClipPlane;
                cam.farClipPlane = template.farClipPlane;
            }
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = fov;
            cam.aspect = (float)width / height;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { antiAliasing = 4 };
            rt.Create();
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                bool srp = RenderPipeline.SupportsRenderRequest(cam, req);
                // first frame of a new camera can read uninitialised shadow/SSAO buffers → warm-up render; then
                // MedianOf renders, per-pixel median (removes the random speckle the Iris Xe editor GPU adds, w2)
                if (srp) RenderPipeline.SubmitRenderRequest(cam, req);
                int n = Mathf.Max(1, MedianOf);
                var frames = new Color32[n][];
                Texture2D tex = null;
                for (int f = 0; f < n; f++)
                {
                    if (srp) RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    if (tex == null) tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    frames[f] = tex.GetPixels32();
                }
                if (n > 1)
                {
                    var outPx = new Color32[frames[0].Length];
                    var r = new byte[n]; var g = new byte[n]; var b = new byte[n];
                    for (int i = 0; i < outPx.Length; i++)
                    {
                        for (int f = 0; f < n; f++) { r[f] = frames[f][i].r; g[f] = frames[f][i].g; b[f] = frames[f][i].b; }
                        System.Array.Sort(r); System.Array.Sort(g); System.Array.Sort(b);
                        outPx[i] = new Color32(r[n / 2], g[n / 2], b[n / 2], 255);
                    }
                    tex.SetPixels32(outPx);
                    tex.Apply();
                }
                File.WriteAllBytes(path, path.EndsWith(".jpg") ? tex.EncodeToJPG(JpgQuality > 0 ? JpgQuality : 85) : tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
        }

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
