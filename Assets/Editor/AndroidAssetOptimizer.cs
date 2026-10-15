using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w0b: Android import overrides for everything the APK ships (dependencies of the build scenes + Resources), so
    /// the APK stays ≤ 150 MB and the headset does not run out of memory:
    /// textures ASTC, max 2048 (ground/terrain, Erlan), 1024 (props, vehicles, ruins), 512 (small items, far scenery),
    /// mipmaps on (except UI), Read/Write off; licensed environment models Read/Write off (runtime code only reads the
    /// casualty, glove, IFAK and drone meshes); audio Vorbis, &gt; 5 s streaming, no PCM.
    /// -executeMethod FieldMedic.EditorTools.AndroidAssetOptimizer.ApplyBatch
    /// </summary>
    public static class AndroidAssetOptimizer
    {
        const string Android = "Android";

        public static void ApplyBatch()
        {
            Apply(SceneBuilders.SceneSplitter.AllPaths.Concat(new[] { SceneBuilders.BootSceneBuilder.ScenePath }).ToArray());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Max texture size for a project texture path (pure rule, EditMode-tested).</summary>
        public static int MaxSizeFor(string path, bool normalMap)
        {
            string p = path.Replace('\\', '/').ToLowerInvariant();
            if (p.Contains("/resources/ui/") || p.Contains("hinticons") || p.Contains("/fonts/")) return 1024;
            if (p.Contains("/characters/erlan/")) return normalMap ? 1024 : 2048;
            if (Regex.IsMatch(p, @"(ground|soil|dirt|mud|terrain|grass_|aerial_|forrest_ground|rocky_trail)") && !p.Contains("/blenderkit/")) return normalMap ? 1024 : 2048;
            if (p.EndsWith(".hdr") || p.EndsWith(".exr")) return 1024;
            if (Regex.IsMatch(p, @"(casing|bullet|shell|grenade|flashbang|jerrycan|fuel_drum|oil_drum|crate|tire|bucket|bottle|can_|debris_small|helmet)")) return 512;
            if (p.Contains("/aircraft/") || p.Contains("farfield") || p.Contains("/far_")) return 512;
            if (p.Contains("/_licensed/") || p.Contains("/thirdparty/")) return 1024;
            return 1024;
        }

        public static TextureImporterFormat FormatFor(string path, bool normalMap, bool hasAlpha)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("/resources/ui/") || p.Contains("hinticons")) return TextureImporterFormat.ASTC_4x4;
            if (normalMap) return TextureImporterFormat.ASTC_6x6;
            return TextureImporterFormat.ASTC_6x6;
        }

        public static void Apply(string[] scenes)
        {
            var deps = new HashSet<string>(AssetDatabase.GetDependencies(scenes, true));
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/") && !path.Contains("/Editor/")) foreach (var d in AssetDatabase.GetDependencies(path, true)) deps.Add(d);
            }
            int tex = 0, models = 0, audio = 0, meshes = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in deps)
                {
                    if (!path.StartsWith("Assets/")) continue; // package assets are immutable
                    var imp = AssetImporter.GetAtPath(path);
                    if (imp is TextureImporter ti) { if (Texture(ti, path)) tex++; }
                    else if (imp is ModelImporter mi) { if (Model(mi, path)) models++; }
                    else if (imp is AudioImporter ai) { if (Audio(ai, path)) audio++; }
                    else if (path.EndsWith(".asset") && path.Contains("/Meshes/"))
                    {
                        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (mesh == null || !mesh.isReadable) continue;
                        var so = new SerializedObject(mesh);
                        var p = so.FindProperty("m_IsReadable");
                        if (p != null && !NeedsReadable(path)) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); meshes++; }
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FMVR] AndroidAssetOptimizer: {deps.Count} shipped assets; changed textures {tex}, models {models}, audio {audio}, mesh assets {meshes}");
        }

        /// <summary>Meshes that runtime code reads (CombineMeshes / vertices / BakeMesh) must stay readable.</summary>
        public static bool NeedsReadable(string path)
        {
            string p = path.ToLowerInvariant();
            p = p.Replace("blenderkit", "");
            return p.Contains("/characters/") || p.Contains("erlan") || p.Contains("glove") || p.Contains("/hands/") ||
                   p.Contains("meshy") || p.Contains("ifak") || p.Contains("medical") || p.Contains("drone") ||
                   p.Contains("/treatment/") || p.Contains("blanket") || p.Contains("/kit");
        }

        static bool Texture(TextureImporter ti, string path)
        {
            bool normal = ti.textureType == TextureImporterType.NormalMap;
            bool ui = ti.textureType == TextureImporterType.Sprite || ti.textureType == TextureImporterType.GUI || path.Contains("/Resources/UI/") || path.Contains("HintIcons");
            int max = MaxSizeFor(path, normal);
            var fmt = FormatFor(path, normal, ti.DoesSourceTextureHaveAlpha());
            var s = ti.GetPlatformTextureSettings(Android);
            bool changed = false;
            if (!s.overridden) { s.overridden = true; changed = true; }
            if (s.maxTextureSize != max) { s.maxTextureSize = max; changed = true; }
            if (s.format != fmt) { s.format = fmt; changed = true; }
            if (s.compressionQuality != 50) { s.compressionQuality = 50; changed = true; }
            if (changed) ti.SetPlatformTextureSettings(s);
            if (ti.isReadable && !path.Contains("/Resources/")) { ti.isReadable = false; changed = true; }
            if (!ui && !ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (changed) ti.SaveAndReimport();
            return changed;
        }

        static bool Model(ModelImporter mi, string path)
        {
            bool changed = false;
            if (mi.isReadable && !NeedsReadable(path)) { mi.isReadable = false; changed = true; }
            if (mi.meshCompression == ModelImporterMeshCompression.Off && !NeedsReadable(path)) { mi.meshCompression = ModelImporterMeshCompression.Medium; changed = true; }
            if (changed) mi.SaveAndReimport();
            return changed;
        }

        static bool Audio(AudioImporter ai, string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            float len = clip != null ? clip.length : 1f;
            var s = ai.GetOverrideSampleSettings(Android);
            var want = s;
            want.compressionFormat = AudioCompressionFormat.Vorbis;
            want.quality = 0.45f;
            want.loadType = len > 5f ? AudioClipLoadType.Streaming : len > 1.5f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            want.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            bool changed = !ai.ContainsSampleSettingsOverride(Android) || s.compressionFormat != want.compressionFormat ||
                           Mathf.Abs(s.quality - want.quality) > 0.01f || s.loadType != want.loadType;
            if (!changed) return false;
            ai.SetOverrideSampleSettings(Android, want);
            ai.SaveAndReimport();
            return true;
        }
    }
}
