using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// A piece of the IFAK. Rides kinematic in its pouch slot until grabbed; held it follows the hand with
    /// weight; released near the open kit it goes back into its own slot, otherwise it drops as a normal physics
    /// object. Subclasses add the procedure (tourniquet, packing, seal...). <see cref="tip"/> is the working point
    /// (needle tip, marker tip...). The carried look lives in the child <see cref="VisualName"/> (art swap,
    /// docs/ART_IMPORT.md).
    /// w9 root cause of "the items float somewhere away from the kit": a kinematic body with Rigidbody interpolation
    /// writes its last physics pose back to the transform every frame, so a slot that moves with the player
    /// (body-locked kit, bone of a dragged casualty) left the items behind (3.9 m after a 5 m walk, KitTests).
    /// Parented items (slot / body) therefore never interpolate; only free or held items do.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MedItem : Grabbable
    {
        public MedItemKind kind;
        public Transform tip;
        public IfakPouch pouch;
        public Transform homeSlot;
        [Tooltip("Released this close to its slot in the OPEN kit → back into the slot (owner: ~25 cm).")]
        public float returnDistance = 0.25f;
        public const string VisualName = "Visual";

        public bool InPouch { get; private set; }
        /// <summary>On the casualty (band on a limb, seal on the chest, NPA in the nose...).</summary>
        public bool Attached { get; protected set; }
        public bool Consumed { get; protected set; }
        /// <summary>Carried look (procedural parts or an imported model). Logic parts are its siblings.</summary>
        public Transform Visual { get; private set; }
        /// <summary>w9d: the imported model inside <see cref="Visual"/> (null = procedural look).</summary>
        public Transform ArtRoot { get; internal set; }
        public ArtModels.Spec ArtSpec => ArtModels.ByKind.TryGetValue(kind, out var s) ? s : null;
        public new HandGrabber Holder => PrimaryHolder;
        public IHandInput HolderInput => Holder != null ? Holder.Input : null;
        /// <summary>Fine action of the held item (w10: re-armed after a trigger-started grab, see HandGrabber.ActionTrigger).</summary>
        public float Trigger => Holder != null ? Holder.ActionTrigger : 0f;
        protected override bool AutoHighlight => true;
        public Vector3 TipPosition => tip != null ? tip.position : transform.position;
        public bool IsHeld => IsGrabbed;
        /// <summary>Not in the kit, not held, not on the casualty, not used up.</summary>
        public bool IsLoose => !InPouch && !Attached && !Consumed && !IsGrabbed;

        bool _built;

        protected override void Awake()
        {
            base.Awake();
            priority = 2;
            if (!_built)
            {
                _built = true;
                BuildVisual();
                OrganizeVisual();
                if (ArtModels.Apply(this) == null) MeshyModels.Apply(this);
                FitCollider();
            }
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

        /// <summary>Moves the carried look (merged procedural meshes + the kind's carried-state child) under "Visual".</summary>
        void OrganizeVisual()
        {
            var v = transform.Find(VisualName);
            if (v == null)
            {
                v = new GameObject(VisualName).transform;
                v.SetParent(transform, false);
            }
            Visual = v;
            string carried = MeshyModels.ByKind.TryGetValue(kind, out var e) ? e.target : null;
            var move = new List<Transform>();
            foreach (Transform c in transform)
                if (c != v && (c.name.StartsWith("Merged_") || (carried != null && c.name == carried) || IsPlainLook(c))) move.Add(c);
            foreach (var c in move) c.SetParent(v, true);
            v.SetSiblingIndex(0);
        }

        /// <summary>Children driven by the procedure logic (never part of the swappable carried look).</summary>
        static readonly HashSet<string> LogicParts = new HashSet<string> { "OnLimb", "Seal", "Sheet", "PadGroup", "UpperBlade", "CutSlit", "Tip" };

        /// <summary>A child that only draws (no grab handle, no written text, not a logic part): e.g. the two gloves.</summary>
        bool IsPlainLook(Transform c) =>
            c != tip && !LogicParts.Contains(c.name) && c.GetComponentInChildren<Renderer>(true) != null &&
            c.GetComponentInChildren<GrabPoint>(true) == null && c.GetComponentInChildren<Grabbable>(true) == null &&
            c.GetComponentInChildren<TextMesh>(true) == null;

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

        /// <summary>In a closed IFAK (rip-away lid not pulled open yet) the item can be neither seen nor taken.
        /// The quick-access pouch (CAT + shears) is always open.</summary>
        public override bool CanBeGrabbedBy(HandGrabber hand) => (!InPouch || pouch == null || pouch.IsSlotOpen(homeSlot)) && base.CanBeGrabbedBy(hand);

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
            Pin();
            // w10 tray: in the kit an item never shoves anything (hip, the casualty under the tray) — hover still finds it
            if (pouch != null && pouch.Tray != null) SetSolid(false);
            transform.SetParent(homeSlot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            OnPouchState(true);
        }

        /// <summary>In the kit / out of it: the imported model shows its packed or its in-hand state (sealed pack →
        /// opened pack, needle in its case → bare needle...). Subclasses add their own parts.</summary>
        protected virtual void OnPouchState(bool inPouch)
        {
            if (ArtRoot != null) ArtModels.ShowState(ArtRoot, ArtSpec, inPouch);
        }

        bool _hiddenInKit;
        /// <summary>Inside a closed kit: not drawn (the item cannot be grabbed there anyway).</summary>
        public void SetHiddenInKit(bool hidden)
        {
            if (hidden == _hiddenInKit || Visual == null) return;
            _hiddenInKit = hidden;
            Visual.gameObject.SetActive(!hidden);
        }

        /// <summary>Hides / shows the whole carried look (state changes: band on a limb, seal torn out, sheet unfolded).</summary>
        protected void ShowCarriedLook(bool on)
        {
            if (ArtRoot != null) ArtRoot.gameObject.SetActive(on);
        }

        /// <summary>Kinematic, no gravity, no interpolation: the transform hierarchy (slot / bone) owns the pose.</summary>
        protected void Pin()
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Body.useGravity = false;
            Body.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>A normal physics object: falls, collides, sleeps (interpolated for smooth motion).</summary>
        public void MakeFree()
        {
            Body.isKinematic = false;
            Body.useGravity = true;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// Guard run by the owning kit every frame (after it moved): slotted items exactly in their slot, nothing
        /// loose hangs kinematic in the air, an item that fell out of the world goes back into the kit.
        /// </summary>
        internal void Enforce()
        {
            if (InPouch)
            {
                if (homeSlot == null) return;
                if (transform.parent != homeSlot) transform.SetParent(homeSlot, false);
                if (transform.localPosition.sqrMagnitude > 1e-8f) transform.localPosition = Vector3.zero;
                if (transform.localRotation != Quaternion.identity) transform.localRotation = Quaternion.identity;
                if (!Body.isKinematic || Body.interpolation != RigidbodyInterpolation.None) Pin();
                return;
            }
            if (!IsLoose || !isActiveAndEnabled || HoldsOwnPose) return;
            if (Body.isKinematic || !Body.useGravity) MakeFree();
            if (transform.position.y < -25f && homeSlot != null) ReturnToSlot();
        }

        /// <summary>Kinematic on purpose while loose (a constraint drives it). Default: no.</summary>
        protected virtual bool HoldsOwnPose => false;

        /// <summary>Out of the pouch for good (scenario setup: the item is already in use).</summary>
        public void TakeOutOfPouch()
        {
            if (InPouch) LeavePouch();
            homeSlot = null;
        }

        bool[] _solidState;
        Collider[] _ownColliders;

        /// <summary>Root colliders solid (their original trigger flags) or all triggers.</summary>
        void SetSolid(bool solid)
        {
            if (_ownColliders == null)
            {
                _ownColliders = GetComponents<Collider>();
                _solidState = new bool[_ownColliders.Length];
                for (int i = 0; i < _ownColliders.Length; i++) _solidState[i] = _ownColliders[i] != null && _ownColliders[i].isTrigger;
            }
            for (int i = 0; i < _ownColliders.Length; i++)
                if (_ownColliders[i] != null) _ownColliders[i].isTrigger = solid ? _solidState[i] : true;
        }

        void LeavePouch()
        {
            InPouch = false;
            if (_ownColliders != null && !Attached) SetSolid(true);
            transform.SetParent(null, true);
            transform.localScale = Vector3.one; // w10 tray shows items scaled down in their cells: full size in the hand
            MakeFree();
            OnPouchState(false);
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
            if (Attached || Consumed || !isActiveAndEnabled) { OnItemReleased(hand); return; } // released by OnDisable: no re-parenting while deactivating
            if (pouch != null && pouch.AcceptsReturn(this))
            {
                ReturnToSlot();
                OnItemReleased(hand);
                return;
            }
            if (!InPouch && !Attached && !Consumed)
            {
                MakeFree();
                if (hand != null && hand.Input != null) Body.linearVelocity = Vector3.ClampMagnitude(hand.Input.Velocity, maxSpeed);
            }
            OnItemReleased(hand);
        }

        /// <summary>An imported model replaced the carried look (bind its moving sub-parts here).</summary>
        public virtual void OnArtApplied(Transform art) { }

        protected virtual void OnItemGrabbed(HandGrabber hand) { }
        protected virtual void OnItemReleased(HandGrabber hand) { }

        /// <summary>Fix the item on the casualty's body (kinematic, parented to a bone, no interpolation).</summary>
        protected void AttachTo(Transform parent)
        {
            Attached = true;
            Pin();
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
