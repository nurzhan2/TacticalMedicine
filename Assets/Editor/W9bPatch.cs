using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w9b self-aid drill: no scene change is needed (the self-aid director, the arm, the band and the overlays are built
    /// at runtime in the existing Main_* scenes); only the hint table asset gains the "Жгут на себя" lesson.
    /// Batch: -executeMethod FieldMedic.EditorTools.W9bPatch.ApplyBatch
    /// </summary>
    public static class W9bPatch
    {
        public static void ApplyBatch()
        {
            bool ok = false;
            try { ok = Apply(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] w9b patch: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/w9/Self-aid: rebuild hint table")]
        public static bool Apply()
        {
            SceneBuilders.W4Builder.BuildHintTable();
            AssetDatabase.SaveAssets();
            var t = AssetDatabase.LoadAssetAtPath<FieldMedic.Scenarios.HintTable>(SceneBuilders.W4Builder.HintTablePath);
            bool has = t != null && t.version == FieldMedic.Scenarios.HintTable.CurrentVersion && t.lessons.Exists(l => l.id == "selftq" && l.selfAid);
            Debug.Log("[FMVR] w9b patch: hint table v" + (t != null ? t.version : -1) + ", selftq lesson " + (has ? "present" : "MISSING"));
            return has;
        }
    }
}
