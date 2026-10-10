using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// A grab spot that does not move by itself (limb, drag handle, tourniquet strap, windlass rod...).
    /// The owning component reads the holding hand and decides what the grab does. Lives on a child object
    /// with a trigger collider; enable/disable it to offer the grab only in the right step of a procedure.
    /// </summary>
    public class ProxyHandle : Grabbable
    {
        public HandGrabber Hand => holders.Count > 0 ? holders[0].Hand : null;
        public HandGrabber SecondHand => holders.Count > 1 ? holders[1].Hand : null;
        /// <summary>Palm position when this grab started (world).</summary>
        public Vector3 GrabStartPalm { get; private set; }
        public Quaternion GrabStartRotation { get; private set; }
        public float GrabTime { get; private set; }
        public Vector3 Palm => Hand != null ? Hand.PalmPosition : transform.position;
        public Vector3 PalmDelta => Hand != null ? Hand.PalmPosition - GrabStartPalm : Vector3.zero;
        public IHandInput Input => Hand != null ? Hand.Input : null;

        protected override void Awake()
        {
            base.Awake();
            allowTwoHands = false;
            followRotation = false;
        }

        protected override void OnGrabbed(HandGrabber hand, GrabPoint point)
        {
            if (holders.Count == 1)
            {
                GrabStartPalm = hand.PalmPosition;
                GrabStartRotation = hand.transform.rotation;
                GrabTime = Time.time;
            }
        }

        /// <summary>Restart the delta measurement from the current hand pose.</summary>
        public void Rebase()
        {
            if (Hand == null) return;
            GrabStartPalm = Hand.PalmPosition;
            GrabStartRotation = Hand.transform.rotation;
        }

        // the handle never chases the hand: the owner moves the body/limb/strap instead
        protected override void FixedUpdate()
        {
            for (int i = holders.Count - 1; i >= 0; i--)
                if (holders[i].Hand == null || !holders[i].Hand.isActiveAndEnabled) holders.RemoveAt(i);
        }

        /// <summary>Creates a handle with a trigger sphere on a new child of `parent`.</summary>
        public static T Create<T>(string name, Transform parent, Vector3 worldPos, float radius, int priority = 1) where T : ProxyHandle
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            float s = Mathf.Max(1e-4f, go.transform.lossyScale.x);
            col.radius = radius / s;
            var h = go.AddComponent<T>();
            h.priority = priority;
            return h;
        }
    }
}
