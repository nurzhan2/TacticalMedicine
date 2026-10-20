using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Comfort
{
    /// <summary>
    /// VR locomotion comfort on the OVRCameraRig root: snap turn on the right stick (30/45°, around the head so
    /// the player doesn't slide), optional short teleport along the trench floor (left stick forward → release),
    /// seated mode (raises the virtual eye by <see cref="ComfortSettings.SeatedOffset"/>) and height calibration
    /// (hold B / Button.Two for 2 s while standing → eye height mapped to <see cref="targetEyeHeight"/>).
    /// Room-scale walking stays the main locomotion; no smooth stick motion (comfort).
    /// </summary>
    public sealed class ComfortRig : MonoBehaviour
    {
        public Transform trackingSpace;
        public Transform head;
        public float targetEyeHeight = 1.62f;
        public float teleportStep = 0.9f;
        public LayerMask floorMask = ~0;

        float _calibOffset;
        bool _turnArmed = true, _tpArmed;
        float _calibHold;

        public float HeightOffset => _calibOffset + (ComfortSettings.Seated ? ComfortSettings.SeatedOffset : 0f);

        void LateUpdate()
        {
            if (!DesktopSimulator.XRActive || trackingSpace == null || head == null) return;

            // snap turn
            float x = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;
            if (InputLocks.RightStickCaptured) x = 0f; // w10: the radio panel steps with the right stick
            if (_turnArmed && Mathf.Abs(x) > 0.75f)
            {
                SnapTurn(Mathf.Sign(x) * ComfortSettings.SnapTurnDegrees);
                _turnArmed = false;
            }
            else if (Mathf.Abs(x) < 0.3f) _turnArmed = true;

            // teleport (optional)
            if (ComfortSettings.TeleportEnabled)
            {
                float y = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y;
                if (y > 0.75f) _tpArmed = true;
                else if (_tpArmed && y < 0.3f) { _tpArmed = false; TeleportForward(); }
            }

            // height calibration
            if (OVRInput.Get(OVRInput.Button.Two))
            {
                _calibHold += Time.unscaledDeltaTime;
                if (_calibHold > 2f) { CalibrateHeight(); _calibHold = -999f; }
            }
            else _calibHold = 0f;

            var lp = trackingSpace.localPosition;
            trackingSpace.localPosition = new Vector3(lp.x, HeightOffset, lp.z);
        }

        public void SnapTurn(float degrees)
        {
            Vector3 pivot = head != null ? head.position : transform.position;
            transform.RotateAround(pivot, Vector3.up, degrees);
        }

        public void CalibrateHeight()
        {
            float eye = head.position.y - transform.position.y - HeightOffset + _calibOffset;
            _calibOffset = Mathf.Clamp(targetEyeHeight - eye, -0.4f, 0.6f);
            Debug.Log($"[FMVR] height calibrated: eye {eye:F2} m → offset {_calibOffset:F2} m");
        }

        public bool TeleportForward()
        {
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return false;
            fwd.Normalize();
            Vector3 from = new Vector3(head.position.x, head.position.y, head.position.z);
            if (Physics.Raycast(from, fwd, teleportStep + 0.3f, floorMask, QueryTriggerInteraction.Ignore)) return false; // wall ahead
            Vector3 target = from + fwd * teleportStep;
            if (!Physics.Raycast(target, Vector3.down, out var hit, 3f, floorMask, QueryTriggerInteraction.Ignore)) return false;
            Vector3 delta = target - from;
            delta.y = hit.point.y - transform.position.y;
            transform.position += new Vector3(delta.x, Mathf.Clamp(delta.y, -0.5f, 0.5f), delta.z);
            return true;
        }
    }
}
