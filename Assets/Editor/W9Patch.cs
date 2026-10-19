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
        public const float Elevation = 30f, Distance = 420f, AngularWidth = 42f;
        /// <summary>Where the player looks at the sky text from: rig spawn, the S1 start in the doorway of house A, the
        /// casualty area (standing + kneeling medic).</summary>
        public static Vector3[] Viewpoints(Transform rig) => new[]
        {
            SpawnEye(rig),
            BattlefieldLayout.DoorwayCentre + new Vector3(-0.8f, BattlefieldLayout.StandEyeHeight, 0f),
            BattlefieldLayout.ErlanFallPoint + Vector3.up * BattlefieldLayout.StandEyeHeight,
            BattlefieldLayout.ErlanFallPoint + new Vector3(0.6f, BattlefieldLayout.KneelEyeHeight, 0f),
        };

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
            Physics.SyncTransforms();
            ChooseDirection(rig.transform, out float yaw, out float elev, out string why);
            Debug.Log("[FMVR] w9 patch: sky text direction " + why);
            var sky = BuildSkyText(scene, rig.transform, yaw, elev);
            RemoveLitter();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FMVR] w9 patch: sky text at {sky.transform.position:F0}, elevation {SkyText.ElevationFrom(SpawnEye(rig.transform), sky.transform.position):F1} deg");
            bool split = SceneBuilders.SceneSplitter.Split();
            Debug.Log("[FMVR] w9 patch: re-split " + (split ? "OK" : "FAILED"));
            return split;
        }

        public static Vector3 SpawnEye(Transform rig) => rig.position + Vector3.up * BattlefieldLayout.StandEyeHeight;

        /// <summary>Fraction (0..1) of a grid over the text rectangle that `eye` sees without a collider in between.</summary>
        public static float Visibility(Vector3 eye, Pose sky, float width, float height)
        {
            int seen = 0, n = 0;
            Vector3 right = sky.rotation * Vector3.right, up = sky.rotation * Vector3.up;
            for (int i = 0; i < 9; i++)
            for (int j = 0; j < 3; j++)
            {
                Vector3 p = sky.position + right * width * (i / 8f - 0.5f) + up * height * (j / 2f - 0.5f);
                Vector3 d = p - eye;
                n++;
                if (!Physics.Raycast(eye, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore)) seen++;
            }
            return seen / (float)n;
        }

        /// <summary>Start view direction +-40 deg, 27-33 deg up: the pose every viewpoint sees best (worst viewpoint wins).</summary>
        public static void ChooseDirection(Transform rig, out float bestYaw, out float bestElev, out string why)
        {
            float startYaw = rig.eulerAngles.y;
            float width = 2f * Distance * Mathf.Tan(AngularWidth * 0.5f * Mathf.Deg2Rad), height = width * 0.36f;
            var eyes = Viewpoints(rig);
            bestYaw = startYaw; bestElev = Elevation;
            float best = -1f;
            string detail = "";
            for (float dy = -40f; dy <= 40.01f; dy += 5f)
            for (float el = 27f; el <= 33.01f; el += 3f)
            {
                var pose = SkyText.PlaceFrom(SpawnEye(rig), startYaw + dy, el, Distance);
                var vis = eyes.Select(e => Visibility(e, pose, width, height)).ToArray();
                // worst viewpoint first, then the average, then: closer to the start direction and to 30 deg
                float score = vis.Min() * 10f + vis.Average() - Mathf.Abs(dy) * 0.004f - Mathf.Abs(el - Elevation) * 0.003f;
                if (score > best)
                {
                    best = score; bestYaw = startYaw + dy; bestElev = el;
                    detail = string.Join(", ", vis.Select(v => (v * 100f).ToString("0") + "%"));
                }
            }
            why = $"yaw {bestYaw:F0} (start {startYaw:F0}), {bestElev:F0} deg up, visible from spawn/S1/casualty/kneeling: {detail}";
        }

        /// <summary>Glowing sky text facing the spawn, `elevation` above the horizon at compass `yaw`.</summary>
        public static GameObject BuildSkyText(Scene scene, Transform rig, float yaw, float elevation)
        {
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == SkyText.ObjectName).ToList()) Object.DestroyImmediate(old);
            var eye = SpawnEye(rig);
            var pose = SkyText.PlaceFrom(eye, yaw, elevation, Distance);
            var root = new GameObject(SkyText.ObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var go = new GameObject("Text");
            go.transform.SetParent(root.transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TMP reads along +Z: away from the viewer
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = FieldMedic.UI.UiMesh.Font;
            tmp.text = SkyText.DisplayText;
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.characterSpacing = 4f;
            tmp.lineSpacing = -8f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(200f, 20f);
            tmp.fontSharedMaterial = SkyMaterial(tmp.font);
            tmp.color = new Color(1f, 0.9f, 0.68f, 0.95f);
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
            // warm white-gold face, a soft gold glow around it and a faint warm-dark halo underneath so the letters
            // also read against the bright overcast sky (TMP shaders take no fog: the haze never eats them)
            m.SetColor("_FaceColor", new Color(1f, 0.93f, 0.76f, 1f));
            m.SetFloat("_FaceDilate", 0.22f);
            m.SetFloat("_OutlineSoftness", 0.08f);
            m.SetFloat("_OutlineWidth", 0f);
            m.EnableKeyword("GLOW_ON");
            m.SetColor("_GlowColor", new Color(1f, 0.72f, 0.28f, 0.8f));
            m.SetFloat("_GlowOffset", 0.12f);
            m.SetFloat("_GlowInner", 0.15f);
            m.SetFloat("_GlowOuter", 0.85f);
            m.SetFloat("_GlowPower", 0.6f);
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0.28f, 0.16f, 0.05f, 0.45f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", 0f);
            m.SetFloat("_UnderlayDilate", 0.6f);
            m.SetFloat("_UnderlaySoftness", 0.7f);
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
            // the side door / cabin floor / bench come from the fuselage mesh, measured here (not readable on the device)
            if (!tac.BakeCabin()) Debug.LogWarning("[FMVR] w9 patch: helicopter cabin could not be measured, runtime defaults");
            EditorUtility.SetDirty(tac);
        }

        // ------------------------------------------------------------------ screenshot
        public static void ApplyAndShotBatch()
        {
            bool ok = false;
            try { ok = Apply() && SkyShot(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] w9 patch: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

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
                if (File.Exists(paths[i]) && !paths[i].EndsWith("Main_Battle.unity")) // battle aircraft rest at the origin in edit mode
                    EditorSceneManager.OpenScene(paths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var sky = GameObject.Find(SkyText.ObjectName);
            if (rig == null || sky == null) { Debug.LogError("[FMVR] w9 sky shot: rig or sky text missing"); return false; }
            foreach (var t in sky.GetComponentsInChildren<TextMeshPro>()) t.ForceMeshUpdate();
            var cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault();
            var pose = new GameObject("~SkyShotPose").transform;
            try
            {
                Directory.CreateDirectory("docs/screens");
                // from the S1 start in the doorway of house A (where a run begins): toward the text, tilted up so the
                // horizon and the text both show; then from the rig spawn and the casualty area
                var eyes = Viewpoints(rig.transform);
                float Yaw(Vector3 e) { var d = sky.transform.position - e; d.y = 0f; return Quaternion.LookRotation(d).eulerAngles.y; }
                pose.SetPositionAndRotation(eyes[1], Quaternion.Euler(-19f, Yaw(eyes[1]), 0f));
                ScreenshotTool.JpgQuality = 0;
                ScreenshotTool.Render(pose, 80f, cam, 1600, 900, "docs/screens/w9_sky_text.png");
                ScreenshotTool.JpgQuality = 85;
                pose.SetPositionAndRotation(eyes[0], Quaternion.Euler(-19f, Yaw(eyes[0]), 0f));
                ScreenshotTool.Render(pose, 80f, cam, 1600, 900, "docs/screens/w9_sky_text_spawn.jpg");
                // from the casualty area (Erlan's fall point), same idea
                pose.SetPositionAndRotation(eyes[3], Quaternion.Euler(-19f, Yaw(eyes[3]), 0f));
                ScreenshotTool.Render(pose, 78f, cam, 1600, 900, "docs/screens/w9_sky_text_casualty.jpg");
            }
            finally { Object.DestroyImmediate(pose.gameObject); ScreenshotTool.JpgQuality = 0; }
            Debug.Log("[FMVR] w9 sky shot: docs/screens/w9_sky_text.png");
            return true;
        }
    }
}
