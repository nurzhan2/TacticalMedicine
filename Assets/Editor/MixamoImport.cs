using System.IO;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Import rules for the casualty:
    ///  - Erlan.fbx + Mixamo animation FBX (Assets/Art/Characters/Erlan/Anims): Humanoid, avatar from each model.
    ///    Clips are named after the file, loop flags per clip, root rotation/height/XZ baked into the pose
    ///    (based upon original) — no root motion, the GameObject stays where the scenario put him.
    ///  - Gear FBX: static meshes, no animation, no avatar.
    ///  - Audio: voice = mono Vorbis decompress-on-load; SFX = mono (except ambience beds), long clips compressed in memory.
    /// </summary>
    public sealed class MixamoImport : AssetPostprocessor
    {
        public const string CharDir = "Assets/Art/Characters/Erlan/";
        public const string AnimDir = "Assets/Art/Characters/Erlan/Anims/";
        public const string GearDir = "Assets/Art/Gear/";

        static readonly string[] Loops =
        {
            "Writhing_In_Pain", "Laying_Moaning", "Laying_Breathless", "Laying_Severe_Cough", "Receiving_Cpr",
            "Recovery_Side_Pose", "Being_Carried", "Rifle_Crouch_Idle", "Crouching_Idle", "Rifle_Kneel_Idle",
            "Crouch_Look_Corner", "Sitting_Dazed", "Injured_Hurting_Idle", "Look_Around_Nervous",
        };

        public static bool IsLoop(string clip) => System.Array.IndexOf(Loops, clip) >= 0;
        public static string ClipName(string path) => Path.GetFileNameWithoutExtension(path).Replace("mixamo_", "");

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(CharDir) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                bool anim = assetPath.StartsWith(AnimDir);
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = anim;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importVisibility = false;
                mi.materialImportMode = anim ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportStandard;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.isReadable = !anim; // CasualtyRig bakes the skinned mesh to place wounds on the surface
                mi.optimizeGameObjects = false;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                mi.resampleCurves = true;
            }
            else if (assetPath.StartsWith(GearDir))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimDir)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            string name = ClipName(assetPath);
            bool loop = IsLoop(name);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                c.name = clips.Length == 1 ? name : name + "_" + i;
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = false;
                c.heightFromFeet = true; // "Original" floats Mixamo lying clips ~1 m up; feet keep the body on the floor
                c.lockRootPositionXZ = true; // no root motion: falls must never carry him through trench walls
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessAudio()
        {
            var ai = (AudioImporter)assetImporter;
            bool voice = assetPath.StartsWith("Assets/Audio/Voice/");
            bool bed = assetPath.Contains("/wind_night/") || assetPath.Contains("/crickets_night/") || assetPath.Contains("/rain_trench/");
            if (!voice && !assetPath.StartsWith("Assets/Audio/SFX/")) return;
            ai.forceToMono = !bed;
            ai.loadInBackground = !voice;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = voice ? 0.6f : 0.45f;
            s.loadType = voice ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            s.preloadAudioData = voice;
            ai.defaultSampleSettings = s;
        }
    }
}
