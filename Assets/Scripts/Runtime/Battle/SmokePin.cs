using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// The pull ring + pin. Grabbable only while the grenade is held by the other hand; pulling the ring 6 cm away
    /// from its socket frees the pin (it stays in that hand and can be dropped).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SmokePin : Grabbable
    {
        public SmokeGrenade grenade;
        public float pullDistance = 0.06f;
        public bool Pulled { get; private set; }

        Vector3 _socketLocal;
        Quaternion _socketRot;
        Vector3 _grabHandStart;
        HandGrabber _hand;

        protected override void Awake()
        {
            base.Awake();
            priority = 5;
            allowTwoHands = false;
            _socketLocal = transform.localPosition;
            _socketRot = transform.localRotation;
            if (Body != null) Body.isKinematic = true;
            if (grenade != null)
                foreach (var a in GetComponentsInChildren<Collider>())
                    foreach (var b in grenade.GetComponents<Collider>()) Physics.IgnoreCollision(a, b);
        }

        public override bool CanBeGrabbedBy(HandGrabber hand)
        {
            if (Pulled) return base.CanBeGrabbedBy(hand);
            // the ring is hooked with the free hand while the other hand holds the grenade
            return grenade != null && grenade.IsGrabbed && !grenade.IsHeldBy(hand) && base.CanBeGrabbedBy(hand);
        }

        protected override void OnGrabbed(HandGrabber hand, GrabPoint point)
        {
            _hand = hand;
            _grabHandStart = grenade != null ? grenade.transform.InverseTransformPoint(hand.PalmPosition) : hand.PalmPosition;
        }

        protected override void OnReleased(HandGrabber hand) { _hand = null; }

        protected override void FixedUpdate()
        {
            if (Pulled) { base.FixedUpdate(); return; }
            if (_hand == null || grenade == null) return;
            // stay in the socket until the hand has moved far enough from the grenade
            transform.localPosition = _socketLocal;
            transform.localRotation = _socketRot;
            Vector3 now = grenade.transform.InverseTransformPoint(_hand.PalmPosition);
            if ((now - _grabHandStart).magnitude >= pullDistance) Pull();
        }

        /// <summary>Frees the pin from the grenade.</summary>
        public void Pull()
        {
            if (Pulled) return;
            Pulled = true;
            transform.SetParent(null, true);
            if (Body != null) { Body.isKinematic = false; Body.mass = 0.02f; }
            var ba = BattleAudio.Instance;
            if (ba != null) ba.Play(BattleAudio.Pick(ba.pinPull), transform.position, 0.8f, 0.5f, 10f);
            if (_hand != null) Haptics.Play(_hand.Input, Haptics.PressClick);
        }
    }
}
