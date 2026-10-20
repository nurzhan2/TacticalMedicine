using System.Collections.Generic;
using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    /// <summary>w9b self-aid drill model: calibrated bleed-out, cover/drone rules, self tourniquet, report, radio
    /// check-ins, rescue and the debrief checklist.</summary>
    public class SelfAidModelTests
    {
        static List<SelfAidCue> Run(SelfAidRun r, float seconds, bool cover, float dt = 0.1f)
        {
            var all = new List<SelfAidCue>();
            for (float t = 0f; t < seconds && !r.Ended; t += dt) all.AddRange(r.Tick(dt, cover));
            return all;
        }

        /// The band as a player puts it on: loop over the hand, slid high, strap, windlass, clip, time strap.
        static void ApplyTq(SelfAidRun r, float pos = 0.2f, int halfTurns = 3, bool mark = true)
        {
            r.tq.Pickup();
            Assert.IsTrue(r.Wrap(2.1f), "loop over the hand");
            Assert.IsTrue(r.tq.SlideTo(pos));
            Assert.IsTrue(r.tq.ConfirmPosition());
            r.tq.PullStrap(16f);
            r.tq.Twist(halfTurns * 180f);
            Assert.IsTrue(r.tq.TryLock(), "rod in the clip");
            r.tq.SecureStrap();
            if (mark) Assert.IsTrue(r.tq.MarkTime(r.Clock));
        }

        static void Report(SelfAidRun r, bool allCorrect = true)
        {
            for (int i = 0; i < SelfAidRun.ReportSteps; i++)
            {
                var opts = r.ReportOptions();
                Assert.That(opts.Count, Is.InRange(2, 3));
                Assert.AreEqual(1, opts.Count(o => o.correct), "one right line per step " + i);
                r.ChooseReport(allCorrect || i != 1 ? opts.First(o => o.correct) : opts.First(o => !o.correct));
            }
            Assert.IsTrue(r.ReportSent);
        }

        [Test]
        public void Untreated_ArmBleedsOutInAboutThreeMinutes_ThighInTwo()
        {
            var arm = new SelfAidRun(new SelfAidConfig { limb = SelfAidLimb.Arm });
            var cues = Run(arm, 400f, true, 0.25f);
            Assert.AreEqual("died", arm.EndReason);
            Assert.That(arm.Clock, Is.InRange(165f, 195f), "arm ~3 min");
            Assert.Contains(SelfAidCue.Died, cues);
            Assert.That(arm.LethalFraction, Is.GreaterThan(0.95f));

            var thigh = new SelfAidRun(new SelfAidConfig { limb = SelfAidLimb.Thigh });
            Run(thigh, 400f, true, 0.25f);
            Assert.AreEqual("died", thigh.EndReason);
            Assert.That(thigh.Clock, Is.InRange(108f, 132f), "thigh ~2 min");
        }

        [Test]
        public void HighAndTightTourniquet_StopsTheBleeding_LooseBandMakesItWorse()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 5f, true);
            float open = r.FlowMlPerSec;
            r.tq.Pickup();
            r.Wrap(2.1f);
            r.tq.SlideTo(0.2f);
            r.tq.ConfirmPosition();
            r.tq.PullStrap(6f); // strap only: veins shut, the artery still pumps
            Run(r, 0.2f, true);
            Assert.Greater(r.FlowMlPerSec, open, "venous congestion");
            r.tq.PullStrap(10f);
            r.tq.Twist(540f);
            Assert.IsTrue(r.tq.TryLock());
            var cues = Run(r, 1f, true);
            Assert.AreEqual(0f, r.FlowMlPerSec, 1e-4f);
            Assert.IsTrue(r.HighAndTight);
            Assert.Contains(SelfAidCue.TqLocked, cues);
            Assert.Contains(SelfAidCue.BleedingStopped, cues);
            Assert.That(r.patient.PulseStrength(PulseSite.RadialL), Is.EqualTo(0f), "no pulse in the wounded hand");
        }

        [Test]
        public void BandBelowAnUpperArmWound_DoesNotStopIt()
        {
            var r = new SelfAidRun(new SelfAidConfig { upperArmWound = true });
            Assert.AreEqual(BodyRegion.UpperArmL, r.wound.region);
            ApplyTq(r, pos: 1.4f); // on the forearm
            Run(r, 1f, true);
            Assert.Greater(r.FlowMlPerSec, 1f);
            Assert.IsFalse(r.BleedingControlled);
        }

        [Test]
        public void InTheOpen_DroneComesBack_ThenKills()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            var cues = Run(r, 10.2f, false);
            Assert.Contains(SelfAidCue.DroneWarning, cues);
            Assert.Contains(SelfAidCue.DronePass, cues);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_stayed_open"));
            Assert.IsFalse(r.Ended);
            cues = Run(r, 10.5f, false);
            Assert.Contains(SelfAidCue.Killed, cues);
            Assert.AreEqual("killed", r.EndReason);
            var rep = SelfAidDebrief.Build(ScenarioCatalog.Get("SA"), r, s => "14:05", false);
            Assert.IsFalse(rep.survived);
            Assert.LessOrEqual(rep.score, 20);
            Assert.That(rep.outcomeText, Does.Contain("Сначала укрытие"));
        }

        [Test]
        public void ReachingCover_ResetsTheExposure_LeavingItIsAMistake()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 8f, false);
            var cues = Run(r, 1f, true);
            Assert.Contains(SelfAidCue.CoverReached, cues);
            Assert.AreEqual(0f, r.OpenSeconds);
            Run(r, 4f, false);
            Assert.IsTrue(r.LeftCover);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_left_cover"));
            Assert.IsFalse(r.Mistakes.ContainsKey("sa_stayed_open"));
        }

        [Test]
        public void TourniquetInTheOpen_IsRecorded()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 1f, false);
            r.tq.Pickup();
            r.Wrap(2.1f);
            Assert.IsTrue(r.TqWrappedInOpen);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_tq_in_open"));
        }

        [Test]
        public void CorrectRun_CoverTqTimeReportBlanketCheckIns_Rescued_FullChecklist()
        {
            var r = new SelfAidRun(new SelfAidConfig { seed = 5 });
            Run(r, 4f, false);
            var cues = Run(r, 3f, true);
            Assert.Contains(SelfAidCue.CoverReached, cues);
            ApplyTq(r);
            cues = Run(r, 2f, true);
            Assert.Contains(SelfAidCue.TimeMarked, cues);
            Report(r);
            cues = Run(r, 3f, true);
            Assert.Contains(SelfAidCue.Ack, cues);
            Assert.That(r.RescueAt, Is.GreaterThan(r.Clock + 60f));
            Assert.IsTrue(r.PutOnBlanket());
            int answered = 0;
            for (int i = 0; i < 1200 && !r.Ended; i++)
            {
                foreach (var c in r.Tick(0.1f, true))
                {
                    if (c == SelfAidCue.CheckIn) { Assert.IsTrue(r.AnswerCheck()); answered++; }
                    Assert.AreNotEqual(SelfAidCue.CheckInLoud, c);
                }
            }
            Assert.AreEqual("rescued", r.EndReason);
            Assert.That(answered, Is.GreaterThanOrEqualTo(2), "check-ins during the 75 s wait");
            Assert.IsEmpty(r.Mistakes.Keys, string.Join(",", r.Mistakes.Keys));
            var rep = SelfAidDebrief.Build(ScenarioCatalog.Get("SA"), r, s => "14:0" + (int)(s / 60), false);
            Assert.IsTrue(rep.survived && rep.selfAid);
            Assert.AreEqual(100, rep.score);
            Assert.AreEqual(9, rep.checks.Count);
            Assert.IsTrue(rep.checks.All(c => c.ok), string.Join("; ", rep.checks.Where(c => !c.ok).Select(c => c.text)));
            Assert.That(rep.checks[1].text, Does.Contain("Жгут наложен за"));
            Assert.That(rep.bleedControlSeconds, Is.InRange(5f, 10f));
            Assert.AreEqual(new[] { "CUF", "TFC", "TACEVAC" }, rep.phases.Select(p => p.id).ToArray());
            Assert.IsNotEmpty(rep.timeline);
            var kz = SelfAidDebrief.Build(ScenarioCatalog.Get("SA"), r, s => "", true);
            Assert.That(kz.checks[0].text, Does.Contain("баспана"));
            Assert.That(kz.timeline[0].text, Does.Not.Contain("|"));
        }

        [Test]
        public void MissedCheckIn_CallsLouder_AndIsADebriefError()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 1f, true);
            ApplyTq(r);
            Report(r);
            var cues = Run(r, r.cfg.ackDelay + r.cfg.firstCheckAfterAck + 0.5f, true);
            Assert.Contains(SelfAidCue.CheckIn, cues);
            cues = Run(r, r.cfg.answerWindow + 0.3f, true);
            Assert.Contains(SelfAidCue.CheckInLoud, cues);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_no_answer"));
            Assert.IsTrue(r.AnswerCheck(), "a late answer still counts as contact");
            Assert.AreEqual(1, r.ChecksMissed);
        }

        [Test]
        public void WrongReportLine_OrReportWithoutTourniquet_IsIncomplete()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 1f, true);
            ApplyTq(r);
            Report(r, allCorrect: false);
            Assert.IsFalse(r.ReportAllCorrect);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_report_incomplete"));

            var early = new SelfAidRun(new SelfAidConfig());
            Run(early, 1f, true);
            for (int i = 0; i < SelfAidRun.ReportSteps; i++) early.ChooseReport(early.ReportOptions().First(o => o.correct));
            Assert.IsFalse(early.ReportAllCorrect, "\"what is done\" needs the tourniquet time");
        }

        [Test]
        public void NoReport_NoBlanket_AreErrors_WhenRescuedLate()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 1f, true);
            ApplyTq(r);
            var cues = Run(r, 400f, true, 0.25f);
            Assert.Contains(SelfAidCue.CheckIn, cues, "nobody heard from him: the unit calls");
            Assert.AreEqual("rescued", r.EndReason);
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_no_report"));
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_no_blanket"));
            var rep = SelfAidDebrief.Build(ScenarioCatalog.Get("SA"), r, s => "", false);
            Assert.IsTrue(rep.mistakes.All(m => !string.IsNullOrEmpty(m.consequence)), "every mistake explains why it matters");
            Assert.Less(rep.score, 100);
        }

        [Test]
        public void SlowOrLowTourniquet_AreErrors()
        {
            var r = new SelfAidRun(new SelfAidConfig());
            Run(r, 65f, true);
            ApplyTq(r, pos: 0.7f, halfTurns: 3);
            r.End("rescued");
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_tq_slow"));
            Assert.IsTrue(r.Mistakes.ContainsKey("sa_tq_low"));
        }

        [Test]
        public void Training_NeverBleedsOut_NoDrone()
        {
            var r = new SelfAidRun(new SelfAidConfig { training = true });
            var cues = Run(r, 600f, false, 0.25f);
            Assert.IsFalse(r.Ended);
            Assert.IsFalse(cues.Contains(SelfAidCue.DronePass));
            Assert.Less(r.LethalFraction, 0.7f);
        }

        [Test]
        public void RandomConfig_WoundsTheNonDominantHand()
        {
            for (int s = 0; s < 20; s++)
            {
                Assert.IsTrue(SelfAidConfig.Random(s, false, false).left, "right-handed → left arm");
                Assert.IsFalse(SelfAidConfig.Random(s, true, false).left);
                Assert.AreEqual(SelfAidLimb.Arm, SelfAidConfig.Random(s, false, false).limb);
            }
            Assert.IsTrue(Enumerable.Range(0, 40).Any(s => SelfAidConfig.Random(s, false, true).limb == SelfAidLimb.Thigh));
            Assert.AreEqual("legR", new SelfAidConfig { limb = SelfAidLimb.Thigh, left = false }.LimbId);
            Assert.IsTrue(SelfAidDebrief.All.All(m => !string.IsNullOrEmpty(m.kz) && !string.IsNullOrEmpty(m.whyKz) && m.points > 0));
        }

        [Test]
        public void SelfAid_IsNotPartOfTheExam_AndHasALesson()
        {
            Assert.IsTrue(ScenarioCatalog.Get("SA").selfAid);
            TrainingCatalog.ResetDefaults();
            var l = TrainingCatalog.Get("selftq");
            Assert.IsNotNull(l);
            Assert.IsTrue(l.selfAid);
            Assert.Contains("selftq", TrainingCatalog.Topics);
            Assert.AreEqual("Жгут на себя", l.titleRu);
        }
    }
}
