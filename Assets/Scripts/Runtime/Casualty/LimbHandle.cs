using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Grab a limb at the ankle / knee / wrist: the limb follows the hand through two-bone IK, with weight
    /// (lag + sag), joint limits, pain from speed near the wound, and on release it falls back to the
    /// animated pose with a gravity-like ease. Lifting a bleeding leg for long instead of a tourniquet is a
    /// graded mistake; lifting to route a tourniquet or to examine is fine.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class LimbHandle : ProxyHandle
    {
        public enum Grip { Ankle, Knee, Wrist }

        public CasualtyController casualty;
        public Limb limb;
        public Grip grip;
        [Tooltip("Seconds of lag: a 12 kg leg does not follow the hand instantly.")]
        public float lag = 0.09f;
        public float sag = 0.025f;

        public static readonly System.Collections.Generic.List<LimbHandle> AllHandles = new System.Collections.Generic.List<LimbHandle>();

        Vector3 _tipAtGrab, _target, _vel, _lastTip;
        float _weight, _fallT = -1f, _liftedSeconds;
        bool _elevationReported, _hasTarget, _owningIK;

        public bool Held => Hand != null;
        public float Weight => _weight;
        /// <summary>Height of the limb's mid joint / tip above the floor (m).</summary>
        public float Clearance { get; private set; }
        public bool IsLifted => Clearance > 0.12f;

        void OnEnable() { if (!AllHandles.Contains(this)) AllHandles.Add(this); }
        protected override void OnDisable() { AllHandles.Remove(this); base.OnDisable(); }

        CasualtyRig Rig => casualty != null ? casualty.rig : null;

        protected override void OnGrabbed(Interaction.HandGrabber hand, GrabPoint point)
        {
            base.OnGrabbed(hand, point);
            if (!LimbMath.Points(Rig, limb, out _, out _, out var tip, out _)) return;
            _tipAtGrab = _hasTarget && _weight > 0.05f ? _target : tip;
            if (!_hasTarget || _weight <= 0.05f) _target = tip;
            _lastTip = tip;
            _vel = Vector3.zero;
            _fallT = -1f;
            _hasTarget = true;
        }

        protected override void OnReleased(Interaction.HandGrabber hand)
        {
            base.OnReleased(hand);
            _fallT = 0f;
        }

        void Update()
        {
            var rig = Rig;
            if (rig == null || !rig.Initialized) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!LimbMath.Points(rig, limb, out var root, out var mid, out var tip, out _)) return;
            float floor = casualty.transform.position.y;

            if (Held)
            {
                Vector3 desired = _tipAtGrab + PalmDelta;
                desired.y -= sag;
                desired = Limit(rig, root, desired, floor);
                _target = Vector3.SmoothDamp(_target, desired, ref _vel, lag, 4f, dt);
                _weight = Mathf.MoveTowards(_weight, 1f, dt / 0.12f);
                // pain: how fast the injured limb is moved, how close to the wound
                float speed = (_target - _lastTip).magnitude / dt;
                casualty.Handle(Vector3.Lerp(mid, _target, 0.5f), speed, dt);
                casualty.Handle(Vector3.Lerp(root, mid, 0.5f), speed * 0.6f, dt); // the whole limb moves
                Haptics.Tension(Input, Mathf.Clamp01(Clearance * 2.5f) * 0.5f);
            }
            else if (_fallT >= 0f)
            {
                // released: drops under gravity onto the ground, IK fades into the animated pose
                _fallT += dt;
                _target.y = Mathf.Max(floor + LimbMath.RadiusAt(limb, 2f), _target.y - 4.9f * dt * _fallT * 2f);
                _weight = 1f - Mathf.SmoothStep(0f, 1f, _fallT / 0.45f);
                if (_weight <= 0f)
                {
                    _fallT = -1f;
                    _hasTarget = false;
                    if (IsLifted) casualty.Handle(tip, 1.2f, 0.1f); // it hit the ground: hurts
                }
            }
            _lastTip = _target;

            if (_weight > 0f && _hasTarget)
            {
                _owningIK = true;
                Vector3 hint = IsLegLimb ? mid + rig.BodyForward * 0.4f + rig.BodyUp * 0.05f : mid - rig.BodyUp * 0.15f + rig.BodyForward * 0.05f;
                rig.SetLimbIK(limb, _target, _weight, hint);
            }
            else if (_owningIK) { rig.ClearLimbIK(limb); _owningIK = false; }

            Clearance = Mathf.Max(0f, Mathf.Min(mid.y, tip.y) - floor - LimbMath.RadiusAt(limb, 1f));
            TrackElevation(dt);
        }

        bool IsLegLimb => LimbMath.IsLeg(limb);

        /// <summary>Joint limits: reach, floor, hip flexion / shoulder range.</summary>
        Vector3 Limit(CasualtyRig rig, Vector3 root, Vector3 desired, float floor)
        {
            rig.LimbChain(limb, out var r, out var m, out var t);
            float len = Vector3.Distance(r.position, m.position) + Vector3.Distance(m.position, t.position);
            Vector3 v = desired - root;
            float d = Mathf.Clamp(v.magnitude, len * 0.35f, len * 0.985f);
            v = v.sqrMagnitude > 1e-8f ? v.normalized * d : rig.BodyUp * -d;
            if (IsLegLimb)
            {
                // the foot cannot pass the hip line toward the head (max ~95° hip flexion with a bent knee)
                float up = Vector3.Dot(v, rig.BodyUp);
                if (up > 0.2f * len) v -= rig.BodyUp * (up - 0.2f * len);
                // abduction/adduction ±35°
                float side = Vector3.Dot(v, rig.BodyRight);
                float maxSide = 0.6f * len;
                if (Mathf.Abs(side) > maxSide) v -= rig.BodyRight * (side - Mathf.Sign(side) * maxSide);
                // cannot hyper-extend backward through the floor side of the body
                float back = Vector3.Dot(v, -rig.BodyForward);
                if (back > 0.25f * len) v += rig.BodyForward * (back - 0.25f * len);
            }
            Vector3 p = root + v;
            p.y = Mathf.Max(p.y, floor + LimbMath.RadiusAt(limb, 2f) * 0.8f);
            return p;
        }

        void TrackElevation(float dt)
        {
            if (!IsLegLimb || casualty == null || _elevationReported) return;
            if (Held && Clearance > 0.25f) _liftedSeconds += dt; else _liftedSeconds = Mathf.Max(0f, _liftedSeconds - dt);
            if (_liftedSeconds < 15f) return;
            // held up for long with a critical bleed on this leg and no tourniquet working: "elevation instead of a TQ"
            string id = LimbMath.Id(limb);
            var p = casualty.Patient;
            bool bleeding = false, tq = false;
            foreach (var w in p.wounds) if (w.LimbId == id && w.IsLifeThreatening && !w.IsControlled) bleeding = true;
            foreach (var b in p.tourniquets) if (b.OnLimb && b.limbId == id && b.stage >= TourniquetStage.Positioned) tq = true;
            if (bleeding && !tq) { casualty.Protocol.Perform(MedAction.LimbElevation); _elevationReported = true; }
            _liftedSeconds = 0f;
        }
    }
}
