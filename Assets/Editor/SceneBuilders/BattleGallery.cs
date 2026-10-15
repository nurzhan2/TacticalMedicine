using System.IO;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>Debug renders of the w3 battle assets in the Battlefield lighting (Logs/w3_gallery_*.jpg): soldier poses
    /// (run frames, standing aim, kneeling aim), vehicles with the turret/rotor turned, the jet, crowd meshes, the
    /// smoke grenade and the impostor sheet. -executeMethod FieldMedic.EditorTools.SceneBuilders.BattleGallery.Render</summary>
    public static class BattleGallery
    {
        /// <summary>Rebuild the Battlefield (with the battle) and render the gallery in one editor session.</summary>
        public static void BuildAndRender()
        {
            try { BattlefieldBuilder.Build(); BootSceneBuilder.Build(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] build failed: " + e); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
            Render();
        }

        public static void GroundFixDiag()
        {
            foreach (var p in GroundTextureFix.Ground) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            GroundTextureFix.Log();
            GroundDiag();
        }

        public static void CasterBisect()
        {
            EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            ScreenshotTool.JpgQuality = 80; ScreenshotTool.MedianOf = 1;
            QualitySettings.SetQualityLevel(0, true);
            var at = new Vector3(-3f, BattlefieldLayout.GroundHeight(-3f, -62f), -62f);
            Vector3 from = at + new Vector3(9f, 1.3f, -3.5f), look = at + new Vector3(1.5f, 0.9f, 0f);
            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var keep = all.ToDictionary(r => r, r => r.shadowCastingMode);
            var casters = all.Where(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off).ToArray();
            string Group(Renderer r) { var t = r.transform; var chain = new System.Collections.Generic.List<string>(); while (t != null) { chain.Insert(0, t.name); t = t.parent; } return chain.Count > 1 ? chain[0] + "/" + chain[1] : chain[0]; }
            var groups = casters.GroupBy(Group).ToArray();
            Debug.Log("[FMVR] casters: " + string.Join(", ", groups.Select(g => g.Key + ":" + g.Count())));
            foreach (var r in casters) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shot("w3_bisect_none", from, look, 50f);
            int i = 0;
            foreach (var g in groups)
            {
                foreach (var r in g) r.shadowCastingMode = keep[r];
                Shot($"w3_bisect_{i:00}", from, look, 50f);
                Debug.Log($"[FMVR] bisect {i:00} = {g.Key}");
                foreach (var r in g) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                i++;
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void GroundDiag()
        {
            EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            foreach (var c in Object.FindObjectsByType<FieldMedic.Casualty.CasualtyController>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            ScreenshotTool.JpgQuality = 90; ScreenshotTool.MedianOf = 1;
            var at = new Vector3(-3f, BattlefieldLayout.GroundHeight(-3f, -62f), -62f);
            Vector3 from = at + new Vector3(9f, 1.3f, -3.5f), look = at + new Vector3(1.5f, 0.9f, 0f);
            var pc = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var ssao = pc != null ? pc.rendererFeatures.FirstOrDefault(f => f != null && f.name.Contains("Ambient")) : null;
            QualitySettings.SetQualityLevel(1, true);
            int hidden = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterials.Any(m => m != null && m.name.ToLowerInvariant().Contains("grass"))) { Debug.Log("[FMVR] grass renderer " + r.name + " mats " + string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name + "/" + m.shader.name : "null")) + " bounds " + r.bounds.size); r.enabled = false; hidden++; }
            Debug.Log("[FMVR] diag hidden grass renderers: " + hidden);
            Shot("w3_diag_nograss", from, look, 50f);
            var ground = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial != null && r.sharedMaterial.name == "Bf_Ground").ToArray();
            Debug.Log("[FMVR] ground renderers: " + ground.Length);
            foreach (var r in ground) r.enabled = false;
            Shot("w3_diag_noground", from, look, 50f);
            foreach (var r in ground) r.enabled = true;
            var gm = ground.Length > 0 ? ground[0].sharedMaterial : null;
            if (gm != null)
            {
                float bump = gm.GetFloat("_BumpScale");
                gm.SetFloat("_BumpScale", 0f);
                Shot("w3_diag_nobump", from, look, 50f);
                gm.SetFloat("_BumpScale", bump);
                var sun = RenderSettings.sun;
                var sh = sun.shadows; sun.shadows = LightShadows.None;
                Shot("w3_diag_noshadow", from, look, 50f);
                sun.shadows = sh;
                foreach (var r in ground) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Shot("w3_diag_groundnocast", from, look, 50f);
                QualitySettings.SetQualityLevel(0, true);
                Shot("w3_diag_groundnocast_mobile", from, look, 50f);
                QualitySettings.SetQualityLevel(1, true);
                foreach (var r in ground) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            Shot("w3_diag_pc_ssao", from, look, 50f);
            if (ssao != null) { ssao.SetActive(false); Shot("w3_diag_pc_nossao", from, look, 50f); ssao.SetActive(true); }
            QualitySettings.SetQualityLevel(0, true);
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            bool hdr = rp.supportsHDR; rp.supportsHDR = true;
            Shot("w3_diag_mobile_hdr", from, look, 50f);
            rp.supportsHDR = hdr;
            Shot("w3_diag_mobile_ldr", from, look, 50f);
            QualitySettings.SetQualityLevel(1, true);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTextureFix.Ground[0]);
            foreach (int size in new[] { 512, 64 })
            {
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var t2 = new Texture2D(size, size, TextureFormat.RGB24, false);
                t2.ReadPixels(new Rect(0, 0, size, size), 0, 0); t2.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Logs/w3_diag_tex{size}.png", t2.EncodeToPNG());
                RenderTexture.ReleaseTemporary(rt);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Render()
        {
            bool ok = false;
            try
            {
                EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
                foreach (var c in Object.FindObjectsByType<FieldMedic.Casualty.CasualtyController>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
                Directory.CreateDirectory("Logs");
                ScreenshotTool.JpgQuality = 85;
                ScreenshotTool.MedianOf = 3;
                int q = QualitySettings.GetQualityLevel();
                QualitySettings.SetQualityLevel(1, true);
                var at = new Vector3(-3f, BattlefieldLayout.GroundHeight(-3f, -62f), -62f);
                var stage = new GameObject("~Gallery").transform;

                // soldiers
                var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(BattleAssets.LDir + "/EnemySoldier.prefab");
                var friend = AssetDatabase.LoadAssetAtPath<GameObject>(BattleAssets.LDir + "/FriendlySoldier.prefab");
                GameObject Soldier(GameObject pf, Vector3 p, float yaw, SoldierPose pose, float phase)
                {
                    var go = (GameObject)Object.Instantiate(pf, stage);
                    go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                    var poser = go.GetComponent<SoldierPoser>();
                    var rig = SoldierPoser.Rig.Capture(go.GetComponentInChildren<Animator>(), go.transform);
                    poser.rig = rig;
                    if (pose == SoldierPose.KneelAim)
                    {
                        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Erlan/Anims/mixamo_Rifle_Kneel_Idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
                        clip.SampleAnimation(go, clip.length * 0.3f);
                    }
                    string Dump(string tag) => $"{tag}: root {go.transform.position} hips {rig.hips.position} head {rig.head.position} lFoot {rig.lFoot.position} rHand {rig.rHand.position} hipsH {rig.hipsHeight:F2} handScale {rig.rHand.lossyScale} hipsRot {rig.hips.rotation.eulerAngles} spineRot {rig.spine.rotation.eulerAngles}";
                    Debug.Log("[FMVR] pose dbg " + pose + " " + Dump("before"));
                    SoldierPoser.Apply(rig, pose, phase, go.transform, poser.rifleAxisLocal, 0f, poser.rifleInLeftHand);
                    Debug.Log("[FMVR] pose dbg " + pose + " " + Dump("after"));
                    // muzzle marker
                    var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(m.GetComponent<Collider>());
                    m.transform.SetParent(stage, false);
                    m.transform.position = poser.Muzzle;
                    m.transform.localScale = Vector3.one * 0.06f;
                    return go;
                }
                if (enemy != null)
                {
                    for (int i = 0; i < 4; i++) Soldier(enemy, at + new Vector3(-3f + i * 1.3f, 0f, 0f), 90f, SoldierPose.Run, i * Mathf.PI / 2f);
                    Soldier(enemy, at + new Vector3(2.4f, 0f, 0f), 90f, SoldierPose.Aim, 0f);
                    Soldier(enemy, at + new Vector3(3.8f, 0f, 0f), 90f, SoldierPose.KneelAim, 0f);
                    if (friend != null) Soldier(friend, at + new Vector3(5.2f, 0f, 0f), 90f, SoldierPose.Aim, 0f);
                    Shot("w3_gallery_soldiers_side", at + new Vector3(1f, 1.1f, -7.5f), at + new Vector3(1f, 0.9f, 0f), 50f);
                    Shot("w3_gallery_soldiers_front", at + new Vector3(9f, 1.3f, -3.5f), at + new Vector3(1.5f, 0.9f, 0f), 50f);
                }

                // vehicles
                var v0 = at + new Vector3(0f, 0f, 22f);
                void Veh(string name, Vector3 p, float yaw, float turret)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{BattleAssets.LDir}/{name}.prefab");
                    if (pf == null) return;
                    var go = (GameObject)Object.Instantiate(pf, stage);
                    go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                    var part = go.transform.Find("Model/Part");
                    if (part != null) part.localRotation = Quaternion.Euler(0f, turret, 0f) * part.localRotation;
                }
                Veh("EnemyTank", v0 + new Vector3(-6f, 0f, 0f), 90f, -50f);
                Veh("EnemyIFV", v0 + new Vector3(6f, 0f, 0f), 90f, 40f);
                Shot("w3_gallery_vehicles", v0 + new Vector3(0f, 4f, -16f), v0 + new Vector3(0f, 1f, 0f), 55f);
                Veh("AttackHeli", v0 + new Vector3(0f, 0f, 30f), 90f, 30f);
                Veh("AttackJet", v0 + new Vector3(0f, 1.5f, 50f), 90f, 0f);
                Shot("w3_gallery_air", v0 + new Vector3(0f, 6f, 10f), v0 + new Vector3(0f, 2f, 40f), 55f);

                // crowd meshes + grenade
                var mat = AssetDatabase.LoadAssetAtPath<Material>(BattleAssets.Dir + "/Materials/Battle_Crowd.mat");
                var crowd = AssetDatabase.FindAssets("Crowd_ t:Mesh", new[] { BattleAssets.LDir + "/Meshes" }).Select(g => AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(m => m.name).ToArray();
                var c0 = at + new Vector3(0f, 0f, -12f);
                for (int i = 0; i < crowd.Length; i++)
                {
                    var g = new GameObject(crowd[i].name);
                    g.transform.SetParent(stage, false);
                    g.transform.SetPositionAndRotation(c0 + new Vector3(-6f + i * 1.3f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
                    g.AddComponent<MeshFilter>().sharedMesh = crowd[i];
                    g.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Shot("w3_gallery_crowd", c0 + new Vector3(0f, 1.2f, -9f), c0 + new Vector3(0f, 0.9f, 0f), 60f);

                var gm = AssetDatabase.LoadAssetAtPath<GameObject>(BattleAssets.ModelPrefabPath("props", "smoke_grenade"));
                if (gm != null)
                {
                    var g = (GameObject)Object.Instantiate(gm, stage);
                    var gp = at + new Vector3(0f, 1f, -20f);
                    g.transform.position = gp;
                    Shot("w3_gallery_grenade", gp + new Vector3(0.12f, 0.1f, -0.25f), gp + new Vector3(0f, 0.07f, 0f), 40f);
                }
                QualitySettings.SetQualityLevel(q, true);
                var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(BattleAssets.LDir + "/Impostor_Run.png");
                if (sheet != null) File.Copy(BattleAssets.LDir + "/Impostor_Run.png", "Logs/w3_gallery_impostor.png", true);
                ok = true;
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] battle gallery failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void Shot(string name, Vector3 from, Vector3 look, float fov)
        {
            var t = new GameObject("~cam").transform;
            t.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
            ScreenshotTool.Render(t, fov, null, 1280, 720, $"Logs/{name}.jpg");
            Object.DestroyImmediate(t.gameObject);
            Debug.Log("[FMVR] gallery " + name);
        }
    }
}
