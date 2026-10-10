using FieldMedic.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Player's head torch on the helmet: red (light discipline) by default, white only on purpose.
    /// VR: left-controller Y (Button.Four) toggles on/off, hold Y 1 s = switch red/white.
    /// Desktop: L toggles, Shift+L = red/white. The current mode is read by the <see cref="ThreatSystem"/>.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class Headlamp : MonoBehaviour
    {
        public static Headlamp Instance { get; private set; }

        public Color red = new Color(1f, 0.08f, 0.04f);
        public Color white = new Color(1f, 0.95f, 0.85f);
        public float redIntensity = 1.3f;
        public float whiteIntensity = 3.2f;
        public LightMode startMode = LightMode.Red;

        public LightMode Mode { get; private set; }
        LightMode _lastOn = LightMode.Red;
        Light _light;
        float _holdTime;
        bool _holdConsumed;

        void Awake()
        {
            Instance = this;
            _light = GetComponent<Light>();
            SetMode(startMode);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetMode(LightMode m)
        {
            Mode = m;
            if (m != LightMode.Off) _lastOn = m;
            if (_light == null) _light = GetComponent<Light>();
            _light.enabled = m != LightMode.Off;
            _light.color = m == LightMode.White ? white : red;
            _light.intensity = m == LightMode.White ? whiteIntensity : redIntensity;
            _light.range = m == LightMode.White ? 9f : 4.5f;
        }

        public void Toggle() => SetMode(Mode == LightMode.Off ? _lastOn : LightMode.Off);
        public void SwapColour() => SetMode(Mode == LightMode.White ? LightMode.Red : LightMode.White);

        void Update()
        {
            if (DesktopSimulator.XRActive)
            {
                bool held = OVRInput.Get(OVRInput.Button.Four);
                if (held)
                {
                    _holdTime += Time.unscaledDeltaTime;
                    if (!_holdConsumed && _holdTime > 1f) { _holdConsumed = true; SwapColour(); }
                }
                else
                {
                    if (_holdTime > 0f && !_holdConsumed) Toggle();
                    _holdTime = 0f; _holdConsumed = false;
                }
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame)
            {
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) SwapColour(); else Toggle();
            }
        }
    }
}
