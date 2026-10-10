using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    public class PainModelTests
    {
        static PatientPhysiology ThighArterial()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { id = "t", type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            return p;
        }

        [Test]
        public void BaselineFromWorstWound()
        {
            var p = ThighArterial();
            p.wounds.Add(new Wound { id = "f", type = WoundType.Capillary, region = BodyRegion.ForearmL });
            var pain = new PainModel();
            float b = pain.ComputeBaseline(p);
            Assert.That(b, Is.GreaterThanOrEqualTo(PainModel.WoundPain(WoundType.ArterialLimb)));
            Assert.That(b, Is.LessThan(PainModel.WoundPain(WoundType.ArterialLimb) + 0.1f));
            Assert.That(new PainModel().ComputeBaseline(new PatientPhysiology()), Is.EqualTo(0f));
        }

        [Test]
        public void RoughHandlingNearWoundHurts_GentleOrFarDoesNot()
        {
            var rough = new PainModel();
            rough.AddHandling(0.05f, 1.2f, 0.2f);
            var gentle = new PainModel();
            gentle.AddHandling(0.05f, 0.1f, 0.2f);
            var far = new PainModel();
            far.AddHandling(0.6f, 1.2f, 0.2f);
            Assert.That(rough.spike, Is.GreaterThan(0.3f));
            Assert.That(gentle.spike, Is.LessThan(0.05f));
            Assert.That(far.spike, Is.EqualTo(0f));
        }

        [Test]
        public void TourniquetTwistSpikesAndRaisesBaseline_ScreamEventWhenAlert()
        {
            var p = ThighArterial();
            p.ApplyTourniquet(p.wounds[0]);
            var pain = new PainModel();
            int screams = 0;
            pain.OnPainSpike += _ => screams++;
            pain.Tick(p, 0.1f);
            float before = pain.baseline;
            for (int i = 0; i < 100; i++) pain.Tick(p, 0.1f);
            pain.TourniquetTwist("legR", 2);
            pain.Tick(p, 0.1f);
            Assert.That(pain.baseline, Is.GreaterThan(before));
            Assert.That(pain.level, Is.GreaterThanOrEqualTo(PainModel.ScreamThreshold));
            Assert.That(screams, Is.GreaterThanOrEqualTo(1));
            Assert.That(pain.TourniquetHalfTurns("legR"), Is.EqualTo(2));
        }

        [Test]
        public void SpikeDecays()
        {
            var p = new PatientPhysiology();
            var pain = new PainModel();
            pain.AddSpike(0.8f);
            for (int i = 0; i < 100; i++) pain.Tick(p, 0.1f);
            Assert.That(pain.spike, Is.LessThan(0.02f));
            Assert.That(pain.level, Is.LessThan(0.02f));
        }

        [Test]
        public void UnresponsiveExpressesNoPain_PainResponsiveOnlySpikes()
        {
            var p = ThighArterial();
            p.bloodLostMl = p.BloodVolumeMl * 0.45f; // class IV -> U
            Assert.That(p.AVPU, Is.EqualTo(Consciousness.Unresponsive));
            var pain = new PainModel();
            pain.AddSpike(1f);
            pain.Tick(p, 0.1f);
            Assert.That(pain.level, Is.EqualTo(0f));

            var q = ThighArterial();
            q.bloodLostMl = q.BloodVolumeMl * 0.34f;
            Assert.That(q.AVPU, Is.EqualTo(Consciousness.Pain));
            var pain2 = new PainModel();
            pain2.Tick(q, 0.1f);
            Assert.That(pain2.level, Is.EqualTo(0f), "baseline alone is not expressed at P");
            pain2.AddSpike(0.6f);
            pain2.Tick(q, 0.1f);
            Assert.That(pain2.level, Is.GreaterThan(0.3f));
        }
    }

    public class CasualtyPoseMapperTests
    {
        static PatientPhysiology Patient(float loss = 0f)
        {
            var p = new PatientPhysiology();
            p.bloodLostMl = p.BloodVolumeMl * loss;
            return p;
        }

        [Test]
        public void AlertInSeverePainWrithes()
        {
            Assert.That(CasualtyPoseMapper.LyingPose(Patient(), 0.8f, false), Is.EqualTo(CasualtyPose.LyingWrithing));
            Assert.That(CasualtyPoseMapper.LyingPose(Patient(), 0.4f, false), Is.EqualTo(CasualtyPose.LyingMoaning));
            Assert.That(CasualtyPoseMapper.LyingPose(Patient(), 0.1f, false), Is.EqualTo(CasualtyPose.LyingBreathless));
        }

        [Test]
        public void AvpuDrivesPose()
        {
            var v = Patient(0.2f);
            Assert.That(v.AVPU, Is.EqualTo(Consciousness.Voice));
            Assert.That(CasualtyPoseMapper.LyingPose(v, 0.3f, false), Is.EqualTo(CasualtyPose.LyingBreathless));
            var pr = Patient(0.34f);
            Assert.That(pr.AVPU, Is.EqualTo(Consciousness.Pain));
            Assert.That(CasualtyPoseMapper.LyingPose(pr, 0.7f, false), Is.EqualTo(CasualtyPose.LyingMoaning));
            Assert.That(CasualtyPoseMapper.LyingPose(pr, 0.0f, false), Is.EqualTo(CasualtyPose.LyingBreathless));
            var u = Patient(0.45f);
            Assert.That(CasualtyPoseMapper.LyingPose(u, 0f, false), Is.EqualTo(CasualtyPose.Unconscious));
            Assert.That(CasualtyPoseMapper.LyingPose(u, 0f, true), Is.EqualTo(CasualtyPose.RecoverySide));
        }

        [Test]
        public void DeadIsDying_ChestWoundsCoughOrStruggleForAir()
        {
            var d = Patient();
            d.outcome = Outcome.Died;
            Assert.That(CasualtyPoseMapper.LyingPose(d, 1f, false), Is.EqualTo(CasualtyPose.LyingDying));
            var open = Patient();
            open.pneumo = PneumoState.Open;
            Assert.That(CasualtyPoseMapper.LyingPose(open, 0.8f, false), Is.EqualTo(CasualtyPose.LyingCough));
            var tension = Patient();
            tension.pneumo = PneumoState.Tension;
            Assert.That(CasualtyPoseMapper.LyingPose(tension, 0.8f, false), Is.EqualTo(CasualtyPose.LyingBreathless));
        }

        [Test]
        public void FallMappingAndCategories()
        {
            Assert.That(CasualtyPoseMapper.FallFor(HitKind.Explosion), Is.EqualTo(CasualtyPose.HitFall_Explosion));
            Assert.That(CasualtyPoseMapper.FallFor(HitKind.Shot), Is.EqualTo(CasualtyPose.HitFall_Shot));
            Assert.That(CasualtyPoseMapper.FallFor(HitKind.KnockedOut), Is.EqualTo(CasualtyPose.KnockedOut));
            Assert.That(CasualtyPoseMapper.IsFall(CasualtyPose.HitFall_Shoulder), Is.True);
            Assert.That(CasualtyPoseMapper.IsFall(CasualtyPose.LyingWrithing), Is.False);
            Assert.That(CasualtyPoseMapper.IsLying(CasualtyPose.Unconscious), Is.True);
            Assert.That(CasualtyPoseMapper.IsLying(CasualtyPose.CrouchIdle), Is.False);
            // animator parameter contract: stable ints
            Assert.That((int)CasualtyPose.CrouchIdle, Is.EqualTo(0));
            Assert.That((int)CasualtyPose.SittingDazed, Is.EqualTo(14));
        }
    }

    public class BleedOutTimingTests
    {
        [Test]
        public void UntreatedArterialThighDiesIn3To7Minutes()
        {
            var p = new PatientPhysiology();
            p.wounds.Add(new Wound { id = "t", type = WoundType.ArterialLimb, region = BodyRegion.ThighR });
            float t = 0f;
            while (p.outcome == Outcome.Ongoing && t < 1800f) { p.Tick(0.5f); t += 0.5f; }
            Assert.That(p.outcome, Is.EqualTo(Outcome.Died));
            Assert.That(t, Is.InRange(180f, 420f));
        }

        [Test]
        public void TourniquetWithinAMinuteSurvivesHalfAnHour_FlowStops()
        {
            var p = new PatientPhysiology();
            var w = new Wound { id = "t", type = WoundType.ArterialLimb, region = BodyRegion.ThighR };
            p.wounds.Add(w);
            for (int i = 0; i < 90; i++) p.Tick(0.5f);
            Assert.That(p.CurrentFlowMlPerSec(w), Is.GreaterThan(5f));
            p.ApplyTourniquet(w);
            Assert.That(p.CurrentFlowMlPerSec(w), Is.EqualTo(0f));
            for (int i = 0; i < 3600; i++) p.Tick(0.5f);
            Assert.That(p.outcome, Is.EqualTo(Outcome.Ongoing));
            Assert.That(p.LossFraction, Is.LessThan(0.15f));
        }
    }
}
