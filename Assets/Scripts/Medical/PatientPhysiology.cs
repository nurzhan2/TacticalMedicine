using System;
using System.Collections.Generic;

namespace FieldMedic.Medical
{
    public enum Consciousness { Alert, Voice, Pain, Unresponsive }   // AVPU
    public enum PneumoState { None, Open, Sealed, Tension, Decompressed }
    public enum Outcome { Ongoing, Stabilized, Died }

    /// Engine-agnostic casualty model. Call Tick() every frame with scaled seconds.
    /// Numbers follow ATLS hemorrhage classes and TCCC teaching; tuned for gameplay, NOT for clinical use.
    [Serializable]
    public class PatientPhysiology
    {
        // --- configuration ---
        public float bodyMassKg = 80f;
        public float startTempC = 36.8f;

        // --- state ---
        public readonly List<Wound> wounds = new();
        public float bloodLostMl;
        public float coreTempC;
        public bool insulated;            // thermal blanket applied
        public bool airwayObstructed;
        public bool airwayAdjunct;        // NPA inserted / recovery position
        public PneumoState pneumo = PneumoState.None;
        public float tensionSeverity;     // 0..1
        public float secondsSinceSeal;
        public float secondsSinceStart;
        public readonly Dictionary<string, float> tourniquetSeconds = new(); // limbId -> time on
        public Outcome outcome = Outcome.Ongoing;
        public string causeOfDeath;
        float _criticalSeconds;

        public event Action<string> OnEvent; // narrative events for UI/log ("tension_pneumo", "airway_obstructed", "death")

        public PatientPhysiology() { coreTempC = startTempC; }

        public float BloodVolumeMl => bodyMassKg * 70f;
        public float LossFraction => Math.Clamp(bloodLostMl / BloodVolumeMl, 0f, 1f);

        /// ATLS class 1..4
        public int HemorrhageClass => LossFraction < 0.15f ? 1 : LossFraction < 0.30f ? 2 : LossFraction < 0.40f ? 3 : 4;

        // ---------- vitals (derived) ----------
        public float HeartRate
        {
            get
            {
                if (outcome == Outcome.Died) return 0;
                float f = LossFraction;
                float hr = 78f + 160f * f;               // ~100 at 15%, ~126 at 30%, ~142 at 40%
                hr += 30f * tensionSeverity;
                if (f > 0.45f) hr -= (f - 0.45f) * 400f; // pre-arrest bradycardia
                return Math.Clamp(hr, 30f, 175f);
            }
        }

        public float SystolicBP
        {
            get
            {
                if (outcome == Outcome.Died) return 0;
                float f = LossFraction;
                float sbp = 122f;
                if (f > 0.15f) sbp -= (f - 0.15f) * 140f;  // compensation fails progressively
                if (f > 0.30f) sbp -= (f - 0.30f) * 260f;
                sbp -= 45f * tensionSeverity;               // obstructive shock
                return Math.Clamp(sbp, 0f, 140f);
            }
        }

        public float DiastolicBP => SystolicBP * 0.65f;

        public float RespRate
        {
            get
            {
                if (outcome == Outcome.Died) return 0;
                if (airwayObstructed && !airwayAdjunct) return 6f;
                float rr = 15f + 35f * LossFraction + 18f * tensionSeverity;
                if (pneumo == PneumoState.Open) rr += 6f;
                return Math.Clamp(rr, 0f, 45f);
            }
        }

        public float SpO2
        {
            get
            {
                if (outcome == Outcome.Died) return 0;
                float s = 98f;
                if (pneumo == PneumoState.Open) s -= 5f;
                s -= 40f * tensionSeverity;
                if (airwayObstructed && !airwayAdjunct) s -= 25f;
                if (LossFraction > 0.35f) s -= (LossFraction - 0.35f) * 60f; // poor perfusion, unreliable reading
                return Math.Clamp(s, 40f, 100f);
            }
        }

        public Consciousness AVPU
        {
            get
            {
                if (outcome == Outcome.Died) return Consciousness.Unresponsive;
                float hypoxia = Math.Max(0f, 90f - SpO2);
                float score = LossFraction * 100f + hypoxia * 1.2f;
                return score < 18f ? Consciousness.Alert
                     : score < 30f ? Consciousness.Voice
                     : score < 38f ? Consciousness.Pain
                     : Consciousness.Unresponsive;
            }
        }

        /// Radial pulse disappears roughly below SBP 80 (teaching rule of thumb).
        public bool RadialPulsePresent => SystolicBP >= 80f;
        public bool CarotidPulsePresent => SystolicBP >= 50f && outcome != Outcome.Died;

        /// Hypothermia and acidosis worsen clotting: the lethal triad.
        public float CoagulopathyMultiplier
        {
            get
            {
                float m = 1f;
                if (coreTempC < 36f) m += (36f - coreTempC) * 0.35f;
                m += LossFraction * 0.6f;
                return m;
            }
        }

        // ---------- simulation ----------
        public void Tick(float dt)
        {
            if (outcome != Outcome.Ongoing || dt <= 0f) return;
            secondsSinceStart += dt;

            // Perfusion pressure drives flow: as BP falls, wounds bleed slower.
            float perfusion = Math.Clamp(SystolicBP / 120f, 0.15f, 1.1f);

            foreach (var w in wounds)
            {
                float rate = w.BaseRateMlPerMin / 60f * w.ResidualFactor * perfusion * CoagulopathyMultiplier;
                float lost = rate * dt;
                w.bloodLostMl += lost;
                bloodLostMl += lost;
                w.handsPressing = false; // must be re-asserted by interaction every frame
            }

            foreach (var key in new List<string>(tourniquetSeconds.Keys)) tourniquetSeconds[key] += dt;

            // Heat loss: casualties lose heat fast, faster when bleeding and on cold ground.
            if (!insulated) coreTempC -= dt / 60f * (0.03f + 0.10f * LossFraction);
            else coreTempC += dt / 60f * 0.01f;
            coreTempC = Math.Clamp(coreTempC, 30f, 37.2f);

            TickChest(dt);
            TickAirway();
            CheckDeath(dt);
        }

        void TickChest(float dt)
        {
            switch (pneumo)
            {
                case PneumoState.Sealed:
                    secondsSinceSeal += dt;
                    // Air keeps leaking from the injured lung under a non-vented seal: tension develops.
                    if (secondsSinceSeal > 120f) { pneumo = PneumoState.Tension; OnEvent?.Invoke("tension_pneumo"); }
                    break;
                case PneumoState.Tension:
                    tensionSeverity = Math.Min(1f, tensionSeverity + dt / 180f);
                    break;
                case PneumoState.Decompressed:
                    tensionSeverity = Math.Max(0f, tensionSeverity - dt / 20f);
                    break;
            }
        }

        void TickAirway()
        {
            bool unconscious = AVPU == Consciousness.Unresponsive || AVPU == Consciousness.Pain;
            if (unconscious && !airwayObstructed && !airwayAdjunct)
            {
                airwayObstructed = true; // tongue falls back: snoring respirations
                OnEvent?.Invoke("airway_obstructed");
            }
        }

        void CheckDeath(float dt)
        {
            if (LossFraction >= 0.5f) { Die("Кровопотеря (геморрагический шок IV класса)"); return; }
            bool critical = SystolicBP < 45f || SpO2 < 55f;
            _criticalSeconds = critical ? _criticalSeconds + dt : Math.Max(0f, _criticalSeconds - dt);
            if (_criticalSeconds > 45f)
                Die(tensionSeverity > 0.8f ? "Напряжённый пневмоторакс" :
                    SpO2 < 55f && LossFraction < 0.3f ? "Гипоксия (нарушение проходимости дыхательных путей)" :
                    "Геморрагический шок");
        }

        void Die(string cause)
        {
            outcome = Outcome.Died;
            causeOfDeath = cause;
            OnEvent?.Invoke("death");
        }

        // ---------- interventions (validation of correctness lives in CareProtocol) ----------
        public void ApplyTourniquet(Wound w)
        {
            w.control = BleedControl.Tourniquet;
            var limb = BodyRegions.LimbId(w.region);
            if (limb != null && !tourniquetSeconds.ContainsKey(limb)) tourniquetSeconds[limb] = 0f;
        }

        public void SealChest(Wound w)
        {
            w.control = BleedControl.OcclusiveSeal;
            if (pneumo == PneumoState.Open) { pneumo = PneumoState.Sealed; secondsSinceSeal = 0f; }
        }

        public bool NeedleDecompress()
        {
            if (pneumo != PneumoState.Tension) return false;
            pneumo = PneumoState.Decompressed;
            OnEvent?.Invoke("decompressed");
            return true;
        }

        public void InsertAirwayAdjunct() { airwayAdjunct = true; airwayObstructed = false; }
        public void ApplyBlanket() { insulated = true; }

        public bool AllBleedingControlled
        {
            get { foreach (var w in wounds) if (!w.IsControlled) return false; return true; }
        }

        /// Limb ischemia risk from tourniquet time.
        public static string IschemiaRisk(float seconds) =>
            seconds < 2 * 3600 ? "низкий" : seconds < 6 * 3600 ? "растёт: оцените конверсию жгута" : "высокий риск потери конечности";
    }
}
