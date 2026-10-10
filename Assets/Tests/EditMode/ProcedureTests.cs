using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// Packing, Israeli bandage, chest seal, needle and NPA step logic (pure C#).
    public class ProcedureTests
    {
        [Test]
        public void Packing_NeedsStartAndDeepStrokes_UntilFull()
        {
            var pk = new PackingProcedure(WoundType.Junctional);
            Assert.AreEqual(0f, pk.Stroke(4f), "gauze end not in the wound yet");
            pk.Start();
            Assert.AreEqual(0f, pk.Stroke(1f), "a shallow dab does not feed gauze");
            int n = 0;
            while (!pk.Complete && n < 100) { pk.Stroke(3f); n++; }
            Assert.IsTrue(pk.Complete);
            Assert.That(n, Is.InRange(8, 14), "groin wound: about ten strokes");
            Assert.That(pk.RemainingGauzeM, Is.GreaterThan(1.5f), "the rest of the roll stays outside");
        }

        [Test]
        public void GroinBleed_PackHoldBandage_Controlled()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.Junctional, region = BodyRegion.GroinR, exposed = true };
            p.wounds.Add(w);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.Perform(MedAction.Gloves);
            Assert.AreEqual("packed", cp.Perform(MedAction.HemostaticPacking, w).code);
            for (int i = 0; i < 180; i++) { cp.HoldPressure(w, 1f); p.Tick(1f); }
            Assert.AreEqual(BleedControl.PackedAndHeld, w.control);
            var bd = new BandageProcedure();
            Assert.IsTrue(bd.PlacePad());
            bd.Wrap(400f);
            Assert.IsFalse(bd.CanClip);
            Assert.IsTrue(bd.TwistBar());
            bd.Wrap(360f);
            Assert.IsFalse(bd.CanClip, "two wraps after the pressure bar");
            bd.Wrap(400f);
            Assert.IsTrue(bd.Clip());
            Assert.That(bd.Pressure, Is.GreaterThan(0.75f));
            Assert.AreEqual("dressing_ok", cp.Perform(MedAction.PressureDressing, w).code);
            Assert.IsTrue(w.IsControlled);
            Assert.IsFalse(cp.penalties.ContainsKey("not_exposed"));
        }

        [Test]
        public void ReleasingPackedWoundEarly_ResetsHold_AndPenalizes()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.Junctional, region = BodyRegion.GroinL, exposed = true };
            p.wounds.Add(w);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.Perform(MedAction.HemostaticPacking, w);
            for (int i = 0; i < 60; i++) cp.HoldPressure(w, 1f);
            cp.PressureReleasedEarly(w);
            Assert.AreEqual(0f, w.packingHoldSeconds);
            Assert.IsTrue(cp.penalties.ContainsKey("pressure_released"));
            for (int i = 0; i < 18; i++) cp.HoldPressure(w, 1f, 10f);
            Assert.AreEqual(BleedControl.PackedAndHeld, w.control, "training x10 hold");
        }

        [Test]
        public void PackingThroughClothing_IsPenalized()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.Junctional, region = BodyRegion.GroinR };
            p.wounds.Add(w);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.Perform(MedAction.HemostaticPacking, w);
            Assert.IsTrue(cp.penalties.ContainsKey("not_exposed"));
        }

        [Test]
        public void ChestSeal_Steps_EntryAndExit_ThenTension_NeedleSaves()
        {
            var p = new PatientPhysiology();
            var entry = new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestR };
            var exit = new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestR, id = "exit" };
            p.wounds.Add(entry); p.wounds.Add(exit);
            p.pneumo = PneumoState.Open;
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);

            var s = new ChestSealProcedure();
            Assert.IsFalse(s.Place(true), "still in the package");
            Assert.IsTrue(s.Tear()); Assert.IsTrue(s.Peel());
            Assert.IsFalse(s.Place(true), "bloody skin: wipe first");
            s.Wipe(); s.Wipe();
            Assert.IsTrue(s.Place(true));
            Assert.IsFalse(s.PressEdge()); Assert.IsFalse(s.PressEdge()); Assert.IsTrue(s.PressEdge());
            Assert.AreEqual("sealed_one", cp.ChestSealApplied(entry, s).code, "exit wound still open");
            Assert.AreEqual(PneumoState.Open, p.pneumo);

            var s2 = new ChestSealProcedure(); s2.Tear(); s2.Peel(); s2.Wipe(); s2.Wipe(); s2.Place(true);
            s2.PressEdge(); s2.PressEdge(); s2.PressEdge();
            Assert.AreEqual("sealed", cp.ChestSealApplied(exit, s2).code);
            Assert.AreEqual(PneumoState.Sealed, p.pneumo);

            for (int i = 0; i < 600 && p.pneumo != PneumoState.Tension; i++) p.Tick(1f);
            Assert.AreEqual(PneumoState.Tension, p.pneumo, "vented seal delays but does not prevent tension");
            Assert.That(p.secondsSinceStart, Is.GreaterThan(PatientPhysiology.SealedTensionDelay + 60f));
            for (int i = 0; i < 60; i++) p.Tick(1f);
            Assert.AreEqual("needle_wrong_site", cp.NeedleDecompression(NeedleSite.MedialToNippleLine, 5f, 6f, false).code);
            Assert.AreEqual("needle_through_seal", cp.NeedleDecompression(NeedleSite.SecondIcsMcl, 5f, 6f, true).code);
            Assert.AreEqual(PneumoState.Tension, p.pneumo);
            Assert.AreEqual("decompressed", cp.NeedleDecompression(NeedleSite.SecondIcsMcl, 8f, 7f, false).code);
            for (int i = 0; i < 1200; i++) p.Tick(1f);
            Assert.AreEqual(Outcome.Ongoing, p.outcome, p.causeOfDeath);
            Assert.That(p.tensionSeverity, Is.LessThan(0.05f));
        }

        [Test]
        public void Burp_RelievesTension()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.PenetratingChest, region = BodyRegion.ChestL };
            p.wounds.Add(w); p.pneumo = PneumoState.Open;
            p.SealChest(w);
            for (int i = 0; i < 200; i++) p.Tick(1f);
            Assert.AreEqual(PneumoState.Tension, p.pneumo);
            float sev = p.tensionSeverity;
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            Assert.AreEqual("burp", cp.Perform(MedAction.SealBurp).code);
            Assert.AreEqual(PneumoState.Sealed, p.pneumo);
            Assert.That(p.tensionSeverity, Is.LessThan(sev));
            var s = new ChestSealProcedure(); s.Tear(); s.Peel(); s.Wipe(); s.Wipe(); s.Place(false);
            s.PressEdge(); s.PressEdge(); s.PressEdge();
            Assert.IsTrue(s.Burp());
            Assert.IsFalse(s.Complete);
            Assert.IsTrue(s.PressEdge(), "one press re-seals the lifted edge");
        }

        [Test]
        public void Npa_DryOrUpwardStops_LubedPerpendicularPasses()
        {
            var n = new NpaProcedure { measured = true };
            n.Advance(10f, 5f);
            Assert.That(n.depthCm, Is.LessThan(NpaProcedure.FullDepthCm), "dry tube sticks");
            Assert.IsTrue(n.resistance);
            var m = new NpaProcedure { measured = true, lubed = true };
            m.Advance(10f, 70f);
            Assert.AreEqual(NpaProcedure.TurbinateDepthCm, m.depthCm, 1e-3f, "aimed up at the turbinates");
            m.Advance(3f, 5f);
            Assert.AreEqual(NpaProcedure.TurbinateDepthCm, m.depthCm, 1e-3f, "stuck until rotated");
            m.Rotate(25f); m.Rotate(25f);
            m.Advance(20f, 5f);
            Assert.IsTrue(m.Inserted);
            Assert.IsTrue(m.GoodTechnique);

            var p = new PatientPhysiology { airwayObstructed = true };
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            cp.NasalAirwayInserted(new NpaProcedure { measured = true, lubed = false, depthCm = 12f });
            Assert.IsTrue(p.airwayAdjunct);
            Assert.IsTrue(cp.penalties.ContainsKey("npa_technique"));
        }

        [Test]
        public void Pulses_FadeWithShock_CarotidLast()
        {
            var p = new PatientPhysiology();
            Assert.That(p.PulseStrength(PulseSite.RadialL), Is.GreaterThan(0.6f));
            p.bloodLostMl = p.BloodVolumeMl * 0.36f;
            Assert.AreEqual(0f, p.PulseStrength(PulseSite.RadialL), "no radial pulse in class III-IV");
            Assert.That(p.PulseStrength(PulseSite.Carotid), Is.GreaterThan(0f));
        }

        [Test]
        public void ObservationsAllowedUnderFire_NoPenalty()
        {
            var p = new PatientPhysiology();
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            Assert.AreEqual(Severity.Info, cp.Perform(MedAction.PulseCheck).severity);
            Assert.AreEqual("log_roll", cp.Perform(MedAction.LogRoll).code);
            Assert.AreEqual(0, cp.penalties.Count);
        }
    }
}
