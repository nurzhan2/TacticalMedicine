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

        void Update()
        {
            if (casualty == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var root = casualty.transform;
            if (!_yawInit) { _yaw = root.eulerAngles.y; _yawInit = true; }

            Vector3 want = Vector3.zero;
            if (Held && casualty.IsDown && !casualty.IsFalling)
            {
                Crawl(dt);
                Vector3 d = Hand.PalmPosition - transform.position;
                d.y = 0f;
                float stretch = d.magnitude - slack;
                if (stretch > 0f) want = d.normalized * Mathf.Min(dragMaxSpeed, stretch / 0.12f);
                if (_vel.magnitude < 0.05f && stretch < staticBreak) want = Vector3.zero; // stiction
                Haptics.Tension(Input, Mathf.Clamp01(stretch / 0.25f));
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

            Move(root, dt);
        }

        void Move(Transform root, float dt)
        {
            var rig = casualty.rig;
            Vector3 step = _vel * dt;
            Vector3 pivot = transform.position;

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

            // turn head-first toward the pull, pivoting around the handle (feet swing behind)
            float speed = _vel.magnitude;
            if (speed > 0.05f && rig != null)
            {
                Vector3 bodyDir = Vector3.ProjectOnPlane(rig.BodyUp, Vector3.up);
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
