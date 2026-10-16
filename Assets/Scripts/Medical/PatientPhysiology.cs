using System;
using System.Collections.Generic;

namespace FieldMedic.Medical
{
    public enum Consciousness { Alert, Voice, Pain, Unresponsive }   // AVPU
    public enum PneumoState { None, Open, Sealed, Tension, Decompressed }
    public enum Outcome { Ongoing, Stabilized, Died }
    public enum PulseSite { Carotid, RadialL, RadialR, PedalL, PedalR, FemoralL, FemoralR }

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
        public bool airwayAdjunct;        // NPA inserted
        public bool recoveryPosition;     // on his side: tongue falls forward, vomit drains
        public bool vomitInMouth;         // vomitus pooling in the mouth (TBI, blast)
        public float obstructionSeconds;  // continuous seconds with a compromised airway
        public bool tbiUnconscious;       // head injury: unresponsive regardless of perfusion
        public bool eardrumsRuptured;     // blast: cannot hear commands, blood from the ears
        public bool tourniquetConverted;  // S8: band loosened after a pressure dressing held
        public bool conversionFailed;     // rebleed on conversion: never try again
        public PneumoState pneumo = PneumoState.None;
        public float tensionSeverity;     // 0..1
        public float secondsSinceSeal;
        public float secondsSinceStart;
        public readonly Dictionary<string, float> tourniquetSeconds = new(); // limbId -> time on
        /// Physical tourniquets (hands-on CAT). Their occlusion drives bleeding and distal pulses.
        public readonly List<TourniquetState> tourniquets = new();
        public Outcome outcome = Outcome.Ongoing;
        public string causeOfDeath;
        float _criticalSeconds;

        public event Action<string> OnEvent; // narrative events for UI/log ("tension_pneumo", "airway_obstructed", "death")

        public PatientPhysiology() { coreTempC = startTempC; }

        public float BloodVolumeMl => bodyMassKg * 70f;
        public float LossFraction => Math.Clamp(bloodLostMl / BloodVolumeMl, 0f, 1f);

        /// ATLS class 1..4
        public int HemorrhageClass => LossFraction < 0.15f ? 1 : LossFraction < 0.30f ? 2 : LossFraction < 0.40f ? 3 : 4;

        /// Airway open? Supine + unconscious = tongue falls back (NPA or recovery position fixes it);
        /// vomit in the mouth of a supine casualty is aspirated whatever adjunct is in the nose.
        public bool AirwayCompromised =>
            outcome != Outcome.Died && !recoveryPosition && ((airwayObstructed && !airwayAdjunct) || vomitInMouth);

        /// 0..1 how far hypoxia from the blocked airway has progressed (~4 min to full, 2.5x faster with vomit).
        public float ObstructionSeverity => Math.Clamp(obstructionSeconds / 240f, 0f, 1f);

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
                if (AirwayCompromised) return 6f;
                float rr = 15f + 35f * LossFraction + 18f * tensionSeverity;
                if (pneumo == PneumoState.Open) rr += 6f;
                return Math.Clamp(rr, 0f, 45f);
            }
        }

        /// Chest excursion 0..1 (w8 breathing check): air hunger deepens it in early shock; an open or tension
        /// pneumothorax and a blocked airway make it shallow; sealing / decompression lets it recover.
        public float BreathDepth
        {
            get
            {
                if (outcome == Outcome.Died) return 0f;
                if (AirwayCompromised) return 0.22f;
                float d = 0.45f + 0.35f * Math.Min(LossFraction, 0.3f);
                if (LossFraction > 0.35f) d -= (LossFraction - 0.35f) * 3f;   // exhausted, agonal
                if (pneumo == PneumoState.Open) d -= 0.15f;
                d -= 0.4f * tensionSeverity;
                return Math.Clamp(d, 0.08f, 1f);
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
                if (AirwayCompromised) s -= 25f + 35f * ObstructionSeverity;
                if (LossFraction > 0.35f) s -= (LossFraction - 0.35f) * 60f; // poor perfusion, unreliable reading
                return Math.Clamp(s, 40f, 100f);
            }
        }

        public Consciousness AVPU
        {
            get
            {
                if (outcome == Outcome.Died || tbiUnconscious) return Consciousness.Unresponsive;
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

        /// Perfusion pressure drives flow: as BP falls, wounds bleed slower.
        public float Perfusion => Math.Clamp(SystolicBP / 120f, 0.15f, 1.1f);

        /// Current blood flow out of a wound, ml/s (0 once dead: no cardiac output).
        public float CurrentFlowMlPerSec(Wound w)
        {
            if (outcome == Outcome.Died) return 0f;
            return w.BaseRateMlPerMin / 60f * w.ResidualFactor * OnsetFactor(w) * Perfusion * CoagulopathyMultiplier;
        }

        /// Delayed onset (vasospasm in an amputation stump, a clot that lets go): only a trickle before the onset.
        public float OnsetFactor(Wound w) =>
            w.onsetDelaySeconds > 0f && secondsSinceStart < w.onsetDelaySeconds ? w.preOnsetFactor : 1f;

        // ---------- simulation ----------
        public void Tick(float dt)
        {
            if (outcome != Outcome.Ongoing || dt <= 0f) return;
            secondsSinceStart += dt;
            RefreshTourniquets();

            foreach (var w in wounds)
            {
                if (w.onsetDelaySeconds > 0f && !w.onsetFired && secondsSinceStart >= w.onsetDelaySeconds)
                {
                    w.onsetFired = true;
                    if (!w.IsControlled) OnEvent?.Invoke("rebleed");
                }
                float lost = CurrentFlowMlPerSec(w) * dt;
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
            TickAirway(dt);
            CheckDeath(dt);
        }

        void TickChest(float dt)
        {
            switch (pneumo)
            {
                case PneumoState.Sealed:
                    secondsSinceSeal += dt;
                    tensionSeverity = Math.Max(0f, tensionSeverity - dt / 30f); // after a burp
                    // Air keeps leaking from the injured lung under the seal: tension develops
                    // (a vented seal delays it a lot but does not make it impossible).
                    if (secondsSinceSeal > (SealVented ? VentedTensionDelay : SealedTensionDelay))
                    { pneumo = PneumoState.Tension; OnEvent?.Invoke("tension_pneumo"); }
                    break;
                case PneumoState.Tension:
                    tensionSeverity = Math.Min(1f, tensionSeverity + dt / 180f);
                    break;
                case PneumoState.Decompressed:
                    tensionSeverity = Math.Max(0f, tensionSeverity - dt / 20f);
                    break;
            }
        }

        void TickAirway(float dt)
        {
            bool unconscious = AVPU == Consciousness.Unresponsive || AVPU == Consciousness.Pain;
            if (unconscious && !airwayObstructed && !airwayAdjunct)
            {
                airwayObstructed = true; // tongue falls back: snoring respirations
                OnEvent?.Invoke("airway_obstructed");
            }
            if (recoveryPosition && vomitInMouth) { vomitInMouth = false; OnEvent?.Invoke("vomit_drained"); }
            if (AirwayCompromised) obstructionSeconds += dt * (vomitInMouth ? 2.5f : 1f);
            else obstructionSeconds = Math.Max(0f, obstructionSeconds - dt * 4f);
        }

        /// He vomits (TBI/blast). Supine and unconscious = aspiration risk; on his side it drains.
        public void Vomit()
        {
            if (outcome != Outcome.Ongoing) return;
            vomitInMouth = !recoveryPosition;
            OnEvent?.Invoke("vomit");
        }

        /// Finger sweep / turning the head: vomitus cleared from the mouth.
        public void ClearMouth() { vomitInMouth = false; }

        /// Advance the model by a long time in small steps (time-skip milestones, S8).
        public void Advance(float seconds, float step = 1f)
        {
            while (seconds > 0f && outcome == Outcome.Ongoing)
            {
                float d = Math.Min(step, seconds);
                Tick(d);
                seconds -= d;
            }
        }

        void CheckDeath(float dt)
        {
            if (LossFraction >= 0.5f) { Die("Кровопотеря (геморрагический шок IV класса)"); return; }
            bool critical = SystolicBP < 45f || SpO2 < 55f;
            _criticalSeconds = critical ? _criticalSeconds + dt : Math.Max(0f, _criticalSeconds - dt);
            if (_criticalSeconds > 45f)
                Die(tensionSeverity > 0.8f ? "Напряжённый пневмоторакс" :
                    vomitInMouth && SpO2 < 55f ? "Аспирация рвотных масс (раненый без сознания лежал на спине)" :
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

        public const float SealedTensionDelay = 120f;
        public const float VentedTensionDelay = 420f;

        bool SealVented
        {
            get { foreach (var w in wounds) if (w.type == WoundType.PenetratingChest && !w.sealVented) return false; return true; }
        }

        /// Seal one chest wound. The chest is closed only when entry AND exit wounds are sealed.
        public void SealChest(Wound w, bool vented = false)
        {
            w.control = BleedControl.OcclusiveSeal;
            w.sealVented = vented;
            bool allSealed = true;
            foreach (var x in wounds)
                if (x.type == WoundType.PenetratingChest && x.control != BleedControl.OcclusiveSeal) allSealed = false;
            if (pneumo == PneumoState.Open && allSealed) { pneumo = PneumoState.Sealed; secondsSinceSeal = 0f; }
        }

        /// Lift a seal edge on exhale: trapped air escapes, tension is relieved (then reseal).
        public bool BurpSeal()
        {
            if (pneumo != PneumoState.Tension && pneumo != PneumoState.Sealed) return false;
            bool wasTension = pneumo == PneumoState.Tension;
            pneumo = PneumoState.Sealed;
            secondsSinceSeal = 0f;
            tensionSeverity *= 0.5f;
            if (wasTension) OnEvent?.Invoke("burped");
            return wasTension;
        }

        // ---------- tourniquets & pulses ----------
        public void AddTourniquet(TourniquetState tq)
        {
            if (!tourniquets.Contains(tq)) tourniquets.Add(tq);
            RefreshTourniquets();
        }

        public void RemoveTourniquet(TourniquetState tq) { tourniquets.Remove(tq); RefreshTourniquets(); }

        /// Recompute every wound's tourniquet factor and the tourniquet clock. Call after any band change.
        public void RefreshTourniquets()
        {
            foreach (var w in wounds)
            {
                float lo = float.MaxValue, hi = 1f;
                string limb = w.LimbId;
                float pos = w.LimbPos;
                foreach (var tq in tourniquets)
                {
                    if (!tq.IsProximalTo(limb, pos)) continue;
                    float f = tq.BleedFactor;
                    lo = Math.Min(lo, f);
                    hi = Math.Max(hi, f);
                }
                // any band that really compresses wins over a loose one (which only adds congestion)
                w.tourniquetFactor = lo == float.MaxValue ? 1f : lo < 1f ? lo : hi;
            }
            foreach (var tq in tourniquets)
            {
                if (tq.stage < TourniquetStage.WindlassTwisted || tq.limbId == null) continue;
                if (tq.appliedAtSeconds < 0f) tq.appliedAtSeconds = secondsSinceStart;
                if (!tourniquetSeconds.ContainsKey(tq.limbId)) tourniquetSeconds[tq.limbId] = 0f;
            }
        }

        public static string LimbOf(PulseSite s) => s switch
        {
            PulseSite.RadialL => "armL", PulseSite.RadialR => "armR",
            PulseSite.PedalL => "legL", PulseSite.PedalR => "legR",
            _ => null,
        };

        /// Palpable pulse strength 0..1 at a site (for haptics). Peripheral pulses fade with BP;
        /// distal pulses vanish below a fully occluding tourniquet and in an amputated limb.
        public float PulseStrength(PulseSite site)
        {
            if (outcome == Outcome.Died) return 0f;
            float sbp = SystolicBP;
            float s = site switch
            {
                PulseSite.Carotid => sbp >= 50f ? Math.Clamp((sbp - 40f) / 80f, 0.15f, 1f) : 0f,
                PulseSite.FemoralL or PulseSite.FemoralR => sbp >= 65f ? Math.Clamp((sbp - 55f) / 70f, 0.15f, 1f) : 0f,
                _ => sbp >= 80f ? Math.Clamp((sbp - 70f) / 55f, 0.15f, 1f) : 0f,
            };
            var limb = LimbOf(site);
            if (limb == null || s <= 0f) return s;
            foreach (var w in wounds)
                if (w.type == WoundType.TraumaticAmputation && w.LimbId == limb) return 0f;
            foreach (var tq in tourniquets)
                if (tq.OnLimb && tq.limbId == limb) s *= tq.DistalPulseFactor;
            foreach (var w in wounds) // a severed artery above: weak or absent distal pulse
                if (w.type == WoundType.ArterialLimb && w.LimbId == limb && !w.IsControlled) s *= 0.5f;
            return s;
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
