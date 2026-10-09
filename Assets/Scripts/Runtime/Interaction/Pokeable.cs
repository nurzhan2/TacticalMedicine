using System;
using UnityEngine;
using UnityEngine.Events;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// A surface that a fingertip or palm can press into (pulse points, wound pressure, buttons, chest).
    /// The surface is the local XZ rectangle of this transform, outward normal = transform.up.
    /// Depth is measured in metres along -up (0 = touching, maxDepth = fully pressed).
    /// </summary>
    public class Pokeable : MonoBehaviour
    {
        public enum ContactKind { Finger, Palm, Any }

        public ContactKind contact = ContactKind.Finger;
        [Tooltip("Surface rectangle size (local X, local Z) in metres, unscaled.")]
        public Vector2 size = new Vector2(0.06f, 0.06f);
        public float maxDepth = 0.02f;
        [Tooltip("Ignore contact points deeper than this behind the surface (came from the other side).")]
        public float backfaceTolerance = 0.06f;

        public UnityEvent onTouchBegin = new UnityEvent();
        public UnityEvent onTouchEnd = new UnityEvent();
        public event Action<Pokeable, HandGrabber> TouchBegan;
        public event Action<Pokeable, HandGrabber> TouchEnded;

        public HandGrabber Toucher { get; private set; }
        public bool IsTouching => Toucher != null;
        /// <summary>Penetration in metres, clamped to [0, maxDepth].</summary>
        public float Depth { get; private set; }
        public float Depth01 => maxDepth > 0f ? Depth / maxDepth : 0f;
        /// <summary>Seconds of continuous contact.</summary>
        public float ContactTime { get; private set; }

        /// <summary>Signed penetration of a world point (positive = below surface), NaN if outside the rectangle.</summary>
        public float Penetration(Vector3 world)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * (world - transform.position);
            if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.z) > size.y * 0.5f) return float.NaN;
            return -local.y;
        }

        protected virtual void Update()
        {
            HandGrabber best = null;
            float bestDepth = 0f;
            foreach (var hand in HandGrabber.All)
            {
                if (hand == null || hand.Held != null) continue; // a holding hand does not poke
                float d = Measure(hand);
                if (!float.IsNaN(d) && d > 0f && d < maxDepth + backfaceTolerance && d > bestDepth)
                {
                    bestDepth = d; best = hand;
                }
            }

            if (best != Toucher)
            {
                if (Toucher != null) EndTouch();
                if (best != null) BeginTouch(best);
            }

            float prev = Depth;
            Depth = best != null ? Mathf.Min(bestDepth, maxDepth) : 0f;
            ContactTime = best != null ? ContactTime + Time.deltaTime : 0f;
            if (best != null) OnDepth(prev, Depth);
        }

        float Measure(HandGrabber hand)
        {
            switch (contact)
            {
                case ContactKind.Finger: return Penetration(hand.FingertipPosition);
                case ContactKind.Palm: return Penetration(hand.PalmPosition);
                default:
                    float a = Penetration(hand.FingertipPosition), b = Penetration(hand.PalmPosition);
                    if (float.IsNaN(a)) return b;
                    if (float.IsNaN(b)) return a;
                    return Mathf.Max(a, b);
            }
        }

        void BeginTouch(HandGrabber hand)
        {
            Toucher = hand;
            Haptics.Play(hand.Input, Haptics.Contact);
            OnTouchBegin(hand);
            TouchBegan?.Invoke(this, hand);
            onTouchBegin.Invoke();
        }

        void EndTouch()
        {
            var h = Toucher;
            Toucher = null;
            OnTouchEnd(h);
            TouchEnded?.Invoke(this, h);
            onTouchEnd.Invoke();
        }

        protected virtual void OnTouchBegin(HandGrabber hand) { }
        protected virtual void OnTouchEnd(HandGrabber hand) { }
        protected virtual void OnDepth(float previous, float current) { }

        protected virtual void OnDisable()
        {
            if (Toucher != null) EndTouch();
            Depth = 0f;
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.001f, size.y));
            Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.03f);
        }
    }
}
