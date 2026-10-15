using System.Collections.Generic;
using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// <summary>w4: the lesson/hint table (topics, steps, actions, wrong-action reasons), the TACEVAC loading hold and the
    /// debrief additions (step titles, numbered mistakes, timeline strip).</summary>
    public class TrainingModelTests
    {
        static readonly string[] KnownIcons = { "run", "tq", "drag", "gloves", "pulse", "marker", "sweep", "bandage", "blanket", "card", "radio", "heli",
            "shears", "gauze", "press", "roll", "seal", "lungs", "needle", "npa" };

        [SetUp] public void Reset() => TrainingCatalog.ResetDefaults();

        [Test]
        public void Topics_AllExist_WithUniqueSteps()
        {
            foreach (var id in TrainingCatalog.Topics)
            {
                var l = TrainingCatalog.Get(id);
                Assert.IsNotNull(l, id);
                Assert.IsNotEmpty(l.titleRu); Assert.IsNotEmpty(l.titleKz);
                Assert.AreEqual(l.steps.Count, l.steps.Select(s => s.id).Distinct().Count(), id + ": step ids unique");
            }
            CollectionAssert.IsSubsetOf(new[] { "full", "tq", "pack", "drag", "chest" }, TrainingCatalog.Topics);
        }

        [Test]
        public void EveryStep_HasTitleActionsIconAndKnownWrongs()
        {
            foreach (var l in TrainingCatalog.All)
                foreach (var s in l.steps)
                {
                    string at = l.id + "/" + s.id;
                    Assert.IsNotEmpty(s.ru, at); Assert.IsNotEmpty(s.kz, at);
                    Assert.That(s.actions.Count, Is.InRange(1, 3), at + ": 1–3 short actions");
                    foreach (var a in s.actions) { Assert.IsNotEmpty(a.ru, at); Assert.IsNotEmpty(a.kz, at); Assert.LessOrEqual(a.ru.Length, 60, at + ": short"); }
                    Assert.Contains(s.icon, KnownIcons, at);
                    Assert.Contains(s.phase, new[] { "CUF", "TFC", "TACEVAC" }, at);
                    foreach (var w in s.watch)
                    {
                        Assert.IsTrue(TrainingCatalog.Wrongs.ContainsKey(w), at + ": reason for " + w);
                        Assert.IsNotEmpty(TrainingCatalog.Reason(w, true), w + " kz");
                    }
                }
        }

        [Test]
        public void FullRun_FollowsTccc_CufThenTfcThenTacevac()
        {
            var full = TrainingCatalog.Get("full");
            Assert.IsTrue(full.inTheOpen);
            Assert.IsTrue(full.secondWound);
            var phases = full.steps.Select(s => s.phase).ToList();
            int lastCuf = phases.LastIndexOf("CUF"), firstTfc = phases.IndexOf("TFC"), firstEvac = phases.IndexOf("TACEVAC");
            Assert.Less(lastCuf, firstTfc, "care under fire first");
            Assert.Less(phases.LastIndexOf("TFC"), firstEvac, "evacuation last");
            Assert.AreEqual("reach", full.steps[0].id);
            Assert.AreEqual("tq_lock", full.steps[1].id, "only the tourniquet under fire");
            Assert.AreEqual("drag_cover", full.steps[2].id);
            Assert.AreEqual("loaded", full.steps.Last().id);
            Assert.Less(full.IndexOf("gloves"), full.IndexOf("bandage2"));
            Assert.Less(full.IndexOf("sweep"), full.IndexOf("bandage2"), "find the wound before dressing it");
            Assert.Less(full.IndexOf("bandage2"), full.IndexOf("blanket"), "H after M");
            Assert.Less(full.IndexOf("blanket"), full.IndexOf("medevac"));
        }

        [Test]
        public void MistakeReasons_AreShared_WithTheDebriefSteps()
        {
            Assert.AreEqual("tq_lock", TrainingCatalog.StepForMistake("tq_below_wound").id);
            Assert.AreEqual("tq_lock", TrainingCatalog.StepForMistake("tq_slow").id);
            Assert.AreEqual("blanket", TrainingCatalog.StepForMistake("hypothermia").id);
            Assert.AreEqual("medevac", TrainingCatalog.StepForMistake("no_medevac").id);
            Assert.IsNull(TrainingCatalog.StepForMistake("nonsense"));
            Assert.That(TrainingCatalog.Reason("tq_below_wound", false), Does.Contain("ниже раны"));
            Assert.That(TrainingCatalog.Reason("tq_below_wound", true), Does.Contain("төмен"));
        }

        [Test]
        public void TableOverride_ReplacesTexts_ResetRestores()
        {
            var lessons = TrainingCatalog.Defaults();
            lessons[0].steps[0].ru = "ИЗМЕНЕНО";
            TrainingCatalog.Use(lessons, new List<HintWrong> { new("tq_below_wound", "X", "Y") });
            Assert.AreEqual("ИЗМЕНЕНО", TrainingCatalog.Get(lessons[0].id).steps[0].ru);
            Assert.AreEqual("Y", TrainingCatalog.Reason("tq_below_wound", true));
            TrainingCatalog.ResetDefaults();
            Assert.AreNotEqual("ИЗМЕНЕНО", TrainingCatalog.Get(lessons[0].id).steps[0].ru);
        }

        [Test]
        public void Progress_AdvancesOneStepAtATime_ActionsTickSticky()
        {
            var l = TrainingCatalog.Get("tq");
            var p = new LessonProgress(l);
            var done = new HashSet<string>();
            Assert.AreEqual(1, p.StepNumber);
            Assert.AreEqual(l.steps.Count, p.StepCount);
            Assert.IsFalse(p.TryAdvanceOne(done.Contains, 0f));
            done.Add("tq_grab"); done.Add("tq_high");
            Assert.IsTrue(p.TryAdvanceOne(done.Contains, 1f));
            Assert.AreEqual("tq_high", p.Step.id, "only one step per call: the card shows each green check");
            Assert.IsTrue(p.TryAdvanceOne(done.Contains, 2f));
            Assert.AreEqual("tq_twist", p.Step.id);
            var states = p.ActionStates(done.Contains);
            Assert.IsFalse(states[0]);
            done.Add("tq_strap");
            states = p.ActionStates(done.Contains);
            Assert.IsTrue(states[0], "strap pulled ticks its own box");
            done.Remove("tq_strap");
            Assert.IsTrue(p.ActionStates(done.Contains, states)[0], "a ticked box stays ticked");
        }

        [Test]
        public void Timeline_WaitsForLoading_WhenTacevacIsOn()
        {
            var t = new ScenarioTimeline(ScenarioCatalog.Resolve(ScenarioCatalog.Get("S1"), ScenarioVariant.Default)) { waitForLoading = true };
            t.Incident();
            t.CasualtyDown(Phase.TacticalFieldCare);
            t.Tick(1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false);
            Assert.AreEqual(ScenarioState.Evacuation, t.State);
            for (int i = 0; i < 400 && !t.EvacArrived; i++) t.Tick(1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false, 60f);
            Assert.IsTrue(t.EvacArrived);
            t.Tick(1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false, 60f);
            Assert.AreEqual(ScenarioState.Evacuation, t.State, "helicopter landed: waits for the loading");
            t.MarkLoaded();
            t.Tick(0.1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false, 0f);
            Assert.AreEqual(ScenarioState.Debrief, t.State);
            Assert.AreEqual("evacuated", t.EndReason);
        }

        [Test]
        public void Debrief_MistakesCarryStepTitles_NumberedInTimeOrder_KazakhReasons()
        {
            var p = new PatientPhysiology();
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            p.Advance(30f, 1f);
            cp.ScenarioMistake("player_exposed");
            p.Advance(60f, 1f);
            cp.ScenarioMistake("tq_slow");
            var r = DebriefBuilder.Build(ScenarioCatalog.Get("S1"), cp, "evacuated", t => t.ToString("0"), 10f, null, true);
            var exposed = r.mistakes.First(m => m.code == "player_exposed");
            var slow = r.mistakes.First(m => m.code == "tq_slow");
            Assert.AreEqual(1, exposed.number);
            Assert.AreEqual(2, slow.number);
            Assert.AreEqual(TrainingCatalog.Get("full").steps.First(s => s.id == "tq_lock").kz, slow.step);
            Assert.AreEqual(TrainingCatalog.Reason("tq_slow", true), slow.text);
            Assert.AreEqual(10f, r.startTime);
            Assert.Greater(r.endTime, r.startTime);
            Assert.That(r.TimelineX(slow.time), Is.InRange(0f, 1f));
            Assert.Greater(r.TimelineX(slow.time), r.TimelineX(exposed.time));
        }

        [Test]
        public void V2Scenarios_PlayOnTheSteppe_NoTrenchWords()
        {
            foreach (var d in ScenarioCatalog.All)
            {
                StringAssert.DoesNotContain("окоп", d.briefingRu.ToLowerInvariant(), d.id);
                StringAssert.DoesNotContain("блиндаж", d.briefingRu.ToLowerInvariant(), d.id);
                StringAssert.DoesNotContain("бруствер", d.briefingRu.ToLowerInvariant(), d.id);
                Assert.That(d.clockHour, Is.InRange(8, 18), d.id + ": overcast day on the steppe");
            }
            Assert.AreEqual(CasualtySpot.OpenFar, ScenarioCatalog.Get("S7").spot);
            Assert.AreEqual(CasualtySpot.FireBay, ScenarioCatalog.Get("S1").spot);
            Assert.That(ScenarioCatalog.All.Select(d => d.spot).Distinct().Count(), Is.GreaterThanOrEqualTo(4), "different positions");
        }
    }
}
