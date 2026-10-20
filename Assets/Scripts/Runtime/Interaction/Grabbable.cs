using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// Anything that can be held. Physics-hand style: a grabbed rigidbody chases the hand with
    /// velocity tracking whose responsiveness drops with mass, so heavy things lag and sag instead of
    /// teleporting. Supports up to two hands. Subclasses (tourniquet strap, windlass, limb) override
    /// <see cref="ComputeTarget"/> to constrain motion.
    /// </summary>
    [DisallowMultipleComponent]
    public class Grabbable : MonoBehaviour
    {
        public int priority = 0;
        public bool allowTwoHands = true;
        [Tooltip("Only grabbable at its GrabPoints (handles), not anywhere on the collider.")]
        public bool requireGrabPoint = false;

        [Header("Follow feel")]
        [Range(0.05f, 1f)] public float stiffness = 0.85f;
        [Tooltip("Mass (kg) that still follows the hand 1:1. Heavier = laggier.")]
        public float referenceMass = 1.5f;
        public float maxSpeed = 8f;
        [Tooltip("Force-release when the object cannot reach the hand (stuck) farther than this (m).")]
        public float breakDistance = 0.45f;
        public bool followRotation = true;

        [Header("Events")]
        public UnityEvent onGrab = new UnityEvent();
        public UnityEvent onRelease = new UnityEvent();
        public event Action<Grabbable, HandGrabber> Grabbed;
        public event Action<Grabbable, HandGrabber> Released;

        protected struct Holder
        {
            public HandGrabber Hand;
            public GrabPoint Point;
            public Vector3 LocalPos;      // hand position in object space (unscaled)
            public Quaternion LocalRot;   // hand rotation in object space
        }

        protected readonly List<Holder> holders = new List<Holder>(2);
        public Rigidbody Body { get; private set; }
        public GrabPoint[] GrabPoints { get; private set; }
        public bool IsGrabbed => holders.Count > 0;
        public int HolderCount => holders.Count;
        public HandGrabber PrimaryHolder => holders.Count > 0 ? holders[0].Hand : null;
        public bool IsHeldBy(HandGrabber h) => holders.Exists(x => x.Hand == h);

        /// <summary>0..1, how strongly a grabbed object tracks the hand (mass feel).</summary>
        public float Responsiveness =>
            Body == null || Body.isKinematic ? stiffness
                : stiffness * Mathf.Clamp(referenceMass / Mathf.Max(0.01f, Body.mass), 0.04f, 1f);

        bool _savedKinematicValid;
        float _stuckTime;
        int _hoverCount;
        GrabHighlight _highlight;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (Body != null) Body.maxAngularVelocity = 40f;
            RefreshGrabPoints();
            _highlight = GetComponent<GrabHighlight>();
        }

        public void RefreshGrabPoints()
        {
            GrabPoints = GetComponentsInChildren<GrabPoint>(true);
            foreach (var p in GrabPoints) p.Owner = this;
        }

        public virtual bool CanBeGrabbedBy(HandGrabber hand)
        {
            if (!isActiveAndEnabled) return false;
            if (holders.Count == 0) return true;
            return allowTwoHands && holders.Count < 2 && !IsHeldBy(hand);
        }

        // ---------------- hover ----------------
        /// <summary>w10: tint the object itself on hover (items). Handles on a body use only the hand's glow cue.</summary>
        protected virtual bool AutoHighlight => false;
        public bool IsHovered => _hoverCount > 0;

        internal void HoverEnter(HandGrabber hand)
        {
            if (_highlight == null) _highlight = GetComponent<GrabHighlight>();
            if (_highlight == null && AutoHighlight) _highlight = gameObject.AddComponent<GrabHighlight>();
            if (_hoverCount++ == 0 && _highlight != null) _highlight.SetHighlighted(true);
        }

        internal void HoverExit(HandGrabber hand)
        {
            _hoverCount = Mathf.Max(0, _hoverCount - 1);
            if (_hoverCount == 0 && _highlight != null) _highlight.SetHighlighted(false);
        }

        // ---------------- grab ----------------
        internal bool BeginGrab(HandGrabber hand, GrabPoint point)
        {
            if (!CanBeGrabbedBy(hand)) return false;
            transform.GetPositionAndRotation(out var objPos, out var objRot);
            var inv = Quaternion.Inverse(objRot);
            Vector3 handPos; Quaternion handRot;
            if (point != null && point.snap)
            {
                handPos = point.transform.position;
                handRot = point.transform.rotation;
            }
            else
            {
                handPos = hand.PalmPosition;
                handRot = hand.transform.rotation;
            }
            holders.Add(new Holder
            {
                Hand = hand,
                Point = point,
                LocalPos = inv * (handPos - objPos),
                LocalRot = inv * handRot,
            });
            _stuckTime = 0f;
            OnGrabbed(hand, point);
            Grabbed?.Invoke(this, hand);
            onGrab.Invoke();
            return true;
        }

        internal void EndGrab(HandGrabber hand)
        {
            int i = holders.FindIndex(x => x.Hand == hand);
            if (i < 0) return;
            holders.RemoveAt(i);
            if (holders.Count == 0 && Body != null && !Body.isKinematic)
            {
                // keep the hand's momentum (throw), but no wild spin
                Body.linearVelocity = Vector3.ClampMagnitude(hand.Input != null ? hand.Input.Velocity : Body.linearVelocity, maxSpeed);
                Body.angularVelocity = Vector3.ClampMagnitude(Body.angularVelocity, 12f);
            }
            OnReleased(hand);
            Released?.Invoke(this, hand);
            onRelease.Invoke();
        }

        /// <summary>Release from every hand (e.g. scripted event, casualty pulled away).</summary>
        public void ForceRelease()
        {
            for (int i = holders.Count - 1; i >= 0; i--) holders[i].Hand.Release();
        }

        protected virtual void OnGrabbed(HandGrabber hand, GrabPoint point) { }
        protected virtual void OnReleased(HandGrabber hand) { }

        // ---------------- follow ----------------
        /// <summary>Pose this object should have so that each holder's grip matches its hand.</summary>
        protected virtual void ComputeTarget(out Vector3 pos, out Quaternion rot)
        {
            var a = holders[0];
            Quaternion handRotA = a.Hand.transform.rotation;
            rot = handRotA * Quaternion.Inverse(a.LocalRot);
            if (holders.Count > 1)
            {
                var b = holders[1];
                // two hands: keep the line between grips aligned with the line between hands
                Vector3 gripLine = rot * (b.LocalPos - a.LocalPos);
                Vector3 handLine = GripPos(b) - GripPos(a);
                if (gripLine.sqrMagnitude > 1e-6f && handLine.sqrMagnitude > 1e-6f)
                    rot = Quaternion.FromToRotation(gripLine, handLine) * rot;
                Vector3 pa = GripPos(a) - rot * a.LocalPos;
                Vector3 pb = GripPos(b) - rot * b.LocalPos;
                pos = (pa + pb) * 0.5f;
            }
            else
            {
                pos = GripPos(a) - rot * a.LocalPos;
            }
        }

        static Vector3 GripPos(Holder h) => h.Hand.PalmPosition;

        protected virtual void FixedUpdate()
        {
            if (holders.Count == 0) return;
            for (int i = holders.Count - 1; i >= 0; i--)
                if (holders[i].Hand == null || !holders[i].Hand.isActiveAndEnabled) holders.RemoveAt(i);
            if (holders.Count == 0) return;

            ComputeTarget(out var targetPos, out var targetRot);
            float dt = Time.fixedDeltaTime;
            float k = Responsiveness;

            if (Body != null && !Body.isKinematic)
            {
                Vector3 vWant = Vector3.ClampMagnitude((targetPos - Body.position) / dt, maxSpeed);
                Body.linearVelocity = Vector3.Lerp(Body.linearVelocity, vWant, k);
                if (followRotation)
                {
                    var delta = targetRot * Quaternion.Inverse(Body.rotation);
                    delta.ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    Vector3 wWant = float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero
                        : axis * (ang * Mathf.Deg2Rad / dt);
                    Body.angularVelocity = Vector3.Lerp(Body.angularVelocity, Vector3.ClampMagnitude(wWant, 30f), k);
                }
            }
            else
            {
                Vector3 p = Vector3.Lerp(transform.position, targetPos, k);
                Quaternion r = followRotation ? Quaternion.Slerp(transform.rotation, targetRot, k) : transform.rotation;
                if (Body != null) { Body.MovePosition(p); Body.MoveRotation(r); }
                else transform.SetPositionAndRotation(p, r);
            }

            // stuck against geometry far from the hand → let go, like a real slipping grip
            float err = Vector3.Distance(transform.position, targetPos);
            _stuckTime = err > breakDistance ? _stuckTime + dt : 0f;
            if (_stuckTime > 0.25f) ForceRelease();
        }

        protected virtual void OnDisable()
        {
            ForceRelease();
            _hoverCount = 0;
            if (_highlight != null) _highlight.SetHighlighted(false);
        }
    }
}
