using System;
using System.Reflection;
using FieldMedic.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w0b: collects the shader variants the battlefield really renders (Main_Core + additive scenes, rendered from the
    /// player's typical viewpoints through the URP pipeline) into Assets/Data/Shaders/Battlefield.shadervariants.
    /// Boot warms them up progressively while Main_Core loads, so the first gameplay frames do not stall on shader /
    /// Vulkan pipeline creation. Uses the editor's variant tracking (ShaderUtil, internal API via reflection).
    /// -executeMethod FieldMedic.EditorTools.SceneBuilders.ShaderWarmupBuilder.CollectBatch
    /// </summary>
    public static class ShaderWarmupBuilder
    {
        public const string SvcPath = "Assets/Data/Shaders/Battlefield.shadervariants";

        public static void CollectBatch()
        {
            int n = Collect();
            if (Application.isBatchMode) EditorApplication.Exit(n > 0 ? 0 : 1);
        }

        static object CallShaderUtil(string name, params object[] args)
        {
            var m = typeof(ShaderUtil).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException("ShaderUtil." + name);
            return m.Invoke(null, args);
        }

        [MenuItem("FieldMedic/Build/Collect Shader Variants (w0b)")]
        public static int Collect()
        {
            System.IO.Directory.CreateDirectory("Assets/Data/Shaders");
            EditorSceneManager.OpenScene(SceneSplitter.CorePath, OpenSceneMode.Single);
            foreach (var p in SceneSplitter.AdditivePaths)
                if (System.IO.File.Exists(p)) EditorSceneManager.OpenScene(p, OpenSceneMode.Additive);
            CallShaderUtil("ClearCurrentShaderVariantCollection");

            var go = new GameObject("~WarmupCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f;
            cam.allowMSAA = true; cam.allowHDR = false;
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            var fall = BattlefieldLayout.ErlanFallPoint;
            var views = new (Vector3 from, Vector3 to)[]
            {
                (new Vector3(-3.2f, 1.6f, -0.5f), new Vector3(0f, 1f, 40f)),
                (new Vector3(-3.2f, 1.6f, -0.5f), new Vector3(-30f, 1f, -30f)),
                (new Vector3(-3.2f, 1.6f, -0.5f), new Vector3(30f, 1f, -10f)),
                (fall + new Vector3(0.8f, 1.2f, -0.8f), fall),
                (new Vector3(-26f, 17f, -30f), new Vector3(0f, 0f, 6f)),
                (new Vector3(7.5f, 1.7f, 9.5f), new Vector3(-2.5f, 1.2f, 1.5f)),
                (new Vector3(0f, 1.6f, -30f), new Vector3(0f, 1.6f, 0f)),
                (new Vector3(-2f, 1.6f, 1f), new Vector3(-2f, 1.0f, 3f)),
            };
            foreach (var (from, to) in views)
            {
                go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.LookRotation(to - from);
                    RenderPipeline.SubmitRenderRequest(cam, req);
                }
            }
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);

            int count = Convert.ToInt32(CallShaderUtil("GetCurrentShaderVariantCollectionVariantCount"));
            CallShaderUtil("SaveCurrentShaderVariantCollection", SvcPath);
            AssetDatabase.ImportAsset(SvcPath);
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcPath);
            Debug.Log($"[FMVR] shader variants collected: tracked {count}, saved {(svc != null ? svc.variantCount : -1)} variants / {(svc != null ? svc.shaderCount : -1)} shaders → {SvcPath}");
            return svc != null ? svc.variantCount : 0;
        }
    }
}
