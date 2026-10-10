using FieldMedic.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.UI
{
    /// <summary>
    /// Points at <see cref="RoundButton"/>s from a distance. VR: a ray from each controller / tracked hand,
    /// trigger (pinch) = click, a thin laser is drawn only while a button is under the ray. Desktop: the mouse
    /// cursor (or the screen centre while the mouse is captured), left click or Enter = click.
    /// Buttons can always be poked directly with a fingertip as well.
    /// </summary>
    [DefaultExecutionOrder(-150)] // before DesktopSimulator captures the mouse on the same click
    public sealed class BoardPointer : MonoBehaviour
    {
        public static BoardPointer Instance { get; private set; }
        public float maxDistance = 4f;
        public RoundButton DesktopHover { get; private set; }

        readonly LineRenderer[] _lasers = new LineRenderer[2];
        readonly bool[] _wasTrigger = new bool[2];

        public static BoardPointer Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BoardPointer");
            return go.AddComponent<BoardPointer>();
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (RoundButton.All.Count == 0) { HideLasers(); return; }
            if (DesktopSimulator.XRActive) { UpdateHands(); return; }
            HideLasers();
            var cam = Camera.main;
            if (cam == null) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            Vector2 sp = Cursor.lockState == CursorLockMode.Locked || mouse == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : mouse.position.ReadValue();
            var hit = Pick(cam.ScreenPointToRay(sp), out _);
            DesktopHover = hit;
            if (hit == null) return;
            hit.Hover();
            bool click = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                         || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (click) hit.Click();
        }

        public RoundButton Pick(Ray ray, out float dist)
        {
            RoundButton best = null;
            dist = maxDistance;
            foreach (var b in RoundButton.All)
            {
                if (b == null) continue;
                float d = b.Raycast(ray);
                if (d >= 0f && d < dist) { dist = d; best = b; }
            }
            return best;
        }

        void UpdateHands()
        {
            for (int i = 0; i < 2; i++)
            {
                HandRig rig = null;
                foreach (var r in HandRig.All) if (r != null && (int)r.handedness == i) { rig = r; break; }
                var input = rig != null ? rig.Input : null;
                if (input == null || !input.IsTracked) { SetLaser(i, false, default, default); continue; }
                // controller ray: forward, tilted down a little like a pointing finger
                var ray = new Ray(input.Position, input.Rotation * Quaternion.Euler(30f, 0f, 0f) * Vector3.forward);
                var hit = Pick(ray, out float d);
                bool trig = input.Trigger > 0.7f;
                if (hit != null)
                {
                    hit.Hover();
                    if (trig && !_wasTrigger[i]) { hit.Click(); input.Haptic(0.4f, 0.04f); }
                }
                _wasTrigger[i] = trig;
                SetLaser(i, hit != null, ray.origin, ray.GetPoint(d));
            }
        }

        void SetLaser(int i, bool on, Vector3 a, Vector3 b)
        {
            if (_lasers[i] == null)
            {
                if (!on) return;
                var go = new GameObject("Laser" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.004f; lr.endWidth = 0.0015f;
                lr.sharedMaterial = WorldUI.Unlit(new Color(1f, 0.75f, 0.35f));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lasers[i] = lr;
            }
            _lasers[i].enabled = on;
            if (on) { _lasers[i].SetPosition(0, a); _lasers[i].SetPosition(1, b); }
        }

        void HideLasers() { for (int i = 0; i < 2; i++) if (_lasers[i] != null) _lasers[i].enabled = false; }
    }
}
