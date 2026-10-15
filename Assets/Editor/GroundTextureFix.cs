using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>Diagnostics/fix for the ground "pixel noise" (supervisor 15.10 03:55 #1): logs the editor-side format of
    /// the ground textures and can switch their Android override.</summary>
    public static class GroundTextureFix
    {
        public static readonly string[] Ground =
        {
            "Assets/ThirdParty/PolyHaven/Textures/withered_grass/withered_grass_diff.jpg",
            "Assets/ThirdParty/PolyHaven/Textures/dry_ground_01/dry_ground_01_diff.jpg",
            "Assets/ThirdParty/PolyHaven/Textures/burned_ground_01/burned_ground_01_diff.jpg",
        };

        public static void Log()
        {
            foreach (var p in Ground)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var s = ti.GetPlatformTextureSettings("Android");
                Debug.Log($"[FMVR] ground tex {p}: runtime format {t.format} mips {t.mipmapCount} {t.width}x{t.height}; android override {s.overridden} {s.format}");
            }
        }

        public static void Apply(TextureImporterFormat fmt)
        {
            foreach (var p in Ground)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var s = ti.GetPlatformTextureSettings("Android");
                s.overridden = true;
                s.format = fmt;
                s.maxTextureSize = 1024;
                ti.SetPlatformTextureSettings(s);
                ti.anisoLevel = 4;
                ti.SaveAndReimport();
            }
            Log();
        }

        public static void Uncompressed() => Apply(TextureImporterFormat.RGB24);
    }
}
