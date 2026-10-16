using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// <summary>w8 hands-on assessment model: what pulse, breathing, AVPU and capillary refill tell the medic.</summary>
    public class VitalsAssessmentTests
    {
        static PatientPhysiology Lost(float fraction)
        {
            var p = new PatientPhysiology();
            p.bloodLostMl = p.BloodVolumeMl * fraction;
            return p;
        }

        [Test]
        public void Pulse_Healthy_Good_Carotid_And_Radial()
        {
            var p = Lost(0f);
            var c = VitalsAssessment.ReadPulse(p, PulseSite.Carotid);
            var r = VitalsAssessment.ReadPulse(p, PulseSite.RadialL);
            Assert.AreEqual(PulseQuality.Good, c.quality);
            Assert.AreEqual(PulseQuality.Good, r.quality);
            Assert.That(r.rate, Is.InRange(70, 90));
            Assert.AreEqual(0, r.rate % 2, "counted to 2 beats like a trainee");
        }

        [Test]
        public void Pulse_Shock_WeakFast_RadialAbsentBelow80()
        {
            var p = Lost(0.3f);                                  // class II–III: fast, weaker
            var r = VitalsAssessment.ReadPulse(p, PulseSite.RadialR);
            Assert.That(r.rate, Is.GreaterThan(110));
            Assert.That(r.quality, Is.EqualTo(PulseQuality.Weak).Or.EqualTo(PulseQuality.Thready));
            var deep = Lost(0.4f);
            Assert.Less(deep.SystolicBP, 80f);
            Assert.IsFalse(VitalsAssessment.ReadPulse(deep, PulseSite.RadialR).Present, "radial pulse gone below SBP ~80");
            Assert.IsTrue(VitalsAssessment.ReadPulse(deep, PulseSite.Carotid).Present, "carotid still there");
            StringAssert.Contains("нет", VitalsAssessment.PulseValue(VitalsAssessment.ReadPulse(deep, PulseSite.RadialR), false));
            StringAssert.Contains("жоқ", VitalsAssessment.PulseValue(VitalsAssessment.ReadPulse(deep, PulseSite.RadialR), true));
            StringAssert.Contains("частый", VitalsAssessment.PulseValue(r, false));
        }

        [Test]
        public void Breathing_Tension_FastAndShallow_DecompressionRecovers()
        {
            var p = Lost(0.05f);
            p.InsertAirwayAdjunct();   // isolate the chest: hypoxic unconsciousness would also block the airway
            var normal = VitalsAssessment.ReadBreathing(p);
            Assert.IsFalse(normal.Fast);
            Assert.IsFalse(normal.Shallow);
            p.pneumo = PneumoState.Tension;
            p.tensionSeverity = 0.8f;
            var t = VitalsAssessment.ReadBreathing(p);
            Assert.IsTrue(t.Fast, "tension: fast");
            Assert.IsTrue(t.Shallow, "tension: shallow");
            StringAssert.Contains("поверхностное", VitalsAssessment.BreathValue(t, false));
            Assert.IsTrue(p.NeedleDecompress());
            for (int i = 0; i < 60; i++) p.Tick(1f);
            var after = VitalsAssessment.ReadBreathing(p);
            Assert.Greater(after.depth, t.depth + 0.2f, "decompressed: deeper");
            Assert.Less(after.rate, t.rate, "decompressed: slower");
        }

        [Test]
        public void Breathing_OpenChest_Shallower_Sealed_Better()
        {
            var p = Lost(0f);
            p.pneumo = PneumoState.Open;
            float open = p.BreathDepth;
            p.pneumo = PneumoState.Sealed;
            Assert.Greater(p.BreathDepth, open, "sealed: the chest moves better");
        }

        [Test]
        public void Breathing_ObstructedAirway_Snoring()
        {
            var p = Lost(0f);
            p.airwayObstructed = true;
            p.tbiUnconscious = true;
            var b = VitalsAssessment.ReadBreathing(p);
            Assert.IsTrue(b.snoring);
            StringAssert.Contains("храп", VitalsAssessment.BreathValue(b, false));
        }

        [Test]
        public void CapillaryRefill_Normal_Under2_Shock_Over3_Cold_Longer()
        {
            Assert.Less(VitalsAssessment.CapillaryRefillSeconds(Lost(0f)), 2f);
            float shock = VitalsAssessment.CapillaryRefillSeconds(Lost(0.35f));
            Assert.Greater(shock, 3f);
            var cold = Lost(0.35f);
            cold.coreTempC = 34f;
            Assert.Greater(VitalsAssessment.CapillaryRefillSeconds(cold), shock);
            var dead = Lost(0.6f);
            dead.outcome = Outcome.Died;
            Assert.IsTrue(float.IsInfinity(VitalsAssessment.CapillaryRefillSeconds(dead)));
            StringAssert.Contains("шок", VitalsAssessment.RefillValue(shock, false));
            StringAssert.Contains("норма", VitalsAssessment.RefillValue(1.5f, false));
        }

        [Test]
        public void Avpu_Responses_ByState()
        {
            Assert.AreEqual(AvpuReaction.Talks, VitalsAssessment.Respond(Consciousness.Alert, Stimulus.Voice).reaction);
            var v = VitalsAssessment.Respond(Consciousness.Voice, Stimulus.Voice);
            Assert.AreEqual(AvpuReaction.Mumbles, v.reaction);
            Assert.IsTrue(v.eyesOpen);
            Assert.AreEqual(AvpuReaction.None, VitalsAssessment.Respond(Consciousness.Pain, Stimulus.Voice).reaction, "P: no answer to the voice");
            var pp = VitalsAssessment.Respond(Consciousness.Pain, Stimulus.Pain);
            Assert.AreEqual(AvpuReaction.Groans, pp.reaction);
            Assert.IsTrue(pp.withdraws);
            Assert.IsFalse(pp.eyesOpen);
            Assert.AreEqual(AvpuReaction.None, VitalsAssessment.Respond(Consciousness.Unresponsive, Stimulus.Pain).reaction);
            StringAssert.Contains("(V)", VitalsAssessment.AvpuValue(Consciousness.Voice, Stimulus.Voice, false));
            StringAssert.Contains("болью", VitalsAssessment.AvpuValue(Consciousness.Pain, Stimulus.Voice, false), "voice only: check with pain");
            StringAssert.Contains("(P)", VitalsAssessment.AvpuValue(Consciousness.Pain, Stimulus.Pain, true));
        }

        [Test]
        public void Protocol_Records_Assessments_And_Debrief_Lines()
        {
            var p = Lost(0.28f);
            var cp = new CareProtocol(p, Phase.TacticalFieldCare);
            p.secondsSinceStart = 52f;
            var r = VitalsAssessment.ReadPulse(p, PulseSite.Carotid);
            cp.RecordAssessment(AssessmentKind.Pulse, VitalsAssessment.SiteName(PulseSite.Carotid, false),
                VitalsAssessment.PulseValue(r, false), VitalsAssessment.PulseValue(r, true), VitalsAssessment.SiteName(PulseSite.Carotid, true));
            Assert.IsTrue(cp.Assessed(AssessmentKind.Pulse));
            Assert.IsFalse(cp.Assessed(AssessmentKind.Breathing));
            Assert.IsTrue(cp.log.Any(e => e.code == "chk_pulse"));
            var rep = DebriefBuilder.Build(null, cp, "evacuated", t => t.ToString("0"), incidentAt: 10f);
            Assert.AreEqual(1, rep.checks.Count);
            string line = rep.checks[0];
            StringAssert.StartsWith("Пульс проверен (на шее) в 0:42 — ", line);
            StringAssert.Contains(r.rate.ToString(), line);
            var kz = DebriefBuilder.Build(null, cp, "evacuated", t => t.ToString("0"), incidentAt: 10f, kz: true);
            StringAssert.Contains("(0:42)", kz.checks[0]);
            StringAssert.Contains("мойында", kz.checks[0]);
        }

        [Test]
        public void Training_Assess_Lesson_RU_KZ()
        {
            TrainingCatalog.ResetDefaults();
            CollectionAssert.Contains(TrainingCatalog.Topics, "assess");
            var l = TrainingCatalog.Get("assess");
            Assert.IsNotNull(l);
            CollectionAssert.AreEqual(new[] { "avpu", "pulse_neck", "pulse_wrist", "breath", "cap_refill" }, l.steps.Select(s => s.id).ToArray());
            foreach (var s in l.steps)
            {
                Assert.IsNotEmpty(s.ru); Assert.IsNotEmpty(s.kz);
                Assert.GreaterOrEqual(s.actions.Count, 2, s.id);
                foreach (var a in s.actions) { Assert.IsNotEmpty(a.ru); Assert.IsNotEmpty(a.kz); }
            }
            Assert.Greater(l.startLossFraction, 0.15f, "visible shock in the assessment lesson");
            var p = Lost(l.startLossFraction);
            Assert.AreEqual(Consciousness.Voice, p.AVPU, "answers the voice: the AVPU step is teachable");
            Assert.IsTrue(p.RadialPulsePresent, "weak but present radial pulse");
        }
    }
}
