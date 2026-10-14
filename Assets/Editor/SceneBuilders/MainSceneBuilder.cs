using System.Collections.Generic;
using System.IO;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using FieldMedic.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Generates Assets/Scenes/Main.unity: OVRCameraRig (floor origin) + desktop simulator, two hands
    /// (HandRig + HandGrabber + placeholder glove), a night trench placeholder lit by the moon, and a few
    /// grabbable / pressable test props. Idempotent вЂ” the scene is rebuilt from scratch every run.
    /// Batch: -executeMethod FieldMedic.EditorTools.SceneBuilders.MainSceneBuilder.Build
    /// </summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string MaterialDir = "Assets/Materials/Placeholder";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string HandPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";


        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("FieldMedic/Scenes/Build Main Scene")]
        public static void Build()
        {
            Mats.Clear();
            Directory.CreateDirectory(MaterialDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupEnvironment();
            var moon = BuildMoon();
            TrenchBuilder.Build();
            var rig = BuildRig(out var sim);
            BuildHands(rig, sim);
            BuildProps();
            BuildCasualty();
            BuildTreatment(rig);
            AtmosphereBuilder.Build(rig, moon);
            BuildBookmarks();

            RenderSettings.sun = moon;
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[FMVR] Main scene built: " + ScenePath);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- environment
        static void SetupEnvironment()
        {
            RenderSettings.skybox = AtmosphereBuilder.SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.035f, 0.045f, 0.075f);
            RenderSettings.ambientEquatorColor = new Color(0.025f, 0.025f, 0.03f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.009f, 0.008f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.35f; // night sky reflections greyed the field at grazing angles
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.032f;
            RenderSettings.fogColor = new Color(0.028f, 0.034f, 0.055f);
        }

        static Light BuildMoon()
        {
            var go = new GameObject("Moon");
            go.transform.rotation = Quaternion.Euler(22f, -35f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.55f, 0.65f, 0.95f);
            l.intensity = 0.22f;
            l.shadows = LightShadows.Hard; // the only shadow caster (steered to flares by NightLightingDirector)
            l.shadowStrength = 0.9f;
            l.shadowBias = 0.06f;
            l.shadowNormalBias = 0.5f;
            return l;
        }

        // ---------------------------------------------------------------- rig + hands
        static GameObject BuildRig(out DesktopSimulator sim)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            GameObject rig;
            if (prefab != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else
            {
                Debug.LogWarning("[FMVR] OVRCameraRig prefab not found, building a plain camera rig");
                rig = new GameObject("OVRCameraRig");
                var ts = new GameObject("TrackingSpace").transform;
                ts.SetParent(rig.transform, false);
                var eye = new GameObject("CenterEyeAnchor", typeof(Camera), typeof(AudioListener));
                eye.transform.SetParent(ts, false);
                foreach (var n in new[] { "LeftHandAnchor", "RightHandAnchor" })
                    new GameObject(n).transform.SetParent(ts, false);
            }
            rig.name = "OVRCameraRig";
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var mgr = rig.GetComponent<OVRManager>();
            if (mgr != null)
            {
                var so = new SerializedObject(mgr);
                var p = so.FindProperty("_trackingOriginType");
                if (p != null) p.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var eyeT = FindDeep(rig.transform, "CenterEyeAnchor");
            var cam = eyeT.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 120f;
            cam.tag = "MainCamera";

            sim = rig.AddComponent<DesktopSimulator>();
            sim.rigRoot = rig.transform;
            sim.pitchPivot = FindDeep(rig.transform, "TrackingSpace");
            sim.head = eyeT;
            return rig;
        }

        static void BuildHands(GameObject rig, DesktopSimulator sim)
        {
            var hands = new GameObject("Hands").transform;
            var glove = Mat("Glove", new Color(0.12f, 0.12f, 0.1f), 0.2f);
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefab);

            foreach (var h in new[] { Handedness.Left, Handedness.Right })
            {
                bool left = h == Handedness.Left;
                var anchor = FindDeep(rig.transform, left ? "LeftHandAnchor" : "RightHandAnchor");

                OVRHand ovrHand = null;
                if (handPrefab != null && anchor != null)
                {
                    var hp = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, anchor);
                    hp.name = left ? "OVRHand_L" : "OVRHand_R";
                    foreach (var c in hp.GetComponents<MonoBehaviour>())
                    {
                        var so = new SerializedObject(c);
                        foreach (var prop in new[] { "HandType", "_skeletonType", "_meshType" })
                        {
                            var p = so.FindProperty(prop);
                            if (p != null) p.intValue = left ? 0 : 1;
                        }
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    ovrHand = hp.GetComponent<OVRHand>();
                }

                var go = new GameObject(left ? "Hand_L" : "Hand_R");
                go.transform.SetParent(hands, false);
                float side = left ? -1f : 1f;
                go.transform.localPosition = new Vector3(0.18f * side, 1.30f, 0.36f);
                go.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                var hr = go.AddComponent<HandRig>();
                hr.handedness = h;
                hr.ovrAnchor = anchor;
                hr.ovrHand = ovrHand;
                hr.desktop = sim;
                go.AddComponent<HandGrabber>();
                BuildGloveVisual(go.transform, side, glove);
                go.AddComponent<BloodOnHands>(); // finds the runtime glove mesh itself
                go.AddComponent<HandTreatment>(); // sweep, pulse, pressure, nitrile gloves
            }
        }

        /// <summary>Procedural skinned tactical glove (built at runtime by <see cref="GloveHand"/>; fingers follow grip/trigger).</summary>
        static void BuildGloveVisual(Transform hand, float side, Material m)
        {
            var v = new GameObject("GloveVisual").transform;
            v.SetParent(hand, false);
            var g = v.gameObject.AddComponent<GloveHand>();
            g.side = side;
            g.material = m;
        }

        // ---------------------------------------------------------------- props
        static void BuildProps()
        {
            var root = new GameObject("TestProps").transform;
            root.position = new Vector3(0f, 0f, -0.34f); // the S4 trench is 1.2 m wide: keep the crate off the wall
            var crateM = Mat("Crate", new Color(0.27f, 0.30f, 0.18f), 0.15f);
            var metal = Mat("Metal", new Color(0.22f, 0.24f, 0.2f), 0.55f, 0.6f);
            var cloth = Mat("Canvas", new Color(0.30f, 0.29f, 0.2f), 0.05f);
            var red = Mat("ButtonRed", new Color(0.55f, 0.07f, 0.05f), 0.5f);

            // crate as a small work surface in front of the player
            var crate = Box("SupplyCrate", root, new Vector3(0.35f, 0.25f, 0.62f), new Vector3(0.7f, 0.5f, 0.42f), crateM);
            crate.transform.localRotation = Quaternion.Euler(0f, -8f, 0f);
            crate.isStatic = true;
            float top = 0.5f;

            var cube = Box("GrabCube", root, new Vector3(0.18f, top + 0.045f, 0.58f), Vector3.one * 0.08f, metal);
            MakeGrabbable(cube, 0.4f);

            var canteen = Prim(PrimitiveType.Cylinder, "Canteen", root, new Vector3(0.36f, top + 0.09f, 0.68f),
                new Vector3(0.09f, 0.09f, 0.09f), cloth);
            MakeGrabbable(canteen, 0.9f);

            // chemlight: grabbable + the scene's small warm-ish fill light
            var chem = Prim(PrimitiveType.Cylinder, "Chemlight", root, new Vector3(0.55f, top + 0.012f, 0.55f),
                new Vector3(0.022f, 0.075f, 0.022f), UnlitMat("ChemlightGlow", new Color(0.45f, 1f, 0.45f)));
            chem.transform.localRotation = Quaternion.Euler(0f, 30f, 90f);
            MakeGrabbable(chem, 0.05f);
            var lgo = new GameObject("ChemlightGlow");
            lgo.transform.SetParent(chem.transform, false);
            var pl = lgo.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(0.45f, 1f, 0.45f);
            pl.intensity = 0.35f;
            pl.range = 1.6f;
            pl.shadows = LightShadows.None;

            // heavy ammo box on the floor: two handles, lags behind the hand (mass feel)
            var ammo = Box("AmmoBox_Heavy", root, new Vector3(-0.55f, 0.13f, 0.55f), new Vector3(0.42f, 0.26f, 0.2f), metal);
            var g = MakeGrabbable(ammo, 12f);
            g.priority = 0;
            for (int s = -1; s <= 1; s += 2)
            {
                var hp = new GameObject(s < 0 ? "Handle_L" : "Handle_R");
                hp.transform.SetParent(ammo.transform, false);
                hp.transform.localPosition = new Vector3(s * 0.36f, 0.62f, 0f); // on top, near each end
                var gp = hp.AddComponent<GrabPoint>();
                gp.radius = 0.09f;
                gp.snap = false;
                Box("HandleBar", hp.transform, Vector3.zero, new Vector3(0.11f, 0.025f, 0.025f), metal, collider: false)
                    .transform.localScale = new Vector3(0.11f / 0.42f, 0.025f / 0.26f, 0.025f / 0.2f);
            }

            // push button on the crate front (Pressable test)
            var btnBase = new GameObject("TestButton");
            btnBase.transform.SetParent(root, false);
            // Pokeable surface = top of the cap; the cap (24 mm tall) sits below it on the crate
            btnBase.transform.localPosition = new Vector3(0.52f, top + 0.024f, 0.75f);
            var cap = Prim(PrimitiveType.Cylinder, "Cap", btnBase.transform, new Vector3(0f, -0.012f, 0f),
                new Vector3(0.045f, 0.012f, 0.045f), red);
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            var press = btnBase.AddComponent<Pressable>();
            press.size = new Vector2(0.05f, 0.05f);
            press.maxDepth = 0.012f;
            press.cap = cap.transform;
        }

        static Grabbable MakeGrabbable(GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<GrabHighlight>();
            return go.AddComponent<Grabbable>();
        }

        // ---------------------------------------------------------------- casualty
        public static readonly Vector3 ErlanPosition = new Vector3(1.9f, 0f, -0.15f);

        static void BuildCasualty()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CasualtyAssetBuilder.PrefabPath);
            if (prefab == null) { Debug.LogWarning("[FMVR] Erlan prefab missing - run CasualtyAssetBuilder.BuildAll"); return; }
            var erlan = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            erlan.name = "Erlan";
            // crouched at the front wall, watching the field (+Z), 2 m to the player's right
            erlan.transform.SetPositionAndRotation(ErlanPosition, Quaternion.Euler(0f, -25f, 0f));
        }

        // ---------------------------------------------------------------- treatment (S3)
        /// <summary>Player IFAK on the belt (body-locked), cover zone in the dugout corner, desktop feedback HUD.</summary>
        static void BuildTreatment(GameObject rig)
        {
            var pouch = new GameObject("PlayerIFAK");
            pouch.transform.position = new Vector3(0f, 0.95f, 0.25f);
            var ifak = pouch.AddComponent<IfakPouch>();
            ifak.bodyLocked = true;
            ifak.head = FindDeep(rig.transform, "CenterEyeAnchor");

            var cover = new GameObject("CoverZone_Dugout");
            cover.transform.position = TrenchBuilder.DugoutCentre + new Vector3(0f, 0.9f, 0f);
            cover.AddComponent<CoverZone>().size = new Vector3(3.0f, 1.9f, 1.9f);

            new GameObject("FeedbackHud").AddComponent<FeedbackHud>();
            BuildScenarioSystem(rig, cover.GetComponent<CoverZone>());
        }

        // ---------------------------------------------------------------- scenarios (S5)
        /// <summary>Scenario director + spot anchors, the menu/debrief board on the dugout end wall, the radio on
        /// the vest, the watch on the left wrist and the S3 mine-path footprints in the communication trench.</summary>
        static void BuildScenarioSystem(GameObject rig, CoverZone cover)
        {
            var root = new GameObject("Scenarios").transform;
            var d = root.gameObject.AddComponent<ScenarioDirector>();
            var erlan = GameObject.Find("Erlan");
            d.casualty = erlan != null ? erlan.GetComponent<FieldMedic.Casualty.CasualtyController>() : null;
            d.playerRig = rig.transform;
            d.head = FindDeep(rig.transform, "CenterEyeAnchor");

            Transform Anchor(string name, Vector3 pos, float yaw)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root, false);
                t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                return t;
            }
            d.spots = new[]
            {
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.FireBay, casualty = Anchor("Spot_FireBay_Casualty", ErlanPosition, 90f), player = Anchor("Spot_FireBay_Player", new Vector3(0.2f, 0f, -0.1f), 60f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.CommTrench, casualty = Anchor("Spot_Comm_Casualty", new Vector3(-2.95f, 0f, -4.6f), 168f), player = Anchor("Spot_Comm_Player", new Vector3(-3.1f, 0f, -2.3f), 175f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.FarBay, casualty = Anchor("Spot_Far_Casualty", new Vector3(3.6f, 0f, 0.05f), 90f), player = Anchor("Spot_Far_Player", new Vector3(2.7f, 0f, -0.1f), 90f) },
                new ScenarioDirector.SpotAnchor { spot = FieldMedic.Medical.CasualtySpot.Dugout, casualty = Anchor("Spot_Dugout_Casualty", new Vector3(-8.0f, 0.05f, -1.55f), 90f), player = Anchor("Spot_Dugout_Player", new Vector3(-7.1f, 0f, -1.2f), 255f) },
            };
            d.menuPlayerSpot = Anchor("Spot_Menu_Player", new Vector3(-8.25f, 0f, -1.5f), 270f);

            var boardGo = new GameObject("MenuBoard");
            boardGo.transform.SetPositionAndRotation(new Vector3(-9.5f, 1.12f, -1.5f), Quaternion.LookRotation(Vector3.left));
            d.board = boardGo.AddComponent<MenuBoard>();

            var mine = new GameObject("MinePath");
            mine.transform.SetParent(root, false);
            var mp = mine.AddComponent<MinePath>();
            mp.points = new[] { new Vector3(-3.1f, 0f, -2.1f), new Vector3(-3.2f, 0f, -3.2f), new Vector3(-3.0f, 0f, -4.2f) };
            mine.SetActive(false);
            d.minePath = mp;

            var radio = new GameObject("Radio");
            var rs = radio.AddComponent<RadioSet>();
            rs.head = d.head;

            var handL = GameObject.Find("Hand_L");
            if (handL != null)
            {
                var w = new GameObject("WristWatch");
                w.transform.SetParent(handL.transform, false);
                w.AddComponent<WristWatch>().hand = handL.transform;
            }
        }

        static Material UnlitMat(string name, Color c)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------- camera bookmarks
        static void BuildBookmarks()
        {
            var root = new GameObject(ScreenshotTool.BookmarkRoot).transform;
            // over-the-shoulder view of the player's spot, crate and props
            Bookmark(root, "s1_main", new Vector3(-0.9f, 1.75f, -0.75f), new Vector3(0.15f, 0.55f, 0.62f), 62f);
            Bookmark(root, "s1_props", new Vector3(0.05f, 1.05f, 0.05f), new Vector3(0.3f, 0.52f, 0.64f), 55f);
            Bookmark(root, "s1_trench", new Vector3(-4.2f, 1.6f, 0f), new Vector3(2f, 0.6f, 0.2f), 60f);
            Bookmark(root, "s5_menu", new Vector3(-8.25f, 1.45f, -1.5f), new Vector3(-9.5f, 1.12f, -1.5f), 62f);
            Bookmark(root, "s2_erlan_idle", ErlanPosition + new Vector3(-1.5f, 1.3f, -0.6f), ErlanPosition + new Vector3(0f, 0.65f, 0f), 50f);
        }

        static void Bookmark(Transform root, string name, Vector3 pos, Vector3 lookAt, float fov)
        {
            var b = new GameObject(name);
            b.transform.SetParent(root, false);
            b.transform.position = pos;
            b.transform.rotation = Quaternion.LookRotation(lookAt - pos);
            b.AddComponent<ScreenshotBookmark>().fieldOfView = fov;
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Build scenes: Boot (tiny, renders at once, loads Main async) first, then Main.</summary>
        public static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = File.Exists(BootSceneBuilder.ScenePath)
                ? new[] { new EditorBuildSettingsScene(BootSceneBuilder.ScenePath, true), new EditorBuildSettingsScene(ScenePath, true) }
                : new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m,
            Quaternion? rot = null, bool collider = true)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, pos, size, m);
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        public static Material Mat(string name, Color c, float smoothness, float metallic = 0f)
        {
            if (Mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Material Emissive(string name, Color c, float intensity)
        {
            var m = Mat(name, c, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
