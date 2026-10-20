using System.Collections;
using FieldMedic.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>w10b: first-person soldier hands (Resources/FPHands) — load, light mesh, aligned to the glove frame,
    /// fingers fold toward the palm with grip/trigger, thumb closes across the palm, fallback to the procedural glove.</summary>
    public class SoldierHandTests
    {
        static GloveHand Make(float side, out GameObject hand)
        {
            hand = new GameObject(side < 0 ? "Hand_L" : "Hand_R");
            hand.transform.SetPositionAndRotation(new Vector3(0.3f, 1.2f, 0.4f), Quaternion.Euler(20f, -30f, 10f));
            var v = new GameObject("GloveVisual"); v.transform.SetParent(hand.transform, false);
            var g = v.AddComponent<GloveHand>();
            g.side = side; g.driveFromInput = false; g.soldierWithoutRig = true;
            return g;
        }

        [UnityTest]
        public IEnumerator SoldierHands_LoadAlignAndCurl([Values(1f, -1f)] float side)
        {
            var g = Make(side, out var hand);
            yield return null;
            Assert.That(g.IsSoldierHand, "Resources/FPHands/FPHands_" + (side < 0 ? "L" : "R") + " should load");
            Assert.That(g.Renderer, Is.Not.Null);
            Assert.AreEqual(16, g.BoneCount, "hand + 5x3 finger bones");
            Assert.That(g.Renderer.sharedMesh.GetIndexCount(0) / 3, Is.LessThan(4000), "light FP mesh");
            Assert.That(g.Renderer.sharedMaterial, Is.Not.Null);
            Assert.That(g.Renderer.sharedMaterial.name, Does.Contain("FPHands_Body"));

            Vector3 L(Vector3 w) => hand.transform.InverseTransformPoint(w);
            Vector3 mid = L(g.Tip(1)), idx = L(g.Tip(0)), pinky = L(g.Tip(3)), thumb = L(g.Tip(4));
            Debug.Log($"[FMVR] soldier hand {side}: tips index {idx} middle {mid} little {pinky} thumb {thumb}");
            Assert.That(mid.z, Is.InRange(0.12f, 0.24f), "open middle finger reaches forward along +z");
            Assert.That(Mathf.Abs(mid.x), Is.LessThan(0.04f), "middle finger on the hand's centre line");
            Assert.That(idx.x * side, Is.LessThan(pinky.x * side), "index on the thumb side (−x right hand, +x left)");
            Assert.That(thumb.x * side, Is.LessThan(0f), "thumb on the index side");

            g.gripCurl = 1f; g.thumbCurl = 1f; g.Pose();
            Vector3 fist = L(g.Tip(1)), thumbIn = L(g.Tip(4));
            Assert.That(fist.z, Is.LessThan(mid.z - 0.04f), "fist pulls the fingertip back");
            Assert.That(fist.y, Is.LessThan(mid.y - 0.01f), "fingers fold under the palm (−y; the soldier rest pose is already half bent)");
            Assert.That(Vector3.Distance(thumbIn, fist), Is.LessThan(Vector3.Distance(thumb, fist)), "thumb closes toward the curled fingers");

            g.gripCurl = 0f; g.indexCurl = 1f; g.thumbCurl = 0f; g.Pose();
            Assert.That(L(g.Tip(0)).z, Is.LessThan(L(g.Tip(2)).z - 0.03f), "trigger curls the index only");
            Object.Destroy(hand);
        }

        [UnityTest]
        public IEnumerator SoldierHands_SwitchOff_FallsBackToGlove()
        {
            bool was = GloveHand.UseSoldierHands;
            GloveHand.UseSoldierHands = false;
            try
            {
                var g = Make(1f, out var hand);
                yield return null;
                Assert.That(g.IsSoldierHand, Is.False);
                Assert.That(g.Renderer, Is.Not.Null);
                Assert.AreEqual("Glove_R", g.Renderer.sharedMesh.name);
                Object.Destroy(hand);
            }
            finally { GloveHand.UseSoldierHands = was; }
        }
    }
}
