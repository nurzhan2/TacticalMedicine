using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Two-hand log roll: one hand on the shoulder, the other on the hip of the same side; lifting that side
    /// up and over rolls him onto the opposite side (toward you when you reach across). Released past ~55° he
    /// stays propped on his side (recovery position: top leg bent forward); less than that he rolls back.
    /// While on the side his back can be swept/treated (exit wounds, hidden wounds under the plate carrier).
    /// The roll is a procedural rotation of the model about the body's long axis on top of the lying clip.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class LogRoll : MonoBehaviour
    {
        public CasualtyController casualty;
        public ProxyHandle shoulderL, hipL, shoulderR, hipR;
        [Tooltip("Hand travel (up + across) for a quarter turn.")]
        public float travelForQuarterTurn = 0.32f;
        public float settleAngle = 82f;
        public float snapThreshold = 55f;

        /// <summary>Current roll, degrees. Sign: + = left side lifted (he lies on his right side).</summary>
        public float Angle { get; private set; }
        public bool OnSide => Mathf.Abs(Angle) > 45f;
        public bool Rolling { get; private set; }

        float _target, _startAngle;
        int _side; // +1 left lifted, -1 right lifted
        bool _reported;
        Transform _model;
        Vector3 _modelBaseLocalPos;
        Quaternion _modelBaseLocalRot;
        bool _baseCaptured;

        void Start()
        {
            if (casualty != null && casualty.animator != null && casualty.animator.transform != casualty.transform)
                _model = casualty.animator.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool left = shoulderL != null && hipL != null && shoulderL.Hand != null && hipL.Hand != null;
            bool right = shoulderR != null && hipR != null && shoulderR.Hand != null && hipR.Hand != null;
            var rig = casualty != null ? casualty.rig : null;

            if ((left || right) && rig != null && casualty.IsDown)
            {
                int side = left ? 1 : -1;
                var a = left ? shoulderL : shoulderR;
                var b = left ? hipL : hipR;
                if (!Rolling) { Rolling = true; _startAngle = Angle; _side = side; a.Rebase(); b.Rebase(); }
                Vector3 delta = (a.PalmDelta + b.PalmDelta) * 0.5f;
                // across = from the held side toward the other side, horizontal
                Vector3 across = Vector3.ProjectOnPlane(rig.BodyRight * (left ? 1f : -1f), Vector3.up).normalized;
                float travel = Vector3.Dot(delta, Vector3.up) + Vector3.Dot(delta, across);
                _target = Mathf.Clamp(_startAngle + side * travel / travelForQuarterTurn * 90f, -100f, 100f);
                Angle = Mathf.MoveTowards(Angle, _target, 160f * dt); // ~heavy body: lags the hands
                casualty.Handle(rig.Bone(HumanBodyBones.Hips).position, Mathf.Abs(Angle - _target) * 0.01f, dt);
                Haptics.Tension(a.Input, Mathf.Abs(Angle) / 90f * 0.6f);
                Haptics.Tension(b.Input, Mathf.Abs(Angle) / 90f * 0.6f);
            }
            else
            {
                if (Rolling)
                {
                    Rolling = false;
                    _target = Mathf.Abs(Angle) >= snapThreshold ? Mathf.Sign(Angle) * settleAngle : 0f;
                }
                // settles under his own weight
                Angle = Mathf.MoveTowards(Angle, _target, (Mathf.Abs(_target) > 1f ? 90f : 140f) * dt);
            }

            if (casualty != null)
            {
                bool side = Mathf.Abs(_target) >= snapThreshold && !Rolling && Mathf.Abs(Angle) > snapThreshold;
                if (side != casualty.rolledOnSide)
                {
                    casualty.rolledOnSide = side;
                    if (side)
                    {
                        // unconscious & breathing: this is the recovery position (airway drains)
                        var p = casualty.Patient;
                        if (p.AVPU >= Consciousness.Pain || p.airwayObstructed) casualty.Protocol.Perform(MedAction.RecoveryPosition);
                    }
                }
                if (!_reported && Mathf.Abs(Angle) > 60f) { _reported = true; casualty.Protocol.Perform(MedAction.LogRoll); }
            }
        }

        /// <summary>Applied after the Animator, before CasualtyRig (ground clamp, breathing, IK).</summary>
        void LateUpdate()
        {
            if (_model == null || casualty == null || casualty.rig == null) return;
            if (!_baseCaptured) { _modelBaseLocalPos = _model.localPosition; _modelBaseLocalRot = _model.localRotation; _baseCaptured = true; }
            // restore, keeping the ground-clamp height the rig manages
            float y = _model.localPosition.y;
            _model.localRotation = _modelBaseLocalRot;
            _model.localPosition = new Vector3(_modelBaseLocalPos.x, y, _modelBaseLocalPos.z);
            if (Mathf.Abs(Angle) < 0.05f) return;

            var rig = casualty.rig;
            var hips = rig.Bone(HumanBodyBones.Hips);
            if (hips == null) return;
            Vector3 axis = Vector3.ProjectOnPlane(rig.BodyUp, Vector3.up);
            if (axis.sqrMagnitude < 0.01f) return;
            axis.Normalize();
            // pivot on the ground under the side he rolls onto
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized; // horizontal, perpendicular to the long axis
            float s = Mathf.Sign(Angle);
            Vector3 pivot = hips.position - right * s * 0.17f;
            pivot.y = casualty.transform.position.y;
            var rot = Quaternion.AngleAxis(Angle, axis);
            _model.SetPositionAndRotation(pivot + rot * (_model.position - pivot), rot * _model.rotation);
        }
    }
}
