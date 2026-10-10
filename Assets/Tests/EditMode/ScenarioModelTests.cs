using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    public class ScenarioModelTests
    {
        static string Clock(float s) => $"{(int)(s / 60):00}:{(int)(s % 60):00}";

        [Test]
        public void Catalog_HasEightScenarios_WithTitlesAndWounds()
        {
            Assert.AreEqual(8, ScenarioCatalog.All.Count);
            for (int i = 0; i < 8; i++)
            {
                var d = ScenarioCatalog.All[i];
                Assert.AreEqual("S" + (i + 1), d.id);
                Assert.IsNotEmpty(d.titleRu); Assert.IsNotEmpty(d.titleKz);
                Assert.IsNotEmpty(d.briefingRu); Assert.IsNotEmpty(d.briefingKz);
                Assert.IsNotEmpty(d.wounds, d.id);
                Assert.IsNotEmpty(d.skillsRu, d.id);
            }
            Assert.IsTrue(ScenarioCatalog.Get("S1").polished && ScenarioCatalog.Get("S2").polished);
        }

        [Test]
        public void Variant_Mirrored_SwapsSides_AndTourniquetLimb()
        {
            var r = ScenarioCatalog.Resolve(ScenarioCatalog.Get("S1"), new ScenarioVariant { mirrored = true });
            Assert.AreEqual(BodyRegion.ThighL, r.wounds[0].region);
            Assert.AreEqual(BodyRegion.ForearmR, r.wounds[1].region);
            var s7 = ScenarioCatalog.Resolve(ScenarioCatalog.Get("S7"), new ScenarioVariant { mirrored = true });
            Assert.AreEqual("legL", s7.preTourniquet.limb);
            Assert.AreEqual(BodyRegion.Head, ScenarioCatalog.Mirror(BodyRegion.Head));
        }

        [Test]
        public void Variant_SecondHit_AddsOptionalComplication_DelayedOnsetDelaysMinorWound()
        {
            var d = ScenarioCatalog.Get("S1");
            var plain = ScenarioCatalog.Resolve(d, ScenarioVariant.Default);
            var hard = ScenarioCatalog.Resolve(d, new ScenarioVariant { secondHit = true, delayedOnset = true });
            Assert.IsFalse(plain.complications.Any(c => c.kind == ComplicationKind.SecondDrop));
            Assert.IsTrue(hard.complications.Any(c => c.kind == ComplicationKind.SecondDrop));
            Assert.That(hard.wounds.First(w => w.type == WoundType.Capillary).onsetDelay, Is.GreaterThan(0f));
            Assert.AreEqual(0f, d.wounds[1].onsetDelay, "the definition itself is not modified");
            var v1 = ScenarioVariant.Random(7); var v2 = ScenarioVariant.Random(7);
            Assert.AreEqual(v1.ToString(), v2.ToString(), "reproducible from the seed");
        }

        [Test]
        public void Timeline_RunsPhases_FiresComplications_EndsOnEvac()
        {
            var r = ScenarioCatalog.Resolve(ScenarioCatalog.Get("S1"), new ScenarioVariant { secondHit = true });
            var t = new ScenarioTimeline(r) { autoEvacAfter = 1000f };
            Assert.AreEqual(ScenarioState.Briefing, t.State);
            t.Tick(5f, Phase.CareUnderFire, false, Outcome.Ongoing, false);
            Assert.AreEqual(0f, t.SinceIncident);
            t.Incident();
            t.CasualtyDown(Phase.CareUnderFire);
            Assert.AreEqual(ScenarioState.CareUnderFire, t.State);
            var fired = t.Tick(21f, Phase.CareUnderFire, false, Outcome.Ongoing, false);
            Assert.IsTrue(fired.Any(c => c.kind == ComplicationKind.RadioLine && c.arg == "air"));
            fired = t.Tick(60f, Phase.CareUnderFire, false, Outcome.Ongoing, false);
            Assert.IsTrue(fired.Any(c => c.kind == ComplicationKind.SecondDrop));
            t.Tick(1f, Phase.TacticalFieldCare, false, Outcome.Ongoing, false);
            Assert.AreEqual(ScenarioState.TacticalFieldCare, t.State);
            t.Tick(1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false);
            Assert.AreEqual(ScenarioState.Evacuation, t.State);
            Assert.IsFalse(t.AutoEvac);
            t.Tick(1f, Phase.TacticalFieldCare, true, Outcome.Ongoing, false, evacScaledDt: 40f * 60f + 1f);
            Assert.AreEqual(ScenarioState.Debrief, t.State);
            Assert.AreEqual("evacuated", t.EndReason);
        }

        [Test]
        public void Timeline_AutoEvac_DeathAndPlayerHitEndRun()
        {
            var r = ScenarioCatalog.Resolve(ScenarioCatalog.Get("S4"), ScenarioVariant.Default);
            var t = new ScenarioTimeline(r) { autoEvacAfter = 10f };
            t.Incident(); t.CasualtyDown(Phase.TacticalFieldCare);
            t.Tick(11f, Phase.TacticalFieldCare, false, Outcome.Ongoing, false);
            Assert.AreEqual(ScenarioState.Evacuation, t.State);
            Assert.IsTrue(t.AutoEvac, "commander ordered the evacuation, not the medic");
            var t2 = new ScenarioTimeline(r); t2.Incident(); t2.CasualtyDown(Phase.CareUnderFire);
            t2.Tick(1f, Phase.CareUnderFire, false, Outcome.Died, false);
            Assert.AreEqual("died", t2.EndReason);
            var t3 = new ScenarioTimeline(r); t3.Incident();
            t3.Tick(1f, Phase.CareUnderFire, false, Outcome.Ongoing, true);
            Assert.AreEqual("player_hit", t3.EndReason);
        }

        [Test]
        public void S7_TqSlip_IsDeferredUntilAsked()
        {
            var t = new ScenarioTimeline(ScenarioCatalog.Resolve(ScenarioCatalog.Get("S7"), ScenarioVariant.Default));
            t.Incident(); t.CasualtyDown(Phase.CareUnderFire);
            Assert.IsFalse(t.Tick(500f, Phase.CareUnderFire, false, Outcome.Ongoing, false).Any(c => c.kind == ComplicationKind.TqSlip));
            Assert.AreEqual(1, t.TakeDeferred(ComplicationKind.TqSlip).Count);
            Assert.AreEqual(0, t.TakeDeferred(ComplicationKind.TqSlip).Count);
        }

        // ------------------------------------------------------------------ medical extensions
        [Test]
        public void Unconscious_Supine_DiesOfHypoxia_InThreeToFiveMinutes_NpaPrevents()
        {
            var p = new PatientPhysiology { tbiUnconscious = true };
            float t = 0f;
            while (p.outcome == Outcome.Ongoing && t < 600f) { p.Tick(1f); t += 1f; }
            Assert.AreEqual(Outcome.Died, p.outcome);
            Assert.That(t, Is.InRange(150f, 300f), "airway obstruction kills in ~3–4 min");
            StringAssert.Contains("Гипоксия", p.causeOfDeath);

            var q = new PatientPhysiology { tbiUnconscious = true };
            q.Advance(60f);
            q.InsertAirwayAdjunct();
            q.Advance(1200f);
            Assert.AreEqual(Outcome.Ongoing, q.outcome);
            Assert.That(q.SpO2, Is.GreaterThan(90f));
        }

        [Test]
        public void Vomit_Supine_IsAspirated_EvenWithNpa_RecoveryPositionDrains()
        {
            var p = new PatientPhysiology { tbiUnconscious = true };
            p.InsertAirwayAdjunct();
            p.Vomit();
            Assert.IsTrue(p.AirwayCompromised, "NPA does not protect against vomit");
            float t = 0f;
            while (p.outcome == Outcome.Ongoing && t < 600f) { p.Tick(1f); t += 1f; }
            Assert.AreEqual(Outcome.Died, p.outcome);
            StringAssert.Contains("Аспирация", p.causeOfDeath);
            Assert.That(t, Is.LessThan(160f), "aspiration is faster");

            var q = new PatientPhysiology { tbiUnconscious = true };
            q.InsertAirwayAdjunct();
            var cp = new CareProtocol(q, Phase.TacticalFieldCare);
            Assert.AreEqual("recovery", cp.Perform(MedAction.RecoveryPosition).code);
            q.Vomit();
            q.Tick(0.1f);
            Assert.IsFalse(q.vomitInMouth, "on the side the vomit drains");
            q.Advance(900f);
            Assert.AreEqual(Outcome.Ongoing, q.outcome);

            var z = new PatientPhysiology { tbiUnconscious = true, vomitInMouth = true };
            new CareProtocol(z, Phase.TacticalFieldCare).Perform(MedAction.ClearMouth);
            Assert.IsFalse(z.vomitInMouth);
        }

        [Test]
        public void DelayedOnset_StumpBleedsAfterSpasm_FiresRebleed()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.TraumaticAmputation, region = BodyRegion.FootR, onsetDelaySeconds = 45f };
            p.wounds.Add(w);
            string ev = null;
            p.OnEvent += e => { if (e == "rebleed") ev = e; };
            p.Advance(40f);
            float early = p.bloodLostMl;
            Assert.That(early, Is.LessThan(60f), "vasospasm: only a trickle");
            p.Advance(10f);
            Assert.AreEqual("rebleed", ev);
            Assert.That(p.bloodLostMl - early, Is.GreaterThan(50f), "massive bleed after the spasm");
        }

        [Test]
        public void Amputation_NoDistalPulse_ConversionForbidden()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.TraumaticAmputation, region = BodyRegion.FootR });
            var tq = new TourniquetState(); tq.PreApply("legR", 0.2f, 4, true, 0f);
            p.AddTourniquet(tq);
            Assert.AreEqual(0f, p.PulseStrength(PulseSite.PedalR));
            Assert.AreEqual("conv_amputation", TourniquetConversion.Check(p, tq, 1800f));
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("conv_amputation", cp.ConvertTourniquet(tq).code);
            Assert.IsTrue(tq.Locked, "forbidden conversion leaves the band tight");
            Assert.IsTrue(cp.penalties.ContainsKey("conv_amputation"));
        }

        [Test]
        public void PosteriorWound_FoundOnlyByLogRoll()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestR });
            p.wounds.Add(new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestR, posterior = true });
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("sweep_front", cp.Perform(MedAction.BloodSweep).code);
            Assert.IsFalse(p.wounds[1].discovered);
            Assert.AreEqual("log_roll_found", cp.Perform(MedAction.LogRoll).code);
            Assert.IsTrue(p.wounds[1].discovered);
        }

        [Test]
        public void Conversion_VenousWoundDressed_NoShock_Succeeds()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.Venous, region = BodyRegion.ShinR };
            p.wounds.Add(w);
            var tq = new TourniquetState(); tq.PreApply("legR", 0.2f, 4, true, 0f);
            p.AddTourniquet(tq);
            p.Advance(1800f);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("conv_no_dressing", TourniquetConversion.Check(p, tq, 1800f));
            w.control = BleedControl.PressureDressing;
            var fb = cp.ConvertTourniquet(tq);
            Assert.AreEqual("converted", fb.code, fb.text);
            Assert.IsFalse(tq.Locked, "band loosened but left on the limb");
            Assert.IsTrue(tq.OnLimb);
            Assert.IsTrue(w.IsControlled, "the dressing holds the venous bleed");
            Assert.IsTrue(p.tourniquetConverted);
            Assert.IsFalse(cp.penalties.ContainsKey("tq_not_reassessed"));
        }

        [Test]
        public void Conversion_InShock_Or_After6h_Forbidden_ArterialRebleeds()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.Venous, region = BodyRegion.ShinR, control = BleedControl.PressureDressing });
            var tq = new TourniquetState(); tq.PreApply("legR", 0.2f, 4, true, 0f);
            p.AddTourniquet(tq);
            p.bloodLostMl = p.BloodVolumeMl * 0.33f;
            Assert.AreEqual("conv_shock", TourniquetConversion.Check(p, tq, 1800f));
            p.bloodLostMl = 0f;
            Assert.AreEqual("conv_too_late", TourniquetConversion.Check(p, tq, 6.5f * 3600f));

            var a = new PatientPhysiology();
            var aw = new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR, control = BleedControl.PressureDressing, limbPosition = 0.5f };
            a.wounds.Add(aw);
            var t2 = new TourniquetState(); t2.PreApply("legR", 0.15f, 4, true, 0f);
            a.AddTourniquet(t2);
            var cp = new CareProtocol(a, Phase.TacticalFieldCare);
            Assert.AreEqual("conv_rebleed", cp.ConvertTourniquet(t2).code);
            Assert.IsTrue(a.conversionFailed);
            Assert.IsFalse(aw.IsControlled, "artery bleeds again");
            Assert.AreEqual("conv_after_fail", TourniquetConversion.Check(a, t2, 100f));
        }

        [Test]
        public void TourniquetSlip_Loosens_RetightenRestoresOcclusion()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR, limbPosition = 0.5f };
            p.wounds.Add(w);
            var tq = new TourniquetState(); tq.PreApply("legR", 0.15f, 4, true, 0f);
            p.AddTourniquet(tq);
            Assert.IsTrue(w.IsControlled);
            tq.Loosen(); p.RefreshTourniquets();
            Assert.IsFalse(w.IsControlled, "a slipped band leaks");
            tq.PreApply("legR", 0.15f, 5, true, 0f);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("tq_retightened", cp.TourniquetRetightened(tq).code);
            Assert.IsTrue(w.IsControlled);
        }

        [Test]
        public void Debrief_ListsMistakesWithConsequences_LimbOutcome_Best()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            cp.Perform(MedAction.Tourniquet, null, BodyRegion.ThighR);
            cp.Perform(MedAction.MoveToCover);
            cp.ScenarioMistake("player_exposed");
            p.Advance(600f);
            var r = DebriefBuilder.Build(ScenarioCatalog.Get("S1"), cp, "evacuated", Clock);
            Assert.IsTrue(r.survived);
            Assert.That(r.score, Is.LessThan(100));
            Assert.IsTrue(r.mistakes.Any(m => m.code == "tq_not_marked" && m.consequence.Length > 0));
            Assert.IsTrue(r.mistakes.Any(m => m.code == "player_exposed"));
            Assert.IsTrue(r.mistakes.Any(m => m.code == "no_gloves") == false, "gloves are not judged under fire");
            StringAssert.Contains("Правая нога", r.limbOutcome);
            Assert.That(r.timeline.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(r.bleedControlSeconds, Is.EqualTo(0f).Within(1f));
            Assert.IsTrue(DebriefBuilder.UpdateBest(r, 40));
            Assert.AreEqual(r.score, r.bestScore);
            Assert.IsFalse(DebriefBuilder.UpdateBest(r, 100));

            var dead = new PatientPhysiology();
            dead.wounds.Add(new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var cp2 = new CareProtocol(dead, Phase.CareUnderFire);
            dead.Advance(900f);
            var r2 = DebriefBuilder.Build(ScenarioCatalog.Get("S1"), cp2, "died", Clock);
            Assert.IsFalse(r2.survived);
            Assert.AreEqual(0, r2.score);
            StringAssert.Contains("погиб", r2.outcomeText);
            Assert.AreEqual("ГИБЕЛЬ", r2.Grade(false));
        }
    }
}
