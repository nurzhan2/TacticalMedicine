using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// w10 audit: no scene change is needed (kit tray, radio panel, step guide and the controls tutorial are built at
    /// runtime in the existing Main_* scenes); the hint table asset is rebuilt because the hints now name the real
    /// buttons (X kit, Y radio, GRIP/КУРОК).
    /// Batch: -executeMethod FieldMedic.EditorTools.W10Patch.ApplyBatch
    /// </summary>
    public static class W10Patch
    {
        public static void ApplyBatch()
        {
            bool ok = false;
            try { ok = Apply(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] w10 patch: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("FieldMedic/w10/Rebuild hint table")]
        public static bool Apply()
        {
            SceneBuilders.W4Builder.BuildHintTable();
            AssetDatabase.SaveAssets();
            var t = AssetDatabase.LoadAssetAtPath<FieldMedic.Scenarios.HintTable>(SceneBuilders.W4Builder.HintTablePath);
            bool ok = t != null && t.version == FieldMedic.Scenarios.HintTable.CurrentVersion &&
                      t.lessons.Exists(l => l.steps.Exists(s => s.actions.Exists(a => a.ru != null && a.ru.Contains("Зажми Y"))));
            Debug.Log("[FMVR] w10 patch: hint table v" + (t != null ? t.version : -1) + (ok ? " with the Y radio hint" : " — radio hint MISSING"));
            return ok;
        }
    }
}
