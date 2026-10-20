using UnityEditor;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w10b: import settings for the first-person soldier hands (tools/blender/fp_hands_extract.py →
    /// Assets/Art/Characters/SoldierKZ/FPHands and the runtime copy in Assets/Resources/FPHands, loaded by GloveHand):
    /// metres, no rig/avatar/animation (the bones are driven by GloveHand), no imported materials
    /// (GloveHand assigns Resources/FPHands/FPHands_Body.mat = the soldier's body material).
    /// </summary>
    public sealed class FPHandsImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/FPHands/") || !assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk; // the body material has a normal map
        }
    }
}
