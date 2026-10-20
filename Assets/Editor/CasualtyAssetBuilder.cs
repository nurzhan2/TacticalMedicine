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
            // w10c: the TTS word placeholders are not played (VoiceOver.CasualtyTtsWords) → keep the ids, ship no clips
            bool words = VoiceOver.CasualtyTtsWords;
            bank.lines = lines.OrderBy(k => k.Key).Select(k => new VoiceBank.Line { id = k.Key, ru = words ? k.Value.ru.ToArray() : new AudioClip[0], kz = words ? k.Value.kz.ToArray() : new AudioClip[0] }).ToArray();
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
            // w10c: the owner's soldier (Soldier_KZ, Biped → Humanoid) is Erlan unless ProjectSettings/FMVR_OLD_ERLAN exists
            bool soldier = SoldierKZImport.Enabled;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(soldier ? SoldierKZImport.Fbx : ErlanFbx);
            if (model == null) throw new Exception((soldier ? SoldierKZImport.Fbx : ErlanFbx) + " not imported");
            var root = new GameObject("Erlan");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Model";
            var anim = inst.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>();
            var body = soldier
                ? smrs.OrderByDescending(r => r.sharedMaterials.Any(m => m != null && m.name.Contains("_Body")) ? 1 : 0).ThenByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault()
                : smrs.FirstOrDefault(r => r.name.Contains("Body"));
            var pc = soldier ? null : smrs.FirstOrDefault(r => r.name.Contains("PlateCarrier"));
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
            soak.targets = soldier ? smrs.Cast<Renderer>().ToArray() : new Renderer[] { body, pc }.Where(r => r != null).ToArray();
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

            if (soldier) { MeasureSoldier(rig, smrs); StripNvg(rig, smrs); AttachGearSoldier(anim, rig); } else AttachGear(anim);

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

        /// <summary>w10c: measure the soldier's mesh in the bind pose: vest surface depth (evac handle, IFAK) and the
        /// nose / ear positions in the head frame used by <see cref="FieldMedic.Treatment.NpaItem.Face"/>.</summary>
        static void MeasureSoldier(CasualtyRig rig, SkinnedMeshRenderer[] smrs)
        {
            rig.Init();
            var verts = new List<Vector3>();
            var headVerts = new List<Vector3>(); // only the face/head submesh (not the helmet or the gear)
            foreach (var r in smrs)
            {
                var m = new Mesh();
                r.BakeMesh(m, true);
                var mat = r.transform.localToWorldMatrix;
                var vs = m.vertices;
                foreach (var v in vs) verts.Add(mat.MultiplyPoint3x4(v));
                for (int sm = 0; sm < m.subMeshCount && sm < r.sharedMaterials.Length; sm++)
                    if (r.sharedMaterials[sm] != null && r.sharedMaterials[sm].name.Contains("_Head"))
                        foreach (var i in m.GetIndices(sm).Distinct()) headVerts.Add(mat.MultiplyPoint3x4(vs[i]));
                Object.DestroyImmediate(m);
            }
            Vector3 up = rig.BodyUp, fwd = rig.BodyForward, right = rig.BodyRight;
            var chest = rig.ChestBone.position;
            var spine = rig.Bone(HumanBodyBones.Spine).position;
            float Depth(Vector3 at, Vector3 dir, float halfH, float halfW) => verts
                .Where(v => Mathf.Abs(Vector3.Dot(v - at, up)) < halfH && Mathf.Abs(Vector3.Dot(v - at, right)) < halfW)
                .Select(v => Vector3.Dot(v - at, dir)).DefaultIfEmpty(0.15f).Max();
            // the evac handle sits just below the collar: the back surface at that height (not the pack's deepest point)
            var neckP = rig.Bone(HumanBodyBones.Neck).position;
            SoldierKZImport.VestBackDepth = Mathf.Clamp(Depth(Vector3.Lerp(chest, neckP, 0.55f), -fwd, 0.035f, 0.08f), 0.06f, 0.3f);
            SoldierKZImport.VestFrontDepth = Mathf.Clamp(Depth(spine, fwd, 0.05f, 0.18f), 0.08f, 0.3f);

            // head frame exactly as NpaItem.Face builds it
            var head = rig.Bone(HumanBodyBones.Head);
            var chestT = rig.ChestBone;
            Quaternion rel = head.rotation * Quaternion.Inverse(chestT != null ? chestT.rotation : rig.transform.rotation);
            Vector3 hf = (rel * fwd).normalized, hu = (rel * up).normalized, hr = Vector3.Cross(hu, hf).normalized;
            Vector3 hp = head.position;
            var face = headVerts.Where(v => (v - hp).sqrMagnitude < 0.2f * 0.2f && Vector3.Dot(v - hp, hu) > -0.02f && Vector3.Dot(v - hp, hu) < 0.14f
                                        && Mathf.Abs(Vector3.Dot(v - hp, hr)) < 0.03f).ToList();
            if (face.Count > 0)
            {
                // nose tip = the most forward face vertex; the nostril sits ~1.2 cm behind, 1.5 cm below, 0.8 cm to the right
                var tip = face.OrderByDescending(v => Vector3.Dot(v - hp, hf)).First();
                Vector3 d = tip - hp;
                var n = new Vector3(0.008f, Vector3.Dot(d, hu) - 0.015f, Vector3.Dot(d, hf) - 0.012f);
                float halfWidth = headVerts.Where(v => (v - hp).sqrMagnitude < 0.16f * 0.16f && Mathf.Abs(Vector3.Dot(v - hp, hu) - n.y) < 0.03f
                                                   && Mathf.Abs(Vector3.Dot(v - hp, hf) - (n.z - 0.09f)) < 0.04f)
                    .Select(v => Mathf.Abs(Vector3.Dot(v - hp, hr))).DefaultIfEmpty(0.075f).Max();
                rig.faceNostril = n;
                rig.faceEar = new Vector3(Mathf.Clamp(halfWidth, 0.06f, 0.1f), n.y - 0.01f, n.z - 0.095f);
            }
            // limb thickness vs the old body's radius profile (90th percentile of the uniform around the axis)
            float Thickness(Limb limb, float pos)
            {
                Vector3 a = LimbMath.PointAt(rig, limb, pos), dir = LimbMath.AxisAt(rig, limb, pos);
                var ds = verts.Where(v => Mathf.Abs(Vector3.Dot(v - a, dir)) < 0.025f)
                    .Select(v => Vector3.ProjectOnPlane(v - a, dir).magnitude).Where(d => d < (LimbMath.IsLeg(limb) ? 0.15f : 0.1f)).OrderBy(d => d).ToList();
                // RadiusAt is the skin; the band/shears add the cloth allowance on top → skin = cloth surface - allowance
                float cloth = ds.Count > 20 ? ds[(int)(ds.Count * 0.9f)] : LimbMath.RadiusAtBase(limb, pos) + FieldMedic.Treatment.ElasticTourniquetItem.ClothAllowanceFor(limb, pos);
                Debug.Log($"[FMVR] soldier {limb} @{pos}: cloth radius {cloth:F3} (old skin {LimbMath.RadiusAtBase(limb, pos):F3})");
                return cloth - FieldMedic.Treatment.ElasticTourniquetItem.ClothAllowanceFor(limb, pos);
            }
            rig.legRadiusScale = Mathf.Clamp(Mathf.Max(Thickness(Limb.RightLeg, 0.35f) / LimbMath.RadiusAtBase(Limb.RightLeg, 0.35f),
                                                       Thickness(Limb.LeftLeg, 0.35f) / LimbMath.RadiusAtBase(Limb.LeftLeg, 0.35f)), 0.9f, 1.5f);
            rig.armRadiusScale = Mathf.Clamp(Mathf.Max(Thickness(Limb.RightArm, 0.6f) / LimbMath.RadiusAtBase(Limb.RightArm, 0.6f),
                                                       Thickness(Limb.LeftArm, 0.6f) / LimbMath.RadiusAtBase(Limb.LeftArm, 0.6f)), 0.9f, 1.5f);
            var hipsT = rig.Bone(HumanBodyBones.Hips).position;
            SoldierKZImport.HipSide = verts.Where(v => Mathf.Abs(Vector3.Dot(v - hipsT, up)) < 0.05f && Mathf.Abs(Vector3.Dot(v - hipsT, fwd)) < 0.1f && Mathf.Abs(Vector3.Dot(v - hipsT, right)) < 0.3f)
                .Select(v => Vector3.Dot(v - hipsT, right)).DefaultIfEmpty(0.18f).Max();
            Debug.Log($"[FMVR] soldier limbs: leg x{rig.legRadiusScale:F2} arm x{rig.armRadiusScale:F2} hip side {SoldierKZImport.HipSide:F3}");
            Debug.Log($"[FMVR] soldier measure: vest back {SoldierKZImport.VestBackDepth:F3} front {SoldierKZImport.VestFrontDepth:F3} nostril {rig.faceNostril:F3} ear {rig.faceEar:F3} (face verts {face.Count}, head {headVerts.Count}, total {verts.Count})");
        }

        /// <summary>w10c: the helmet's night-vision monocular hangs in front of the right eye and blocks the nose (NPA)
        /// and the face. A medic flips it away: a copy of the mesh without the helmet-submesh triangles in front of the face.</summary>
        static void StripNvg(CasualtyRig rig, SkinnedMeshRenderer[] smrs)
        {
            var head = rig.Bone(HumanBodyBones.Head);
            var chestT = rig.ChestBone;
            Quaternion rel = head.rotation * Quaternion.Inverse(chestT != null ? chestT.rotation : rig.transform.rotation);
            Vector3 hf = (rel * rig.BodyForward).normalized, hu = (rel * rig.BodyUp).normalized;
            Vector3 hp = head.position;
            float noseZ = rig.faceNostril.z + 0.012f, noseY = rig.faceNostril.y;
            foreach (var r in smrs)
            {
                var src = r.sharedMesh;
                if (src == null || !src.isReadable) continue;
                int helmet = System.Array.FindIndex(r.sharedMaterials, m => m != null && m.name.Contains("_Helmet"));
                if (helmet < 0 || helmet >= src.subMeshCount) continue;
                var baked = new Mesh();
                r.BakeMesh(baked, true);
                var bv = baked.vertices;
                var l2w = r.transform.localToWorldMatrix;
                var idx = src.GetIndices(helmet);
                // mesh islands of the helmet submesh (vertices welded by position): the NVG monocular and its mount
                // are separate islands hanging in front of the face; the shell, straps and covers stay
                var wp = bv.Select(v => l2w.MultiplyPoint3x4(v)).ToArray();
                var weld = new Dictionary<Vector3Int, int>();
                var parent = new int[bv.Length];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
                foreach (int i in idx.Distinct())
                {
                    var key = Vector3Int.RoundToInt(wp[i] * 2000f); // 0.5 mm
                    if (weld.TryGetValue(key, out int j)) Union(i, j); else weld[key] = i;
                }
                for (int t = 0; t + 2 < idx.Length; t += 3) { Union(idx[t], idx[t + 1]); Union(idx[t], idx[t + 2]); }
                var islands = new Dictionary<int, List<int>>();
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int r0 = Find(idx[t]);
                    if (!islands.TryGetValue(r0, out var l)) islands[r0] = l = new List<int>();
                    l.Add(t);
                }
                int total = idx.Length / 3;
                var drop = new HashSet<int>();
                foreach (var kv in islands)
                {
                    if (kv.Value.Count > total * 0.25f) continue; // the shell
                    Vector3 c = Vector3.zero; int n = 0;
                    foreach (int t in kv.Value) { c += wp[idx[t]] + wp[idx[t + 1]] + wp[idx[t + 2]]; n += 3; }
                    c /= n;
                    float z = Vector3.Dot(c - hp, hf), y = Vector3.Dot(c - hp, hu);
                    // in front of the eyes, below the top of the helmet
                    if (z > noseZ - 0.05f && y < noseY + 0.12f) drop.Add(kv.Key);
                }
                var keep = new List<int>();
                int removed = 0;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    if (drop.Contains(Find(idx[t]))) { removed++; continue; }
                    keep.Add(idx[t]); keep.Add(idx[t + 1]); keep.Add(idx[t + 2]);
                }
                Debug.Log($"[FMVR] soldier helmet islands {islands.Count}, dropped {drop.Count}");
                Object.DestroyImmediate(baked);
                if (removed == 0) continue;
                var copy = Object.Instantiate(src);
                copy.name = src.name + "_noNVG";
                copy.SetIndices(keep.ToArray(), MeshTopology.Triangles, helmet);
                string path = SoldierKZImport.Dir + "/Soldier_KZ_noNVG.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(copy, path);
                r.sharedMesh = copy;
                Debug.Log($"[FMVR] soldier: NVG removed ({removed} helmet triangles in front of the face) -> {path}");
            }
        }

        /// <summary>w10c: the soldier has his own vest but no evac handle / IFAK of ours: place them from the bones
        /// (the old gear_attach.json offsets belong to the Mixamo body).</summary>
        static void AttachGearSoldier(Animator anim, CasualtyRig rig)
        {
            rig.Init();
            Transform chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            Transform spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            Vector3 up = rig.BodyUp, fwd = rig.BodyForward, right = rig.BodyRight;
            Quaternion bodyRot = Quaternion.LookRotation(fwd, up);
            float back = SoldierKZImport.VestBackDepth, front = SoldierKZImport.VestFrontDepth;
            // evac handle: top of the back panel, just below the collar
            Vector3 handlePos = Vector3.Lerp(chest.position, neck.position, 0.55f) - fwd * back;
            var drag = Spawn(MixamoImport.GearDir + "DragHandle.fbx", chest, handlePos, bodyRot);
            if (drag != null)
            {
                drag.name = "DragHandle";
                var gp = new GameObject("DragHandle_GrabPoint");
                gp.transform.SetParent(drag.transform, false);
                gp.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            }
            // IFAK on the right hip of the belt, its face outward
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 ifakPos = hips.position + right * (SoldierKZImport.HipSide + 0.03f) + up * 0.02f;
            var ifak = Spawn(MixamoImport.GearDir + "IFAK_Pouch.fbx", hips, ifakPos, Quaternion.LookRotation(right, up));
            if (ifak != null)
            {
                ifak.name = "IFAK";
                var cat = Spawn(MixamoImport.GearDir + "CAT_Tourniquet.fbx", ifak.transform,
                    ifak.transform.TransformPoint(new Vector3(-0.09f, 0.03f, 0.035f)), ifak.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
                if (cat != null) { cat.name = "CAT_Erlan"; cat.transform.localScale = Vector3.one * 0.8f; }
            }
            Debug.Log($"[FMVR] soldier gear: handle {handlePos:F3} ifak {ifakPos:F3} up {up:F2} fwd {fwd:F2}");
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
