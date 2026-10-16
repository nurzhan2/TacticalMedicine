using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>Which hand-tracking gesture holds an item (controllers: the grip button holds everything).</summary>
    public enum GrabStyle { Any, Pinch, Grip }

    /// <summary>
    /// How the fingers wrap a held item (w8 hand feel): curl per finger 0 (straight) … 1 (closed), thumb curl and
    /// opposition, and the hand-tracking gesture that holds it (pinch = small items, full grip = handles and the
    /// strap). Applied to the player's glove (controllers); tracked hands show the real fingers.
    /// Assets in Assets/Data/HandPoses, collected by Resources/HandPoseLibrary (built by W8Builder).
    /// </summary>
    [CreateAssetMenu(menuName = "FieldMedic/Hand Pose")]
    public sealed class HandPose : ScriptableObject
    {
        public string key;
        [Range(0f, 1f)] public float index, middle, ring, little, thumb;
        [Tooltip("Thumb across the palm toward the fingers (opposition), 0..1.")]
        [Range(0f, 1f)] public float thumbOpposition = 0.5f;
        public GrabStyle style = GrabStyle.Grip;
        [TextArea] public string note;

        public static HandPose Make(string key, float index, float middle, float ring, float little, float thumb, float opposition, GrabStyle style, string note)
        {
            var p = CreateInstance<HandPose>();
            p.name = "HandPose_" + key;
            p.key = key; p.index = index; p.middle = middle; p.ring = ring; p.little = little; p.thumb = thumb;
            p.thumbOpposition = opposition; p.style = style; p.note = note;
            return p;
        }

        public float Finger(int f) => f switch { 0 => index, 1 => middle, 2 => ring, 3 => little, _ => thumb };
    }
}
