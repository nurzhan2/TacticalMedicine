using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    /// <summary>w10b: scenario hints (the coach), the 10-second holds, the skipped-step flag, the TACEVAC briefing line.</summary>
    public class W10bCoachTests
    {
        static CareProtocol Make(Phase phase, params Wound[] wounds)
        {
            var p = new PatientPhysiology();
            foreach (var w in wounds) p.wounds.Add(w);
            return new CareProtocol(p, phase);
        }

        static TourniquetState Band(CareProtocol c, string limb, float appliedAt = 0f)
        {
            var tq = new TourniquetState();
            tq.PreApply(limb, 0.12f, 5, false, appliedAt);
            c.TourniquetApplied(tq);
            return tq;
        }

        [Test]
        public void UnderFire_ArterialThigh_AsksForTourniquetThenCover()
        {
            var c = Make(Phase.CareUnderFire, new Wound { id = "a", type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var s = ScenarioCoach.Next(c);
            Assert.AreEqual("tq_lock", s.id);
            Assert.AreEqual("CAT", s.item);
            StringAssert.Contains("бедро", s.ru);
            Band(c, "legR");
            Assert.IsTrue(c.patient.wounds[0].IsControlled, "5 half-turns occlude a thigh");
            s = ScenarioCoach.Next(c);
            Assert.AreEqual("drag_cover", s.id);
            Assert.IsNull(s.item);
        }

        [Test]
        public void Groin_UnderFire_GoesToCover_ThenPackHoldBandage()
        {
            var w = new Wound { id = "g", type = WoundType.Junctional, region = BodyRegion.GroinR, hiddenUnderClothing = true, discovered = true };
            var c = Make(Phase.CareUnderFire, w);
            var s = ScenarioCoach.Next(c);
            Assert.AreEqual("drag_cover", s.id);
            StringAssert.Contains("Пах", s.ru);
            c.Perform(MedAction.MoveToCover);
            Assert.AreEqual("gloves", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.Gloves);
            Assert.AreEqual("exposed", ScenarioCoach.Next(c).id);
            Assert.AreEqual("Shears", ScenarioCoach.Next(c).item);
            w.exposed = true;
            s = ScenarioCoach.Next(c);
            Assert.AreEqual("packed", s.id);
            Assert.AreEqual("Gauze", s.item);
            StringAssert.Contains("пах", s.ru);
            c.Perform(MedAction.HemostaticPacking, w);
            s = ScenarioCoach.Next(c);
            Assert.AreEqual("held", s.id);
            StringAssert.Contains("10 секунд", s.ru);
            StringAssert.Contains("3 минут", s.kz);
            // 10 s at the training scale complete the 3-minute hold
            for (int i = 0; i < 100; i++) c.HoldPressure(w, 0.1f, HoldTimes.TrainingScale);
            Assert.AreEqual(BleedControl.PackedAndHeld, w.control);
            s = ScenarioCoach.Next(c);
            Assert.AreEqual("bandage", s.id);
            Assert.AreEqual("Bandage", s.item);
        }

        [Test]
        public void AfterTourniquet_PulseBelow_ThenTime_ThenBlanketCardRadio()
        {
            var c = Make(Phase.TacticalFieldCare, new Wound { id = "a", type = WoundType.ArterialLimb, region = BodyRegion.ShinL, discovered = true });
            c.Perform(MedAction.Gloves);
            Assert.AreEqual("tq_high", ScenarioCoach.Next(c).id);
            Band(c, "legL");
            Assert.AreEqual("pulse_foot", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.PulseCheck, site: BodyRegion.FootR);
            Assert.AreEqual("pulse_foot", ScenarioCoach.Next(c).id, "the other foot does not count");
            c.Perform(MedAction.PulseCheck, site: BodyRegion.FootL);
            var s = ScenarioCoach.Next(c);
            Assert.AreEqual("tq_time", s.id);
            Assert.AreEqual("Marker", s.item);
            c.Perform(MedAction.MarkTourniquetTime);
            Assert.AreEqual("blanket", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.ThermalBlanket);
            Assert.AreEqual("card", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.CasualtyCard);
            s = ScenarioCoach.Next(c);
            Assert.AreEqual("medevac", s.id);
            Assert.AreEqual("Radio", s.item);
            StringAssert.Contains("Y", s.ru);
            c.Perform(MedAction.MedevacRequest);
            s = ScenarioCoach.Next(c, CoachEvac.Waiting);
            Assert.AreEqual("wait", s.id);
            Assert.IsFalse(s.skippable);
            Assert.AreEqual("loaded", ScenarioCoach.Next(c, CoachEvac.Landed).id);
            Assert.AreEqual("board", ScenarioCoach.Next(c, CoachEvac.Aboard).id);
        }

        [Test]
        public void HiddenWounds_AskForSweepThenRoll_ChestAsksForSeal()
        {
            var back = new Wound { id = "b", type = WoundType.Venous, region = BodyRegion.ChestL, posterior = true };
            var front = new Wound { id = "f", type = WoundType.Venous, region = BodyRegion.ShinR };
            var c = Make(Phase.TacticalFieldCare, front, back);
            c.Perform(MedAction.Gloves);
            Assert.AreEqual("sweep", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.BloodSweep);
            Assert.AreEqual("rolled", ScenarioCoach.Next(c).id);
            c.Perform(MedAction.LogRoll);
            front.exposed = back.exposed = true;
            var s = ScenarioCoach.Next(c);
            Assert.AreEqual("seal_back", s.id);
            Assert.AreEqual("ChestSeal", s.item);
        }

        [Test]
        public void Tension_AsksForNeedle_Unconscious_AsksForAirway()
        {
            var c = Make(Phase.TacticalFieldCare);
            c.Perform(MedAction.Gloves);
            c.patient.pneumo = PneumoState.Tension;
            Assert.AreEqual("needle", ScenarioCoach.Next(c).id);
            c.patient.pneumo = PneumoState.Decompressed;
            c.patient.airwayObstructed = true;
            Assert.AreEqual("npa_in", ScenarioCoach.Next(c).id);
            c.patient.vomitInMouth = true;
            Assert.AreEqual("mouth", ScenarioCoach.Next(c).id);
        }

        [Test]
        public void HoldTimes_TenSecondsOutsideExam_RealInExam()
        {
            Assert.AreEqual(18f, HoldTimes.TrainingScale, 1e-3f);
            Assert.AreEqual(1f, HoldTimes.ScaleFor(true));
            Assert.AreEqual(HoldTimes.TrainingScale, HoldTimes.ScaleFor(false));
            StringAssert.Contains("3 минуты", HoldTimes.LineRu);
            StringAssert.Contains("10 секунд", HoldTimes.LineRu);
            StringAssert.Contains("10 секунд", HoldTimes.LineKz);
        }

        [Test]
        public void SkippedStep_IsMarkedInLogAndDebrief_WithoutPenalty()
        {
            var w = new Wound { id = "a", type = WoundType.ArterialLimb, region = BodyRegion.ThighR, discovered = true };
            var c = Make(Phase.TacticalFieldCare, w);
            c.Perform(MedAction.Gloves);
            Assert.IsFalse(c.Skipping);
            c.BeginSkip("tq_high", "Наложи жгут высоко");
            Assert.IsTrue(c.Skipping);
            Band(c, "legR");
            c.EndSkip();
            c.Perform(MedAction.MarkTourniquetTime);
            Assert.IsTrue(c.WasSkipped("tq_high"));
            Assert.IsFalse(c.WasSkipped("tq_time"));
            Assert.IsTrue(c.log.First(e => e.code == "tq_ok").skipped);
            Assert.IsFalse(c.log.First(e => e.code == "tq_marked").skipped);
            Assert.IsFalse(c.log.First(e => e.code == "gloves").skipped);
            int before = c.Score();
            var r = DebriefBuilder.Build(null, c, "evacuated", t => t.ToString("0"));
            Assert.AreEqual(1, r.SkippedCount);
            StringAssert.Contains("Наложи жгут высоко", r.skippedSteps[0]);
            Assert.IsTrue(r.timeline.Any(e => e.skipped && e.text.Contains("(пропущен)")));
            Assert.IsFalse(r.mistakes.Any(m => m.code == CareProtocol.SkippedCode));
            Assert.AreEqual(before, r.score, "skipping is shown, not scored");
        }

        [Test]
        public void EveryErlanScenarioBriefing_SaysTheHelicopterEnd()
        {
            foreach (var d in ScenarioCatalog.All)
            {
                if (d.selfAid) { StringAssert.DoesNotContain("вертолёт", d.Briefing(false).Substring(d.briefingRu.Length)); continue; }
                StringAssert.Contains("эвакуация вертолётом", d.Briefing(false), d.id);
                StringAssert.Contains("зажми Y", d.Briefing(false), d.id);
                StringAssert.Contains("тікұшақ", d.Briefing(true), d.id);
                StringAssert.StartsWith(d.briefingRu, d.Briefing(false), d.id);
            }
        }
    }
}
