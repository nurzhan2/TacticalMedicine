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

        public static int CaptureScene(string scenePath, string[] only, int width, int height)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
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
                var path = Path.Combine(OutDir, b.name + ".png");
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
                if (RenderPipeline.SupportsRenderRequest(cam, req))
                {
                    RenderPipeline.SubmitRenderRequest(cam, req);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
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
