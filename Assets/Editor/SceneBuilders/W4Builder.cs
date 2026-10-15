using System.IO;
using System.Linq;
using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w4 (modes): the Cyrillic + Kazakh SDF font for the hint card (Liberation Sans from TMP Essentials, static atlas),
    /// the hint table asset (Resources/HintTable, shared by the training card and the debrief), the TACEVAC helicopter +
    /// litter in the Battlefield scene, and the top-down mini map for the debrief (Resources/UI/battlefield_map.jpg).
    /// Batch: -executeMethod FieldMedic.EditorTools.SceneBuilders.W4Builder.BuildBatchW4
    /// </summary>
    public static class W4Builder
    {
        public const string FontPath = "Assets/Resources/Fonts/BauyrSans SDF.asset";
        public const string HintTablePath = "Assets/Resources/HintTable.asset";
        public const string MapPath = "Assets/Resources/UI/battlefield_map.jpg";

        public static void BuildBatchW4()
        {
            bool ok = false;
            try
            {
                EnsureFont();
                BuildHintTable();
                SetupNewModels();
                BattlefieldBuilder.Build();
                BootSceneBuilder.Build();
                CaptureMinimap();
                ok = true;
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] w4 build failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Only the assets (font, hint table, mini map) without rebuilding the scene.</summary>
        public static void AssetsBatch()
        {
            bool ok = false;
            try { EnsureFont(); BuildHintTable(); CaptureMinimap(); ok = true; }
            catch (System.Exception e) { Debug.LogError("[FMVR] w4 assets failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Materials + prefabs for the w4 BlenderKit downloads (bombed apartment block, ruined farmhouse).</summary>
        public static void SetupNewModels()
        {
            foreach (var slug in new[] { "bombed_apartment", "ruined_farmhouse" })
            {
                string fbx = $"Assets/_Licensed/BlenderKit/ruins/{slug}/{slug}.fbx";
                if (!File.Exists(fbx)) { Debug.LogWarning("[FMVR] w4: missing " + fbx + " (python tools/bk_fetch.py get, see tools/bk_assets.json)"); continue; }
                if (File.Exists(Path.ChangeExtension(fbx, ".prefab"))) continue;
                var pf = LicensedModelSetup.SetupModel(fbx);
                Debug.Log("[FMVR] w4 model setup " + slug + ": " + (pf != null ? "ok" : "FAILED"));
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ font
        static string Charset()
        {
            var sb = new System.Text.StringBuilder();
            for (int c = 32; c < 127; c++) sb.Append((char)c);
            for (int c = 0x400; c <= 0x4FF; c++) sb.Append((char)c);    // Cyrillic incl. Kazakh (Ә Ғ Қ Ң Ө Ұ Ү Һ І)
            sb.Append("«»—–№…•·°±×→←↑↓−‹›„“”‘’");
            return sb.ToString();
        }

        [MenuItem("FieldMedic/W4/Build hint font")]
        public static void EnsureFont()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) != null) return;
            var ttf = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (ttf == null) throw new System.Exception("TMP Essentials missing: Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            Directory.CreateDirectory(Path.GetDirectoryName(FontPath));
            var fa = TMP_FontAsset.CreateFontAsset(ttf, 56, 6, GlyphRenderMode.SDFAA, 2048, 1024, AtlasPopulationMode.Dynamic, false);
            fa.name = "BauyrSans SDF";
            AssetDatabase.CreateAsset(fa, FontPath);
            fa.atlasTextures[0].name = "BauyrSans Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "BauyrSans Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.TryAddCharacters(Charset(), out string missing);
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontPath);
            Debug.Log($"[FMVR] w4 font {FontPath}: {fa.characterTable.Count} glyphs, missing [{missing}]");
        }

        // ------------------------------------------------------------------ hint table
        [MenuItem("FieldMedic/W4/Build hint table")]
        public static void BuildHintTable()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HintTablePath));
            var t = AssetDatabase.LoadAssetAtPath<HintTable>(HintTablePath);
            if (t == null) { t = ScriptableObject.CreateInstance<HintTable>(); AssetDatabase.CreateAsset(t, HintTablePath); }
            t.version = HintTable.CurrentVersion;
            t.lessons = TrainingCatalog.Defaults();
            t.wrongs = TrainingCatalog.DefaultWrongs();
            EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FMVR] w4 hint table: {t.lessons.Count} lessons, {t.lessons.Sum(l => l.steps.Count)} steps, {t.wrongs.Count} reasons");
        }

        // ------------------------------------------------------------------ TACEVAC
        /// <summary>Called by <see cref="BattlefieldBuilder.Build"/>: the evacuation helicopter (hidden until called) and
        /// the litter that goes down next to Erlan when it lands.</summary>
        public static void Build(Transform root)
        {
            var go = new GameObject("Tacevac");
            go.transform.SetParent(root, false);
            var tac = go.AddComponent<TacevacController>();
            var heli = BattleAssets.SplitVehicle("aircraft", "utility_helicopter", "EvacHeli", 0, 0.74f, new Color(0.42f, 0.47f, 0.36f), "mast");
            if (heli != null && heli.prefab != null)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(heli.prefab, go.transform);
                h.name = "EvacHeli";
                tac.heli = h.transform;
                tac.rotor = h.transform.Find("Model/Part");
                foreach (var c in h.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var a = h.AddComponent<AudioSource>();
                var clips = AtmosphereBuilder.Clips("heli_flyby");
                a.clip = clips != null && clips.Length > 0 ? clips[0] : null;
                a.loop = true;
                a.playOnAwake = false;
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 20f;
                a.maxDistance = 900f;
                a.dopplerLevel = 0.6f;
                tac.rotorAudio = a;
                h.SetActive(false);
            }
            else Debug.LogWarning("[FMVR] w4: utility_helicopter prefab missing — TACEVAC without a visible helicopter");
            var stretcherPath = AssetGallery.AllPrefabs().FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "stretcher");
            if (stretcherPath != null)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(stretcherPath), go.transform);
                s.name = "EvacLitter";
                foreach (var c in s.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                tac.stretcher = s.transform;
                s.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ mini map
        [MenuItem("FieldMedic/W4/Capture mini map")]
        public static void CaptureMinimap()
        {
            EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            foreach (var c in Object.FindObjectsByType<FieldMedic.Casualty.CasualtyController>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var n in new[] { "SmokeColumns", "Battle", "Tacevac" }) { var g = GameObject.Find(n); if (g != null) g.SetActive(false); }
            Vector2 min = FieldMedic.UI.MenuBoard.MapMin, max = FieldMedic.UI.MenuBoard.MapMax;
            const int W = 600, H = 800;
            var go = new GameObject("~MapCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (max.y - min.y) * 0.5f;
            cam.aspect = (max.x - min.x) / (max.y - min.y);
            cam.nearClipPlane = 1f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.29f, 0.25f);
            go.transform.SetPositionAndRotation(new Vector3((min.x + max.x) * 0.5f, 120f, (min.y + max.y) * 0.5f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            RenderSettings.fog = fog;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            // map look: slightly desaturated, darker, with a faint 10 m grid
            var px = tex.GetPixels();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = px[y * W + x];
                    float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    c = Color.Lerp(c, new Color(l, l, l), 0.35f) * 0.85f;
                    float wx = min.x + (x + 0.5f) / W * (max.x - min.x), wz = min.y + (y + 0.5f) / H * (max.y - min.y);
                    bool grid = Mathf.Abs(wx - Mathf.Round(wx / 10f) * 10f) < 0.06f || Mathf.Abs(wz - Mathf.Round(wz / 10f) * 10f) < 0.06f;
                    if (grid) c = Color.Lerp(c, Color.white, 0.18f);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));
            File.WriteAllBytes(MapPath, tex.EncodeToJPG(88));
            Object.DestroyImmediate(tex);
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            AssetDatabase.ImportAsset(MapPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(MapPath);
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            Debug.Log("[FMVR] w4 mini map " + MapPath);
        }
    }
}
