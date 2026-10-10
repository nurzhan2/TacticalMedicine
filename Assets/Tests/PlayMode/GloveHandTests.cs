using System.Collections;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>S7: procedural skinned glove — one renderer, finger curl from grip/trigger, mirrored left hand.</summary>
    public class GloveHandTests
    {
        [UnityTest]
        public IEnumerator Glove_IsOneSkinnedMesh_FingersCurlWithGrip()
        {
            var hand = new GameObject("Hand_R");
            var v = new GameObject("GloveVisual"); v.transform.SetParent(hand.transform, false);
            var g = v.AddComponent<GloveHand>();
            g.side = 1f; g.driveFromInput = false;
            yield return null;
            Assert.That(g.Renderer, Is.Not.Null);
            Assert.AreEqual(16, g.BoneCount, "palm + 4×3 fingers + 3 thumb");
            Assert.AreEqual(1, hand.GetComponentsInChildren<Renderer>().Length, "one draw call per hand");
            Vector3 open = hand.transform.InverseTransformPoint(g.Tip(1));
            Vector3 thumbOpen = hand.transform.InverseTransformPoint(g.Tip(4));
            Assert.That(open.z, Is.GreaterThan(0.14f), "open middle finger reaches forward");
            g.gripCurl = 1f; g.thumbCurl = 1f; g.Pose();
            Vector3 fist = hand.transform.InverseTransformPoint(g.Tip(1));
            Assert.That(fist.z, Is.LessThan(open.z - 0.05f), "fist pulls the fingertip back");
            Assert.That(fist.y, Is.LessThan(-0.01f), "fingers fold under the palm (−y)");
            Vector3 thumbIn = hand.transform.InverseTransformPoint(g.Tip(4));
            Assert.That(thumbIn.x, Is.GreaterThan(thumbOpen.x), "thumb closes across the palm");
            // index follows the trigger only
            g.gripCurl = 0f; g.indexCurl = 1f; g.Pose();
            Assert.That(hand.transform.InverseTransformPoint(g.Tip(0)).z, Is.LessThan(hand.transform.InverseTransformPoint(g.Tip(2)).z - 0.04f));
            Object.Destroy(hand);
        }

        [UnityTest]
        public IEnumerator Glove_LeftIsMirrored_AndBloodTintsIt()
        {
            var hand = new GameObject("Hand_L");
            var v = new GameObject("GloveVisual"); v.transform.SetParent(hand.transform, false);
            var g = v.AddComponent<GloveHand>();
            g.side = -1f; g.driveFromInput = false;
            var boh = hand.AddComponent<BloodOnHands>();
            yield return null;
            Assert.That(hand.transform.InverseTransformPoint(g.Tip(4)).x, Is.GreaterThan(0f), "left thumb on +x");
            Assert.That(hand.transform.InverseTransformPoint(g.Tip(0)).x, Is.GreaterThan(0f), "left index next to the thumb");
            Assert.That(boh.gloves, Has.Length.EqualTo(1));
            Assert.AreSame(g.Renderer, boh.gloves[0]);
            boh.bloodiness = 1f; boh.wetness = 1f; boh.Apply();
            var mpb = new MaterialPropertyBlock();
            g.Renderer.GetPropertyBlock(mpb);
            Assert.That(mpb.GetColor("_BaseColor").r, Is.GreaterThan(0.2f));
            Object.Destroy(hand);
        }
    }
}
