using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using FieldMedic.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Generates Assets/Scenes/Battlefield.unity — the V2 open steppe (GDD_V2 §1): overcast day (Poly Haven HDRI
    /// with a haze band, one soft directional light, exponential fog), 400×400 m ground, smoke columns on the
    /// horizon, and our position in the centre: two procedurally ruined brick houses + a gutted concrete frame +
    /// a shell-scarred house, a burnt APC, a sandbagged barrier line, czech hedgehogs, barbed wire, craters,
    /// debris and wrecks — all real models from w1 (BlenderKit / Poly Haven). Every cover piece: collider on
    /// layer + tag Cover, NavMeshObstacle, and a <see cref="CoverPoint"/> behind it (verified by PlayMode tests).
    /// Player: OVRCameraRig + hands + IFAK + V1 scenario system, smooth/snap/teleport locomotion.
    /// Batch: -executeMethod FieldMedic.EditorTools.SceneBuilders.BattlefieldBuilder.BuildBatch
    /// </summary>
    public static class BattlefieldBuilder
    {
        public const string ArtDir = "Assets/Art/Battlefield";
        public const string MeshDir = ArtDir + "/Meshes";
        public const string MatDir = ArtDir + "/Materials";
        /// <summary>Tinted/burnt copies of licensed materials (git-ignored with the models).</summary>
        public const string OverrideDir = "Assets/_Licensed/_Overrides";
        const string PH = "Assets/ThirdParty/PolyHaven/Textures";

        static readonly Dictionary<string, string> PrefabPaths = new Dictionary<string, string>();
        static readonly Dictionary<string, Material> OverrideCache = new Dictionary<string, Material>();
        static readonly List<GameObject> SolidCover = new List<GameObject>();
        static readonly List<string> Missing = new List<string>();
        static readonly List<Bounds> Footprints = new List<Bounds>();
        static Transform _root;

        /// <summary>Build + Boot + JPG screenshots of every bookmark in one editor session (with graphics).</summary>
        public static void BuildAndShoot()
        {
            bool ok = false;
            try
            {
                Build();
                BootSceneBuilder.Build();
                ScreenshotTool.JpgQuality = 85;
                ScreenshotTool.MedianOf = 5;
                // edit-mode capture with the editor's PC pipeline (HDR): on this laptop's Iris Xe (D3D11, no ASTC)
                // the LDR/Mobile and play-mode paths show colour speckle on the licensed materials — see PROGRESS w2
                int q = QualitySettings.GetQualityLevel();
                QualitySettings.SetQualityLevel(1, true); // "PC" (editor default) — see the note above
                int n = ScreenshotTool.CaptureScene(BattlefieldLayout.ScenePath, null, 1600, 900, PrepareShots);
                QualitySettings.SetQualityLevel(q, true);
                Debug.Log($"[FMVR] battlefield screenshots: {n}");
                PerfReportV2.Report();

                ok = n > 0;
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] Battlefield build/shoot failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Edit-mode captures: particles don't run, so pre-simulate the smoke; Erlan is not posed outside
        /// play mode (T-pose), so hide him (w4 shows him in the scenario shots).</summary>
        public static void PrepareShots()
        {
            foreach (var c in Object.FindObjectsByType<FieldMedic.Casualty.CasualtyController>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() != null) continue;
                bool smoke = ps.name.StartsWith("SmokeColumn") || ps.name == "Smoulder" || ps.name == "SignalSmoke";
                if (smoke) ps.Simulate(ps.main.startLifetime.constantMax * 0.9f, true, true, true);
            }
        }

        /// <summary>Debug aid: renders two bookmarks with shadow variants (soft / hard / none / sun off).</summary>
        public static void Diagnose()
        {
            var sun = RenderSettings.sun;
            foreach (var name in new[] { "v2_w2_flag", "v2_w2_cover_view" })
            {
                var b = GameObject.Find(name);
                if (b == null || sun == null) continue;
                var keep = sun.shadows;
                sun.shadows = LightShadows.Hard;
                ScreenshotTool.Render(b.transform, 62f, null, 800, 450, $"Logs/diag_{name}_hard.jpg");
                sun.shadows = LightShadows.None;
                ScreenshotTool.Render(b.transform, 62f, null, 800, 450, $"Logs/diag_{name}_noshadow.jpg");
                sun.shadows = keep;
                float far = 0f;
                var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (rp != null) { far = rp.shadowDistance; Debug.Log($"[FMVR] diag rp={rp.name} shadowDist={rp.shadowDistance} cascades={rp.shadowCascadeCount} soft={rp.supportsSoftShadows}"); }
            }
        }

        public static void DiagnoseOnly()
        {
            ScreenshotTool.CaptureScene(BattlefieldLayout.ScenePath, new[] { "none" }, 16, 16, PrepareShots);
            var sun = RenderSettings.sun;
            var add = sun.GetComponent<UniversalAdditionalLightData>() ?? sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            var b = GameObject.Find("v2_w2_cover_view").transform;
            foreach (int q in new[] { 1, 0 })
            {
                QualitySettings.SetQualityLevel(q, true);
                var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                bool hdr = rp.supportsHDR;
                foreach (var mode in new[] { "none", "hard", "hardbias", "hdr_hard" })
                {
                    sun.shadows = mode == "none" ? LightShadows.None : LightShadows.Hard;
                    add.usePipelineSettings = mode != "hardbias";
                    if (mode == "hardbias") { sun.shadowBias = 3f; sun.shadowNormalBias = 3f; }
                    rp.supportsHDR = mode == "hdr_hard" || hdr;
                    ScreenshotTool.Render(b, 75f, null, 1600, 900, $"Logs/diag_full_q{q}_{mode}.jpg");
                    rp.supportsHDR = hdr;
                }
                add.usePipelineSettings = true;
            }
            QualitySettings.SetQualityLevel(1, true);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void BuildBatch()
        {
            bool ok = false;
            try
            {
                Build();
                BootSceneBuilder.Build(); // Boot now loads the Battlefield (V2 main flow)
                ok = true;
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] Battlefield build failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/Scenes/Build Battlefield (V2)")]
        public static void Build()
        {
            PrefabPaths.Clear(); OverrideCache.Clear(); _sandbag = null; SolidCover.Clear(); Missing.Clear(); Footprints.Clear();
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(OverrideDir);
            LicensedModelSetup.EnsureCoverTagAndLayer();
            foreach (var p in AssetGallery.AllPrefabs()) PrefabPaths[Path.GetFileNameWithoutExtension(p)] = p;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("Battlefield").transform;

            var sun = SetupSkyAndLight();
            var envRoot = new GameObject("Environment").transform; envRoot.SetParent(_root, false);
            BattlefieldTerrain.BuildGround(envRoot, GroundMaterial(), MeshDir);
            BuildRuins();
            BuildCoverLine();
            BuildObstaclesAndWrecks();
            BuildProps();
            BuildFarField();
            Physics.SyncTransforms();
            BattlefieldTerrain.ScatterGrass(envRoot, Prefab("grass_medium_01"), MeshDir, GrassBlocked);
            BuildSmoke();
            var flagTex = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_Licensed/BlenderKit/props/kz_flag_stand/Textures/standing_flag_mat_015_albedo.jpg");
            BattlefieldFx.Flag(_root, new Vector3(BattlefieldLayout.FlagPole.x, BattlefieldLayout.GroundHeight(BattlefieldLayout.FlagPole.x, BattlefieldLayout.FlagPole.z), BattlefieldLayout.FlagPole.z), MatDir, MeshDir, flagTex);
            BattlefieldFx.LandingZone(_root, BattlefieldLayout.LandingZone, MatDir, MeshDir,
                BattlefieldFx.PuffMat(ArtDir, "FX_SignalSmoke", Color.white, false));
            Physics.SyncTransforms();
            BuildCoverPoints();

            var rig = MainSceneBuilder.BuildRig(out var sim);
            rig.transform.SetPositionAndRotation(BattlefieldLayout.PlayerStart, Quaternion.Euler(0f, BattlefieldLayout.PlayerStartYaw, 0f));
            var eye = MainSceneBuilder.FindDeep(rig.transform, "CenterEyeAnchor").GetComponent<Camera>();
            eye.farClipPlane = 420f;
            eye.clearFlags = CameraClearFlags.Skybox;
            MainSceneBuilder.BuildHands(rig, sim);
            BuildGameplay(rig);
            AtmosphereBuilder.Build(rig, sun, night: false, parapetHeight: 99f);
            BattleBuilder.Build(rig, eye.transform); // w3: the continuous battle
            W4Builder.Build(_root); // w4: TACEVAC helicopter + litter
            BuildLocomotion(rig);
            BuildBookmarks();
            BattleBuilder.Bookmarks();
            TuneLods();
            LimitShadowCasters();
            CombineSmallStatics();

            RenderSettings.sun = sun;
            EditorSceneManager.SaveScene(scene, BattlefieldLayout.ScenePath); // occlusion data needs a saved scene
            BakeOcclusion();
            EditorSceneManager.SaveScene(scene, BattlefieldLayout.ScenePath);
            MainSceneBuilder.SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FMVR] Battlefield built: {BattlefieldLayout.ScenePath}; cover pieces {SolidCover.Count}, " +
                      $"cover points {Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None).Length}, missing prefabs: {(Missing.Count == 0 ? "none" : string.Join(",", Missing.Distinct()))}");
        }

        // ================================================================== sky, light, fog
        public static readonly Color Haze = new Color(0.63f, 0.645f, 0.64f);

        static Light SetupSkyAndLight()
        {
            var hdr = AssetDatabase.LoadAssetAtPath<Texture>("Assets/ThirdParty/PolyHaven/HDRI/overcast_soil_puresky_2k.hdr");
            string sp = MatDir + "/Bf_HazySky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(sp);
            var sh = Shader.Find("FieldMedic/HazySky");
            if (sky == null) { sky = new Material(sh); AssetDatabase.CreateAsset(sky, sp); }
            sky.shader = sh;
            sky.SetTexture("_MainTex", hdr);
            sky.SetFloat("_Exposure", 0.8f);
            sky.SetColor("_HazeColor", Haze);
            sky.SetFloat("_HazeHeight", 0.16f);
            sky.SetVector("_SmokeDir", new Vector4(0f, 0f, 1f, 0f));
            sky.SetFloat("_SmokeAmount", 0.35f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.7f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0078f;
            RenderSettings.fogColor = Haze;

            var go = new GameObject("Sun (overcast)");
            go.transform.SetParent(_root, false);
            go.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.96f, 0.97f, 1f);   // overcast: neutral-cool, not golden
            l.intensity = 1.0f;
            l.shadows = LightShadows.Hard;  // soft shadows rendered black diffuse in URP 17 captures; hard is also cheaper on Quest
            l.shadowStrength = 0.62f;   // overcast: soft, not black
            l.shadowBias = 0.05f;
            l.shadowNormalBias = 0.4f;

            // the open field needs shadows further than the trench did (Quest: one 2-cascade map)
            foreach (var asset in new[] { "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/PC_RPAsset.asset" })
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(asset);
                if (rp == null) continue;
                rp.shadowDistance = Mathf.Max(rp.shadowDistance, 32f);
                EditorUtility.SetDirty(rp);
            }
            return l;
        }

        static Material GroundMaterial()
        {
            string path = MatDir + "/Bf_Ground.mat";
            var sh = Shader.Find("FieldMedic/GroundBlend");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_GrassMap", Tex("withered_grass/withered_grass_diff.jpg"));
            m.SetTexture("_GrassNormal", Tex("withered_grass/withered_grass_nor_gl.jpg"));
            m.SetTexture("_DirtMap", Tex("dry_ground_01/dry_ground_01_diff.jpg"));
            m.SetTexture("_DirtNormal", Tex("dry_ground_01/dry_ground_01_nor_gl.jpg"));
            m.SetTexture("_BurnMap", Tex("burned_ground_01/burned_ground_01_diff.jpg"));
            m.SetColor("_GrassTint", new Color(0.72f, 0.7f, 0.58f));    // autumn steppe: muted khaki-olive, war-grey, not desert sand
            m.SetColor("_DirtTint", new Color(0.76f, 0.7f, 0.62f));
            m.SetColor("_BurnTint", new Color(0.8f, 0.78f, 0.76f));
            m.SetVector("_Tile", new Vector4(3.2f, 2.6f, 3f, 60f));
            m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string rel) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PH}/{rel}");

        static Material WallMaterial(string name, Color plaster, Color brick)
        {
            string path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find("FieldMedic/RuinWall");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_PlasterMap", Tex("rough_plaster_brick_04/rough_plaster_brick_04_diff.jpg"));
            m.SetTexture("_PlasterNormal", Tex("rough_plaster_brick_04/rough_plaster_brick_04_nor_gl.jpg"));
            m.SetTexture("_BrickMap", Tex("broken_brick_wall/broken_brick_wall_diff.jpg"));
            m.SetTexture("_BrickNormal", Tex("broken_brick_wall/broken_brick_wall_nor_gl.jpg"));
            m.SetColor("_PlasterTint", plaster);
            m.SetColor("_BrickTint", brick);
            m.SetVector("_Tile", new Vector4(2.4f, 1.25f, 0f, 0f));
            m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================== prefab helpers
        static GameObject Prefab(string slug)
        {
            if (PrefabPaths.TryGetValue(slug, out var p)) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Missing.Add(slug);
            return null;
        }

        /// <summary>Instantiates a w1 prefab on the ground (pivot = bottom centre). tilt = extra x/z rotation.</summary>
        static GameObject Place(string slug, Transform parent, Vector3 pos, float yaw, float scale = 1f, Vector3 tilt = default, float sink = 0f)
        {
            var pf = Prefab(slug);
            if (pf == null) { Debug.LogWarning("[FMVR] missing prefab " + slug); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            pos.y = BattlefieldLayout.GroundHeight(pos.x, pos.z) - sink + pos.y;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(tilt.x, yaw, tilt.z));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>Solid cover: Cover tag/layer already set by the prefab setup; adds a NavMeshObstacle and
        /// registers it for an automatic CoverPoint.</summary>
        static GameObject Cover(GameObject go, bool point = true)
        {
            if (go == null) return null;
            int layer = LayerMask.NameToLayer(CoverMath.CoverLayerName);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (layer >= 0) t.gameObject.layer = layer;
                t.gameObject.tag = CoverMath.CoverTag;
            }
            if (go.GetComponentInChildren<Collider>() == null)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (!mf.name.Contains("_LOD") || mf.name.EndsWith("_LOD0")) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            RuinBuilder.AddObstacle(go);
            if (point) SolidCover.Add(go);
            Footprints.Add(WorldBounds(go));
            return go;
        }

        /// <summary>Obstacle that does NOT stop bullets (wire, hedgehog steel): Default layer, still a NavMeshObstacle.</summary>
        static GameObject Obstacle(GameObject go)
        {
            if (go == null) return null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; t.gameObject.tag = "Untagged"; }
            RuinBuilder.AddObstacle(go);
            Footprints.Add(WorldBounds(go));
            return go;
        }

        static GameObject Prop(GameObject go)
        {
            if (go != null) Footprints.Add(WorldBounds(go));
            return go;
        }

        public static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Replaces every material on the instance with a tinted copy (burnt metal, soot, sun-bleached).</summary>
        static void Retint(GameObject go, string suffix, Color mul, float smoothMul = 1f, float? metallic = null, string onlyMat = null)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    if (onlyMat != null && !src.name.Contains(onlyMat)) continue;
                    string key = src.GetInstanceID() + suffix;
                    if (!OverrideCache.TryGetValue(key, out var m))
                    {
                        string path = $"{OverrideDir}/{SafeName(src.name)}_{suffix}.mat";
                        m = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
                        else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor") * mul);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", src.GetFloat("_Smoothness") * smoothMul);
                        if (metallic.HasValue && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic.Value);
                        m.enableInstancing = true;
                        EditorUtility.SetDirty(m);
                        OverrideCache[key] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        static void Burn(GameObject go) => Retint(go, "burnt", new Color(0.2f, 0.185f, 0.17f), 0.35f, 0.25f);

        static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root, false);
            return t;
        }

        // ================================================================== ruins
        static void BuildRuins()
        {
            var g = Group("Ruins");
            var wallA = WallMaterial("Bf_RuinWall_A", new Color(0.86f, 0.83f, 0.78f), new Color(0.86f, 0.8f, 0.76f));
            var wallC = WallMaterial("Bf_RuinWall_C", new Color(0.74f, 0.72f, 0.68f), new Color(0.7f, 0.62f, 0.58f));
            float t = BattlefieldLayout.WallThickness * 0.5f;
            var a0 = BattlefieldLayout.HouseAMin; var a1 = BattlefieldLayout.HouseAMax;
            var d = BattlefieldLayout.DoorwayZ;
            var houseA = new GameObject("HouseA").transform; houseA.SetParent(g, false);

            // north wall (faces the enemy): two firing windows, a shell hole high up, broken top
            RuinBuilder.BuildWall(houseA, new RuinBuilder.WallSpec
            {
                name = "HouseA_North", start = new Vector3(a0.x - t, 0f, a1.y), end = new Vector3(a1.x + t, 0f, a1.y),
                height = 5.6f, jag = 0.9f, seed = 11, soot = 0.35f,
                openings = new[] { new RuinBuilder.Opening(1.95f, 2.85f, 1.3f, 2.1f), new RuinBuilder.Opening(6.15f, 6.9f, 1.35f, 2.15f), new RuinBuilder.Opening(4.4f, 5.2f, 1.75f, 2.4f),
                                   new RuinBuilder.Opening(1.5f, 2.6f, 3.35f, 4.55f), new RuinBuilder.Opening(4.1f, 5.1f, 3.35f, 4.55f) },
                collapses = new[] { (3.3f, 4.6f, 1.75f), (6.0f, 8.6f, 3.4f) },
            }, wallA, MeshDir);
            // west wall: collapsed at the south end (the debris pile outside is what is left of it)
            RuinBuilder.BuildWall(houseA, new RuinBuilder.WallSpec
            {
                name = "HouseA_West", start = new Vector3(a0.x, 0f, a0.y - t), end = new Vector3(a0.x, 0f, a1.y + t),
                height = 5.2f, jag = 0.9f, seed = 12, soot = 0.3f,
                openings = new[] { new RuinBuilder.Opening(3.3f, 4.2f, 1.1f, 2.0f), new RuinBuilder.Opening(4.6f, 5.5f, 3.3f, 4.4f) },
                collapses = new[] { (-0.6f, 2.4f, 0.35f), (2.4f, 3.6f, 3.1f) },
            }, wallA, MeshDir);
            // south wall (rear): mostly down, a breach for the way back to the LZ
            float sx(float worldX) => a1.x + t - worldX;
            RuinBuilder.BuildWall(houseA, new RuinBuilder.WallSpec
            {
                name = "HouseA_South", start = new Vector3(a1.x + t, 0f, a0.y), end = new Vector3(a0.x - t, 0f, a0.y),
                height = 1.15f, jag = 0.7f, seed = 13, soot = 0.25f,
                openings = new[] { RuinBuilder.Opening.Breach(sx(0.5f), sx(-1.1f)) },
                collapses = new[] { (sx(-2.5f), sx(-5.5f), 0.4f) },
            }, wallA, MeshDir);
            // east wall: the drag doorway (a gap to the ground), rest stands
            float ez(float worldZ) => a1.y + t - worldZ;
            RuinBuilder.BuildWall(houseA, new RuinBuilder.WallSpec
            {
                name = "HouseA_East", start = new Vector3(a1.x, 0f, a1.y + t), end = new Vector3(a1.x, 0f, a0.y - t),
                height = 4.3f, jag = 0.8f, seed = 14, soot = 0.3f,
                openings = new[] { RuinBuilder.Opening.Breach(ez(d.y), ez(d.x)), new RuinBuilder.Opening(0.7f, 1.6f, 2.9f, 3.8f) },
                collapses = new[] { (4.6f, 6.6f, 2.5f) },
            }, wallA, MeshDir);
            var floorMat = BattlefieldFx.Lit(MatDir, "Bf_FloorConcrete", $"{PH}/concrete_floor_damaged_01/concrete_floor_damaged_01_diff.jpg",
                $"{PH}/concrete_floor_damaged_01/concrete_floor_damaged_01_nor_gl.jpg", new Color(0.78f, 0.75f, 0.7f), 0.12f, 0f, new Vector2(0.4f, 0.4f));
            RuinBuilder.BuildFloor(houseA, "HouseA_Floor", new Vector2(a0.x + t, a0.y + t), new Vector2(a1.x - t, a1.y - t), 0.04f, floorMat, MeshDir, 3);
            // what is left of the first floor: a slab hanging off the north-west corner (the storey above is gone)
            RuinBuilder.BuildFloor(houseA, "HouseA_Slab1F", new Vector2(a0.x + t, a1.y - 2.1f), new Vector2(a0.x + 3.1f, a1.y - t), 2.95f, floorMat, MeshDir, 33);
            Footprints.Add(new Bounds(new Vector3((a0.x + a1.x) / 2f, 1f, (a0.y + a1.y) / 2f), new Vector3(a1.x - a0.x + 0.8f, 2f, a1.y - a0.y + 0.8f)));

            // collapsed roof + rubble inside and at the broken corners
            Prop(Place("junk_slab_pile", houseA, new Vector3(-4.4f, 0.04f, 2.2f), 100f, 0.85f));
            Prop(Place("rubble_pile", houseA, new Vector3(-5.2f, 0.04f, 3.3f), 30f, 1.2f));
            Prop(Place("rubble_pile", houseA, new Vector3(1.2f, 0.04f, 3.2f), 160f, 1.0f));
            Prop(Place("rubble_pile", houseA, new Vector3(-2.0f, 0.04f, -1.3f), 75f, 1.3f));
            Cover(Place("bricks_debris_pile", houseA, new Vector3(-7.4f, 0f, -3.0f), 20f, 0.95f, sink: 0.1f));
            Prop(Place("ruined_masonry_wall", houseA, new Vector3(-3.6f, 0f, -3.3f), 170f, 0.9f, sink: 0.1f));
            ScatterDebris(houseA, new Vector3(-2f, 0f, 1f), 7.5f, 14, 101);

            // house C: a two-storey brick house reduced to an L of walls
            var houseC = new GameObject("HouseC").transform; houseC.SetParent(g, false);
            RuinBuilder.BuildWall(houseC, new RuinBuilder.WallSpec
            {
                name = "HouseC_North", start = new Vector3(-21.2f, 0f, 11f), end = new Vector3(-12.8f, 0f, 11f),
                height = 5.4f, jag = 1.1f, seed = 21, soot = 0.55f,
                openings = new[] { new RuinBuilder.Opening(1.6f, 2.5f, 1.05f, 2.05f), new RuinBuilder.Opening(5.3f, 6.2f, 1.05f, 2.05f), new RuinBuilder.Opening(3.3f, 4.2f, 2.7f, 3.8f) },
                collapses = new[] { (6.6f, 8.6f, 1.6f) },
            }, wallC, MeshDir);
            RuinBuilder.BuildWall(houseC, new RuinBuilder.WallSpec
            {
                name = "HouseC_West", start = new Vector3(-21f, 0f, 3.2f), end = new Vector3(-21f, 0f, 11.2f),
                height = 4.6f, jag = 1.0f, seed = 22, soot = 0.45f,
                openings = new[] { new RuinBuilder.Opening(4.6f, 5.5f, 1.05f, 2.05f) },
                collapses = new[] { (-0.5f, 3.2f, 0.45f) },
            }, wallC, MeshDir);
            RuinBuilder.BuildWall(houseC, new RuinBuilder.WallSpec
            {
                name = "HouseC_East", start = new Vector3(-13f, 0f, 11.2f), end = new Vector3(-13f, 0f, 7.6f),
                height = 1.7f, jag = 0.8f, seed = 23, soot = 0.4f,
            }, wallC, MeshDir);
            RuinBuilder.BuildFloor(houseC, "HouseC_Floor", new Vector2(-20.8f, 3.6f), new Vector2(-13.2f, 10.8f), 0.04f, floorMat, MeshDir, 9);
            Footprints.Add(new Bounds(new Vector3(-17f, 1f, 7.2f), new Vector3(9f, 2f, 8.8f)));
            Cover(Place("bricks_debris_pile", houseC, new Vector3(-21.5f, 0f, 4.4f), 140f, 1.05f, sink: 0.1f));
            Prop(Place("junk_slab_pile", houseC, new Vector3(-16.5f, 0.04f, 8f), 20f, 1.0f));
            Prop(Place("rubble_pile", houseC, new Vector3(-13.8f, 0.04f, 10.2f), 60f, 1.4f));
            Cover(Place("pillar_bullet_holes", houseC, new Vector3(-13f, 0f, 4.2f), 15f));
            ScatterDebris(houseC, new Vector3(-17f, 0f, 7f), 7f, 8, 202);

            // house B: behind-left, burnt out - three walls with window holes, the roof slab fallen inside
            var houseB = new GameObject("HouseB").transform; houseB.SetParent(g, false);
            var wallB = WallMaterial("Bf_RuinWall_B", new Color(0.8f, 0.78f, 0.72f), new Color(0.78f, 0.7f, 0.64f));
            RuinBuilder.BuildWall(houseB, new RuinBuilder.WallSpec
            {
                name = "HouseB_North", start = new Vector3(-21f, 0f, -10f), end = new Vector3(-13.2f, 0f, -10f),
                height = 5.0f, jag = 1.0f, seed = 31, soot = 0.7f,
                openings = new[] { new RuinBuilder.Opening(1.2f, 2.1f, 1.0f, 2.0f), new RuinBuilder.Opening(4.6f, 5.5f, 1.0f, 2.0f), RuinBuilder.Opening.Breach(2.8f, 3.9f) },
            }, wallB, MeshDir);
            RuinBuilder.BuildWall(houseB, new RuinBuilder.WallSpec
            {
                name = "HouseB_East", start = new Vector3(-13.2f, 0f, -9.8f), end = new Vector3(-13.2f, 0f, -16.2f),
                height = 4.4f, jag = 0.9f, seed = 32, soot = 0.6f,
                openings = new[] { new RuinBuilder.Opening(2.4f, 3.3f, 1.0f, 2.0f) },
                collapses = new[] { (4.6f, 6.6f, 0.6f) },
            }, wallB, MeshDir);
            RuinBuilder.BuildWall(houseB, new RuinBuilder.WallSpec
            {
                name = "HouseB_West", start = new Vector3(-21f, 0f, -16.2f), end = new Vector3(-21f, 0f, -9.8f),
                height = 2.2f, jag = 0.9f, seed = 33, soot = 0.5f,
                collapses = new[] { (-0.5f, 2.5f, 0.5f) },
            }, wallB, MeshDir);
            RuinBuilder.BuildFloor(houseB, "HouseB_Floor", new Vector2(-20.8f, -16f), new Vector2(-13.4f, -10.2f), 0.04f, floorMat, MeshDir, 17);
            Footprints.Add(new Bounds(new Vector3(-17f, 1f, -13f), new Vector3(8.8f, 2f, 7.4f)));
            Prop(Place("junk_slab_pile", houseB, new Vector3(-17.5f, 0.04f, -13.5f), 200f, 1.1f));
            Prop(Place("rubble_pile", houseB, new Vector3(-14.2f, 0.04f, -11f), 20f, 1.5f));
            Cover(Place("bricks_debris_pile", houseB, new Vector3(-21.6f, 0f, -15.4f), 70f, 0.9f, sink: 0.1f));
            Prop(Place("bricks_debris_pile", houseB, new Vector3(-12.2f, 0f, -15.2f), 160f, 0.75f, sink: 0.1f));
            ScatterDebris(houseB, new Vector3(-17f, 0f, -13f), 6f, 6, 303);

            // real ruins at full building scale (supervisor 03:55 #2 / 07:00 #2): a shelled five-storey apartment block
            // behind the position (BlenderKit "Abandoned House", PBRPX) and a roofless farmhouse on the right flank
            var block = Cover(Place("bombed_apartment", g, new Vector3(-30f, 0f, -26f), 62f, 1f, sink: 0.15f), false);
            Retint(block, "war", new Color(0.82f, 0.8f, 0.77f), 0.8f);
            ScatterDebris(g, new Vector3(-24f, 0f, -18f), 9f, 7, 404);
            Prop(Place("bricks_debris_pile", g, new Vector3(-20.5f, 0f, -21f), 40f, 1.6f, sink: 0.1f));
            Prop(Place("rubble_pile", g, new Vector3(-23.5f, 0f, -16.5f), 110f, 2.0f));
            var farm = Cover(Place("ruined_farmhouse", g, new Vector3(25f, 0f, -14f), -24f, 1f, sink: 0.05f), false);
            Retint(farm, "war", new Color(0.78f, 0.74f, 0.7f), 0.8f);
            Prop(Place("bricks_debris_pile", g, new Vector3(19.5f, 0f, -10f), 10f, 1.1f, sink: 0.1f));
            Prop(Place("junk_slab_pile", g, new Vector3(27f, 0.02f, -9.5f), 70f, 1.1f));
        }

        static void ScatterDebris(Transform parent, Vector3 centre, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            string[] pieces = { "brick_ruins_a", "brick_ruins_b", "brick_ruins_b", "concrete_chunk", "concrete_chunk" };
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                var p = centre + new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r * 0.8f);
                var go = Place(pieces[rng.Next(pieces.Length)], parent, p, (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 0.6f,
                    new Vector3((float)rng.NextDouble() * 20f - 10f, 0f, (float)rng.NextDouble() * 20f - 10f), 0.03f);
                if (go != null) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); // small debris: no physics cost
            }
        }

        // ================================================================== cover line in front of house A
        static void BuildCoverLine()
        {
            var g = Group("CoverLine");
            // jersey barriers topped with two courses of filled sacks: ~1.5 m, covers a kneeling man
            Sandbagged(g, "concrete_blockade", new Vector3(-4.6f, 0f, 6.6f), 0f, 2.88f, 0.8f);
            Sandbagged(g, "concrete_blockade", new Vector3(-1.6f, 0f, 6.7f), -4f, 2.88f, 0.8f);
            // checkpoint barriers covering the doorway approach from the north-east
            Sandbagged(g, "concrete_barriers_lowpoly", new Vector3(5.3f, 0f, 4.4f), 90f, 3.3f, 0.8f, alongZ: true);
            // a culvert pipe on the west flank and a bullet-pocked concrete post out front (cover on the drag line)
            Burn(Cover(Place("car_wreck_beetle", g, new Vector3(-10.4f, 0f, 2.6f), 172f, 1f, new Vector3(0f, 0f, 3f), 0.08f)));
            Prop(Place("junk_slab_pile", g, new Vector3(-12.2f, 0.02f, 1.2f), 30f, 0.9f));
            Cover(Place("pillar_bullet_holes", g, new Vector3(6.9f, 0f, 7.2f), -10f));
            // single road barriers dragged out as improvised cover (low: prone cover only)
            Retint(Prop(Place("concrete_road_barrier", g, new Vector3(8.6f, 0f, 2.4f), 35f)), "dirty", new Color(0.72f, 0.69f, 0.63f), 0.7f);
            Retint(Prop(Place("concrete_road_barrier", g, new Vector3(-8.6f, 0f, 7.8f), -20f)), "dirty", new Color(0.72f, 0.69f, 0.63f), 0.7f);
        }

        static Material _sandbag;
        static Material SandbagMaterial() => _sandbag != null ? _sandbag : (_sandbag = BattlefieldFx.Lit(MatDir, "Bf_Sandbag",
            $"{PH}/hessian_230/hessian_230_diff.jpg", $"{PH}/hessian_230/hessian_230_nor_gl.jpg", new Color(0.66f, 0.62f, 0.47f), 0.12f, 0f, new Vector2(2.2f, 2.2f)));

        static void Sandbagged(Transform g, string barrier, Vector3 pos, float yaw, float length, float height, bool alongZ = false)
        {
            var b = Place(barrier, g, pos, yaw);
            if (b == null) return;
            Cover(b);
            Retint(b, "dirty", new Color(0.72f, 0.69f, 0.63f), 0.7f); // road dust and soot, not showroom white
            // sacks: 0.87 × 0.56 footprint (×2 stacked, 0.35 high) laid along the barrier, second course staggered
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 along = rot * (alongZ ? Vector3.forward : Vector3.right);
            int n = Mathf.Max(2, Mathf.FloorToInt(length / 0.86f));
            float y0 = BattlefieldLayout.GroundHeight(pos.x, pos.z) + height - 0.03f;
            for (int course = 0; course < 2; course++)
            {
                int cnt = course == 0 ? n : n - 1;
                for (int i = 0; i < cnt; i++)
                {
                    float off = (i - (cnt - 1) * 0.5f) * 0.86f;
                    var p = pos + along * off;
                    var s = Place("burlap_sacks", b.transform, new Vector3(p.x, 0f, p.z), yaw + (alongZ ? 90f : 0f) + (i % 2 == 0 ? 2f : -3f) + course * 4f,
                        1f, new Vector3(0f, 0f, (i % 3 - 1) * 2f));
                    if (s == null) continue;
                    // military sandbags: plain olive-khaki hessian, no "50KG" grain-sack print (supervisor 15.10 #3)
                    foreach (var r in s.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => SandbagMaterial()).ToArray();
                    s.transform.position = new Vector3(p.x, y0 + course * 0.33f, p.z);
                    Cover(s, false);
                }
            }
        }

        // ================================================================== obstacles, wrecks
        static void BuildObstaclesAndWrecks()
        {
            var g = Group("ObstaclesAndWrecks");
            // burnt APC on the right of the ruins, nosed into the dirt
            var apc = Cover(Place("arx_apc", g, new Vector3(10.6f, 0f, -0.8f), 214f, 1f, new Vector3(1.5f, 0f, -3f), 0.12f));
            Burn(apc);
            BattlefieldFx.Smoulder(g, new Vector3(10.4f, 2.6f, -0.4f), SmokeMat(), 1.1f);
            Prop(Place("old_tyre", g, new Vector3(13.6f, 0f, 1.8f), 30f, 1f, new Vector3(85f, 0f, 0f)));
            Prop(Place("old_tyre", g, new Vector3(7.8f, 0f, -4.6f), 0f));

            // czech hedgehogs scattered across the open ground (steel: obstacle, not cover)
            var hh = new[] { new Vector3(-16f, 0f, 24f), new Vector3(-9f, 0f, 27.5f), new Vector3(-2.5f, 0f, 23.5f), new Vector3(6f, 0f, 26.5f), new Vector3(13.5f, 0f, 24f), new Vector3(20f, 0f, 28.5f), new Vector3(-23f, 0f, 29f) };
            for (int i = 0; i < hh.Length; i++)
                Retint(Obstacle(Place("czech_hedgehog", g, hh[i], i * 47f + 13f, 1f, new Vector3(0f, 0f, i % 2 == 0 ? 0f : 6f))), "rust", new Color(0.62f, 0.52f, 0.44f), 0.5f, 0.5f);
            // barbed wire: a broken fence line further out + X-frames in between
            // concertina coils strung out in front (gaps where shells cut them), X-frame pickets at the ends
            var steel = BattlefieldFx.Lit(MatDir, "Bf_RazorWire", null, null, new Color(0.24f, 0.22f, 0.2f), 0.45f, 0.7f, Vector2.one);
            var runs = new[] { (new Vector3(-26f, 0f, 34.5f), new Vector3(-14f, 0f, 35.5f)), (new Vector3(-11f, 0f, 35.8f), new Vector3(1f, 0f, 34.6f)),
                               (new Vector3(4.5f, 0f, 34.2f), new Vector3(17f, 0f, 35.2f)), (new Vector3(20f, 0f, 35.4f), new Vector3(29f, 0f, 34f)),
                               (new Vector3(-19f, 0f, 20.5f), new Vector3(-12.5f, 0f, 20f)), (new Vector3(9.5f, 0f, 19f), new Vector3(17f, 0f, 20.6f)) };
            int ri = 0;
            foreach (var (a, b) in runs)
            {
                Obstacle(BattlefieldFx.Concertina(g, "Concertina_" + ri, a, b, steel, MeshDir, 300 + ri));
                float yawRun = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
                Retint(Obstacle(Place("barbed_wire_fence", g, a, yawRun, 1f)), "rust", new Color(0.6f, 0.5f, 0.42f), 0.6f);
                Retint(Obstacle(Place("barbed_wire_fence", g, b, yawRun + 8f, 1f)), "rust", new Color(0.6f, 0.5f, 0.42f), 0.6f);
                ri++;
            }

            // wrecks out in the field
            var car = Cover(Place("car_wreck_beetle", g, new Vector3(-14.5f, 0f, 17f), 34f, 1f, new Vector3(0f, 0f, 4f), 0.08f));
            Burn(car);
            var truck = Cover(Place("pinzgauer_truck", g, new Vector3(31f, 0f, 22f), 252f, 1f, new Vector3(0f, 0f, -6f), 0.15f));
            Burn(truck);
            BattlefieldFx.Smoulder(g, new Vector3(31f, 2.2f, 22f), SmokeMat(), 1.4f);
            var tank = Cover(Place("m1a2_tank", g, new Vector3(-33f, 0f, 49f), 142f, 1f, new Vector3(-7f, 0f, 3f), 0.6f));
            Burn(tank);
            BattlefieldFx.Smoulder(g, new Vector3(-33f, 3.0f, 49f), SmokeMat(), 2f);
        }

        // ================================================================== props
        static void BuildProps()
        {
            var g = Group("Props");
            Prop(Place("military_crate", g, new Vector3(-5.1f, 0.04f, -0.9f), 92f));
            Prop(Place("military_crate", g, new Vector3(-5.05f, 0.5f, -0.85f), 86f));
            Prop(Place("ammo_box", g, new Vector3(-4.4f, 0.04f, -1.3f), 20f, 1f));
            Prop(Place("ammo_box", g, new Vector3(-4.25f, 0.04f, -1.15f), 75f, 1f));
            Prop(Place("artillery_shells", g, new Vector3(-5.4f, 0.04f, 0.6f), 90f));
            Prop(Place("jerrycan_green", g, new Vector3(1.4f, 0.04f, -1.5f), 200f));
            Prop(Place("jerrycan_green", g, new Vector3(1.0f, 0.04f, -1.6f), 170f));
            Prop(Place("radio_r311", g, new Vector3(-5.05f, 1.0f, -1.05f), 95f));
            Prop(Place("fuel_drums", g, new Vector3(6.6f, 0f, -5.8f), 120f));
            Prop(Place("stretcher", g, new Vector3(-0.2f, 0f, -4.4f), 92f));
            // casualty collection point near the LZ
            var lz = BattlefieldLayout.LandingZone;
            Prop(Place("military_tent", g, lz + new Vector3(9f, 0f, 4f), -60f));
            Prop(Place("military_crate", g, lz + new Vector3(7.2f, 0f, 1.5f), 10f));
            Prop(Place("jerrycan_green", g, lz + new Vector3(6.6f, 0f, 2.6f), 40f));
        }

        // ================================================================== far field
        static void BuildFarField()
        {
            var g = Group("FarField");
            var rng = new System.Random(77);
            for (int i = 0; i < 16; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 34f + (float)rng.NextDouble() * 110f;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                string slug = i % 3 == 0 ? "sand_rocks_small_01" : "namaqualand_boulder_02";
                var go = Place(slug, g, p, (float)rng.NextDouble() * 360f, 0.7f + (float)rng.NextDouble() * 0.8f, default, 0.25f);
                if (go != null) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            // a second hamlet on the horizon to the left: wall stubs + debris in the haze
            Prop(Place("debris_bricks_wall", g, new Vector3(-62f, 0f, 70f), 160f, 1.6f));
            Prop(Place("brick_block_wall", g, new Vector3(-58f, 0f, 74f), 190f, 1.8f));
            Prop(Place("bricks_debris_pile", g, new Vector3(-55f, 0f, 69f), 30f, 1.4f, sink: 0.1f));
            // (w4: the intact derelict block and the glass-like unfinished frame read as fake on the horizon — removed;
            // the real bombed apartment block stands behind our position instead, see BuildRuins)
        }

        static Material _smoke;
        static Material SmokeMat() => _smoke != null ? _smoke : (_smoke = BattlefieldFx.SmokeSheetMat(ArtDir));

        static void BuildSmoke()
        {
            var g = Group("SmokeColumns");
            var glow = AtmosphereBuilder.FxMat("FX_Glow", Color.white, true, 2f);
            int i = 0;
            foreach (var s in BattlefieldLayout.SmokeColumnSites())
            {
                var p = new Vector3(s.x, BattlefieldLayout.GroundHeight(s.x, s.z), s.z);
                float scale = 0.9f + (i % 3) * 0.25f;
                BattlefieldFx.SmokeColumn(g, "SmokeColumn" + i, p, scale, i < 4, SmokeMat(), glow, 1000 + i * 17);
                i++;
            }
        }

        static bool GrassBlocked(Vector3 p)
        {
            foreach (var b in Footprints)
            {
                var e = b; e.Expand(new Vector3(0.6f, 10f, 0.6f));
                if (e.Contains(new Vector3(p.x, e.center.y, p.z))) return true;
            }
            return false;
        }

        // ================================================================== cover points
        static void BuildCoverPoints()
        {
            var g = Group("CoverPoints");
            // walls: hand-placed behind the solid stretches (away from windows/breaches)
            var a1 = BattlefieldLayout.HouseAMax;
            float behindA = a1.y - BattlefieldLayout.WallThickness * 0.5f - 0.5f;
            AddPoint(g, "HouseA_North_W", new Vector3(-5.2f, 0f, behindA), "HouseA_North", 10f);
            AddPoint(g, "HouseA_North_C", new Vector3(-2.6f, 0f, behindA), "HouseA_North", 10f);
            AddPoint(g, "HouseA_North_E", new Vector3(1.3f, 0f, behindA), "HouseA_North", 10f);
            AddPoint(g, "HouseC_North_W", new Vector3(-20.4f, 0f, 10.3f), "HouseC_North", 10f);
            AddPoint(g, "HouseC_North_C", new Vector3(-17.4f, 0f, 10.3f), "HouseC_North", 10f);
            AddPoint(g, "HouseB_North_W", new Vector3(-20.0f, 0f, -10.7f), "HouseB_North", 10f);
            AddPoint(g, "HouseB_North_E", new Vector3(-14.4f, 0f, -10.7f), "HouseB_North", 10f);

            // every registered solid cover piece: find its back face at a kneeling man's head height
            int mask = CoverMath.CoverMask;
            foreach (var go in SolidCover)
            {
                var b = WorldBounds(go);
                float ground = BattlefieldLayout.GroundHeight(b.center.x, b.center.z);
                if (b.max.y - ground < 1.3f) { Debug.Log($"[FMVR] {go.name}: too low for kneeling cover ({b.max.y - ground:F2} m), no cover point"); continue; }
                Vector3 head = Vector3.zero;
                bool found = false;
                foreach (float dx in new[] { 0f, -0.25f, 0.25f, -0.5f, 0.5f })
                {
                    var from = new Vector3(b.center.x + dx * b.extents.x, ground + BattlefieldLayout.KneelEyeHeight, b.min.z - 4f);
                    // nearest hit on THIS object (other cover, e.g. house A's wall, may lie on the probe line)
                    var hits = Physics.RaycastAll(from, Vector3.forward, b.size.z + 8f, mask, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.transform.IsChildOf(go.transform)).OrderBy(h => h.distance).ToArray();
                    if (hits.Length > 0)
                    {
                        head = hits[0].point - Vector3.forward * 0.5f;
                        found = true;
                        break;
                    }
                }
                if (!found) { Debug.LogWarning($"[FMVR] {go.name}: no back face found for a cover point"); continue; }
                var p = AddPoint(g, "CP_" + go.name, new Vector3(head.x, 0f, head.z), go.name, 6f);
                p.cover = go;
            }
        }

        static CoverPoint AddPoint(Transform g, string name, Vector3 pos, string coverName, float arc)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            pos.y = BattlefieldLayout.GroundHeight(pos.x, pos.z);
            go.transform.position = pos;
            var cp = go.AddComponent<CoverPoint>();
            cp.threatDirection = BattlefieldLayout.EnemyDirection;
            cp.arcHalfAngle = arc;
            cp.cover = GameObject.Find(coverName);
            return cp;
        }

        // ================================================================== gameplay (V1 systems on the new map)
        static void BuildGameplay(GameObject rig)
        {
            var eye = MainSceneBuilder.FindDeep(rig.transform, "CenterEyeAnchor");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CasualtyAssetBuilder.PrefabPath);
            if (prefab != null)
            {
                var erlan = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                erlan.name = "Erlan";
                erlan.transform.SetPositionAndRotation(BattlefieldLayout.TreatmentSpot + new Vector3(0.3f, 0.04f, 0f), Quaternion.Euler(0f, 160f, 0f));
            }
            else Debug.LogWarning("[FMVR] Erlan prefab missing");

            var pouch = new GameObject("PlayerIFAK");
            pouch.transform.position = BattlefieldLayout.PlayerStart + new Vector3(0f, 0.95f, 0.25f);
            var ifak = pouch.AddComponent<IfakPouch>();
            ifak.bodyLocked = true;
            ifak.head = eye;

            // inside house A counts as "in cover" for the V1 care-under-fire → field-care transition
            var cover = new GameObject("CoverZone_HouseA");
            var a0 = BattlefieldLayout.HouseAMin; var a1 = BattlefieldLayout.HouseAMax;
            cover.transform.position = new Vector3((a0.x + a1.x) / 2f, 1f, (a0.y + a1.y) / 2f);
            cover.AddComponent<CoverZone>().size = new Vector3(a1.x - a0.x - 0.4f, 2f, a1.y - a0.y - 0.4f);
            var coverC = new GameObject("CoverZone_HouseC");
            coverC.transform.position = new Vector3(-17f, 1f, 7.4f);
            coverC.AddComponent<CoverZone>().size = new Vector3(7.6f, 2f, 7f);

            new GameObject("FeedbackHud").AddComponent<FeedbackHud>();

            var root = new GameObject("Scenarios").transform;
            var d = root.gameObject.AddComponent<ScenarioDirector>();
            var er = GameObject.Find("Erlan");
            d.casualty = er != null ? er.GetComponent<FieldMedic.Casualty.CasualtyController>() : null;
            d.playerRig = rig.transform;
            d.head = eye;
            Transform Anchor(string name, Vector3 pos, float yaw)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root, false);
                pos.y = BattlefieldLayout.GroundHeight(pos.x, pos.z) + pos.y;
                t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                return t;
            }
            // V1 spot ids mapped onto the battlefield until w4 ports the scenarios to the open-ground flow
            var fall = BattlefieldLayout.ErlanFallPoint;
            d.spots = new[]
            {
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.FireBay, casualty = Anchor("Spot_Open_Casualty", fall, 200f), player = Anchor("Spot_Open_Player", BattlefieldLayout.DoorwayCentre + new Vector3(-0.8f, 0f, 0f), 20f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.CommTrench, casualty = Anchor("Spot_HouseA_Casualty", BattlefieldLayout.TreatmentSpot + new Vector3(0f, 0.04f, 0f), 90f), player = Anchor("Spot_HouseA_Player", BattlefieldLayout.TreatmentSpot + new Vector3(0.9f, 0.04f, -0.6f), 300f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.FarBay, casualty = Anchor("Spot_HouseC_Casualty", new Vector3(-16.5f, 0.04f, 9.4f), 90f), player = Anchor("Spot_HouseC_Player", new Vector3(-15.4f, 0.04f, 8.6f), 300f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.Dugout, casualty = Anchor("Spot_HouseA_Rear_Casualty", new Vector3(-3.6f, 0.04f, -0.6f), 0f), player = Anchor("Spot_HouseA_Rear_Player", new Vector3(-2.6f, 0.04f, 0.1f), 240f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.OpenFar, casualty = Anchor("Spot_OpenFar_Casualty", BattlefieldLayout.ErlanFarPoint, 170f), player = Anchor("Spot_OpenFar_Player", BattlefieldLayout.DoorwayCentre + new Vector3(-0.8f, 0f, 0.2f), 0f) },
            };
            d.menuPlayerSpot = Anchor("Spot_Menu_Player", new Vector3(-3.9f, 0.04f, 1.2f), 270f);
            var boardGo = new GameObject("MenuBoard");
            boardGo.transform.SetPositionAndRotation(new Vector3(a0.x + BattlefieldLayout.WallThickness * 0.5f + 0.06f, 1.3f, 1.2f), Quaternion.LookRotation(Vector3.left));
            d.board = boardGo.AddComponent<MenuBoard>();

            var mine = new GameObject("MinePath");
            mine.transform.SetParent(root, false);
            var mp = mine.AddComponent<MinePath>();
            mp.points = new[] { new Vector3(-0.3f, 0f, -2.6f), new Vector3(-0.6f, 0f, -3.8f), new Vector3(-0.4f, 0f, -5.0f) };
            mine.SetActive(false);
            d.minePath = mp;

            var radio = new GameObject("Radio");
            radio.AddComponent<RadioSet>().head = eye;
            var handL = GameObject.Find("Hand_L");
            if (handL != null)
            {
                var w = new GameObject("WristWatch");
                w.transform.SetParent(handL.transform, false);
                w.AddComponent<WristWatch>().hand = handL.transform;
            }
        }

        static void BuildLocomotion(GameObject rig)
        {
            var eye = MainSceneBuilder.FindDeep(rig.transform, "CenterEyeAnchor");
            var loco = rig.AddComponent<Locomotion>();
            loco.head = eye;
            loco.aimHand = MainSceneBuilder.FindDeep(rig.transform, "RightHandAnchor");
            int cover = LayerMask.NameToLayer(CoverMath.CoverLayerName);
            loco.blockMask = (1 << 0) | (cover >= 0 ? 1 << cover : 0);
            loco.groundMask = 1 << 0;
            var arcGo = new GameObject("TeleportArc");
            arcGo.transform.SetParent(rig.transform, false);
            var lr = arcGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.018f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.sharedMaterial = AtmosphereBuilder.FxMat("FX_TeleportArc", Color.white, true, 1.2f);
            loco.arcLine = lr;
            var ret = new GameObject("TeleportReticle");
            ret.transform.SetParent(rig.transform, false);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(ret.transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = Vector3.one * 0.6f;
            q.GetComponent<Renderer>().sharedMaterial = AtmosphereBuilder.FxMat("FX_TeleportRing", new Color(0.8f, 0.95f, 1f, 0.8f), true, 1.5f);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            loco.reticle = ret.transform;
            var stance = rig.AddComponent<PlayerStance>();
            stance.head = eye;
            stance.rigRoot = rig.transform;
            stance.groundMask = 1 << 0;
            ComfortSettings.TeleportEnabled = false; // V1 left-stick step teleport off: the left stick moves smoothly
        }

        public const string CombinedDir = "Assets/_Licensed/_Combined"; // merged licensed geometry stays git-ignored

        /// <summary>Draw-call budget (GDD_V2 §7 ≤ 150): sandbags, scattered debris, far rocks, wire pickets and
        /// concertina coils are many small renderers sharing few materials → merged per material into a handful of
        /// static meshes (LOD1 geometry for the small licensed pieces). Original renderers are disabled; their
        /// colliders stay (sandbags are cover).</summary>
        static void CombineSmallStatics()
        {
            Directory.CreateDirectory(CombinedDir);
            foreach (var dir in new[] { CombinedDir, MeshDir })
                foreach (var f in Directory.GetFiles(dir, "Combined_*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            int before = 0, after = 0;
            void Merge(string name, IEnumerable<GameObject> roots, int lod, bool licensed, bool castShadows)
            {
                var byMat = new Dictionary<Material, List<CombineInstance>>();
                foreach (var go in roots)
                {
                    if (go == null) continue;
                    foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) { lg.enabled = false; }
                    var mrs = go.GetComponentsInChildren<MeshRenderer>(true);
                    int maxLod = 0;
                    foreach (var r in mrs) for (int l = 1; l <= 3; l++) if (r.name.EndsWith("_LOD" + l)) maxLod = Mathf.Max(maxLod, l);
                    int want = Mathf.Min(lod, maxLod);
                    foreach (var r in mrs)
                    {
                        if (!r.enabled) continue;
                        string n = r.name;
                        bool isLod = n.Contains("_LOD");
                        bool take = !isLod || n.EndsWith("_LOD" + want);
                        r.enabled = false;
                        before++;
                        if (!take) continue;
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var mats = r.sharedMaterials;
                        for (int sm = 0; sm < mf.sharedMesh.subMeshCount && sm < mats.Length; sm++)
                        {
                            if (mats[sm] == null) continue;
                            if (!byMat.TryGetValue(mats[sm], out var list)) byMat[mats[sm]] = list = new List<CombineInstance>();
                            list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sm, transform = r.transform.localToWorldMatrix });
                        }
                    }
                }
                var parent = new GameObject("Combined_" + name).transform;
                parent.SetParent(_root, false);
                int i = 0;
                foreach (var kv in byMat)
                {
                    var m = new Mesh { name = $"Combined_{name}_{i}", indexFormat = IndexFormat.UInt32 };
                    m.CombineMeshes(kv.Value.ToArray(), true, true);
                    m.RecalculateBounds();
                    string dir = licensed ? CombinedDir : MeshDir;
                    string path = $"{dir}/{m.name}.asset";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(m, path);
                    var g = new GameObject(m.name);
                    g.transform.SetParent(parent, false);
                    g.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = g.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    i++; after++;
                }
            }
            GameObject[] Named(string prefix) => _root.GetComponentsInChildren<Transform>(true).Where(t => t.name == prefix || t.name.StartsWith(prefix + " ")).Select(t => t.gameObject).ToArray();
            Merge("Sandbags", Named("burlap_sacks"), 1, true, false);
            Merge("Debris", new[] { "brick_ruins_a", "brick_ruins_b", "concrete_chunk" }.SelectMany(Named), 1, true, false);
            Merge("Rocks", new[] { "namaqualand_boulder_02", "sand_rocks_small_01" }.SelectMany(Named), 2, true, false);
            Merge("WireFrames", Named("barbed_wire_fence"), 0, true, false);
            Merge("Concertina", _root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Concertina_")).Select(t => t.gameObject), 0, false, false);
            // (LOD-managed props/rubble/hedgehogs stay separate: merging them dropped their LOD/culling and cost ~+120k tris)
            Debug.Log($"[FMVR] combined small statics: {before} renderers -> {after} meshes");
        }

        /// <summary>Quest budget: only big silhouettes (walls, vehicles, buildings, barrier lines, pipe, pillar, debris
        /// mounds, the flag) cast into the single shadow map; sacks, small debris, props, rocks, wire and grass don't.</summary>
        static void LimitShadowCasters()
        {
            string[] big = { "HouseA_", "HouseB_", "HouseC_", "arx_apc", "car_wreck", "pinzgauer", "m1a2", "unfinished_building", "derelict_concrete",
                             "concrete_blockade", "concrete_barriers_lowpoly", "concrete_pipe", "pillar_bullet_holes", "bricks_debris_pile", "Pole", "Cloth", "czech_hedgehog" };
            int on = 0, off = 0;
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                bool cast = false;
                for (var t = r.transform; t != null && !cast; t = t.parent)
                    foreach (var b in big) if (t.name.StartsWith(b)) { cast = true; break; }
                if (r.GetComponentInParent<CoverPoint>() != null) cast = false;
                // sacks are children of the barriers: never cast
                for (var t = r.transform; t != null; t = t.parent) if (t.name.StartsWith("burlap_sacks")) { cast = false; break; }
                r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (cast) on++; else off++;
            }
            Debug.Log($"[FMVR] shadow casters: {on} on, {off} off");
        }

        // ================================================================== LODs, occlusion, bookmarks
        /// <summary>Tighter LOD switches for small debris (8k-triangle brick scans must not stay LOD0 at 20 m).</summary>
        static void TuneLods()
        {
            foreach (var lg in _root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lg.GetLODs();
                if (lods.Length < 2) continue;
                float size = lg.size * lg.transform.lossyScale.x;
                // big pieces keep the import defaults; small ones drop detail early and cull when tiny
                float k = size < 1.2f ? 2.2f : size < 3f ? 1.4f : 1f;
                for (int i = 0; i < lods.Length; i++)
                {
                    float h = lods[i].screenRelativeTransitionHeight * k;
                    if (i == lods.Length - 1) h = Mathf.Max(h, size < 1.2f ? 0.012f : 0.004f);
                    lods[i].screenRelativeTransitionHeight = Mathf.Clamp(h, 0.002f, i == 0 ? 0.9f : lods[i - 1].screenRelativeTransitionHeight - 0.01f);
                }
                lg.SetLODs(lods);
            }
        }

        static void BakeOcclusion()
        {
            var area = new GameObject("OcclusionArea_Position");
            area.transform.SetParent(_root, false);
            var oa = area.AddComponent<OcclusionArea>();
            oa.center = new Vector3(0f, 4f, -12f);
            oa.size = new Vector3(90f, 10f, 100f);
            StaticOcclusionCulling.smallestOccluder = 2.5f;
            StaticOcclusionCulling.smallestHole = 0.3f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            var t0 = System.DateTime.Now;
            bool ok = StaticOcclusionCulling.Compute();
            Debug.Log($"[FMVR] occlusion bake {(ok ? "ok" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:F0} s");
        }

        static void BuildBookmarks()
        {
            var root = new GameObject(ScreenshotTool.BookmarkRoot).transform;
            var fall = BattlefieldLayout.ErlanFallPoint;
            float k = BattlefieldLayout.KneelEyeHeight;
            MainSceneBuilder.Bookmark(root, "v2_w2_overview", new Vector3(-26f, 17f, -30f), new Vector3(0f, 0f, 6f), 60f);
            MainSceneBuilder.Bookmark(root, "v2_w2_ruins", new Vector3(7.5f, 1.7f, 9.5f), new Vector3(-2.5f, 1.2f, 1.5f), 62f);
            MainSceneBuilder.Bookmark(root, "v2_w2_cover_view", new Vector3(-3.2f, k, 5.75f), new Vector3(-2.2f, 1.25f, 30f), 75f);
            MainSceneBuilder.Bookmark(root, "v2_w2_enemy", new Vector3(-3.85f, 1.62f, 3.55f), new Vector3(0f, 2f, 60f), 72f);
            MainSceneBuilder.Bookmark(root, "v2_w2_flag", new Vector3(-2.5f, 1.2f, 9.5f), new Vector3(-6.6f, 3.6f, 4.9f), 62f);
            MainSceneBuilder.Bookmark(root, "v2_w2_drag", BattlefieldLayout.DoorwayCentre + new Vector3(0.7f, 1.55f, -0.6f), fall + Vector3.up * 0.6f, 70f);
            MainSceneBuilder.Bookmark(root, "v2_w2_lz", BattlefieldLayout.LandingZone + new Vector3(-7f, 1.7f, 9f), BattlefieldLayout.LandingZone + new Vector3(1f, 2.2f, 0f), 66f);
            MainSceneBuilder.Bookmark(root, "v2_w2_inside", new Vector3(1.0f, 1.6f, -1.2f), new Vector3(-5f, 1.0f, 3.2f), 75f);
        }
    }
}
