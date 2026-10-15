using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w4 speckle hunt (supervisor 15.10 07:00 #1): renders the sandbag/assault view under render-setting variants and
    /// scores the speckle numerically (share of pixels that differ from their 3×3 median by a lot), so the global cause
    /// can be bisected without eyeballing. -executeMethod FieldMedic.EditorTools.SceneBuilders.W4Diag.Speckle
    /// </summary>
    public static class W4Diag
    {
        const int W = 1280, H = 720;

        public static float Score(Color32[] px, int w, int h)
        {
            int bad = 0, n = 0;
            var lum = new float[px.Length];
            for (int i = 0; i < px.Length; i++) lum[i] = 0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b;
            var win = new float[9];
            for (int y = 1; y < h - 1; y += 1)
                for (int x = 1; x < w - 1; x += 1)
                {
                    int k = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) win[k++] = lum[(y + dy) * w + x + dx];
                    Array.Sort(win);
                    float c = lum[y * w + x];
                    // isolated outlier: far from the median AND far from the 2nd closest neighbours
                    if (Mathf.Abs(c - win[4]) > 45f) bad++;
                    n++;
                }
            return 1000f * bad / Mathf.Max(1, n);
        }

        static Color32[] Render(Vector3 from, Vector3 look, float fov, bool msaa = true, bool hdr = false)
        {
            var go = new GameObject("~DiagCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 450f;
            cam.fieldOfView = fov; cam.aspect = (float)W / H;
            cam.allowHDR = hdr; cam.allowMSAA = msaa;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = msaa ? 4 : 1 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, req);
            RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return px;
        }

        static readonly List<string> Report = new();

        static void Variant(string name, Vector3 from, Vector3 look, float fov, bool msaa = true, bool hdr = false, bool save = false)
        {
            var px = Render(from, look, fov, msaa, hdr);
            float s = Score(px, W, H);
            var px2 = Render(from, look, fov, msaa, hdr);
            int diff = 0;
            for (int i = 0; i < px.Length; i++) if (Mathf.Abs(px[i].r - px2[i].r) + Mathf.Abs(px[i].g - px2[i].g) + Mathf.Abs(px[i].b - px2[i].b) > 30) diff++;
            string line = $"{name,-28} speckle {s,7:F2}‰  nondeterministic {1000f * diff / px.Length,7:F2}‰";
            Report.Add(line);
            Debug.Log("[FMVR] diag " + line);
            if (save)
            {
                var t = new Texture2D(W, H, TextureFormat.RGB24, false);
                t.SetPixels32(px); t.Apply();
                File.WriteAllBytes($"Logs/w4_diag_{name}.jpg", t.EncodeToJPG(90));
                Object.DestroyImmediate(t);
            }
        }

        public static void Speckle()
        {
            try { SpeckleInner(); }
            catch (Exception e) { Debug.LogError("[FMVR] diag failed: " + e); }
            File.WriteAllLines("Logs/w4_diag.txt", Report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void SpeckleInner()
        {
            EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            Vector3 from = new Vector3(-2.4f, 1.85f, 5.2f), look = new Vector3(2f, 1.2f, 50f);
            Vector3 oFrom = new Vector3(-24f, 16f, -22f), oLook = new Vector3(4f, 0f, 48f);
            Report.Add("GPU " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + " astc6x6=" + SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6));

            QualitySettings.SetQualityLevel(0, true);
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Report.Add("pipeline " + (rp != null ? rp.name : "null"));
            Variant("mobile_base", from, look, 70f, save: true);
            Variant("mobile_overview", oFrom, oLook, 62f, save: true);
            Variant("mobile_nomsaa", from, look, 70f, msaa: false);
            bool hdr = rp.supportsHDR;
            rp.supportsHDR = true;
            Variant("mobile_hdr", from, look, 70f, hdr: true, save: true);
            rp.supportsHDR = hdr;

            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            // shadows
            var sun = RenderSettings.sun;
            if (sun != null) { var sh = sun.shadows; sun.shadows = LightShadows.None; Variant("mobile_noshadow", from, look, 70f); sun.shadows = sh; }
            // LOD cross-fade off
            var so = new SerializedObject(rp); var cfp = so.FindProperty("m_EnableLODCrossFade");
            if (cfp != null) { bool cf = cfp.boolValue; cfp.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); Variant("mobile_nocrossfade", from, look, 70f); cfp.boolValue = cf; so.ApplyModifiedPropertiesWithoutUndo(); }
            // material instances: no normal maps / no specular / simple lit
            var mats = all.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            Report.Add("materials " + mats.Length + " shaders " + string.Join(",", mats.Select(m => m.shader.name).Distinct()));
            var backups = mats.ToDictionary(m => m, m => new Material(m));
            void Restore() { foreach (var kv in backups) kv.Key.CopyPropertiesFromMaterial(kv.Value); foreach (var kv in backups) { kv.Key.shader = kv.Value.shader; kv.Key.shaderKeywords = kv.Value.shaderKeywords; } }
            foreach (var m in mats) foreach (var p in new[] { "_BumpMap", "_GrassNormal", "_DirtNormal", "_NormalMap" }) if (m.HasProperty(p)) m.SetTexture(p, null);
            foreach (var m in mats) m.DisableKeyword("_NORMALMAP");
            Variant("no_normalmaps", from, look, 70f, save: true);
            Restore();
            foreach (var m in mats) { if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f); if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
            Variant("no_specular", from, look, 70f, save: true);
            Restore();
            // all albedo textures → white (texture sampling vs lighting)
            foreach (var m in mats) foreach (var p in new[] { "_BaseMap", "_MainTex", "_GrassMap", "_DirtMap", "_BurnMap" }) if (m.HasProperty(p)) m.SetTexture(p, Texture2D.whiteTexture);
            Variant("white_albedo", from, look, 70f, save: true);
            Restore();
            // fog off
            bool fog = RenderSettings.fog; RenderSettings.fog = false; Variant("no_fog", from, look, 70f); RenderSettings.fog = fog;
            // ambient flat
            var amb = RenderSettings.ambientMode; RenderSettings.ambientMode = AmbientMode.Flat; Variant("flat_ambient", from, look, 70f); RenderSettings.ambientMode = amb;
            // PC pipeline
            QualitySettings.SetQualityLevel(1, true);
            Variant("pc_base", from, look, 70f, save: true);
            QualitySettings.SetQualityLevel(0, true);
        }
    }
}
