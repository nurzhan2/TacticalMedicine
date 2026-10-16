using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 pain reactions: tightening the CAT by hand makes Erlan flinch (procedural, on the rig bones), cry out, and —
    /// conscious — grab the medic's wrist with his free hand (IK hand target on the player's hand). Unresponsive: no
    /// reaction. Packing strokes and the needle use the same reaction.
    /// </summary>
    public class PainReactionTests : W8SceneTest
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Tourniquet_Tighten_Flinch_Voice_And_WristGrab_On_PlayerHand()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            Assert.AreEqual(Consciousness.Alert, C.Patient.AVPU, "tq lesson: he is conscious");
            var sources = new List<string>();
            C.PainReacted += (s, k) => sources.Add(s);
            float maxFlinch = 0f, minGrabDist = 99f;
            bool grabbing = false;
            Transform grabTarget = null;
            Limb grabLimb = Limb.LeftArm;
            string line = null;
            var watch = new GameObject("~watch").AddComponent<FrameWatch>();
            watch.onFrame = () =>
            {
                maxFlinch = Mathf.Max(maxFlinch, C.rig.FlinchAmount);
                if (C.voice != null && C.voice.LastLine != null) line = C.voice.LastLine;
                var ci = C.Interaction;
                if (ci != null && ci.GrabbingPlayer)
                {
                    grabbing = true;
                    grabTarget = ci.ArmGrabTarget;
                    grabLimb = ci.ArmGrabLimb;
                    var hand = C.rig.Bone(grabLimb == Limb.LeftArm ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (hand != null) minGrabDist = Mathf.Min(minGrabDist, Vector3.Distance(hand.position, ci.ArmGrabPoint));
                }
            };
            var cat = run.Pouch.Find<TourniquetCAT>();
            yield return run.bot.ApplyTourniquet(cat, C, Limb.RightLeg, 0.12f, 4, null);
            yield return DemoFlowRunner.Seconds(0.8f);
            Object.Destroy(watch.gameObject);
            Debug.Log($"[FMVR] w8 test pain: reactions {string.Join(",", sources)}, flinch {maxFlinch:F2}, line {line}, grab {grabbing} {grabLimb} → {(grabTarget != null ? grabTarget.name : "-")}, hand→wrist {minGrabDist:F2} m");
            Assert.IsTrue(sources.Contains("tq"), "the windlass half-turns hurt");
            Assert.Greater(maxFlinch, 0.15f, "procedural flinch on the rig");
            Assert.IsNotNull(line, "a voice line / scream");
            Assert.IsTrue(grabbing, "he grabs the medic's wrist");
            Assert.AreEqual(run.bot.right.transform, grabTarget, "IK target = the hand that twists the windlass");
            Assert.Less(minGrabDist, 0.2f, "his hand reaches the medic's wrist");
        }

        [UnityTest]
        public IEnumerator Unresponsive_NoReaction_Conscious_GrabCooldown()
        {
            yield return LoadLesson("tq");
            yield return Ready(1.3f);
            var hand = run.bot.right.transform;
            int n0 = C.PainReactionCount;
            C.PainReaction(1f, hand, "needle");
            Assert.AreEqual(n0 + 1, C.PainReactionCount);
            Assert.IsTrue(C.Interaction.GrabbingPlayer, "conscious: grabs at once");
            C.PainReaction(1f, hand, "pack");
            Assert.AreEqual("pack", C.LastPainSource);
            yield return DemoFlowRunner.Seconds(2.2f);
            Assert.IsFalse(C.Interaction.GrabbingPlayer, "lets go after a moment");
            C.PainReaction(1f, hand, "pack");
            Assert.IsFalse(C.Interaction.GrabbingPlayer, "not every stroke: cooldown");
            // unresponsive
            SetLoss(0.46f);
            yield return DemoFlowRunner.Seconds(0.3f);
            Assert.AreEqual(Consciousness.Unresponsive, C.Patient.AVPU);
            int n1 = C.PainReactionCount;
            C.PainReaction(1f, hand, "needle");
            Assert.AreEqual(n1, C.PainReactionCount, "unresponsive: no reaction");
        }
    }
}
