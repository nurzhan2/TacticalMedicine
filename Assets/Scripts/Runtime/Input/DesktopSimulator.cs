using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.Input
{
    /// <summary>
    /// Mouse + keyboard VR simulator for the editor (no headset).
    /// WASD move, mouse look (click to capture, Esc to release), RMB + mouse = move active hand in a plane
    /// in front of the camera, scroll = hand depth, LMB = grip, Shift+LMB = trigger, Space = primary button,
    /// Tab = switch hand, Q/E = rotate wrist, C = crouch toggle.
    /// Only active when no XR device is running.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DesktopSimulator : MonoBehaviour
    {
        [Tooltip("Rotated for yaw and moved for WASD (usually the OVRCameraRig root).")]
        public Transform rigRoot;
        [Tooltip("Raised to eye height and pitched (usually OVRCameraRig/TrackingSpace).")]
        public Transform pitchPivot;
        [Tooltip("Camera transform hands are relative to (CenterEyeAnchor).")]
        public Transform head;

        public float standingEyeHeight = 1.65f;
        public float crouchEyeHeight = 1.0f;
        public float moveSpeed = 1.4f;
        public float lookSensitivity = 0.12f;
        public float handSensitivity = 0.0012f;
        public float wristSpeed = 180f;

        public DesktopHandInput Left { get; private set; }
        public DesktopHandInput Right { get; private set; }
        public DesktopHandInput Active => _activeRight ? Right : Left;
        public bool Crouched { get; private set; }

        bool _activeRight = true;
        float _yaw, _pitch, _eyeHeight;

        public static bool XRActive => UnityEngine.XR.XRSettings.isDeviceActive;

        void Awake() => EnsureInit();

        /// <summary>Creates the hand inputs; safe to call from other components' Awake.</summary>
        public void EnsureInit()
        {
            if (Left != null) return;
            if (rigRoot == null) rigRoot = transform;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            Left = new DesktopHandInput(Handedness.Left, head);
            Right = new DesktopHandInput(Handedness.Right, head);
            _yaw = rigRoot.eulerAngles.y;
            _eyeHeight = standingEyeHeight;
        }

        public DesktopHandInput Get(Handedness h) { EnsureInit(); return h == Handedness.Left ? Left : Right; }

        void Update()
        {
            if (XRActive) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.deltaTime;

            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) _activeRight = !_activeRight;
                if (kb.cKey.wasPressedThisFrame) Crouched = !Crouched;
                if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;

                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move.z += 1f;
                if (kb.sKey.isPressed) move.z -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
                float speed = Crouched ? moveSpeed * 0.5f : moveSpeed;
                rigRoot.position += Quaternion.Euler(0f, _yaw, 0f) * Vector3.ClampMagnitude(move, 1f) * speed * dt;

                float roll = 0f;
                if (kb.qKey.isPressed) roll += 1f;
                if (kb.eKey.isPressed) roll -= 1f;
                Active.WristRoll += roll * wristSpeed * dt;
            }

            bool rmb = false;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                rmb = mouse.rightButton.isPressed;
                if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked && !rmb)
                    Cursor.lockState = CursorLockMode.Locked;

                if (rmb)
                {
                    var h = Active;
                    h.LocalOffset += new Vector3(delta.x, delta.y, 0f) * handSensitivity;
                }
                else if (Cursor.lockState == CursorLockMode.Locked)
                {
                    _yaw += delta.x * lookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -80f, 80f);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    Active.LocalOffset.z += Mathf.Sign(scroll) * 0.03f;

                var a = Active;
                a.LocalOffset = new Vector3(Mathf.Clamp(a.LocalOffset.x, -0.6f, 0.6f),
                                            Mathf.Clamp(a.LocalOffset.y, -0.9f, 0.4f),
                                            Mathf.Clamp(a.LocalOffset.z, 0.12f, 0.85f));

                bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                bool lmb = mouse.leftButton.isPressed;
                a.Grip = lmb && !shift ? 1f : 0f;
                a.Trigger = lmb && shift ? 1f : 0f;
                a.PrimaryButton = kb != null && kb.spaceKey.isPressed;
                var idle = _activeRight ? Left : Right;
                idle.Grip = idle.Trigger = 0f;
                idle.PrimaryButton = false;
            }

            _eyeHeight = Mathf.MoveTowards(_eyeHeight, Crouched ? crouchEyeHeight : standingEyeHeight, 2.5f * dt);
            rigRoot.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (pitchPivot != null)
            {
                pitchPivot.localPosition = new Vector3(0f, _eyeHeight, 0f);
                pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        void OnGUI()
        {
            if (XRActive) return;
            var a = Active;
            string hand = a.Handedness == Handedness.Left ? "ЛЕВАЯ" : "ПРАВАЯ";
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(10, 10, 900, 22),
                $"Рука: {hand}  |  ПКМ+мышь — рука, колесо — глубина, ЛКМ — хват, Shift+ЛКМ — триггер, Tab — рука, Q/E — кисть, C — присесть, WASD — шаг");
            if (Time.unscaledTime < a.HapticUntil)
            {
                GUI.color = new Color(1f, 0.3f, 0.2f, Mathf.Clamp01(a.LastHapticAmplitude));
                GUI.Label(new Rect(10, 32, 200, 22), "● вибро");
            }
        }
    }
}
