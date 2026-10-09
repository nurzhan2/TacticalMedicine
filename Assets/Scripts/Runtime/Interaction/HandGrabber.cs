using System;
using System.Collections.Generic;
using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// Proximity grab for one hand: sphere overlap at the palm, best candidate by priority then distance,
    /// hover highlight + haptic tick, grab on grip press, release on grip open (with hysteresis).
    /// Also exposes palm/fingertip points used by <see cref="Pokeable"/>.
    /// </summary>
    [RequireComponent(typeof(HandRig))]
    public sealed class HandGrabber : MonoBehaviour
    {
        [Tooltip("Palm centre relative to the hand transform.")]
        public Vector3 palmOffset = new Vector3(0f, -0.01f, 0.04f);
        [Tooltip("Index fingertip relative to the hand transform (poking, pulse checks).")]
        public Vector3 fingertipOffset = new Vector3(0f, -0.015f, 0.12f);
        public float grabRadius = 0.07f;
        [Range(0f, 1f)] public float gripOn = 0.6f;
        [Range(0f, 1f)] public float gripOff = 0.35f;
        public LayerMask grabMask = ~0;

        public static readonly List<HandGrabber> All = new List<HandGrabber>();

        public HandRig Rig { get; private set; }
        public IHandInput Input => Rig != null ? Rig.Input : null;
        public Handedness Handedness => Rig != null ? Rig.handedness : Handedness.Right;
        public Grabbable Hovered { get; private set; }
        public GrabPoint HoveredPoint { get; private set; }
        public Grabbable Held { get; private set; }
        public GrabPoint HeldPoint { get; private set; }
        public bool IsGripping { get; private set; }

        public Vector3 PalmPosition => transform.TransformPoint(palmOffset);
        public Vector3 FingertipPosition => transform.TransformPoint(fingertipOffset);

        public event Action<HandGrabber, Grabbable> GrabStarted;
        public event Action<HandGrabber, Grabbable> GrabEnded;

        readonly Collider[] _buffer = new Collider[32];
        readonly HashSet<Grabbable> _seen = new HashSet<Grabbable>();

        void Awake() => Rig = GetComponent<HandRig>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }

        void OnDisable()
        {
            All.Remove(this);
            Release();
            SetHover(null, null);
        }

        void Update()
        {
            var input = Input;
            if (input == null) return;

            bool wasGripping = IsGripping;
            IsGripping = IsGripping ? input.Grip > gripOff : input.Grip >= gripOn;
            bool gripPressed = IsGripping && !wasGripping;

            if (Held != null)
            {
                if (!IsGripping || !input.IsTracked) Release();
                return;
            }

            FindBest(out var best, out var bestPoint);
            SetHover(best, bestPoint);
            if (gripPressed && best != null) TryGrab(best, bestPoint);
        }

        /// <summary>Best grab candidate near the palm (null if none).</summary>
        public void FindBest(out Grabbable best, out GrabPoint bestPoint)
        {
            best = null; bestPoint = null;
            Vector3 palm = PalmPosition;
            int n = Physics.OverlapSphereNonAlloc(palm, grabRadius, _buffer, grabMask, QueryTriggerInteraction.Collide);
            float bestScore = float.NegativeInfinity;
            _seen.Clear();
            for (int i = 0; i < n; i++)
            {
                var col = _buffer[i];
                var g = col.GetComponentInParent<Grabbable>();
                if (g == null || !g.CanBeGrabbedBy(this)) continue;

                float colDist = Vector3.Distance(SafeClosestPoint(col, palm), palm);
                bool first = _seen.Add(g);

                GrabPoint point = null;
                float pointDist = float.PositiveInfinity;
                if (first && g.GrabPoints != null)
                {
                    foreach (var p in g.GrabPoints)
                    {
                        if (p == null || !p.isActiveAndEnabled || !p.Allows(Handedness)) continue;
                        float d = p.Distance(palm);
                        if (d <= p.radius && d < pointDist) { pointDist = d; point = p; }
                    }
                }
                if (point == null && g.requireGrabPoint) continue;

                float dist = point != null ? pointDist : colDist;
                float score = (g.priority + (point != null ? point.priorityBonus + 1 : 0)) * 1000f - dist;
                if (score > bestScore) { bestScore = score; best = g; bestPoint = point; }
            }
        }

        static Vector3 SafeClosestPoint(Collider c, Vector3 p)
        {
            if (c is MeshCollider mc && !mc.convex) return c.bounds.ClosestPoint(p);
            return c.ClosestPoint(p);
        }

        public bool TryGrab(Grabbable g, GrabPoint point = null)
        {
            if (g == null) return false;
            if (Held != null) Release();
            if (!g.BeginGrab(this, point)) return false;
            SetHover(null, null);
            Held = g;
            HeldPoint = point;
            Haptics.Play(Input, Haptics.Grab);
            GrabStarted?.Invoke(this, g);
            return true;
        }

        public void Release()
        {
            if (Held == null) return;
            var g = Held;
            Held = null;
            HeldPoint = null;
            g.EndGrab(this);
            Haptics.Play(Input, Haptics.Release);
            GrabEnded?.Invoke(this, g);
        }

        void SetHover(Grabbable g, GrabPoint p)
        {
            HoveredPoint = p;
            if (g == Hovered) return;
            if (Hovered != null) Hovered.HoverExit(this);
            Hovered = g;
            if (Hovered != null)
            {
                Hovered.HoverEnter(this);
                Haptics.Play(Input, Haptics.Hover);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(PalmPosition, grabRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(FingertipPosition, 0.01f);
        }
    }
}
