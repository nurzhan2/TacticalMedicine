using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// A piece of the IFAK. Rides kinematic in its pouch slot until grabbed; held it follows the hand with
    /// weight; released near its slot it goes back, otherwise it drops. Subclasses add the procedure
    /// (tourniquet, packing, seal...). <see cref="tip"/> is the working point (needle tip, marker tip...).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MedItem : Grabbable
    {
        public MedItemKind kind;
        public Transform tip;
        public IfakPouch pouch;
        public Transform homeSlot;
        public float returnDistance = 0.12f;

        public bool InPouch { get; private set; }
        /// <summary>On the casualty (band on a limb, seal on the chest, NPA in the nose...).</summary>
        public bool Attached { get; protected set; }
        public bool Consumed { get; protected set; }
        public new HandGrabber Holder => PrimaryHolder;
        public IHandInput HolderInput => Holder != null ? Holder.Input : null;
        public float Trigger => HolderInput != null ? HolderInput.Trigger : 0f;
        public Vector3 TipPosition => tip != null ? tip.position : transform.position;
        public bool IsHeld => IsGrabbed;

        bool _built;

        protected override void Awake()
        {
            base.Awake();
            priority = 2;
            if (!_built) { _built = true; BuildVisual(); MeshyModels.Apply(this); FitCollider(); }
        }

        /// <summary>Item look from primitives. Base: lubricant packet.</summary>
        public virtual void BuildVisual()
        {
            var m = Kit.Mat("LubePacket", new Color(0.82f, 0.84f, 0.86f), 0.55f);
            var blue = Kit.Mat("LubeBlue", new Color(0.12f, 0.3f, 0.65f), 0.4f);
            Kit.Part(transform, PrimitiveType.Cube, "Packet", Vector3.zero, new Vector3(0.045f, 0.004f, 0.06f), m);
            Kit.Part(transform, PrimitiveType.Cube, "Print", new Vector3(0f, 0.0022f, 0.005f), new Vector3(0.03f, 0.0005f, 0.025f), blue);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.03f);
            Body.mass = 0.01f;
            Kit.Merge(transform);
        }

        /// <summary>Solid box collider around the visual (so it can lie on the ground).</summary>
        protected void FitCollider()
        {
            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(false)) // only what is visible now (not the unfolded blanket, the CAT on a limb...)
            {
                if (r.GetComponent<TextMesh>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            col.center = b.center;
            col.size = Vector3.Max(b.size, Vector3.one * 0.012f);
        }

        /// <summary>In a closed IFAK (rip-away lid not pulled open yet) the item can be neither seen nor taken.</summary>
        public override bool CanBeGrabbedBy(HandGrabber hand) => (!InPouch || pouch == null || pouch.IsOpen) && base.CanBeGrabbedBy(hand);

        // ------------------------------------------------------------------ pouch
        public void PutInPouch(IfakPouch p, Transform slot)
        {
            pouch = p;
            homeSlot = slot;
            ReturnToSlot();
        }

        public void ReturnToSlot()
        {
            if (homeSlot == null) return;
            InPouch = true;
            Body.isKinematic = true;
            Body.useGravity = false;
            transform.SetParent(homeSlot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Out of the pouch for good (scenario setup: the item is already in use).</summary>
        public void TakeOutOfPouch()
        {
            if (InPouch) LeavePouch();
            homeSlot = null;
        }

        void LeavePouch()
        {
            InPouch = false;
            transform.SetParent(null, true);
            Body.isKinematic = false;
            Body.useGravity = true;
        }

        protected override void OnGrabbed(HandGrabber hand, GrabPoint point)
        {
            if (InPouch) LeavePouch();
            // small tools follow the hand exactly (kinematic) while held; physics again once dropped
            if (!Attached) { Body.isKinematic = true; Body.useGravity = false; }
            OnItemGrabbed(hand);
        }

        protected override void OnReleased(HandGrabber hand)
        {
            if (IsGrabbed) return;
            if (Attached || Consumed) { OnItemReleased(hand); return; }
            if (homeSlot != null && Vector3.Distance(transform.position, homeSlot.position) < returnDistance)
            {
                ReturnToSlot();
                OnItemReleased(hand);
                return;
            }
            if (!InPouch && !Attached && !Consumed)
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                if (hand != null && hand.Input != null) Body.linearVelocity = Vector3.ClampMagnitude(hand.Input.Velocity, maxSpeed);
            }
            OnItemReleased(hand);
        }

        protected virtual void OnItemGrabbed(HandGrabber hand) { }
        protected virtual void OnItemReleased(HandGrabber hand) { }

        /// <summary>Fix the item on the casualty's body (kinematic, parented to a bone).</summary>
        protected void AttachTo(Transform parent)
        {
            Attached = true;
            Body.isKinematic = true;
            Body.useGravity = false;
            Body.linearVelocity = Vector3.zero;
            transform.SetParent(parent, true);
            // on the body it must not shove other tools away (marker on the TIME strap, fingers on the seal)
            foreach (var col in GetComponents<Collider>()) col.isTrigger = true;
        }

        /// <summary>Used up (dressing applied, gloves on...): released and hidden.</summary>
        protected void Consume()
        {
            Consumed = true;
            ForceRelease();
            gameObject.SetActive(false);
        }

        public static CasualtyController NearestCasualty(Vector3 p, float max = 2.5f) => CasualtyController.Nearest(p, max);
    }
}
