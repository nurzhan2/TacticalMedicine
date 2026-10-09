using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// Preferred place to hold a <see cref="Grabbable"/> (e.g. a drag handle, the tourniquet tail, an ankle).
    /// When <see cref="snap"/> is on, the object is re-posed so this point's transform aligns with the palm.
    /// Its forward = direction the fingers point, up = back of the hand.
    /// </summary>
    public sealed class GrabPoint : MonoBehaviour
    {
        public enum Hand { Any, LeftOnly, RightOnly }

        [Tooltip("Hand must be within this distance of the point to use it.")]
        public float radius = 0.08f;
        public int priorityBonus = 0;
        public bool snap = true;
        public Hand allowedHand = Hand.Any;

        public Grabbable Owner { get; internal set; }

        public bool Allows(Handedness h) =>
            allowedHand == Hand.Any ||
            (allowedHand == Hand.LeftOnly && h == Handedness.Left) ||
            (allowedHand == Hand.RightOnly && h == Handedness.Right);

        public float Distance(Vector3 p) => Vector3.Distance(transform.position, p);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.06f);
        }
    }
}
