using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w0b toolchain probe: Assets/Scenes/Hello.unity — OVRCameraRig (floor origin), a grey floor, a cube and white
    /// "HELLO VR" text on a dark-blue background (blue, not black, so "the app runs" and "black screen" look different).
    /// Built into 1_HELLO.apk with exactly the same player/XR settings as the game.
    /// </summary>
    public static class HelloSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Hello.unity";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("FieldMedic/Scenes/Build Hello Scene (w0b)")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.fog = false;

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab));
            rig.name = "OVRCameraRig";
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.12f, 0.22f, 1f);
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<Renderer>().sharedMaterial = Mat("Assets/Data/Hello/HelloFloor.mat", unlit, new Color(0.45f, 0.45f, 0.45f));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube";
            cube.transform.position = new Vector3(0f, 1.1f, 1.6f);
            cube.transform.localScale = Vector3.one * 0.4f;
            cube.transform.rotation = Quaternion.Euler(20f, 35f, 0f);
            cube.GetComponent<Renderer>().sharedMaterial = Mat("Assets/Data/Hello/HelloCube.mat", Shader.Find("Universal Render Pipeline/Lit"), new Color(0.85f, 0.3f, 0.2f));
            var panel = new GameObject("Text").transform;
            panel.position = new Vector3(0f, 1.6f, 2f);
            FieldMedic.UI.WorldUI.Text(panel, "Hello", "HELLO VR", Vector3.zero, 0.18f, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center);
            FieldMedic.UI.WorldUI.Text(panel, "Sub", "Field Medic VR · toolchain test 1/3", new Vector3(0f, -0.2f, 0f), 0.05f, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter, TextAlignment.Center);

            SceneSplitter.Sanitize(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[FMVR] Hello scene built: " + ScenePath);
        }

        static Material Mat(string path, Shader sh, Color c)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
