using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Comfort
{
    /// <summary>
    /// Open-battlefield locomotion on the OVRCameraRig root (V2): room-scale walking stays primary; the left stick
    /// moves smoothly (head-relative, collides with walls/cover so the stick never pushes you through a ruin),
    /// pushing the right stick forward shows a parabolic teleport arc from the right controller and releasing it
    /// teleports there (only onto walkable ground, never onto/into cover), the right stick sideways snap-turns
    /// (<see cref="ComfortRig"/>). The rig follows the ground height under the head (craters, rubble floor), so a
    /// physical crouch is always measured from the real ground → cover works at true kneeling height.
    /// Disabled while the player drags a casualty (the drag moves the rig itself).
    /// Desktop: T = teleport where you look. Tests drive it via <see cref="MoveOverride"/>/<see cref="Teleport"/>.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class Locomotion : MonoBehaviour
    {
        public static bool SmoothEnabled = true;
        public static bool TeleportEnabled = true;

        public Transform head;
        [Tooltip("Controller the teleport arc starts from (RightHandAnchor).")]
        public Transform aimHand;
        public LineRenderer arcLine;
        public Transform reticle;
        public float speed = 1.7f;
        public float bodyRadius = 0.22f;
        public float arcVelocity = 9f;
        public float maxTeleportDistance = 12f;
        public float maxStepUp = 0.45f;
        [Tooltip("Layers that block stick movement and teleport (walls, cover, props).")]
        public LayerMask blockMask = ~0;
        [Tooltip("Layers you can stand on.")]
        public LayerMask groundMask = ~0;

        /// <summary>Tests: replaces the left stick when set.</summary>
        public Vector2? MoveOverride;
        public bool Aiming { get; private set; }
        public bool AimValid { get; private set; }
        public Vector3 AimPoint { get; private set; }

        readonly List<Vector3> _arc = new List<Vector3>(48);
        CasualtyDrag[] _drags;
        float _dragScanT;

        void Start()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (arcLine != null) arcLine.enabled = false;
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        bool Dragging()
        {
            if (Time.time > _dragScanT) { _drags = FindObjectsByType<CasualtyDrag>(FindObjectsSortMode.None); _dragScanT = Time.time + 2f; }
            if (_drags == null) return false;
            foreach (var d in _drags) if (d != null && d.Held) return true;
            return false;
        }

        void Update()
        {
            if (head == null) return;
            float dt = Time.deltaTime;
            bool drag = Dragging();

            Vector2 move = MoveOverride ?? (DesktopSimulator.XRActive ? OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick) : Vector2.zero);
            if (SmoothEnabled && !drag && move.sqrMagnitude > 0.02f) Move(move, dt);

            if (TeleportEnabled && !drag) UpdateTeleport();
            FollowGround(dt);
        }

        // ------------------------------------------------------------------ smooth move
        /// <summary>Moves the rig along the head's yaw; slides along walls; returns the distance moved.</summary>
        public float Move(Vector2 stick, float dt)
        {
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 delta = (fwd * stick.y + right * stick.x);
            if (delta.sqrMagnitude > 1f) delta.Normalize();
            delta *= speed * dt;
            return MoveBy(delta);
        }

        public float MoveBy(Vector3 delta)
        {
            delta.y = 0f;
            float len = delta.magnitude;
            if (len < 1e-5f) return 0f;
            Vector3 body = BodyPoint();
            for (int i = 0; i < 2 && len > 1e-5f; i++)
            {
                Vector3 dir = delta / len;
                if (Physics.CapsuleCast(body + Vector3.up * 0.25f, body + Vector3.up * 0.9f, bodyRadius, dir, out var hit,
                        len + 0.02f, blockMask, QueryTriggerInteraction.Ignore) && hit.normal.y < 0.7f)
                {
                    float free = Mathf.Max(0f, hit.distance - 0.02f);
                    transform.position += dir * free;
                    body += dir * free;
                    // slide: remove the component into the wall
                    Vector3 n = hit.normal; n.y = 0f; n.Normalize();
                    delta = Vector3.ProjectOnPlane(delta - dir * free, n);
                    len = delta.magnitude;
                    continue;
                }
                transform.position += delta;
                return len;
            }
            return 0f;
        }

        /// <summary>Feet point under the head (the tracked body, not the rig origin).</summary>
        Vector3 BodyPoint() => new Vector3(head.position.x, transform.position.y, head.position.z);

        // ------------------------------------------------------------------ ground
        void FollowGround(float dt)
        {
            if (!GroundAt(head.position, out float y)) return;
            float dy = y - transform.position.y;
            if (Mathf.Abs(dy) < 0.003f || dy > maxStepUp) return;
            // fast when stepping down into a crater, quick but smooth up
            float rate = 1f - Mathf.Exp(-dt * 14f);
            var p = transform.position;
            p.y += dy * rate;
            transform.position = p;
        }

        /// <summary>Highest walkable surface under (x,z) within reach of the current feet.</summary>
        public bool GroundAt(Vector3 above, out float y)
        {
            float feet = transform.position.y;
            Vector3 from = new Vector3(above.x, feet + maxStepUp + 0.3f, above.z);
            if (Physics.SphereCast(from, 0.08f, Vector3.down, out var hit, maxStepUp + 2.3f, groundMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.6f && hit.rigidbody == null)
            {
                y = hit.point.y;
                return true;
            }
            y = feet;
            return false;
        }

        // ------------------------------------------------------------------ teleport
        void UpdateTeleport()
        {
            bool xr = DesktopSimulator.XRActive;
            float stickY = xr ? OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y : 0f;
            float stickX = xr ? OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x : 0f;
            bool desktopKey = !xr && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.tKey.isPressed;
            bool wantAim = (stickY > 0.7f && Mathf.Abs(stickX) < 0.6f) || desktopKey;
            Transform from = xr && aimHand != null ? aimHand : head;

            if (wantAim)
            {
                Aiming = true;
                AimValid = ComputeArc(from.position, from.forward, out var p);
                AimPoint = p;
                ShowArc(true);
            }
            else if (Aiming && (xr ? stickY < 0.3f : true))
            {
                Aiming = false;
                ShowArc(false);
                if (AimValid) Teleport(AimPoint);
            }
        }

        void ShowArc(bool on)
        {
            if (arcLine != null)
            {
                arcLine.enabled = on;
                if (on)
                {
                    arcLine.positionCount = _arc.Count;
                    for (int i = 0; i < _arc.Count; i++) arcLine.SetPosition(i, _arc[i]);
                    var c = AimValid ? new Color(0.85f, 0.95f, 1f, 0.9f) : new Color(1f, 0.35f, 0.25f, 0.8f);
                    arcLine.startColor = c;
                    arcLine.endColor = new Color(c.r, c.g, c.b, 0.3f);
                }
            }
            if (reticle != null)
            {
                reticle.gameObject.SetActive(on && AimValid);
                if (on) reticle.position = AimPoint + Vector3.up * 0.02f;
            }
        }

        /// <summary>Parabolic arc (ballistic, g = 9.81) from <paramref name="origin"/>; true when it lands on a valid
        /// spot within range. <paramref name="point"/> = landing point (or last arc point).</summary>
        public bool ComputeArc(Vector3 origin, Vector3 dir, out Vector3 point)
        {
            _arc.Clear();
            Vector3 v = dir.normalized * arcVelocity;
            Vector3 p = origin;
            _arc.Add(p);
            const float step = 0.05f;
            for (int i = 0; i < 60; i++)
            {
                Vector3 next = p + v * step + 0.5f * Physics.gravity * step * step;
                v += Physics.gravity * step;
                Vector3 seg = next - p;
                if (Physics.Raycast(p, seg.normalized, out var hit, seg.magnitude, blockMask | groundMask, QueryTriggerInteraction.Ignore))
                {
                    _arc.Add(hit.point);
                    point = hit.point;
                    return ValidTarget(hit);
                }
                _arc.Add(next);
                p = next;
                if (p.y < transform.position.y - 5f) break;
            }
            point = p;
            return false;
        }

        /// <summary>Walkable: flat enough, not on top of cover/walls, not a movable prop, within range, and the
        /// standing body fits (no wall through it).</summary>
        public bool ValidTarget(RaycastHit hit)
        {
            if (hit.normal.y < 0.75f) return false;
            if (hit.rigidbody != null) return false;
            var go = hit.collider.gameObject;
            if (go.CompareTag("Cover") || go.layer == LayerMask.NameToLayer("Cover")) return false;
            Vector3 flat = hit.point - transform.position; flat.y = 0f;
            if (flat.magnitude > maxTeleportDistance) return false;
            if (Physics.CheckCapsule(hit.point + Vector3.up * (bodyRadius + 0.3f), hit.point + Vector3.up * 1.6f, bodyRadius,
                    blockMask, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// <summary>Moves the rig so the tracked head ends up above <paramref name="ground"/>.</summary>
        public void Teleport(Vector3 ground)
        {
            Vector3 headOffset = head.position - transform.position; headOffset.y = 0f;
            transform.position = new Vector3(ground.x - headOffset.x, ground.y, ground.z - headOffset.z);
        }
    }
}
