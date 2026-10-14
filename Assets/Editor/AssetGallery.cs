using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Visual check of the imported V2 models: renders every prefab under Assets/_Licensed, Assets/ThirdParty and
    /// Assets/Art/Meshy alone under an overcast sky (Poly Haven HDRI when present) into Temp/w1_gallery/&lt;slug&gt;.png
    /// and writes Logs/asset_report.tsv (category, slug, tris, materials, LODs, humanoid avatar ok, size).
    /// tools/gallery_sheet.py turns the PNGs into docs/screens/v2_w1_gallery_*.jpg.
    /// Batch: -executeMethod FieldMedic.EditorTools.AssetGallery.Capture (without -nographics).
    /// </summary>
    public static class AssetGallery
    {
        public static string OutDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "w1_gallery");
        static readonly string[] Roots = { LicensedModelSetup.LicensedRoot, LicensedModelSetup.ThirdPartyRoot, LicensedModelSetup.MeshyRoot };

        [MenuItem("Field Medic/Assets/Render Gallery")]
        public static void Capture()
        {
            LicensedModelSetup.SetupAll();
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();
            var report = new StringBuilder("category\tslug\ttris\tmaterials\tlods\thumanoid\tsize_m\n");
            int n = 0;
            foreach (var path in AllPrefabs())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                string cat = LicensedModelSetup.CategoryOf(path);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    go.transform.position = Vector3.zero;
                    var rs = go.GetComponentsInChildren<Renderer>();
                    var b = LicensedModelSetup.Bounds(go, rs);
                    // the lowest point on the ground plane
                    go.transform.position += Vector3.up * (-b.min.y);
                    b = LicensedModelSetup.Bounds(go, rs);
                    var ground = MakeGround(Mathf.Max(b.size.x, b.size.z) * 3f + 2f);
                    var mats = new HashSet<Material>();
                    foreach (var r in rs) foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                    var lod = go.GetComponentInChildren<LODGroup>();
                    string human = "-";
                    var anim = go.GetComponentInChildren<Animator>();
                    if (cat == "soldiers") human = anim != null && anim.avatar != null && anim.avatar.isValid && anim.avatar.isHuman ? "yes" : "NO";
                    report.Append($"{cat}\t{prefab.name}\t{LicensedModelSetup.CountTris(go)}\t{mats.Count}\t{(lod != null ? lod.lodCount : 1)}\t{human}\t" +
                                  $"{b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2}\n");
                    Shoot(b, Path.Combine(OutDir, $"{cat}__{prefab.name}.png"), -38f);
                    // walls and scans can be one-sided: a second shot from behind shows which side is the face
                    if (cat == "ruins" || cat == "cover") Shoot(b, Path.Combine(OutDir, $"{cat}__{prefab.name}~back.png"), 142f);
                    Object.DestroyImmediate(ground);
                    n++;
                }
                finally { Object.DestroyImmediate(go); }
            }
            Lineup();
            var logs = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "asset_report.tsv"), report.ToString());
            Debug.Log($"[FMVR] gallery: {n} prefabs rendered -> {OutDir}");
        }

        public static IEnumerable<string> AllPrefabs()
        {
            foreach (var root in Roots)
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { root }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x)))
                    yield return AssetDatabase.GUIDToAssetPath(g);
            }
        }

        static Material _groundMat;

        static GameObject MakeGround(float size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "~Ground";
            g.transform.localScale = Vector3.one * (size / 10f);
            if (_groundMat == null)
            {
                _groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/PolyHaven/Textures/dry_ground_01/dry_ground_01_diff.jpg");
                if (tex != null) { _groundMat.SetTexture("_BaseMap", tex); _groundMat.SetTextureScale("_BaseMap", Vector2.one * 4f); }
                else _groundMat.SetColor("_BaseColor", new Color(0.42f, 0.38f, 0.3f));
                _groundMat.SetFloat("_Smoothness", 0.1f);
            }
            g.GetComponent<Renderer>().sharedMaterial = _groundMat;
            _groundMat.SetTextureScale("_BaseMap", Vector2.one * Mathf.Max(1f, size / 4f));
            return g;
        }

        static void SetupLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            var hdr = AssetDatabase.FindAssets("t:Texture", new[] { "Assets/ThirdParty/PolyHaven/HDRI" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith(".hdr"));
            var tex = hdr != null ? AssetDatabase.LoadAssetAtPath<Texture>(hdr) : null;
            if (tex != null)
            {
                var sky = new Material(Shader.Find("Skybox/Panoramic"));
                sky.SetTexture("_MainTex", tex);
                sky.SetFloat("_Exposure", 1.0f);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1.1f;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.7f);
                RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.48f);
                RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.24f);
            }
            RenderSettings.fog = false;
        }

        static void Shoot(Bounds b, string path, float yaw, int w = 640, int h = 480)
        {
            float r = Mathf.Max(0.05f, b.extents.magnitude);
            var pose = new GameObject("~pose").transform;
            var dir = Quaternion.Euler(18f, yaw, 0f) * Vector3.back;
            const float fov = 38f;
            float dist = r / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 0.95f;
            pose.position = b.center + dir * dist;
            pose.rotation = Quaternion.LookRotation(b.center - pose.position);
            var tmpl = new GameObject("~tmpl").AddComponent<Camera>();
            tmpl.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            tmpl.backgroundColor = new Color(0.55f, 0.58f, 0.6f);
            tmpl.nearClipPlane = Mathf.Max(0.005f, dist * 0.02f);
            tmpl.farClipPlane = dist * 10f + 100f;
            ScreenshotTool.Render(pose, fov, tmpl, w, h, path);
            Object.DestroyImmediate(tmpl.gameObject);
            Object.DestroyImmediate(pose.gameObject);
        }

        /// <summary>One composed shot: ruins, cover and a vehicle together at real scale.</summary>
        static void Lineup()
        {
            var placed = new List<GameObject>();
            void Put(string slugContains, Vector3 pos, float yaw)
            {
                var p = AllPrefabs().FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == slugContains);
                if (p == null) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p));
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                placed.Add(go);
            }
            var ground = MakeGround(120f);
            placed.Add(ground);
            Put("unfinished_building", new Vector3(-6f, 0f, 14f), 10f);
            Put("arx_apc", new Vector3(9f, 0f, 10f), -30f);
            Put("czech_hedgehog", new Vector3(2f, 0f, 3f), 20f);
            Put("czech_hedgehog", new Vector3(5f, 0f, 4.5f), 65f);
            Put("concrete_road_barrier", new Vector3(-2.5f, 0f, 3f), 5f);
            Put("burlap_sacks", new Vector3(-1f, 0f, 2.2f), 80f);
            Put("burlap_sacks", new Vector3(-0.3f, 0f, 2.3f), 95f);
            Put("bricks_debris_pile", new Vector3(-9f, 0f, 6f), 0f);
            Put("barbed_wire_metal_fence", new Vector3(4f, 0f, 16f), 0f);
            Put("kz_flag_stand", new Vector3(-3.5f, 0f, 7f), 160f);
            Put("car_wreck_beetle", new Vector3(13f, 0f, 20f), 120f);
            Put("broken_wall", new Vector3(-4f, 0f, 9f), 0f);
            var pose = new GameObject("~pose").transform;
            pose.position = new Vector3(0f, 1.6f, -4f);
            pose.rotation = Quaternion.LookRotation(new Vector3(0f, 2.5f, 10f) - pose.position);
            var tmpl = new GameObject("~tmpl").AddComponent<Camera>();
            tmpl.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            tmpl.farClipPlane = 1000f;
            ScreenshotTool.Render(pose, 70f, tmpl, 1600, 900, Path.Combine(OutDir, "lineup.png"));
            Object.DestroyImmediate(tmpl.gameObject);
            Object.DestroyImmediate(pose.gameObject);
            foreach (var g in placed) Object.DestroyImmediate(g);
        }
    }
}
