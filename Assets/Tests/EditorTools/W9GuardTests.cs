using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9 owner decisions that must not regress: the sculpted w8 face was rejected ("те изменения с лицом ваще отмени") —
    /// Erlan keeps the balaclava look with no visible face; the pulse-check vibration stays (HandTreatment beats,
    /// covered by TreatmentBotTests); the litter cart is gone from the shipped scenes.
    /// </summary>
    public class W9GuardTests
    {
        [Test]
        public void Erlan_HasNoSculptedFace()
        {
            Assert.IsNull(System.Type.GetType("FieldMedic.Casualty.CasualtyFace, FieldMedic.Runtime"), "w8 CasualtyFace must not be on this branch");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
            Assert.IsNotNull(prefab, "Erlan prefab");
            var face = prefab.GetComponentsInChildren<Transform>(true)
                .Where(t => new[] { "eyelid", "eyeball", "lips", "brow", "face_sculpt", "casualtyface" }.Any(k => t.name.ToLowerInvariant().Contains(k)))
                .Select(t => t.name).ToList();
            CollectionAssert.IsEmpty(face, "no sculpted face parts under the balaclava");
        }

        [Test]
        public void PulseHaptics_StillWired()
        {
            var ht = typeof(FieldMedic.Treatment.HandTreatment);
            Assert.IsNotNull(ht.GetProperty("PulseBeats"), "pulse beats counter (haptic beat per heartbeat)");
            Assert.IsNotNull((object)typeof(FieldMedic.Interaction.Haptics).GetField("Heartbeat") ?? typeof(FieldMedic.Interaction.Haptics).GetProperty("Heartbeat"), "heartbeat haptic preset");
        }
    }
}
