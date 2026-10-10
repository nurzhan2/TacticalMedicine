using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Medical;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Builds everything the casualty needs from the imported art:
    /// anim FBX copies, materials (+ FBX remaps), Erlan.controller, VoiceBank, Resources/BloodSettings, Erlan.prefab.
    /// Batch: -executeMethod FieldMedic.EditorTools.CasualtyAssetBuilder.BuildAll
    /// </summary>
    public static class CasualtyAssetBuilder
    {
        public const string ErlanFbx = "Assets/Art/Characters/Erlan/Erlan.fbx";
        public const string TexDir = "Assets/Art/Characters/Erlan/Textures";
        public const string MatDir = "Assets/Art/Materials";
        public const string ControllerPath = "Assets/Art/Characters/Erlan/Erlan.controller";
        public const string PrefabPath = "Assets/Prefabs/Erlan.prefab";
        public const string VoiceBankPath = "Assets/Data/Voice/VoiceBank.asset";
        public const string BloodSettingsPath = "Assets/Resources/BloodSettings.asset";
        public const string BloodTexDir = "Assets/Art/Blood/Textures";

        public static readonly Dictionary<CasualtyPose, string> PoseClips = new Dictionary<CasualtyPose, string>
        {
            [CasualtyPose.CrouchIdle] = "Crouching_Idle",
            [CasualtyPose.LookAround] = "Crouch_Look_Corner",
            [CasualtyPose.HitFall_Explosion] = "Explosion_Fall",
            [CasualtyPose.HitFall_Shot] = "Shot_Chest_Fall_Back",
            [CasualtyPose.HitFall_Shoulder] = "Shoulder_Hit_Fall",
            [CasualtyPose.KnockedOut] = "Knocked_Out_Fall_Back",
            [CasualtyPose.LyingWrithing] = "Writhing_In_Pain",
            [CasualtyPose.LyingMoaning] = "Laying_Moaning",
            [CasualtyPose.LyingBreathless] = "Laying_Breathless",
            [CasualtyPose.LyingCough] = "Laying_Severe_Cough",
            [CasualtyPose.LyingDying] = "Laying_Dying",
            [CasualtyPose.Unconscious] = "Receiving_Cpr",
            [CasualtyPose.RecoverySide] = "Recovery_Side_Pose",
            [CasualtyPose.Carried] = "Being_Carried",
            [CasualtyPose.SittingDazed] = "Sitting_Dazed",
        };

        /// <summary>Logs compile errors of our custom shaders (a broken shader silently falls back).</summary>
        public static bool CheckShaders()
        {
            bool ok = true;
            foreach (var name in new[] { "FieldMedic/CasualtyLit", "FieldMedic/BloodDecal" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogError("[FMVR] shader missing " + name); ok = false; continue; }
                foreach (var msg in ShaderUtil.GetShaderMessages(sh))
                    Debug.Log($"[FMVR] shader {name}: {msg.severity} {msg.message} (line {msg.line}, {msg.platform})");
                if (ShaderUtil.ShaderHasError(sh)) { Debug.LogError("[FMVR] shader has errors: " + name); ok = false; }
                else Debug.Log($"[FMVR] shader OK: {name} supported={sh.isSupported}");
            }
            return ok;
        }

        [MenuItem("FieldMedic/Casualty/Build All Casualty Assets")]
        public static void BuildAll()
        {
            try
            {
                CopyAnimations();
                CheckShaders();
                BuildMaterials();
                BuildController();
                BuildVoiceBank();
                BuildBloodSettings();
                BuildPrefab();
                AssetDatabase.SaveAssets();
                Debug.Log("[FMVR] Casualty assets built");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[FMVR] CasualtyAssetBuilder failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        // ------------------------------------------------------------------ animations
        public static void CopyAnimations()
        {
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SourceAssets", "Mixamo"));
            Directory.CreateDirectory(MixamoImport.AnimDir);
            int n = 0;
            if (Directory.Exists(src))
            {
                foreach (var f in Directory.GetFiles(src, "mixamo_*.fbx"))
                {
                    if (f.Contains("__SKIN")) continue; // 115 MB, only used by the Blender pipeline
                    var dst = Path.Combine(MixamoImport.AnimDir, Path.GetFileName(f));
                    if (!File.Exists(dst)) { File.Copy(f, dst); n++; }
                }
            }
            AssetDatabase.Refresh();
            // import rules live in MixamoImport: force a re-import so rule changes always apply
            foreach (var f in Directory.GetFiles(MixamoImport.AnimDir, "*.fbx"))
                AssetDatabase.ImportAsset(f.Replace(Path.DirectorySeparatorChar, '/'), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ErlanFbx, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[FMVR] animations copied: {n} new");
        }

        static AnimationClip Clip(string name)
        {
            var path = MixamoImport.AnimDir + "mixamo_" + name + ".fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        }

        // ------------------------------------------------------------------ materials
        static Material GetMat(string name, Shader shader)
        {
            Directory.CreateDirectory(MatDir);
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            return m;
        }

        static Texture2D Tex(string path, bool normal = false, bool linear = false)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null)
            {
                bool dirty = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
                if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (path.Contains("/Blood/") && !ti.alphaIsTransparency && !linear) { ti.alphaIsTransparency = true; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material CasualtyMat(string name, string albedo, string normal, string ms, Color tint)
        {
            var m = GetMat(name, Shader.Find("FieldMedic/CasualtyLit"));
            if (albedo != null) m.SetTexture("_BaseMap", Tex(albedo));
            if (normal != null) m.SetTexture("_BumpMap", Tex(normal, normal: true));
            if (ms != null) m.SetTexture("_MetallicGlossMap", Tex(ms, linear: true));
            m.SetTexture("_SoakNoise", Tex(BloodTexDir + "/SoakNoise.png", linear: true));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", ms != null ? 1f : 0.25f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LitMat(string name, Color c, float smooth, float metal = 0f)
        {
            var m = GetMat(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        static Material DecalMat(string name, string tex, float gloss, int queueOffset)
        {
            var m = GetMat(name, Shader.Find("FieldMedic/BloodDecal"));
            m.SetTexture("_BaseMap", Tex(tex));
            m.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.6f)); // night-plausible, not a glowing red patch
            m.SetFloat("_Gloss", gloss);
            m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials()
        {
            AssetDatabase.Refresh();
            var body1 = CasualtyMat("Erlan_Body1", TexDir + "/Erlan_Body1_Albedo.jpg", TexDir + "/Erlan_Body1_Normal.png", TexDir + "/Erlan_Body1_MetalSmooth.png", Color.white);
            var body2 = CasualtyMat("Erlan_Body2", TexDir + "/Erlan_Body2_Albedo.jpg", TexDir + "/Erlan_Body2_Normal.png", TexDir + "/Erlan_Body2_MetalSmooth.png", Color.white);
            var pcC = CasualtyMat("PC_Coyote", null, null, null, new Color(0.36f, 0.29f, 0.19f));
            var pcW = CasualtyMat("PC_Webbing", null, null, null, new Color(0.20f, 0.19f, 0.13f));
            Remap(ErlanFbx, new Dictionary<string, Material> { ["Erlan_Body1"] = body1, ["Erlan_Body2"] = body2, ["PC_Coyote"] = pcC, ["PC_Webbing"] = pcW });

            var gear = new Dictionary<string, Material>
            {
                ["Gear_Webbing"] = LitMat("Gear_Webbing", new Color(0.22f, 0.20f, 0.13f), 0.15f),
                ["Gear_Coyote"] = LitMat("Gear_Coyote", new Color(0.36f, 0.29f, 0.19f), 0.12f),
                ["Gear_RedCross"] = LitMat("Gear_RedCross", new Color(0.5f, 0.06f, 0.05f), 0.2f),
                ["CAT_Black"] = LitMat("CAT_Black", new Color(0.035f, 0.035f, 0.035f), 0.35f),
                ["CAT_Red"] = LitMat("CAT_Red", new Color(0.62f, 0.05f, 0.04f), 0.4f),
                ["CAT_Strap"] = LitMat("CAT_Strap", new Color(0.12f, 0.12f, 0.12f), 0.3f),
                ["CAT_TimeLabel"] = LitMat("CAT_TimeLabel", new Color(0.85f, 0.85f, 0.8f), 0.3f),
                ["Helmet_Olive"] = LitMat("Helmet_Olive", new Color(0.25f, 0.26f, 0.17f), 0.2f),
                ["Helmet_Black"] = LitMat("Helmet_Black", new Color(0.05f, 0.05f, 0.05f), 0.4f),
                ["Wound_Flesh"] = LitMat("Wound_Flesh", new Color(0.42f, 0.04f, 0.03f), 0.75f),
                ["Wound_Muscle"] = LitMat("Wound_Muscle", new Color(0.30f, 0.03f, 0.03f), 0.7f),
                ["Wound_Bone"] = LitMat("Wound_Bone", new Color(0.86f, 0.80f, 0.66f), 0.45f),
                ["Wound_Cloth"] = LitMat("Wound_Cloth", new Color(0.12f, 0.07f, 0.05f), 0.5f),
                ["Wound_Clot"] = LitMat("Wound_Clot", new Color(0.12f, 0.01f, 0.01f), 0.8f),
            };
            foreach (var f in new[] { "DragHandle", "IFAK_Pouch", "CAT_Tourniquet", "Helmet", "AmputationStump" })
                Remap(MixamoImport.GearDir + f + ".fbx", gear);
        }

        static void Remap(string fbx, Dictionary<string, Material> map)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) { Debug.LogWarning("[FMVR] no importer for " + fbx); return; }
            foreach (var kv in map) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ animator
        public static AnimatorController BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("State", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;
            int i = 0;
            AnimatorState defaultState = null;
            foreach (var kv in PoseClips)
            {
                var clip = Clip(kv.Value);
                if (clip == null) Debug.LogWarning("[FMVR] missing clip " + kv.Value);
                var st = sm.AddState(kv.Key.ToString(), new Vector3(300 + (i % 3) * 220, 60 * (i / 3), 0));
                st.motion = clip;
                st.writeDefaultValues = true;
                if (kv.Key == CasualtyPose.CrouchIdle) defaultState = st;
                var t = sm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.Equals, (int)kv.Key, "State");
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
                bool fall = CasualtyPoseMapper.IsFall(kv.Key);
                t.duration = fall ? 0.12f : 0.7f;
                t.interruptionSource = TransitionInterruptionSource.None;
                i++;
            }
            sm.defaultState = defaultState;
            EditorUtility.SetDirty(ctrl);
            Debug.Log("[FMVR] controller states: " + sm.states.Length);
            return ctrl;
        }

        // ------------------------------------------------------------------ voice
        public static VoiceBank BuildVoiceBank()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VoiceBankPath));
            var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(VoiceBankPath);
            if (bank == null) { bank = ScriptableObject.CreateInstance<VoiceBank>(); AssetDatabase.CreateAsset(bank, VoiceBankPath); }
            var re = new Regex(@"^(?<id>.+?)_(?<lang>ru|kz)(_\d+)?$");
            var lines = new Dictionary<string, (List<AudioClip> ru, List<AudioClip> kz)>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Voice" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = re.Match(Path.GetFileNameWithoutExtension(path));
                if (!m.Success) continue;
                var id = m.Groups["id"].Value;
                if (!lines.TryGetValue(id, out var e)) lines[id] = e = (new List<AudioClip>(), new List<AudioClip>());
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                (m.Groups["lang"].Value == "ru" ? e.ru : e.kz).Add(clip);
            }
            bank.lines = lines.OrderBy(k => k.Key).Select(k => new VoiceBank.Line { id = k.Key, ru = k.Value.ru.ToArray(), kz = k.Value.kz.ToArray() }).ToArray();
            bank.groan = Pool("pain_groan");
            bank.scream = Pool("pain_scream");
            bank.gasp = Pool("gasp_choke");
            bank.cough = Pool("cough");
            bank.breathHeavy = Pool("breath_heavy").Where(c => !c.name.Contains("snore")).ToArray();
            bank.snore = Pool("breath_heavy").Where(c => c.name.Contains("snore")).ToArray();
            bank.heartbeat = Pool("heartbeat");
            EditorUtility.SetDirty(bank);
            Debug.Log($"[FMVR] voice bank: {bank.lines.Length} lines, groan {bank.groan.Length}, scream {bank.scream.Length}");
            return bank;
        }

        static AudioClip[] Pool(string cat)
        {
            return AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/SFX/" + cat })
                .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null).OrderBy(c => c.name).ToArray();
        }

        // ------------------------------------------------------------------ blood
        public static BloodSettings BuildBloodSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BloodSettingsPath));
            var s = AssetDatabase.LoadAssetAtPath<BloodSettings>(BloodSettingsPath);
            if (s == null) { s = ScriptableObject.CreateInstance<BloodSettings>(); AssetDatabase.CreateAsset(s, BloodSettingsPath); }

            var p = GetMat("Blood_Particle", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            p.SetTexture("_BaseMap", Tex(BloodTexDir + "/BloodDrop.png"));
            p.SetColor("_BaseColor", new Color(0.38f, 0.015f, 0.015f, 1f));
            MakeTransparent(p);
            EditorUtility.SetDirty(p);
            s.particle = p;
            s.pool = DecalMat("Blood_Pool", BloodTexDir + "/BloodPool.png", 0.88f, 0);
            s.woundFragment = DecalMat("Wound_Fragment", BloodTexDir + "/Wound_Fragment.png", 0.75f, 5);
            s.woundRagged = DecalMat("Wound_Ragged", BloodTexDir + "/Wound_Ragged.png", 0.75f, 5);
            s.woundChestEntry = DecalMat("Wound_ChestEntry", BloodTexDir + "/Wound_ChestEntry.png", 0.7f, 5);
            s.woundExit = DecalMat("Wound_Exit", BloodTexDir + "/Wound_Exit.png", 0.75f, 5);
            s.stumpFlesh = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Wound_Flesh.mat");
            s.stumpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MixamoImport.GearDir + "AmputationStump.fbx");
            EditorUtility.SetDirty(s);
            return s;
        }

        // ------------------------------------------------------------------ prefab
        [Serializable]
        class AttachJson
        {
            public float[] drag_handle_unity;
            public float[] ifak_unity;
        }

        public static GameObject BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ErlanFbx);
            if (model == null) throw new Exception("Erlan.fbx not imported");
            var root = new GameObject("Erlan");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Model";
            var anim = inst.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var body = inst.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name.Contains("Body"));
            var pc = inst.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name.Contains("PlateCarrier"));
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = false;
                smr.skinnedMotionVectors = false;
                // lying bodies leave the bind-pose bounds: pad them
                var b = smr.localBounds; b.Expand(1.2f); smr.localBounds = b;
            }

            // log bind-pose bone positions vs Blender export to validate the axis conversion
            var neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Debug.Log($"[FMVR] Erlan bind pose: hips {hips.position:F3} neck {neck.position:F3} rootRot {inst.transform.GetChild(0).localEulerAngles} rootScale {inst.transform.GetChild(0).localScale}");

            var rig = root.AddComponent<CasualtyRig>();
            rig.animator = anim;
            rig.bodyRenderer = body;
            var soak = root.AddComponent<BloodSoak>();
            soak.targets = new Renderer[] { body, pc }.Where(r => r != null).ToArray();
            root.AddComponent<BloodPoolManager>();
            var voice = root.AddComponent<CasualtyVoice>();
            voice.bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(VoiceBankPath);
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var vgo = new GameObject("Voice"); vgo.transform.SetParent(head, false); vgo.transform.localPosition = Vector3.zero;
            voice.voice = CasualtyVoice.MakeSource(vgo, false);
            var bgo = new GameObject("Breath"); bgo.transform.SetParent(head, false);
            voice.breath = CasualtyVoice.MakeSource(bgo, true);
            var ctrl = root.AddComponent<CasualtyController>();
            ctrl.animator = anim;
            ctrl.rig = rig;
            ctrl.voice = voice;
            ctrl.soak = soak;
            ctrl.PoolManager = root.GetComponent<BloodPoolManager>();

            AttachGear(anim);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[FMVR] prefab saved " + PrefabPath);
            return prefab;
        }

        static Vector3 V(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static void AttachGear(Animator anim)
        {
            var json = File.Exists("Assets/Art/Gear/gear_attach.json")
                ? JsonUtility.FromJson<AttachJson>(File.ReadAllText("Assets/Art/Gear/gear_attach.json")) : new AttachJson();
            Transform chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            Transform spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            Transform modelRoot = anim.transform;

            var drag = Spawn(MixamoImport.GearDir + "DragHandle.fbx", chest, modelRoot.TransformPoint(V(json.drag_handle_unity)), modelRoot.rotation);
            if (drag != null)
            {
                drag.name = "DragHandle";
                var gp = new GameObject("DragHandle_GrabPoint");
                gp.transform.SetParent(drag.transform, false);
                gp.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            }
            var ifak = Spawn(MixamoImport.GearDir + "IFAK_Pouch.fbx", spine, modelRoot.TransformPoint(V(json.ifak_unity)), modelRoot.rotation);
            if (ifak != null)
            {
                ifak.name = "IFAK";
                // his own CAT rides in the open holder on the side of the pouch, windlass up
                var cat = Spawn(MixamoImport.GearDir + "CAT_Tourniquet.fbx", ifak.transform,
                    ifak.transform.TransformPoint(new Vector3(-0.09f, 0.03f, 0.035f)), ifak.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
                if (cat != null) { cat.name = "CAT_Erlan"; cat.transform.localScale = Vector3.one * 0.8f; }
            }
        }

        static GameObject Spawn(string path, Transform parent, Vector3 worldPos, Quaternion worldRot)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || parent == null) { Debug.LogWarning("[FMVR] gear missing " + path); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(worldPos, worldRot * asset.transform.localRotation);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
