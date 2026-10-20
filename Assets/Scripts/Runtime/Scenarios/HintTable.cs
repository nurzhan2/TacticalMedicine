using System.Collections.Generic;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// The lesson/hint data table (GDD_V2 §3) as an editable asset: Resources/HintTable.asset, generated from the
    /// defaults in <see cref="TrainingCatalog"/> by the editor (FieldMedic ▸ Build hint table). The training hint card
    /// AND the debrief read their step titles and mistake reasons from it, so the texts stay consistent. A table
    /// written by an older code version (different <see cref="version"/>) is ignored.
    /// </summary>
    [CreateAssetMenu(menuName = "FieldMedic/Hint table")]
    public sealed class HintTable : ScriptableObject
    {
        public const int CurrentVersion = 5; // w9b: self tourniquet lesson
        public int version = CurrentVersion;
        public List<TrainingLesson> lessons = new();
        public List<HintWrong> wrongs = new();

        static bool _loaded;

        /// <summary>Apply the asset (once per session) before the catalog is read.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var t = Resources.Load<HintTable>("HintTable");
            if (t == null || t.version != CurrentVersion || t.lessons == null || t.lessons.Count == 0) return;
            TrainingCatalog.Use(t.lessons, t.wrongs);
        }
    }
}
