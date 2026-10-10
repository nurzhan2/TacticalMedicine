using System;
using System.Collections.Generic;

namespace FieldMedic.Medical
{
    /// Perceived pain of the casualty, 0..1. Engine-agnostic.
    /// Sources: (1) baseline from wounds, (2) handling — limb moved fast near a wound, pressure on a wound,
    /// (3) tourniquet — every windlass half-turn hurts a lot (RESEARCH §6: "турникет очень болезнен").
    /// Spikes decay quickly; the baseline is permanent until treated. Consciousness gates what is expressed.
    [Serializable]
    public class PainModel
    {
        public const float ScreamThreshold = 0.75f;
        public const float GroanThreshold = 0.4f;
        public const float SpikeHalfLife = 1.6f;          // s
        public const float HandlingRadius = 0.35f;        // m: handling further from a wound barely hurts
        public const float PainfulSpeed = 0.6f;           // m/s of the handled limb that is clearly painful

        public float baseline;        // from wounds + tourniquets, 0..1
        public float spike;           // acute, decays
        public float level;           // expressed pain, 0..1 (after consciousness gating)
        public float raw;             // baseline + spike before gating
        readonly Dictionary<string, int> _tqHalfTurns = new();

        /// Fired once when the expressed pain crosses the scream threshold upward (voice: scream/"Больно!").
        public event Action<float> OnPainSpike;

        public static float WoundPain(WoundType t) => t switch
        {
            WoundType.ArterialLimb => 0.55f,
            WoundType.TraumaticAmputation => 0.45f, // often less pain at first (shock), RESEARCH §2
            WoundType.Junctional => 0.6f,
            WoundType.Venous => 0.3f,
            WoundType.Capillary => 0.12f,
            WoundType.PenetratingChest => 0.6f,
            _ => 0.2f,
        };

        public static float ConsciousnessGate(Consciousness c) => c switch
        {
            Consciousness.Alert => 1f,
            Consciousness.Voice => 0.75f,
            Consciousness.Pain => 0.4f,   // responds only to painful stimuli
            _ => 0f,
        };

        public int TourniquetHalfTurns(string limbId) => _tqHalfTurns.TryGetValue(limbId, out var n) ? n : 0;

        /// Recomputes the baseline from the wound list and tourniquet state.
        public float ComputeBaseline(PatientPhysiology p)
        {
            float sum = 0f, max = 0f;
            foreach (var w in p.wounds)
            {
                float v = WoundPain(w.type);
                sum += v;
                max = Math.Max(max, v);
            }
            // the worst wound dominates, others add a little
            float b = max + 0.25f * (sum - max);
            // a tight tourniquet hurts a lot, and more with every windlass half-turn
            foreach (var kv in p.tourniquetSeconds) b += 0.25f + Math.Min(0.2f, 0.06f * TourniquetHalfTurns(kv.Key));
            return Math.Clamp(b, 0f, 0.9f);
        }

        /// A limb/body part is being moved at `speed` (m/s) at `distanceToWound` (m) from the nearest wound.
        public void AddHandling(float distanceToWound, float speed, float dt)
        {
            if (speed <= 0f || dt <= 0f) return;
            float proximity = Math.Clamp(1f - distanceToWound / HandlingRadius, 0f, 1f);
            proximity *= proximity;
            float rough = Math.Clamp(speed / PainfulSpeed, 0f, 2.5f);
            AddSpike(proximity * rough * rough * 3.0f * dt);
        }

        /// Hands pressing on / packing a wound: pressure 0..1.
        public void AddPressure(float pressure, float dt) => AddSpike(Math.Clamp(pressure, 0f, 1f) * 0.9f * dt);

        /// Each windlass half-turn: a sharp spike + a lasting increase of the baseline.
        public void TourniquetTwist(string limbId, int halfTurnsTotal)
        {
            int prev = TourniquetHalfTurns(limbId);
            _tqHalfTurns[limbId] = Math.Max(0, halfTurnsTotal);
            if (halfTurnsTotal > prev) AddSpike(0.35f * (halfTurnsTotal - prev));
        }

        public void AddSpike(float amount) => spike = Math.Clamp(spike + amount, 0f, 1f);

        public void Tick(PatientPhysiology p, float dt)
        {
            float before = level;
            baseline = ComputeBaseline(p);
            spike *= (float)Math.Pow(0.5, dt / SpikeHalfLife);
            raw = Math.Clamp(baseline + spike, 0f, 1f);
            var avpu = p.AVPU;
            float gate = ConsciousnessGate(avpu);
            // a P-responsive casualty reacts only to the acute stimulus, not the baseline
            level = avpu == Consciousness.Pain ? Math.Clamp(spike * 1.2f, 0f, 1f) * gate * 2f : raw * gate;
            level = Math.Clamp(level, 0f, 1f);
            if (before < ScreamThreshold && level >= ScreamThreshold) OnPainSpike?.Invoke(level);
        }
    }
}
