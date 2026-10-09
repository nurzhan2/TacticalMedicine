using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>Named haptic presets so feel stays consistent across all interactions.</summary>
    public static class Haptics
    {
        public struct Preset
        {
            public float Amplitude;
            public float Duration;
            public Preset(float amplitude, float duration) { Amplitude = amplitude; Duration = duration; }
        }

        public static readonly Preset Hover = new Preset(0.08f, 0.02f);
        public static readonly Preset Grab = new Preset(0.45f, 0.06f);
        public static readonly Preset Release = new Preset(0.15f, 0.03f);
        public static readonly Preset PressStart = new Preset(0.3f, 0.04f);
        public static readonly Preset PressClick = new Preset(0.7f, 0.05f);
        public static readonly Preset Contact = new Preset(0.12f, 0.03f);
        /// <summary>Single pulse beat (pulse check). Scale amplitude by pulse strength.</summary>
        public static readonly Preset Heartbeat = new Preset(0.5f, 0.07f);

        public static void Play(IHandInput hand, Preset p, float scale = 1f)
        {
            if (hand == null) return;
            hand.Haptic(Mathf.Clamp01(p.Amplitude * scale), p.Duration);
        }

        /// <summary>Continuous effort feedback (e.g. strap tension 0..1): call every frame.</summary>
        public static void Tension(IHandInput hand, float tension01)
        {
            if (hand == null || tension01 <= 0.01f) return;
            hand.Haptic(Mathf.Lerp(0.05f, 0.6f, Mathf.Clamp01(tension01)), Time.deltaTime * 1.5f);
        }
    }
}
