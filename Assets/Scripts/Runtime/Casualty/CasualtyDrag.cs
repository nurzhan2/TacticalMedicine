using System.Collections.Generic;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Drag by the plate-carrier handle (behind the neck): the body slides on the ground behind the hand with
    /// friction and mass lag (~95 kg with gear never teleports), turns head-first toward the pull, follows the
    /// floor (drops into the trench over an edge, pitching along the slope), head nods with jolts. Rough jerks
    /// loosen tourniquets (S7). Thumbstick back while holding = "pull-to-crawl" for small play areas.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public sealed class CasualtyDrag : ProxyHandle
    {
        public CasualtyController casualty;
        public float massKg = 95f;
        [Tooltip("Hand travel before the strap is taut (m).")]
        public float slack = 0.04f;
        [Tooltip("Strap stretch needed to break static friction (m).")]
        public float staticBreak = 0.07f;
        public float dragMaxSpeed = 1.5f;
        public float crawlSpeed = 0.55f;
        [Tooltip("Acceleration (m/s²) above which a jerk loosens tourniquets.")]
        public float joltThreshold = 5f;
        public float maxStepUp = 0.3f;

        public float DraggedDistance { get; private set; }
        public Vector3 Velocity => _vel;
        public float LastJolt { get; private set; }
        public bool Held => Hand != null;

        Vector3 _vel;
        float _fallSpeed, _yaw, _pitch;
        bool _yawInit, _reported;
        Transform _playerRig;

        /// <summary>Shoulder-strap handles of the plate carrier (a second hand, or a two-hand drag).</summary>
        public readonly List<ProxyHandle> strapHandles = new List<ProxyHandle>();
        [Tooltip("Each extra pulling hand adds this fraction of drag speed (two hands drag a 95 kg man faster).")]
        public float extraHandBoost = 0.35f;
        [Tooltip("Limb grips (wrist / ankle) pull the body once the hand is this far beyond the limb's reach (m).")]
        public float limbSlack = 0.03f;

        /// <summary>Hands currently pulling the body (drag handle, straps, wrists, ankles).</summary>
        public int PullingHands { get; private set; }
        /// <summary>Any drag grip is held (drag handle, straps, wrist or ankle).</summary>
        public bool AnyHeld => Held || strapHandles.Exists(h => h != null && h.Hand != null) || LimbGrips() > 0;
        public bool BlockedByWall { get; private set; }

        int LimbGrips()
        {
            var ci = casualty != null ? casualty.Interaction : null;
            if (ci == null) return 0;
            int n = 0;
            foreach (var h in ci.LimbHandles) if (h != null && h.Held && h.grip != LimbHandle.Grip.Knee) n++;
            return n;
        }

        float _lastBlockLog = -10f;
        Vector3 _wantSum, _pivotSum;
        int _pulls;
        float _maxStretch;

        void AddPull(Vector3 at, Vector3 palm, float free, IHandInput input)
        {
            Vector3 d = palm - at;
            d.y = 0f;
            float stretch = d.magnitude - free;
            if (stretch > 0f) _wantSum += d.normalized * Mathf.Min(dragMaxSpeed, stretch / 0.12f);
            _maxStretch = Mathf.Max(_maxStretch, stretch);
            _pivotSum += at;
            _pulls++;
            HapticsProfile.Current.DragRumble(input, Mathf.Clamp01(stretch / 0.25f) * Mathf.Clamp01(massKg / 95f));
        }

        void Update()
        {
            if (casualty == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var root = casualty.transform;
            if (!_yawInit) { _yaw = root.eulerAngles.y; _yawInit = true; }

            Vector3 want = Vector3.zero;
            _wantSum = _pivotSum = Vector3.zero; _pulls = 0; _maxStretch = float.MinValue;
            if (casualty.IsDown && !casualty.IsFalling)
            {
                if (Held) { Crawl(dt); AddPull(transform.position, Hand.PalmPosition, slack, Input); }
                foreach (var sh in strapHandles)
                    if (sh != null && sh.Hand != null) AddPull(sh.transform.position, sh.Hand.PalmPosition, slack, sh.Input);
                // wrist / ankle: the limb straightens toward the hand first, then the whole body follows
                var ci = casualty.Interaction;
                if (ci != null && casualty.rig != null)
                    foreach (var lh in ci.LimbHandles)
                    {
                        if (lh == null || !lh.Held || lh.grip == LimbHandle.Grip.Knee) continue;
                        if (!LimbMath.Points(casualty.rig, lh.limb, out var lr, out var lm, out var lt, out _)) continue;
                        float reach = Vector3.Distance(lr, lm) + Vector3.Distance(lm, lt);
                        AddPull(lr, lh.Hand.PalmPosition, reach * 0.92f + limbSlack, lh.Input);
                    }
            }
            PullingHands = _pulls;
            if (_pulls > 0)
            {
                float boost = 1f + extraHandBoost * (_pulls - 1);
                want = Vector3.ClampMagnitude(_wantSum / _pulls * boost, dragMaxSpeed * boost);
                if (_vel.magnitude < 0.05f && _maxStretch < staticBreak) want = Vector3.zero; // stiction
            }
            // heavier = slower to respond; ground friction brakes quickly once the pull stops
            float k = 1f - Mathf.Exp(-dt * (want == Vector3.zero ? 9f : 5f) * 80f / massKg);
            Vector3 prev = _vel;
            _vel = Vector3.Lerp(_vel, want, k);
            if (_vel.sqrMagnitude < 1e-6f) _vel = Vector3.zero;

            float accel = (_vel - prev).magnitude / dt;
            if (accel > joltThreshold)
            {
                LastJolt = accel;
                float sev = Mathf.Clamp01((accel - joltThreshold) / 15f) * dt * 6f;
                foreach (var tq in casualty.Patient.tourniquets) tq.Slip(sev);
                casualty.Patient.RefreshTourniquets();
                if (casualty.rig != null) casualty.rig.HeadJolt(Mathf.Clamp(accel * 0.6f, 0f, 12f));
                casualty.Handle(transform.position, accel * 0.08f, dt);
            }

            Move(root, dt, _pulls > 0 ? _pivotSum / _pulls : transform.position);
        }

        /// <summary>
        /// Would the body run into a wall/obstacle moving by `step`? Spheres at knee height along the body (head,
        /// chest, hips, feet) are swept; floors, ramps, his own colliders, loose items and the player are ignored.
        /// </summary>
        public bool Obstructed(Vector3 step)
        {
            if (step.sqrMagnitude < 1e-8f || casualty == null) return false;
            var root = casualty.transform;
            var rig = casualty.rig;
            Vector3 dir = step.normalized;
            float dist = step.magnitude + 0.06f;
            Transform hb = rig != null ? rig.Bone(HumanBodyBones.Hips) : null, headB = rig != null ? rig.Bone(HumanBodyBones.Head) : null;
            Transform fl = rig != null ? rig.Bone(HumanBodyBones.LeftFoot) : null, fr = rig != null ? rig.Bone(HumanBodyBones.RightFoot) : null;
            Vector3 hips = hb != null ? hb.position : root.position;
            Vector3 head = headB != null ? headB.position : transform.position;
            Vector3 feet = fl != null && fr != null ? (fl.position + fr.position) * 0.5f : hips;
            Transform player = Camera.main != null ? Camera.main.transform.root : null;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = i == 0 ? head : i == 1 ? Vector3.Lerp(head, hips, 0.5f) : i == 2 ? hips : feet;
                p.y = root.position.y + 0.28f;
                var hits = Physics.SphereCastAll(p, 0.11f, dir, dist, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider == null || h.rigidbody != null || h.collider is CharacterController) continue;
                    var t = h.collider.transform;
                    if (t.IsChildOf(root) || (player != null && t.IsChildOf(player))) continue;
                    if (h.distance <= 0f) continue; // overlapping at the start (rubble he lies on, a doorframe he is in)
                    if (Mathf.Abs(h.normal.y) > 0.6f) continue; // floor / ramp / step top
                    if (Vector3.Dot(h.normal, dir) > -0.2f) continue; // grazing along it
                    // only wall-sized solids (walls, sandbags, wrecks, blocks): not pebbles, hint ghosts, small props
                    var bs = h.collider.bounds.size;
                    if (Mathf.Max(bs.x, bs.z) < 0.6f || h.collider.bounds.max.y < root.position.y + 0.3f) continue;
                    if (h.collider.GetComponentInParent<Interaction.Grabbable>() != null) continue;
                    if (Time.time - _lastBlockLog > 2f) { _lastBlockLog = Time.time; Debug.Log("[FMVR] drag blocked by " + h.collider.name + " at " + h.point.ToString("F2")); }
                    return true;
                }
            }
            return false;
        }

        void Move(Transform root, float dt, Vector3 pivot)
        {
            var rig = casualty.rig;
            Vector3 step = _vel * dt;
            BlockedByWall = false;
            if (step.sqrMagnitude > 0f && Obstructed(step))
            {
                // never through a wall: the body stops against it (pull around the corner)
                step = Vector3.zero;
                _vel *= 0.2f;
                BlockedByWall = true;
            }

            // blocked by a wall/step too high at the head end
            if (step.sqrMagnitude > 0f && FloorAt(pivot + step.normalized * 0.25f, out float ahead)
                && ahead > root.position.y + maxStepUp)
            {
                step = Vector3.zero;
                _vel = Vector3.zero;
            }

            root.position += step;
            DraggedDistance += step.magnitude;
            if (!_reported && DraggedDistance > 1f)
            {
                _reported = true;
                casualty.Protocol.Perform(MedAction.PlayerDrag);
            }

            // turn the pulled end toward the pull (head-first by the handle/straps, feet-first by the ankles),
            // pivoting around the grip (the rest of the body swings behind)
            float speed = _vel.magnitude;
            Transform hbNow = rig != null ? rig.Bone(HumanBodyBones.Hips) : null;
            Vector3 hipsNow = hbNow != null ? hbNow.position : root.position;
            if (speed > 0.05f && rig != null && !BlockedByWall)
            {
                Vector3 bodyDir = Vector3.ProjectOnPlane(pivot - hipsNow, Vector3.up);
                if (bodyDir.sqrMagnitude < 0.04f) bodyDir = Vector3.ProjectOnPlane(rig.BodyUp, Vector3.up);
                if (bodyDir.sqrMagnitude > 0.01f)
                {
                    float ang = Vector3.SignedAngle(bodyDir, _vel, Vector3.up);
                    float turn = Mathf.Clamp(ang, -1f, 1f) * Mathf.Min(Mathf.Abs(ang), 70f * speed * dt);
                    _yaw += turn;
                    Vector3 offs = root.position - pivot;
                    root.position = pivot + Quaternion.AngleAxis(turn, Vector3.up) * offs;
                }
            }

            // follow the floor: drop over an edge (gravity), climb small steps, pitch along a slope
            Vector3 hips = rig != null && rig.Bone(HumanBodyBones.Hips) != null ? rig.Bone(HumanBodyBones.Hips).position : root.position;
            if (FloorAt(hips, out float fh))
            {
                float y = root.position.y;
                if (fh < y - 0.01f) { _fallSpeed += 9.81f * dt; y = Mathf.Max(fh, y - _fallSpeed * dt); }
                else { _fallSpeed = 0f; y = Mathf.MoveTowards(y, fh, dt * 1.5f); }
                root.position = new Vector3(root.position.x, y, root.position.z);
                if (FloorAt(pivot, out float headFloor))
                {
                    Vector3 flat = pivot - hips; flat.y = 0f;
                    float slope = flat.magnitude > 0.2f ? Mathf.Atan2(headFloor - fh, flat.magnitude) * Mathf.Rad2Deg : 0f;
                    _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(slope, -35f, 35f) * 0.6f, 60f * dt);
                }
            }
            if (_vel != Vector3.zero || Mathf.Abs(_pitch) > 0.01f)
            {
                Vector3 side = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
                root.rotation = Quaternion.AngleAxis(-_pitch, side) * Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        /// <summary>Floor height under a point: highest static (non-rigidbody, non-trigger) surface.</summary>
        public static bool FloorAt(Vector3 p, out float y)
        {
            y = 0f;
            var hits = Physics.RaycastAll(p + Vector3.up * 0.8f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            foreach (var h in hits)
            {
                if (h.rigidbody != null) continue; // items lying around, not the ground
                if (h.point.y > best) best = h.point.y;
            }
            if (best == float.MinValue) return false;
            y = best;
            return true;
        }

        /// <summary>Pull-to-crawl: thumbstick back moves the player rig away from the casualty.</summary>
        void Crawl(float dt)
        {
            var input = Input;
            if (input == null || input.Thumbstick.y > -0.3f) return;
            if (_playerRig == null)
            {
                var sim = FindAnyObjectByType<DesktopSimulator>();
                if (sim != null) _playerRig = sim.rigRoot;
                else if (Camera.main != null) _playerRig = Camera.main.transform.root;
            }
            if (_playerRig == null) return;
            Vector3 away = Hand.PalmPosition - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f && Camera.main != null) away = Camera.main.transform.position - transform.position;
            away.y = 0f;
            _playerRig.position += away.normalized * (crawlSpeed * -input.Thumbstick.y * dt);
        }
    }
}
