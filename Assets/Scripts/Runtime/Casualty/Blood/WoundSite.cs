using System.Collections.Generic;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty.Blood
{
    public enum WoundVisual { FragmentHole, RaggedLaceration, ChestEntry, ExitWound, AmputationStump }

    /// <summary>
    /// Visible wound on the casualty: decal (or stump mesh), pulsating arterial jet synced to the heart rate,
    /// venous ooze / drips, feeding the ground pool and the clothing soak. Lives on a bone; +Z = outward normal.
    /// Flow comes from the medical model (<see cref="PatientPhysiology.CurrentFlowMlPerSec"/>), so a tourniquet
    /// or packing visibly stops the spurting.
    /// </summary>
    public sealed class WoundSite : MonoBehaviour
    {
        public static readonly List<WoundSite> Active = new List<WoundSite>();

        public CasualtyController owner;
        public WoundVisual visual;
        public ParticleSystem jet;
        public ParticleSystem drip;
        public Renderer decal;
        [Tooltip("Size of the wound decal (m).")]
        public float size = 0.06f;
        public Wound Wound { get; private set; }

        float _beat;
        float _dripAccum;
        public float FlowMlPerSec { get; private set; }
        public bool Pulsatile => Wound != null && Wound.type is WoundType.ArterialLimb or WoundType.TraumaticAmputation or WoundType.Junctional;
        /// <summary>Radius of blood soaking into the clothes around the wound (m).</summary>
        public float SoakRadius => Wound == null ? 0f : Mathf.Min(0.34f, 0.025f + 0.11f * Mathf.Sqrt(Wound.bloodLostMl / 500f));
        public int JetBursts { get; private set; }

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        public void Bind(CasualtyController c, Wound w, WoundVisual v)
        {
            owner = c; Wound = w; visual = v;
            if (!Active.Contains(this)) Active.Add(this);
            if (jet == null) jet = MakeParticles("ArterialJet", true);
            if (drip == null) drip = MakeParticles("Drip", false);
        }

        ParticleSystem MakeParticles(string name, bool isJet)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.startLifetime = isJet ? 0.9f : 1.2f;
            main.startSize = isJet ? 0.012f : 0.008f;
            var s = BloodSettings.Load();
            main.maxParticles = isJet ? (s != null ? s.maxJetParticles : 400) : 80;
            main.startColor = new Color(0.45f, 0.02f, 0.02f, 1f);
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.dampen = 1f; col.bounce = 0f; col.lifetimeLoss = 1f;
            col.collidesWith = ~0;
            col.enableDynamicColliders = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = isJet ? 0.035f : 0.02f;
            r.lengthScale = 1.5f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (s != null) r.sharedMaterial = s.particle;
            return ps;
        }

        /// <summary>realDt drives visuals (heartbeats, particles); simDt is physiology time (volume).</summary>
        public void Simulate(float realDt, float simDt)
        {
            if (Wound == null || owner == null) return;
            var p = owner.Patient;
            FlowMlPerSec = p.CurrentFlowMlPerSec(Wound);
            float baseFlow = Wound.BaseRateMlPerMin / 60f;
            float norm = baseFlow > 0f ? Mathf.Clamp01(FlowMlPerSec / Mathf.Max(baseFlow, 1f)) : 0f;
            Vector3 outward = transform.forward;

            if (Pulsatile && jet != null && FlowMlPerSec > 0.3f)
            {
                float hz = Mathf.Max(0.3f, p.HeartRate / 60f);
                float prev = _beat;
                _beat += realDt * hz;
                int beats = Mathf.FloorToInt(_beat) - Mathf.FloorToInt(prev);
                for (int i = 0; i < beats && i < 3; i++) Spurt(outward, norm, p.Perfusion);
                // between beats: a weaker continuous stream
                EmitStream(outward, norm * 0.35f, realDt * 60f * norm);
            }
            // ooze / drips: venous flow, residual flow under pressure, soaked cloth dripping
            float dripRate = Mathf.Clamp(FlowMlPerSec * 1.5f, 0f, 25f) + (Wound.bloodLostMl > 200f && FlowMlPerSec > 0.01f ? 1.5f : 0f);
            _dripAccum += dripRate * realDt;
            while (_dripAccum >= 1f && drip != null)
            {
                _dripAccum -= 1f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = transform.position + Random.insideUnitSphere * size * 0.4f,
                    velocity = outward * Random.Range(0.02f, 0.12f) + Random.insideUnitSphere * 0.03f,
                    startSize = Random.Range(0.005f, 0.011f),
                    startLifetime = 1.5f,
                    applyShapeToPosition = false,
                };
                drip.Emit(ep, 1);
            }
            if (!Application.isPlaying)
            {
                if (jet != null) jet.Simulate(realDt, false, false, false);
                if (drip != null) drip.Simulate(realDt, false, false, false);
            }

            float ml = FlowMlPerSec * simDt;
            if (owner.PoolManager != null) owner.PoolManager.Feed(transform.position, ml, realDt);
        }

        void Spurt(Vector3 outward, float norm, float perfusion)
        {
            JetBursts++;
            float speed = Mathf.Lerp(0.8f, 3.2f, norm) * Mathf.Lerp(0.5f, 1f, perfusion);
            int n = Mathf.RoundToInt(Mathf.Lerp(8, 34, norm));
            for (int i = 0; i < n; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = transform.position + Random.insideUnitSphere * size * 0.15f,
                    velocity = (outward + Random.insideUnitSphere * 0.18f).normalized * speed * Random.Range(0.75f, 1.1f),
                    startSize = Random.Range(0.008f, 0.02f),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    applyShapeToPosition = false,
                };
                jet.Emit(ep, 1);
            }
        }

        void EmitStream(Vector3 outward, float norm, float count)
        {
            int n = Mathf.Min(6, Mathf.FloorToInt(count + Random.value));
            for (int i = 0; i < n; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = transform.position,
                    velocity = (outward + Random.insideUnitSphere * 0.3f).normalized * Mathf.Lerp(0.2f, 1.2f, norm),
                    startSize = Random.Range(0.006f, 0.012f),
                    startLifetime = 0.7f,
                    applyShapeToPosition = false,
                };
                jet.Emit(ep, 1);
            }
        }
    }
}
