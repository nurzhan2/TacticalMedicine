using System.IO;
using System.Linq;
using FieldMedic.Environment;
using FieldMedic.Scenarios;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w9 owner-feedback patch of the authored battlefield (no full rebuild): the sky text «САТПАЕВ ЛУЧШЕ ВСЕХ», the
    /// TACEVAC without the litter cart, the hint table with the new evacuation steps; then the scene is split again
    /// into the shipped Main_* scenes. Batch: -executeMethod FieldMedic.EditorTools.W9Patch.ApplyBatch
    /// Screenshot of the sky text from the spawn: -executeMethod FieldMedic.EditorTools.W9Patch.SkyShotBatch
    /// </summary>
    public static class W9Patch
    {
        public const string SkyMaterialPath = "Assets/Art/Sky/SkyText_Satpaev.mat";
        public const float Elevation = 30f, Distance = 420f, AngularWidth = 58f;

        public static void ApplyBatch()
        {
            bool ok = false;
            try { ok = Apply(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] w9 patch: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/w9/Apply scene patch (sky text, TACEVAC, hints)")]
        public static bool Apply()
        {
            SceneBuilders.W4Builder.BuildHintTable();
            var scene = EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) { Debug.LogError("[FMVR] w9 patch: no OVRCameraRig in the battlefield"); return false; }
            var sky = BuildSkyText(scene, rig.transform);
            RemoveLitter();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FMVR] w9 patch: sky text at {sky.transform.position:F0}, elevation {SkyText.ElevationFrom(SpawnEye(rig.transform), sky.transform.position):F1} deg");
            bool split = SceneBuilders.SceneSplitter.Split();
            Debug.Log("[FMVR] w9 patch: re-split " + (split ? "OK" : "FAILED"));
            return split;
        }

        public static Vector3 SpawnEye(Transform rig) => rig.position + Vector3.up * BattlefieldLayout.StandEyeHeight;

        /// <summary>Glowing sky text facing the spawn, `Elevation` above the horizon in the start view direction.</summary>
        public static GameObject BuildSkyText(Scene scene, Transform rig)
        {
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == SkyText.ObjectName).ToList()) Object.DestroyImmediate(old);
            var eye = SpawnEye(rig);
            var pose = SkyText.PlaceFrom(eye, rig.eulerAngles.y, Elevation, Distance);
            var root = new GameObject(SkyText.ObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var go = new GameObject("Text");
            go.transform.SetParent(root.transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TMP reads along +Z: away from the viewer
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = FieldMedic.UI.UiMesh.Font;
            tmp.text = SkyText.Phrase;
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.characterSpacing = 6f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(200f, 20f);
            tmp.fontSharedMaterial = SkyMaterial(tmp.font);
            tmp.color = new Color(1f, 0.93f, 0.78f, 0.82f);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            tmp.ForceMeshUpdate();
            // scale so the line spans `AngularWidth` degrees from the spawn
            float want = 2f * Distance * Mathf.Tan(AngularWidth * 0.5f * Mathf.Deg2Rad);
            float have = Mathf.Max(0.01f, tmp.textBounds.size.x);
            go.transform.localScale = Vector3.one * (want / have);
            var st = root.AddComponent<SkyText>();
            st.text = tmp;
            return root;
        }

        /// <summary>TMP Distance Field (Always Included) with a soft gold glow; a scene-independent asset.</summary>
        static Material SkyMaterial(TMP_FontAsset font)
        {
            var dir = Path.GetDirectoryName(SkyMaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) { Directory.CreateDirectory(dir); AssetDatabase.Refresh(); }
            var m = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            bool create = m == null;
            if (create) m = new Material(font.material);
            m.shader = Shader.Find("TextMeshPro/Distance Field");
            m.SetTexture("_MainTex", font.material.GetTexture("_MainTex"));
            foreach (var p in new[] { "_TextureWidth", "_TextureHeight", "_GradientScale", "_WeightNormal", "_WeightBold" })
                if (font.material.HasProperty(p)) m.SetFloat(p, font.material.GetFloat(p));
            m.SetColor("_FaceColor", new Color(1f, 0.95f, 0.85f, 1f));
            m.SetFloat("_FaceDilate", 0.08f);
            m.SetFloat("_OutlineSoftness", 0.12f);
            m.SetFloat("_OutlineWidth", 0f);
            m.EnableKeyword("GLOW_ON");
            m.SetColor("_GlowColor", new Color(1f, 0.78f, 0.38f, 0.55f));
            m.SetFloat("_GlowOffset", 0.05f);
            m.SetFloat("_GlowInner", 0.1f);
            m.SetFloat("_GlowOuter", 0.75f);
            m.SetFloat("_GlowPower", 0.55f);
            m.DisableKeyword("UNDERLAY_ON");
            m.renderQueue = 3000;
            m.name = "SkyText_Satpaev";
            if (create) AssetDatabase.CreateAsset(m, SkyMaterialPath);
            else EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static void RemoveLitter()
        {
            var tac = Object.FindAnyObjectByType<TacevacController>(FindObjectsInactive.Include);
            if (tac == null) return;
            if (tac.stretcher != null) Object.DestroyImmediate(tac.stretcher.gameObject);
            tac.stretcher = null;
            EditorUtility.SetDirty(tac);
        }

        // ------------------------------------------------------------------ screenshot
        public static void SkyShotBatch()
        {
            bool ok = false;
            try { ok = SkyShot(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] w9 sky shot: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/w9/Screenshot sky text")]
        public static bool SkyShot()
        {
            var paths = SceneBuilders.SceneSplitter.AllPaths;
            for (int i = 0; i < paths.Length; i++)
                if (File.Exists(paths[i])) EditorSceneManager.OpenScene(paths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var sky = GameObject.Find(SkyText.ObjectName);
            if (rig == null || sky == null) { Debug.LogError("[FMVR] w9 sky shot: rig or sky text missing"); return false; }
            foreach (var t in sky.GetComponentsInChildren<TextMeshPro>()) t.ForceMeshUpdate();
            var cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault();
            var pose = new GameObject("~SkyShotPose").transform;
            try
            {
                Directory.CreateDirectory("docs/screens");
                // from the spawn: looking where the player looks at the start, tilted up so horizon and text both show
                pose.SetPositionAndRotation(SpawnEye(rig.transform), Quaternion.Euler(-17f, rig.transform.eulerAngles.y, 0f));
                ScreenshotTool.JpgQuality = 0;
                ScreenshotTool.Render(pose, 78f, cam, 1600, 900, "docs/screens/w9_sky_text.png");
                // from the casualty area (Erlan's fall point), same idea
                var fall = BattlefieldLayout.ErlanFallPoint + Vector3.up * 1.5f;
                Vector3 look = sky.transform.position - fall; look.y = 0f;
                pose.SetPositionAndRotation(fall, Quaternion.Euler(-17f, Quaternion.LookRotation(look).eulerAngles.y, 0f));
                ScreenshotTool.JpgQuality = 85;
                ScreenshotTool.Render(pose, 78f, cam, 1600, 900, "docs/screens/w9_sky_text_casualty.jpg");
            }
            finally { Object.DestroyImmediate(pose.gameObject); ScreenshotTool.JpgQuality = 0; }
            Debug.Log("[FMVR] w9 sky shot: docs/screens/w9_sky_text.png");
            return true;
        }
    }
}
