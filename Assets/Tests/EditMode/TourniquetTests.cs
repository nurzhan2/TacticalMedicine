using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// Pure-C# CAT state machine and its effect on the casualty model (GDD §5, RESEARCH §3).
    public class TourniquetTests
    {
        static (PatientPhysiology p, CareProtocol cp, Wound w) ThighBleed(Phase phase = Phase.CareUnderFire)
        {
            var p = new PatientPhysiology();
            var w = new Wound { id = "thigh", type = WoundType.ArterialLimb, region = BodyRegion.ThighR, limbPosition = 0.5f };
            p.wounds.Add(w);
            return (p, new CareProtocol(p, phase), w);
        }

        /// The full correct sequence: high & tight, strap pulled, 2 turns, locked, secured, time marked.
        static TourniquetState ApplyCorrectly(PatientPhysiology p, CareProtocol cp, float pos = 0.12f, float twistDeg = 720f)
        {
            var tq = new TourniquetState();
            tq.Pickup();
            Assert.IsTrue(tq.WrapAround("legR", 2.4f), "slid over the foot");
            Assert.IsTrue(tq.SlideTo(pos));
            Assert.IsTrue(tq.ConfirmPosition());
            p.AddTourniquet(tq);
            tq.PullStrap(15f);
            Assert.That(tq.slackCm, Is.LessThanOrEqualTo(TourniquetState.TightSlackCm));
            for (float d = 0; d < twistDeg; d += 30f) { tq.Twist(30f); p.RefreshTourniquets(); }
            Assert.IsTrue(tq.TryLock());
            Assert.AreEqual("tq_ok", cp.TourniquetApplied(tq).code);
            Assert.IsTrue(tq.SecureStrap());
            Assert.IsTrue(tq.MarkTime(p.secondsSinceStart));
            cp.TourniquetTimeMarked(tq);
            return tq;
        }

        [Test]
        public void Stages_AdvanceOnlyInOrder()
        {
            var tq = new TourniquetState();
            Assert.IsFalse(tq.ConfirmPosition());
            Assert.AreEqual(0f, tq.Twist(180f), "cannot twist in the pouch");
            tq.Pickup();
            Assert.AreEqual(TourniquetStage.Held, tq.stage);
            Assert.IsFalse(tq.TryLock());
            tq.WrapAround("legR", 0.2f);
            Assert.AreEqual(TourniquetStage.BandAroundLimb, tq.stage);
            tq.ConfirmPosition();
            tq.PullStrap(15f);
            Assert.AreEqual(TourniquetStage.StrapPulled, tq.stage);
            Assert.IsFalse(tq.SecureStrap(), "strap secured only after the windlass is in the clip");
            tq.Twist(200f);
            Assert.AreEqual(1, tq.HalfTurns);
            Assert.AreEqual(TourniquetStage.WindlassTwisted, tq.stage);
            Assert.IsTrue(tq.TryLock(), "20 deg from the clip snaps in");
            Assert.AreEqual(180f, tq.windlassDegrees, 1e-3f);
            Assert.AreEqual(0f, tq.Twist(90f), "locked windlass does not turn");
            Assert.IsTrue(tq.SecureStrap());
            Assert.IsTrue(tq.MarkTime(100f));
            Assert.AreEqual(TourniquetStage.TimeMarked, tq.stage);
        }

        [Test]
        public void HalfTurnCounting_EitherDirectionTightens()
        {
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("armL", 0.3f); tq.ConfirmPosition(); tq.PullStrap(15f);
            tq.Twist(-370f);
            Assert.AreEqual(2, tq.HalfTurns);
            Assert.IsTrue(tq.FullOcclusion, "arm: 1 full turn with a tight strap occludes");
            tq.Twist(5000f);
            Assert.That(tq.HalfTurns, Is.LessThanOrEqualTo(TourniquetState.MaxHalfTurns));
        }

        [Test]
        public void CorrectTourniquet_StopsBleeding_PulseGone_Survives()
        {
            var (p, cp, w) = ThighBleed();
            for (int i = 0; i < 40; i++) p.Tick(1f);
            Assert.That(p.PulseStrength(PulseSite.PedalR), Is.GreaterThan(0f), "pedal pulse before the TQ");
            var tq = ApplyCorrectly(p, cp);
            Assert.IsTrue(w.IsControlled);
            Assert.AreEqual(0f, p.CurrentFlowMlPerSec(w));
            Assert.AreEqual(0f, p.PulseStrength(PulseSite.PedalR), "no distal pulse under a correct CAT");
            Assert.That(p.PulseStrength(PulseSite.PedalL), Is.GreaterThan(0f), "other foot still has a pulse");
            Assert.That(p.tourniquetSeconds.ContainsKey("legR"));
            Assert.That(tq.appliedAtSeconds, Is.EqualTo(40f).Within(0.01f));
            for (int i = 0; i < 1800; i++) p.Tick(1f);
            Assert.AreEqual(Outcome.Ongoing, p.outcome, p.causeOfDeath);
            Assert.That(cp.Score(), Is.GreaterThanOrEqualTo(85), "correct sequence scores high: " + string.Join(",", cp.penalties.Keys));
            Assert.That(cp.penalties.ContainsKey("tq_not_marked"), Is.False);
        }

        [Test]
        public void StrapOnlyWithoutWindlass_VenousCongestion_BleedsMore()
        {
            var (p, _, w) = ThighBleed();
            float baseFlow = p.CurrentFlowMlPerSec(w);
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 0.15f); tq.ConfirmPosition(); tq.PullStrap(15f);
            p.AddTourniquet(tq);
            Assert.That(tq.Occlusion, Is.InRange(0.3f, 0.6f));
            Assert.That(p.CurrentFlowMlPerSec(w), Is.GreaterThan(baseFlow * 1.2f), "venous congestion: bleeds more than without a band");
            Assert.That(p.PulseStrength(PulseSite.PedalR), Is.GreaterThan(0f));
        }

        [Test]
        public void PartialTwist_ReducesButDoesNotStop_ScoreFlagsLooseTourniquet()
        {
            var (p, cp, w) = ThighBleed();
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 0.15f); tq.ConfirmPosition(); tq.PullStrap(15f);
            p.AddTourniquet(tq);
            float baseFlow = new Wound { type = WoundType.ArterialLimb }.BaseRateMlPerMin / 60f * p.Perfusion * p.CoagulopathyMultiplier;
            tq.Twist(370f); // 2 half-turns of the 4 a thigh needs
            p.RefreshTourniquets();
            float f = p.CurrentFlowMlPerSec(w);
            Assert.That(f, Is.GreaterThan(0f).And.LessThan(baseFlow), "partially reduced");
            Assert.IsTrue(tq.TryLock());
            Assert.AreEqual("tq_partial", cp.TourniquetApplied(tq).code);
            Assert.That(p.PulseStrength(PulseSite.PedalR), Is.GreaterThan(0f), "distal pulse still felt");
            cp.Score();
            Assert.IsTrue(cp.penalties.ContainsKey("tq_loose"));
        }

        [Test]
        public void SlackLeftInBand_NeedsMoreTurnsThanTheWindlassHas()
        {
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 0.15f); tq.ConfirmPosition();
            tq.Twist(TourniquetState.MaxHalfTurns * 180f);
            Assert.IsFalse(tq.FullOcclusion, "never pulled the strap: windlass runs out before occlusion");
        }

        [Test]
        public void TourniquetOnKnee_DoesNotOcclude_AndIsPenalized()
        {
            var p = new PatientPhysiology();
            var w = new Wound { type = WoundType.ArterialLimb, region = BodyRegion.ShinR, limbPosition = 1.4f };
            p.wounds.Add(w);
            var cp = new CareProtocol(p, Phase.CareUnderFire);
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 2.4f); tq.SlideTo(1.02f); tq.ConfirmPosition(); tq.PullStrap(15f);
            Assert.IsTrue(tq.OnJoint);
            tq.Twist(720f);
            p.AddTourniquet(tq);
            Assert.IsFalse(tq.FullOcclusion);
            Assert.IsFalse(w.IsControlled, "the knee shields the artery");
            tq.TryLock();
            Assert.AreEqual("tq_on_joint", cp.TourniquetApplied(tq).code);
            Assert.IsTrue(cp.penalties.ContainsKey("tq_on_joint"));
            for (int i = 0; i < 900 && p.outcome == Outcome.Ongoing; i++) p.Tick(1f);
            Assert.AreEqual(Outcome.Died, p.outcome);
        }

        [Test]
        public void TourniquetBelowWound_NoEffect()
        {
            var (p, cp, w) = ThighBleed();
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 1.5f); tq.ConfirmPosition(); tq.PullStrap(15f); tq.Twist(720f); tq.TryLock();
            Assert.AreEqual("tq_below_wound", cp.TourniquetApplied(tq).code);
            Assert.IsFalse(w.IsControlled);
        }

        [Test]
        public void SecondTourniquetAbove_ControlsWhatTheFirstMissed()
        {
            var (p, cp, w) = ThighBleed();
            var a = new TourniquetState();
            a.Pickup(); a.WrapAround("legR", 0.35f); a.ConfirmPosition(); a.PullStrap(15f); a.Twist(370f); a.TryLock();
            cp.TourniquetApplied(a);
            Assert.IsFalse(w.IsControlled);
            var b = new TourniquetState();
            b.Pickup(); b.WrapAround("legR", 0.2f); b.ConfirmPosition(); b.PullStrap(15f); b.Twist(720f); b.TryLock();
            cp.TourniquetApplied(b);
            Assert.IsTrue(w.IsControlled, "a full band wins over a partial one");
        }

        [Test]
        public void ReleasingUnlockedWindlass_UnwindsToLastClipPosition()
        {
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 0.2f); tq.ConfirmPosition(); tq.PullStrap(15f);
            tq.Twist(300f);
            Assert.IsFalse(tq.TryLock(), "120 deg off the clip");
            float lost = tq.ReleaseWindlass();
            Assert.AreEqual(120f, lost, 1e-3f);
            Assert.AreEqual(1, tq.HalfTurns);
        }

        [Test]
        public void RoughHandling_LoosensUnlockedBandMuchMore()
        {
            var loose = new TourniquetState(); loose.Pickup(); loose.WrapAround("legR", 0.2f); loose.ConfirmPosition(); loose.PullStrap(15f); loose.Twist(720f);
            var locked = new TourniquetState(); locked.Pickup(); locked.WrapAround("legR", 0.2f); locked.ConfirmPosition(); locked.PullStrap(15f); locked.Twist(720f);
            locked.TryLock(); locked.SecureStrap();
            Assert.IsTrue(loose.FullOcclusion && locked.FullOcclusion);
            for (int i = 0; i < 4; i++) { loose.Slip(1f); locked.Slip(1f); }
            Assert.IsFalse(loose.FullOcclusion, "loose band slipped");
            Assert.That(locked.Occlusion, Is.GreaterThan(loose.Occlusion));
        }

        [Test]
        public void TfcOverClothing_SmallPenalty_TimeMarkingRequired()
        {
            var (p, cp, _) = ThighBleed(Phase.TacticalFieldCare);
            cp.Perform(MedAction.Gloves);
            var tq = new TourniquetState();
            tq.Pickup(); tq.WrapAround("legR", 0.3f); tq.ConfirmPosition(); tq.PullStrap(15f); tq.Twist(720f); tq.TryLock();
            Assert.AreEqual("tq_ok", cp.TourniquetApplied(tq).code);
            Assert.IsTrue(cp.penalties.ContainsKey("tq_over_clothes"));
            cp.Score();
            Assert.IsTrue(cp.penalties.ContainsKey("tq_not_marked"));
        }

        [Test]
        public void LimbGeometry_DistancesAndJoints()
        {
            Assert.AreEqual(0.42f, LimbGeometry.Distance("legR", 0f, 1f), 1e-4f);
            Assert.AreEqual(0.42f * 0.5f + 0.40f * 0.5f, LimbGeometry.Distance("legR", 0.5f, 1.5f), 1e-4f);
            Assert.IsTrue(LimbGeometry.OnJoint(1.0f));
            Assert.IsTrue(LimbGeometry.OnJoint(2.1f));
            Assert.IsFalse(LimbGeometry.OnJoint(0.5f));
            Assert.AreEqual(BodyRegion.ShinL, LimbGeometry.RegionAt("legL", 1.4f));
            Assert.AreEqual(BodyRegion.UpperArmR, LimbGeometry.RegionAt("armR", 0.2f));
        }

        [Test]
        public void ElevatingLegInsteadOfTourniquet_IsAMistake()
        {
            var (p, cp, _) = ThighBleed();
            Assert.AreEqual("elevation", cp.Perform(MedAction.LimbElevation).code);
            Assert.IsTrue(cp.penalties.ContainsKey("elevation"));
        }
    }
}
