using System;
using System.Collections.Generic;
using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    public enum HapticKind { StrapPull, WindlassClick, Packing, SealPress, Heartbeat, DragRumble }

    /// <summary>
    /// One place for how every medical action feels in the controllers (w8): strap pull = continuous resistance
    /// proportional to tension; windlass = one click per half turn; packing = soft pulses; seal press = one firm
    /// pulse; pulse = the heartbeat; drag = heavy low rumble by load. Tunable as an asset
    /// (Resources/HapticsProfile); the defaults below are used when the asset is missing.
    /// Quest Touch: frequency 0..1 maps to the actuator's low..high band; tracked hands have no actuator.
    /// </summary>
    [CreateAssetMenu(menuName = "FieldMedic/Haptics Profile")]
    public sealed class HapticsProfile : ScriptableObject
    {
        [Header("Tourniquet strap: continuous, ∝ tension")]
        public Vector2 strapAmplitude = new Vector2(0.06f, 0.75f);
        [Range(0f, 1f)] public float strapFrequency = 0.45f;
        [Header("Windlass: one click per half turn")]
        public float clickAmplitude = 0.85f;
        public float clickDuration = 0.035f;
        [Header("Packing: soft pulses while the finger pushes gauze")]
        public float packAmplitude = 0.28f;
        public float packDuration = 0.05f;
        public float packRateHz = 7f;
        [Range(0f, 1f)] public float packFrequency = 0.3f;
        [Header("Chest seal edge press: one firm pulse")]
        public float sealAmplitude = 0.95f;
        public float sealDuration = 0.09f;
        [Range(0f, 1f)] public float sealFrequency = 0.7f;
        [Header("Pulse check: one beat, amplitude from blood pressure")]
        public Vector2 beatAmplitude = new Vector2(0.12f, 0.85f);
        public float beatDuration = 0.07f;
        [Range(0f, 1f)] public float beatFrequency = 0.25f;
        [Header("Drag: heavy low rumble ∝ load")]
        public Vector2 dragAmplitude = new Vector2(0.08f, 0.7f);
        [Range(0f, 1f)] public float dragFrequency = 0.12f;

        /// <summary>Every played effect (tests / debug overlay). Not invoked for null inputs.</summary>
        public static event Action<HapticKind, IHandInput, float> Played;

        static HapticsProfile _current;
        public static HapticsProfile Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Resources.Load<HapticsProfile>("HapticsProfile");
                if (_current == null) { _current = CreateInstance<HapticsProfile>(); _current.name = "HapticsProfile(default)"; }
                return _current;
            }
            set => _current = value;
        }

        // per-hand pulse timers (packing); a handful of entries for the whole session, no per-frame allocation
        readonly Dictionary<IHandInput, float> _nextPulse = new Dictionary<IHandInput, float>(4);

        static void Emit(HapticKind k, IHandInput h, float amp, float dur, float freq)
        {
            if (h == null) return;
            h.Haptic(Mathf.Clamp01(amp), dur, freq);
            Played?.Invoke(k, h, amp);
        }

        /// <summary>Call every frame while the strap is pulled (tension 0..1).</summary>
        public void StrapPull(IHandInput h, float tension01)
        {
            if (tension01 <= 0.01f) return;
            Emit(HapticKind.StrapPull, h, Mathf.Lerp(strapAmplitude.x, strapAmplitude.y, Mathf.Clamp01(tension01)), Time.deltaTime * 1.5f, strapFrequency);
        }

        public void WindlassClick(IHandInput h) => Emit(HapticKind.WindlassClick, h, clickAmplitude, clickDuration, 1f);

        /// <summary>Call every frame while a finger pushes gauze into the wound (depth 0..1); pulses at packRateHz.</summary>
        public void Packing(IHandInput h, float depth01)
        {
            if (h == null) return;
            _nextPulse.TryGetValue(h, out float next);
            if (Time.time < next) return;
            _nextPulse[h] = Time.time + 1f / Mathf.Max(0.5f, packRateHz);
            Emit(HapticKind.Packing, h, packAmplitude * Mathf.Lerp(0.6f, 1.2f, Mathf.Clamp01(depth01)), packDuration, packFrequency);
        }

        public void SealPress(IHandInput h) => Emit(HapticKind.SealPress, h, sealAmplitude, sealDuration, sealFrequency);

        /// <summary>One heartbeat under the fingers; strength 0..1 from the blood pressure at that site.</summary>
        public void Heartbeat(IHandInput h, float strength01) =>
            Emit(HapticKind.Heartbeat, h, Mathf.Lerp(beatAmplitude.x, beatAmplitude.y, Mathf.Clamp01(strength01)), beatDuration, beatFrequency);

        /// <summary>Call every frame while pulling a body (load 0..1).</summary>
        public void DragRumble(IHandInput h, float load01)
        {
            if (load01 <= 0.01f) return;
            Emit(HapticKind.DragRumble, h, Mathf.Lerp(dragAmplitude.x, dragAmplitude.y, Mathf.Clamp01(load01)), Time.deltaTime * 1.5f, dragFrequency);
        }
    }
}
