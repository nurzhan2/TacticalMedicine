using System;
using System.Linq;
using FieldMedic.Interaction;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>w8 data: hand poses (one per item and handle, gestures), haptics profile, tutorial props.</summary>
    public class W8AssetTests
    {
        [Test]
        public void HandPoseLibrary_HasEveryRequiredKey_AsAssets()
        {
            var lib = AssetDatabase.LoadAssetAtPath<HandPoseLibrary>("Assets/Resources/HandPoseLibrary.asset");
            Assert.IsNotNull(lib, "Resources/HandPoseLibrary (W8Tools.BuildAssets)");
            foreach (var k in HandPoseLibrary.RequiredKeys)
            {
                var p = lib.poses.FirstOrDefault(x => x != null && x.key == k);
                Assert.IsNotNull(p, "pose asset for " + k);
                Assert.IsTrue(AssetDatabase.GetAssetPath(p).StartsWith("Assets/Data/HandPoses/"), k + " is its own asset");
                for (int f = 0; f < 5; f++) Assert.That(p.Finger(f), Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Every_MedItemKind_Has_A_Pose()
        {
            foreach (MedItemKind k in Enum.GetValues(typeof(MedItemKind)))
            {
                string key = HandPoses.KindKey(k);
                Assert.IsNotNull(key, "pose key for " + k);
                Assert.IsNotNull(HandPoseLibrary.Get(key), "pose for " + k);
            }
        }

        [Test]
        public void Gestures_Pinch_SmallItems_Grip_Handles()
        {
            foreach (var k in new[] { "gauze", "seal", "npa", "needle", "card", "tab", "ring" })
                Assert.AreEqual(GrabStyle.Pinch, HandPoseLibrary.Get(k).style, k);
            foreach (var k in new[] { "drag_handle", "cat_strap", "wrist", "ankle", "cat", "blanket" })
                Assert.AreEqual(GrabStyle.Grip, HandPoseLibrary.Get(k).style, k);
            Assert.IsTrue(HandGrabber.Accepts(GrabStyle.Grip, GrabStyle.Pinch), "a fist can take a small item too");
            Assert.IsFalse(HandGrabber.Accepts(GrabStyle.Pinch, GrabStyle.Grip), "a pinch never takes a drag handle / the strap");
            Assert.IsTrue(HandGrabber.Accepts(GrabStyle.Pinch, GrabStyle.Any));
            // poses differ: the drag handle is a full fist, the card is almost flat
            Assert.Greater(HandPoseLibrary.Get("drag_handle").middle, 0.8f);
            Assert.Less(HandPoseLibrary.Get("card").index, 0.3f);
            Assert.Less(HandPoseLibrary.Get("radio").index, 0.1f, "press-to-talk: the index points");
        }

        [Test]
        public void HapticsProfile_And_TutorialProps_Exist()
        {
            var hp = AssetDatabase.LoadAssetAtPath<HapticsProfile>("Assets/Resources/HapticsProfile.asset");
            Assert.IsNotNull(hp);
            Assert.Less(hp.dragFrequency, 0.3f, "drag = low rumble");
            Assert.Greater(hp.clickAmplitude, hp.packAmplitude, "windlass click is sharper than the packing pulses");
            Assert.Greater(hp.sealAmplitude, hp.packAmplitude, "seal press is a firm pulse");
            var tp = AssetDatabase.LoadAssetAtPath<TutorialProps>("Assets/Resources/TutorialProps.asset");
            Assert.IsNotNull(tp);
            Assert.IsNotNull(tp.crate, "real crate model for the practice dummy");
            Assert.IsNotNull(tp.ammoCan, "real ammo can for the lift");
        }
    }
}
