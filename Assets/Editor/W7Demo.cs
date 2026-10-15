using System.IO;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w7 presentation fixes that patch existing assets without rebuilding the battlefield:
    /// the Kazakhstan flag hung upside down (the BlenderKit atlas holds the flag rotated 180°, the cloth UV flipped
    /// only u). <see cref="FlagTexturePath"/> is that flag cropped and rotated upright (sun above the eagle, hoist
    /// ornament at u = 0); the cloth mesh asset gets the plain UV of <see cref="SceneBuilders.BattlefieldFx.FlagUV"/>.
    /// Batch: -executeMethod FieldMedic.EditorTools.W7Demo.FixFlag
    /// </summary>
    public static class W7Demo
    {
        public const string FlagTexturePath = "Assets/_Licensed/_Insignia/kz_flag.png";
        public const string FlagMeshPath = "Assets/Art/Battlefield/Meshes/FlagCloth.asset";
        public const string FlagMaterialPath = "Assets/Art/Battlefield/Materials/Bf_FlagKZ.mat";
        public const float FlagLength = 1.8f, FlagHeight = 0.9f;

        [MenuItem("FieldMedic/w7/Fix KZ flag")]
        public static void FixFlag()
        {
            bool ok = Apply();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Apply()
        {
            // texture import: colour, clamp, mipmaps, ≤ 1024
            var ti = AssetImporter.GetAtPath(FlagTexturePath) as TextureImporter;
            if (ti == null) { Debug.LogError("[FMVR] w7 flag: missing " + FlagTexturePath); return false; }
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            foreach (var p in new[] { "Assets/_Licensed/_Insignia/Resources/Insignia/kz_flag_patch.png", "Assets/Art/Insignia/Resources/Insignia/satbayev_patch.png" })
            {
                if (!(AssetImporter.GetAtPath(p) is TextureImporter pi)) continue;
                pi.sRGBTexture = true; pi.wrapMode = TextureWrapMode.Clamp; pi.mipmapEnabled = true; pi.maxTextureSize = 256;
                pi.SaveAndReimport();
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FlagMeshPath);
            if (mesh == null) { Debug.LogError("[FMVR] w7 flag: missing " + FlagMeshPath); return false; }
            var v = mesh.vertices;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = SceneBuilders.BattlefieldFx.FlagUV(v[i].x / FlagLength, v[i].y / FlagHeight);
            mesh.uv = uv;
            EditorUtility.SetDirty(mesh);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(FlagMaterialPath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(FlagTexturePath);
            if (mat == null || tex == null) { Debug.LogError("[FMVR] w7 flag: missing material/texture"); return false; }
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FMVR] w7 flag: cloth UV re-mapped ({v.Length} verts), material → {FlagTexturePath}");
            return true;
        }
    }
}
