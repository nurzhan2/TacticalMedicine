using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w10c: the owner's soldier (SourceAssets/Incoming/soldier → Assets/Art/Characters/SoldierKZ): 3ds Max Biped rig
    /// imported as Humanoid (the Mixamo casualty clips retarget onto it), URP materials from the packed textures
    /// (Albedo, Normal, MetalSmooth = metallic R + smoothness A), Android ASTC sizes chosen for the APK budget.
    /// Erlan.prefab uses it unless the switch file <see cref="OldBodySwitch"/> exists (then the old Mixamo body).
    /// Batch: -executeMethod FieldMedic.EditorTools.SoldierKZImport.Run
    /// </summary>
    public static class SoldierKZImport
    {
        public const string Dir = "Assets/Art/Characters/SoldierKZ";
        public const string Fbx = Dir + "/Soldier_KZ.fbx";
        public const string TexDir = Dir + "/Textures";
        /// <summary>Lighter body for the friendly infantry / rescuers (tools/blender/soldier_lod.py, ratio 0.35, about 5k tris).</summary>
        public const string LodFbx = Dir + "/Soldier_KZ_LOD1.fbx";
        public static bool LodReady => Enabled && File.Exists(LodFbx);
        public const string MatDir = "Assets/Art/Materials";
        /// <summary>Create this (empty) file to go back to the old Erlan body; re-run CasualtyAssetBuilder.BuildPrefab.</summary>
        public const string OldBodySwitch = "ProjectSettings/FMVR_OLD_ERLAN";

        /// <summary>Vest surface offsets from the chest/spine bones (measured on the mesh, see Report).</summary>
        public static float VestBackDepth = 0.15f, VestFrontDepth = 0.15f, HipSide = 0.18f;

        public static bool Enabled => File.Exists(Fbx) && !File.Exists(OldBodySwitch);

        // material slot in the FBX → texture prefix
        public static readonly Dictionary<string, string> Slots = new Dictionary<string, string>
        {
            ["sm005_body"] = "Body", ["sm005_equipment"] = "Equipment", ["sm005_head"] = "Head", ["sm005_helmet"] = "Helmet",
        };

        // Android max size per texture (APK budget: the build fails above 200 MB)
        public static int AndroidSize(string file)
        {
            bool albedo = file.Contains("_Albedo"), normal = file.Contains("_Normal");
            bool bigPart = file.Contains("_Body_") || file.Contains("_Head_");
            if (albedo) return bigPart ? 2048 : 1024;
            if (normal) return bigPart ? 1024 : 512;
            return 512; // MetalSmooth
        }

        public static void Run()
        {
            try
            {
                ConfigureTextures();
                ConfigureModel(Fbx);
                var mats = BuildMaterials();
                Remap(Fbx, mats);
                Report(Fbx);
                if (File.Exists(LodFbx)) { ConfigureModel(LodFbx); Remap(LodFbx, mats); Report(LodFbx); }
                CasualtyAssetBuilder.BuildVoiceBank();
                CasualtyAssetBuilder.BuildPrefab();
                // the friendly line + self-aid rescuers (same prefab path/GUID: the scenes pick it up)
                if (LodReady) FieldMedic.EditorTools.SceneBuilders.BattleAssets.SoldierPrefab(FieldMedic.Battle.Team.Friendly, FieldMedic.EditorTools.SceneBuilders.BattleAssets.SoldierController());
                AssetDatabase.SaveAssets();
                Debug.Log("[FMVR] soldier: done");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[FMVR] soldier import failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        public static void ConfigureTextures()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(TexDir, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                string f = Path.GetFileName(path);
                bool normal = f.Contains("_Normal"), ms = f.Contains("_MetalSmooth");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !ms;
                ti.alphaSource = ms ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 2048;
                ti.anisoLevel = 2;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true;
                a.maxTextureSize = AndroidSize(f);
                a.format = normal ? TextureImporterFormat.ASTC_6x6 : ms ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
                a.compressionQuality = 50;
                ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
            }
        }

        public static void ConfigureModel(string fbx)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (mi == null) throw new Exception("not found " + fbx);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = true; // like the old body: SurfacePoint filters wound vertices by bone weights
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.optimizeGameObjects = false;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.SaveAndReimport();
            // Unity's Biped auto-map: make sure the ones the casualty needs are mapped
            var hd = mi.humanDescription;
            Debug.Log("[FMVR] soldier: human bones mapped " + hd.human.Length + ": " +
                      string.Join(", ", hd.human.Where(h => !h.humanName.Contains("Proximal") && !h.humanName.Contains("Intermediate") && !h.humanName.Contains("Distal"))
                          .Select(h => h.humanName + "=" + h.boneName)));
        }

        static Texture2D Tex(string part, string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Soldier_{part}_{kind}.png");

        public static Dictionary<string, Material> BuildMaterials()
        {
            var shader = Shader.Find("FieldMedic/CasualtyLit");
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(CasualtyAssetBuilder.BloodTexDir + "/SoakNoise.png");
            var map = new Dictionary<string, Material>();
            foreach (var kv in Slots)
            {
                string path = $"{MatDir}/SoldierKZ_{kv.Value}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader;
                m.SetTexture("_BaseMap", Tex(kv.Value, "Albedo"));
                m.SetTexture("_BumpMap", Tex(kv.Value, "Normal"));
                m.SetTexture("_MetallicGlossMap", Tex(kv.Value, "MetalSmooth"));
                if (noise != null) m.SetTexture("_SoakNoise", noise);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 1f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                map[kv.Key] = m;
            }
            return map;
        }

        static void Remap(string fbx, Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var names = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().ToList();
            foreach (var n in names)
            {
                var key = Slots.Keys.FirstOrDefault(k => n.StartsWith(k, StringComparison.OrdinalIgnoreCase)) ??
                          Slots.FirstOrDefault(k => n.IndexOf(k.Value, StringComparison.OrdinalIgnoreCase) >= 0).Key;
                if (key == null) { Debug.LogWarning("[FMVR] soldier: unmapped material " + n); continue; }
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mats[key]);
            }
            mi.SaveAndReimport();
        }

        public static void Report(string fbx)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var anim = model.GetComponent<Animator>();
            var avatar = anim != null ? anim.avatar : AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            var sb = new StringBuilder("[FMVR] soldier report " + Path.GetFileName(fbx) + ": ");
            sb.Append($"avatar {(avatar != null ? avatar.name : "none")} valid {avatar != null && avatar.isValid} human {avatar != null && avatar.isHuman}; ");
            int tris = 0;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r.sharedMesh;
                int t = m != null ? Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3) : 0;
                tris += t;
                sb.Append($"SMR {r.name} tris {t} sub {m?.subMeshCount} bones {r.bones.Length} mats [{string.Join(",", r.sharedMaterials.Select(x => x ? x.name : "null"))}]; ");
            }
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) sb.Append($"MR {r.name}; ");
            var b = new Bounds(model.transform.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
            sb.Append($"total tris {tris}, bounds {b.size:F2} center {b.center:F2}");
            Debug.Log(sb.ToString());
        }
    }
}
