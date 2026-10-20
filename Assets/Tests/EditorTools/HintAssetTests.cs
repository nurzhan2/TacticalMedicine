using System.Linq;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using NUnit.Framework;
using UnityEditor;
using FieldMedic.UI;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w10 audit: the game reads Resources/HintTable.asset when its version matches the code, so a hint text edited in
    /// Training.cs never reaches the player unless the asset is rebuilt (or the version bumped). Guard the two in sync.
    /// </summary>
    public class HintAssetTests
    {
        [Test]
        public void HintAsset_MatchesTheCodeDefaults()
        {
            var t = AssetDatabase.LoadAssetAtPath<HintTable>(EditorTools.SceneBuilders.W4Builder.HintTablePath);
            Assert.IsNotNull(t, "hint table asset exists");
            Assert.AreEqual(HintTable.CurrentVersion, t.version, "asset version = code version (rebuild with FieldMedic/w10/Rebuild hint table)");
            var defs = TrainingCatalog.Defaults().ToList();
            Assert.AreEqual(defs.Count, t.lessons.Count, "lesson count");
            for (int i = 0; i < defs.Count; i++)
            {
                var a = defs[i]; var b = t.lessons.FirstOrDefault(l => l.id == a.id);
                Assert.IsNotNull(b, "lesson " + a.id + " in the asset");
                Assert.AreEqual(a.steps.Count, b.steps.Count, a.id + " step count");
                for (int s = 0; s < a.steps.Count; s++)
                {
                    Assert.AreEqual(a.steps[s].actions.Count, b.steps[s].actions.Count, $"{a.id}/{a.steps[s].id} action count");
                    for (int k = 0; k < a.steps[s].actions.Count; k++)
                    {
                        Assert.AreEqual(a.steps[s].actions[k].ru, b.steps[s].actions[k].ru, $"{a.id}/{a.steps[s].id} RU");
                        Assert.AreEqual(a.steps[s].actions[k].kz, b.steps[s].actions[k].kz, $"{a.id}/{a.steps[s].id} KZ");
                    }
                }
            }
        }

        [Test]
        public void ExamHintsOff_IsForTheRunOnly_NotSaved()
        {
            bool saved = GameSettings.Hints;
            try
            {
                GameSettings.Hints = true;
                GameSettings.OverrideHintsForRun(false);
                Assert.IsFalse(GameSettings.Hints, "exam run: hints off");
                GameSettings.OverrideHintsForRun(null);
                Assert.IsTrue(GameSettings.Hints, "after the exam the player's own setting is back");
            }
            finally { GameSettings.OverrideHintsForRun(null); GameSettings.Hints = saved; }
        }
    }
}
