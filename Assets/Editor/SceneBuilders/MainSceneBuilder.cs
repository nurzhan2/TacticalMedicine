using System.Collections.Generic;
using System.IO;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
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

        // trench geometry (metres): floor at y=0, runs along X, player stands at the origin facing +Z
        const float TrenchHalfWidth = 1.1f;
        const float TrenchDepth = 1.45f;
        const float TrenchHalfLength = 9f;

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("FieldMedic/Scenes/Build Main Scene")]
        public static void Build()
        {
            Mats.Clear();
            Directory.CreateDirectory(MaterialDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupEnvironment();
            var moon = BuildMoon();
            BuildTrench();
            var rig = BuildRig(out var sim);
            BuildHands(rig, sim);
            BuildProps();
            BuildCasualty();
            BuildTreatment(rig);
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
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.20f, 0.30f);
            RenderSettings.ambientEquatorColor = new Color(0.13f, 0.12f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.035f, 0.03f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.fogColor = new Color(0.05f, 0.065f, 0.10f);
        }

        static Light BuildMoon()
        {
            var go = new GameObject("Moon");
            go.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.66f, 0.74f, 0.95f);
            l.intensity = 0.75f;
            l.shadows = LightShadows.Hard;
            l.shadowStrength = 0.85f;
            return l;
        }

        // ---------------------------------------------------------------- trench
        static void BuildTrench()
        {
            var root = new GameObject("Trench").transform;
            var dirt = Mat("Dirt", new Color(0.30f, 0.22f, 0.14f), 0.08f);
            var dirtDark = Mat("DirtDark", new Color(0.22f, 0.16f, 0.11f), 0.15f);
            var mud = Mat("Mud", new Color(0.16f, 0.12f, 0.08f), 0.5f);
            var wood = Mat("Wood", new Color(0.33f, 0.24f, 0.15f), 0.12f);
            var bag = Mat("Sandbag", new Color(0.42f, 0.38f, 0.27f), 0.05f);

            float L = TrenchHalfLength * 2f;
            Box("Floor", root, new Vector3(0f, -0.1f, 0f), new Vector3(L, 0.2f, TrenchHalfWidth * 2f), mud);

            // walls + the ground surface beyond them
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (TrenchHalfWidth + 0.25f);
                Box(side > 0 ? "Wall_Front" : "Wall_Back", root, new Vector3(0f, TrenchDepth * 0.5f, z),
                    new Vector3(L, TrenchDepth, 0.5f), dirtDark);
                Box(side > 0 ? "Ground_Front" : "Ground_Back", root,
                    new Vector3(0f, TrenchDepth - 0.1f, side * (TrenchHalfWidth + 0.5f + 12f)),
                    new Vector3(60f, 0.2f, 24f), dirt);

                // plank revetment with posts
                float wz = side * (TrenchHalfWidth - 0.02f);
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.18f + i * 0.3f;
                    Box("Plank", root, new Vector3(0f, y, wz), new Vector3(L, 0.26f, 0.04f), wood,
                        Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 0.25f : -0.3f)), collider: false);
                }
                for (float x = -TrenchHalfLength + 0.6f; x < TrenchHalfLength; x += 1.8f)
                    Box("Post", root, new Vector3(x, TrenchDepth * 0.5f, wz - side * 0.05f),
                        new Vector3(0.1f, TrenchDepth + 0.1f, 0.1f), wood, collider: false);

                // sandbag parapet: two staggered rows
                for (int row = 0; row < 2; row++)
                for (float x = -TrenchHalfLength; x < TrenchHalfLength; x += 0.52f)
                {
                    float jitter = Mathf.Sin(x * 7.3f + row * 2.1f + side) * 0.03f;
                    var p = new Vector3(x + row * 0.26f, TrenchDepth + 0.07f + row * 0.13f, side * (TrenchHalfWidth + 0.22f) + jitter);
                    Sandbag(root, p, Quaternion.Euler(0f, jitter * 120f, jitter * 30f), bag);
                }
            }

            // end caps so the desktop player cannot walk out
            for (int end = -1; end <= 1; end += 2)
                Box("Wall_End", root, new Vector3(end * (TrenchHalfLength + 0.25f), TrenchDepth * 0.5f, 0f),
                    new Vector3(0.5f, TrenchDepth, TrenchHalfWidth * 2f + 1f), dirtDark);

            // duckboards on the floor
            for (float x = -TrenchHalfLength + 0.5f; x < TrenchHalfLength; x += 1.25f)
            {
                var db = new GameObject("Duckboard").transform;
                db.SetParent(root, false);
                db.localPosition = new Vector3(x, 0.03f, 0f);
                db.localRotation = Quaternion.Euler(0f, Mathf.Sin(x) * 2f, 0f);
                for (int s = -1; s <= 1; s += 2)
                    Box("Rail", db, new Vector3(0f, 0f, s * 0.28f), new Vector3(1.15f, 0.05f, 0.06f), wood, collider: false);
                for (float sx = -0.5f; sx <= 0.51f; sx += 0.2f)
                    Box("Slat", db, new Vector3(sx, 0.035f, 0f), new Vector3(0.12f, 0.02f, 0.72f), wood, collider: false);
            }
        }

        static void Sandbag(Transform parent, Vector3 pos, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Sandbag";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot * Quaternion.Euler(0f, 0f, 90f);
            go.transform.localScale = new Vector3(0.16f, 0.27f, 0.3f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
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
            cam.clearFlags = CameraClearFlags.SolidColor;
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
                var boh = go.AddComponent<BloodOnHands>();
                boh.gloves = go.GetComponentsInChildren<Renderer>();
                go.AddComponent<HandTreatment>(); // sweep, pulse, pressure, nitrile gloves
            }
        }

        /// <summary>Low-poly placeholder glove (palm + four fingers + thumb) until the real hand mesh lands.</summary>
        static void BuildGloveVisual(Transform hand, float side, Material m)
        {
            var v = new GameObject("GloveVisual").transform;
            v.SetParent(hand, false);
            Box("Palm", v, new Vector3(0f, 0f, 0.035f), new Vector3(0.082f, 0.028f, 0.09f), m, collider: false);
            Box("Cuff", v, new Vector3(0f, 0f, -0.035f), new Vector3(0.07f, 0.045f, 0.06f), m, collider: false);
            for (int i = 0; i < 4; i++)
            {
                float x = (-0.03f + i * 0.02f) * side;
                float len = i == 1 || i == 2 ? 0.075f : 0.062f;
                Box("Finger", v, new Vector3(x, -0.006f, 0.08f + len * 0.5f), new Vector3(0.017f, 0.017f, len), m,
                    Quaternion.Euler(14f, 0f, 0f), collider: false);
            }
            Box("Thumb", v, new Vector3(-0.05f * side, -0.008f, 0.05f), new Vector3(0.018f, 0.018f, 0.055f), m,
                Quaternion.Euler(0f, -35f * side, 0f), collider: false);
        }

        // ---------------------------------------------------------------- props
        static void BuildProps()
        {
            var root = new GameObject("TestProps").transform;
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
            cover.transform.position = new Vector3(-7.4f, 0.8f, 0f);
            cover.AddComponent<CoverZone>().size = new Vector3(2.6f, 2f, 2.3f);

            new GameObject("FeedbackHud").AddComponent<FeedbackHud>();
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
            Bookmark(root, "s1_trench", new Vector3(-6f, 1.9f, 0f), new Vector3(2f, 0.6f, 0.2f), 60f);
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
        static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
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
