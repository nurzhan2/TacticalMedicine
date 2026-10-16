using FieldMedic.Casualty;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// Physical hands (w8): the VISUAL glove does not pass through walls, the ground, the IFAK or Erlan's body. It
    /// sweeps from where it was toward the tracked hand and stops (and slides) at solid surfaces; the tracked hand
    /// may go further — all interaction logic keeps using the tracked pose, so pressing a wound "into" the body
    /// still works while the glove rests on the surface. When the two are more than 5 cm apart a faint ghost glove
    /// shows the tracked position. Heavy loads (dragging Erlan, a full item) pull the visual hand down a little.
    /// Lives on the glove visual (child of the hand rig).
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class HandPhysicalVisual : MonoBehaviour
    {
        public float palmRadius = 0.035f;
        public float bodyRadius = 0.028f;
        public float ghostDistance = 0.05f;
        public float maxSag = 0.04f;
        public LayerMask solidMask = ~0;
        public bool collide = true;

        /// <summary>Visual palm minus tracked palm (world).</summary>
        public Vector3 Offset { get; private set; }
        public float Separation => Offset.magnitude;
        public bool GhostVisible { get; private set; }
        public Vector3 VisualPalm { get; private set; }
        public Vector3 TrackedPalm { get; private set; }
        public float Sag { get; private set; }
        public Collider LastBlocker { get; private set; }

        HandGrabber _grabber;
        GloveHand _glove;
        Vector3 _restLocal;
        Quaternion _restRot;
        bool _init;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Mesh _ghostMesh;
        static Material _ghostMat;

        void Awake()
        {
            _grabber = GetComponentInParent<HandGrabber>();
            _glove = GetComponent<GloveHand>();
            _restLocal = transform.localPosition;
            _restRot = transform.localRotation;
        }

        void OnDisable() { transform.localPosition = _restLocal; _init = false; GhostVisible = false; }

        void LateUpdate()
        {
            if (_grabber == null || transform.parent == null) return;
            float dt = Time.deltaTime;
            Transform hand = transform.parent;
            Vector3 palmLocal = _grabber.palmOffset;
            Vector3 tracked = hand.TransformPoint(palmLocal);
            TrackedPalm = tracked;
            var held = _grabber.Held;

            // weight: heavy things pull the hand down
            float load = held != null ? held.VisualLoad : 0f;
            Sag = Mathf.MoveTowards(Sag, load * maxSag, dt * 0.25f);
            Vector3 target = tracked + Vector3.down * Sag;

            if (!_init || (target - VisualPalm).sqrMagnitude > 0.35f * 0.35f) { VisualPalm = target; _init = true; } // first frame / teleport / snap turn
            Vector3 pos = VisualPalm;
            LastBlocker = null;
            // holding something: the item follows the tracked hand, the glove stays on it (no collision)
            if (collide && held == null)
            {
                Vector3 delta = target - pos;
                for (int it = 0; it < 3 && delta.sqrMagnitude > 1e-8f; it++)
                {
                    float len = delta.magnitude;
                    Vector3 dir = delta / len;
                    if (!Sweep(pos, dir, len, out var hit)) { pos += delta; break; }
                    float go = Mathf.Max(0f, hit.distance - 0.002f);
                    pos += dir * go;
                    LastBlocker = hit.collider;
                    delta = Vector3.ProjectOnPlane(delta - dir * go, hit.normal);   // slide along the surface
                }
                pos = PushOutOfBodies(pos);
            }
            else pos = target;
            VisualPalm = pos;
            Offset = pos - tracked;
            transform.position = hand.TransformPoint(_restLocal) + Offset;
            transform.localRotation = _restRot;

            GhostVisible = Offset.magnitude > ghostDistance && held == null;
            if (GhostVisible) DrawGhost(hand);
        }

        bool Sweep(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(from, palmRadius, dir, _hits, dist, solidMask, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue;            // started inside: ignore
                if (Ignored(h.collider)) continue;
                if (h.distance < bd) { bd = h.distance; best = h; any = true; }
            }
            return any;
        }

        bool Ignored(Collider c)
        {
            if (c == null) return true;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return true; // loose objects get pushed, not walls
            if (c.GetComponentInParent<Grabbable>() != null) return true;                    // items, handles
            if (c.GetComponentInParent<HandRig>() != null) return true;
            if (c is CharacterController) return true;
            return false;
        }

        /// <summary>Erlan's body parts are triggers: treat their capsules/spheres as solid for the glove.</summary>
        Vector3 PushOutOfBodies(Vector3 p)
        {
            for (int ci = 0; ci < CasualtyController.All.Count; ci++)
            {
                var c = CasualtyController.All[ci];
                if (c == null || c.rig == null) continue;
                var parts = c.rig.Parts;
                Vector3 hips = c.transform.position;
                if ((p - hips).sqrMagnitude > 3f * 3f) continue;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part == null) continue;
                    var col = part.Collider;
                    if (col == null) continue;
                    if (!Segment(col, out Vector3 a, out Vector3 b, out float r)) continue;
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    Vector3 q = a + ab * t;
                    Vector3 d = p - q;
                    float min = r * 0.85f + bodyRadius;   // the trigger capsules are a little fat: rest on the clothing
                    float dist = d.magnitude;
                    if (dist >= min) continue;
                    Vector3 n = dist > 1e-5f ? d / dist : Vector3.up;
                    p = q + n * min;
                    LastBlocker = col;
                }
            }
            return p;
        }

        static bool Segment(Collider col, out Vector3 a, out Vector3 b, out float r)
        {
            var t = col.transform;
            float s = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Max(Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z)));
            switch (col)
            {
                case SphereCollider sc:
                    a = b = t.TransformPoint(sc.center);
                    r = sc.radius * s;
                    return true;
                case CapsuleCollider cc:
                {
                    Vector3 axis = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                    r = cc.radius * s;
                    float half = Mathf.Max(0f, cc.height * 0.5f * s - r);
                    Vector3 c = t.TransformPoint(cc.center);
                    Vector3 w = t.TransformDirection(axis).normalized;
                    a = c - w * half; b = c + w * half;
                    return true;
                }
            }
            a = b = Vector3.zero; r = 0f;
            return false;
        }

        void DrawGhost(Transform hand)
        {
            if (_glove == null || _glove.Renderer == null) return;
            if (_ghostMesh == null) _ghostMesh = new Mesh { name = "GhostGlove" };
            _glove.Renderer.BakeMesh(_ghostMesh, true);
            if (_ghostMat == null) _ghostMat = FieldMedic.UI.UiMesh.Mat(new Color(0.78f, 0.9f, 1f, 0.22f), 3050);
            // the baked mesh is in the renderer's space at the VISUAL pose: draw it shifted back to the tracked pose
            var m = Matrix4x4.Translate(-Offset) * _glove.Renderer.transform.localToWorldMatrix;
            Graphics.DrawMesh(_ghostMesh, m, _ghostMat, gameObject.layer);
        }

        void OnDestroy() { if (_ghostMesh != null) Destroy(_ghostMesh); }
    }
}
