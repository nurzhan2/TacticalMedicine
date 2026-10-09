using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    public class PhysiologyTests
    {
        static PatientPhysiology Make(WoundType t, BodyRegion r)
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { id = "w0", type = t, region = r });
            if (t == WoundType.PenetratingChest) p.pneumo = PneumoState.Open;
            return p;
        }

        static float RunUntilDeath(PatientPhysiology p, float maxSec, System.Action<float> step = null)
        {
            float t = 0;
            while (t < maxSec && p.outcome == Outcome.Ongoing) { step?.Invoke(t); p.Tick(0.5f); t += 0.5f; }
            return t;
        }

        [Test]
        public void UntreatedFemoralBleed_KillsWithinMinutes()
        {
            var p = Make(WoundType.ArterialLimb, BodyRegion.ThighR);
            float t = RunUntilDeath(p, 900);
            Assert.AreEqual(Outcome.Died, p.outcome);
            Assert.That(t, Is.InRange(180f, 480f), "arterial limb bleed should be fatal in ~3-8 min");
        }

        [Test]
        public void VenousBleed_IsNotRapidlyFatal()
        {
            var p = Make(WoundType.Venous, BodyRegion.ForearmR);
            RunUntilDeath(p, 1200);
            Assert.AreEqual(Outcome.Ongoing, p.outcome);
            Assert.Less(p.LossFraction, 0.2f);
        }

        [Test]
        public void EarlyTourniquet_StopsBleedAndSavesCasualty()
        {
            var p = Make(WoundType.ArterialLimb, BodyRegion.ThighR);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            RunUntilDeath(p, 600, t => { if (t == 40f) cp.Perform(MedAction.Tourniquet, null, BodyRegion.ThighR); });
            Assert.AreEqual(Outcome.Ongoing, p.outcome);
            Assert.Less(p.LossFraction, 0.10f);
            Assert.IsTrue(p.AllBleedingControlled);
        }

        [Test]
        public void SealedChest_WithoutDecompression_DevelopsTensionAndKills()
        {
            var p = Make(WoundType.PenetratingChest, BodyRegion.ChestR);
            RunUntilDeath(p, 900, t => { if (t == 30f) p.SealChest(p.wounds[0]); });
            Assert.AreEqual(Outcome.Died, p.outcome);
            StringAssert.Contains("пневмоторакс", p.causeOfDeath);
        }

        [Test]
        public void SealedChest_WithDecompression_Survives()
        {
            var p = Make(WoundType.PenetratingChest, BodyRegion.ChestR);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            RunUntilDeath(p, 900, t =>
            {
                if (t == 30f) cp.Perform(MedAction.ChestSeal, p.wounds[0]);
                if (p.pneumo == PneumoState.Tension && p.tensionSeverity > 0.3f) cp.Perform(MedAction.NeedleDecompression);
            });
            Assert.AreEqual(Outcome.Ongoing, p.outcome);
        }
    }

    public class ProtocolTests
    {
        [Test]
        public void TourniquetBelowWound_IsRejected()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            var fb = cp.Perform(MedAction.Tourniquet, null, BodyRegion.ShinR);
            Assert.AreEqual("tq_below_wound", fb.code);
            Assert.IsFalse(p.AllBleedingControlled);
        }

        [Test]
        public void TourniquetForGroinWound_IsRejectedAsJunctional()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.Junctional, region = BodyRegion.GroinR });
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("tq_junctional", cp.Perform(MedAction.Tourniquet, null, BodyRegion.ThighR).code);
        }

        [Test]
        public void TourniquetOnVenousBleed_IsPenalizedAsUnneeded()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.Venous, region = BodyRegion.ForearmR });
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("tq_unneeded", cp.Perform(MedAction.Tourniquet, null, BodyRegion.UpperArmR).code);
            Assert.IsTrue(cp.penalties.ContainsKey("tq_unneeded"));
        }

        [Test]
        public void GroinPacking_HeldThreeMinutes_ThenDressing_Controls()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.Junctional, region = BodyRegion.GroinR };
            p.wounds.Add(w);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.Perform(MedAction.Gloves);
            cp.Perform(MedAction.HemostaticPacking, w);
            for (float t = 0; t < 181f; t += 0.5f) { cp.HoldPressure(w, 0.5f); p.Tick(0.5f); }
            Assert.AreEqual(BleedControl.PackedAndHeld, w.control);
            Assert.AreEqual("dressing_ok", cp.Perform(MedAction.PressureDressing, w).code);
            Assert.IsTrue(w.IsControlled);
            Assert.AreEqual(Outcome.Ongoing, p.outcome);
        }

        [Test]
        public void UnderFire_OnlyTourniquetAllowed()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestR });
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            Assert.AreEqual("fire_non_tq", cp.Perform(MedAction.ChestSeal, p.wounds[0]).code);
            cp.Perform(MedAction.MoveToCover);
            Assert.AreEqual("sealed", cp.Perform(MedAction.ChestSeal, p.wounds[0]).code);
        }

        [Test]
        public void PerfectRun_ScoresHundred()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.Perform(MedAction.Gloves);
            cp.Perform(MedAction.Tourniquet, null, BodyRegion.ThighR);
            cp.Perform(MedAction.MarkTourniquetTime);
            cp.Perform(MedAction.BloodSweep);
            cp.Perform(MedAction.ThermalBlanket);
            Assert.AreEqual(100, cp.Score());
        }
    }
}
