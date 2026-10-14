using FieldMedic.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Generates Assets/Scenes/Boot.unity — the first scene of the APK: OVRCameraRig (floor origin), black
    /// background, title "БАУЫР" + loading line, <see cref="BootLoader"/> that loads Main asynchronously.
    /// Batch: -executeMethod FieldMedic.EditorTools.SceneBuilders.BootSceneBuilder.BuildBatch
    /// </summary>
    public static class BootSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Boot.unity";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        public static void BuildBatch()
        {
            Build();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("FieldMedic/Scenes/Build Boot Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "OVRCameraRig";
            var mgr = rig.GetComponent<OVRManager>();
            if (mgr != null)
            {
                var so = new SerializedObject(mgr);
                var p = so.FindProperty("_trackingOriginType");
                if (p != null) p.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Transform head = null;
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 20f;
                if (cam.name == "CenterEyeAnchor") head = cam.transform;
            }

            var panel = new GameObject("BootPanel").transform;
            panel.position = new Vector3(0f, 1.5f, 2.2f);
            var white = new Color(0.93f, 0.92f, 0.88f);
            WorldUI.Text(panel, "Title", "БАУЫР", new Vector3(0f, 0.22f, 0f), 0.16f, white, TextAnchor.MiddleCenter, TextAlignment.Center);
            WorldUI.Text(panel, "Subtitle", "Field Medic VR · TCCC", new Vector3(0f, 0.02f, 0f), 0.05f,
                new Color(0.7f, 0.7f, 0.66f), TextAnchor.MiddleCenter, TextAlignment.Center);
            var status = WorldUI.Text(panel, "Status", "Загрузка / Жүктелуде…", new Vector3(0f, -0.14f, 0f), 0.045f,
                new Color(0.75f, 0.8f, 0.72f), TextAnchor.MiddleCenter, TextAlignment.Center);

            var boot = new GameObject("BootLoader").AddComponent<BootLoader>();
            boot.head = head;
            boot.panel = panel;
            boot.status = status;
            boot.nextScene = "Main";

            EditorSceneManager.SaveScene(scene, ScenePath);
            MainSceneBuilder.SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[FMVR] Boot scene built: " + ScenePath);
        }
    }
}
