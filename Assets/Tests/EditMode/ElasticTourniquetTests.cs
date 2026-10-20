using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// w9d: elastic tourniquets (Alpha / Esmarch) — stretched turns occlude, slack turns only congest, letting go before
    /// the end is fixed loses everything, and the debrief accepts them with the "CAT preferred on a thigh" note.
    public class ElasticTourniquetTests
    {
        static TourniquetState Band(string limb, float pos)
        {
            var tq = new TourniquetState { type = TourniquetType.Elastic };
            tq.Pickup();
            Assert.IsTrue(tq.WrapAround(limb, pos));
            Assert.IsTrue(tq.ConfirmPosition());
            return tq;
        }

        static (PatientPhysiology p, CareProtocol cp, Wound w) Bleed(BodyRegion region, float woundPos, Phase phase = Phase.CareUnderFire)
        {
            var p = new PatientPhysiology();
            var w = new Wound { id = "w", type = WoundType.ArterialLimb, region = region, limbPosition = woundPos };
            p.wounds.Add(w);
            return (p, new CareProtocol(p, phase), w);
        }

        [Test]
        public void StretchedTurns_FullOcclusion_ThighNeedsFour_ArmThree()
        {
            var thigh = Band("legR", 0.2f);
            for (int i = 0; i < 3; i++) thigh.ElasticTurn(1f);
            Assert.IsFalse(thigh.FullOcclusion, "3 turns are not enough on a thigh");
            thigh.ElasticTurn(1f);
            Assert.IsTrue(thigh.FullOcclusion, "4 stretched turns occlude the femoral artery");
            Assert.AreEqual(0f, thigh.BleedFactor);
            var arm = Band("armL", 0.4f);
            for (int i = 0; i < 3; i++) arm.ElasticTurn(1f);
            Assert.IsTrue(arm.FullOcclusion, "3 turns on the upper arm");
            Assert.AreEqual(TourniquetStage.WindlassTwisted, arm.stage);
        }

        [Test]
        public void SlackTurns_OnlyCongest_BleedMore()
        {
            var tq = Band("legR", 0.2f);
            for (int i = 0; i < 6; i++) tq.ElasticTurn(0.1f); // wrapped without pulling
            Assert.AreEqual(0f, tq.Occlusion, "unstretched turns do not compress");
            var half = Band("legR", 0.2f);
            half.ElasticTurn(1f); half.ElasticTurn(1f);
            Assert.That(half.BleedFactor, Is.GreaterThan(1f), "partial occlusion: venous congestion, bleeds more");
            Assert.That(TourniquetState.StretchWeight(0.5f), Is.InRange(0.6f, 0.9f));
        }

        [Test]
        public void LetGoBeforeFixing_BandSpringsOff_FixRequiresATurn()
        {
            var tq = Band("armR", 0.3f);
            Assert.IsFalse(tq.ElasticFix(), "nothing to fix without a turn");
            tq.ElasticTurn(1f); tq.ElasticTurn(1f); tq.ElasticTurn(1f);
            Assert.That(tq.ElasticRelease(), Is.GreaterThan(2.9f));
            Assert.AreEqual(0f, tq.Occlusion);
            Assert.AreEqual(TourniquetStage.Positioned, tq.stage);
            tq.ElasticTurn(1f); tq.ElasticTurn(1f); tq.ElasticTurn(1f);
            Assert.IsTrue(tq.ElasticFix());
            Assert.IsTrue(tq.Locked && tq.Secured, "hooks / balls are the whole lock");
            Assert.AreEqual(0f, tq.ElasticRelease(), "a fixed band stays");
            Assert.IsTrue(tq.FullOcclusion);
            Assert.IsTrue(tq.MarkTime(42f));
            Assert.IsTrue(tq.TimeMarked);
        }

        [Test]
        public void CatOnlyRules_DoNotApplyToElastic_AndViceVersa()
        {
            var cat = new TourniquetState();
            cat.Pickup(); cat.WrapAround("legR", 0.2f); cat.ConfirmPosition();
            Assert.AreEqual(0f, cat.ElasticTurn(1f), "a CAT is not wrapped like an elastic band");
            Assert.IsFalse(cat.ElasticFix());
            var el = Band("legR", 0.2f);
            el.Twist(720f);
            Assert.AreEqual(0f, el.Occlusion, "a windlass twist does not tighten an elastic band");
        }

        [Test]
        public void Protocol_ElasticOnThigh_Accepted_WithCatPreferredNote_NoPoints()
        {
            var (p, cp, w) = Bleed(BodyRegion.ThighR, 0.5f);
            var tq = Band("legR", 0.15f);
            p.AddTourniquet(tq);
            for (int i = 0; i < 4; i++) { tq.ElasticTurn(1f); p.RefreshTourniquets(); }
            Assert.IsTrue(tq.ElasticFix());
            var fb = cp.TourniquetApplied(tq);
            Assert.AreEqual("tq_ok_elastic", fb.code, fb.text);
            StringAssert.Contains("воротк", fb.text, "windlass TQ preferred on a thigh");
            Assert.IsTrue(cp.penalties.ContainsKey("tq_elastic_thigh"), "debrief note");
            Assert.AreEqual(0, CareProtocol.Penalty["tq_elastic_thigh"].points, "accepted as a tourniquet: no points lost");
            Assert.IsTrue(w.IsControlled || tq.BleedFactor == 0f, "bleeding stopped");
        }

        [Test]
        public void Protocol_ElasticOnArm_PlainOk_PartialWarns()
        {
            var (p, cp, _) = Bleed(BodyRegion.ForearmR, 1.5f);
            var tq = Band("armR", 0.4f);
            p.AddTourniquet(tq);
            for (int i = 0; i < 3; i++) tq.ElasticTurn(1f);
            tq.ElasticFix();
            Assert.AreEqual("tq_ok", cp.TourniquetApplied(tq).code);
            Assert.IsFalse(cp.penalties.ContainsKey("tq_elastic_thigh"));

            var (p2, cp2, _) = Bleed(BodyRegion.ThighL, 0.6f);
            var loose = Band("legL", 0.2f);
            p2.AddTourniquet(loose);
            loose.ElasticTurn(1f); loose.ElasticTurn(0.3f);
            loose.ElasticFix();
            var fb = cp2.TourniquetApplied(loose);
            Assert.AreEqual("tq_partial", fb.code);
            StringAssert.Contains("витков", fb.text);
        }
    }
}
