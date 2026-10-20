using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// What a bare (or gloved) hand does on the casualty without a tool:
    /// <list type="bullet">
    /// <item>Blood sweep — open hand moving slowly over the body finds hidden wounds (glove gets bloody, haptic).</item>
    /// <item>Pulse check — fingertips resting on the carotid / radial / pedal site: haptic beats at his heart
    /// rate, strength ∝ perfusion; nothing below a correct tourniquet.</item>
    /// <item>Direct pressure — palm or fist pushed into a wound.</item>
    /// <item>Nitrile gloves — purple once pulled on.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(HandGrabber))]
    [DefaultExecutionOrder(40)]
    public sealed class HandTreatment : MonoBehaviour
    {
        public static readonly List<HandTreatment> All = new List<HandTreatment>();

        public float sweepContact = 0.05f;
        public float discoverRadius = 0.11f;
        public float pulseRadius = 0.035f;
        public Renderer[] gloveRenderers;

        public HandGrabber Grabber { get; private set; }
        public bool Gloved { get; private set; }
        public readonly HashSet<BodyRegion> Swept = new HashSet<BodyRegion>();
        public PulseSite? PalpatingSite { get; private set; }
        public float LastPulseStrength { get; private set; }
        /// <summary>Beats felt during the current / last palpation (w10d: restarts at each new site contact, so the big
        /// forgiving zones brushed on the way do not count toward the site actually checked).</summary>
        public int PulseBeats { get; private set; }

        BloodOnHands _blood;
        float _pulseContact, _beatTimer;
        PulseSite _pulseSite;
        readonly Dictionary<PulseSite, float> _lastLogged = new Dictionary<PulseSite, float>();

        void Awake()
        {
            Grabber = GetComponent<HandGrabber>();
            _blood = GetComponent<BloodOnHands>();
            if (gloveRenderers == null || gloveRenderers.Length == 0)
                gloveRenderers = _blood != null && _blood.gloves != null ? _blood.gloves : GetComponentsInChildren<Renderer>();
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Update()
        {
            var input = Grabber.Input;
            if (input == null) return;
            var c = CasualtyController.Nearest(Grabber.PalmPosition, 1.6f);
            if (c == null || c.rig == null) { PalpatingSite = null; return; }
            float dt = Time.deltaTime;
            bool emptyHand = Grabber.Held == null;
            // w10d: outside the exam a grip that closed on his wrist / ankle handle still feels the pulse there
            bool pulseHand = emptyHand || (Forgiving.On && !(Grabber.Held is MedItem));
            if (!pulseHand) { PalpatingSite = null; _pulseContact = 0f; return; }
            if (emptyHand)
            {
                Pressure(c, input);
                if (input.Grip < 0.5f && input.Trigger < 0.6f) Sweep(c, input, dt);
            }
            Pulse(c, input, dt);
        }

        // ------------------------------------------------------------------ pressure
        void Pressure(CasualtyController c, FieldMedic.Input.IHandInput input)
        {
            Vector3 palm = Grabber.PalmPosition;
            foreach (var s in c.WoundSites)
            {
                if (s == null) continue;
                var care = s.GetComponent<WoundCare>();
                if (care == null) continue;
                float lateral = care.Lateral(palm);
                // w10d: outside the exam the palm anywhere within ~12 cm of the wound and up to ~6 cm above the skin presses
                if (lateral > Mathf.Max(Forgiving.Scale(0.06f, WoundCare.ForgivingPressureRadius), s.size)) continue;
                float h = care.Height(palm);
                float above = Forgiving.Scale(0.03f, 0.06f);
                if (h > above || h < -0.07f) continue; // deeper than flesh gives = the hand is on the other side of the body
                if (s.Wound != null && s.Wound.posterior && !c.rolledOnSide) continue; // under his back
                // pushed 0..4 cm into the wound (flesh/clothing give); a fist (grip) counts the same as a palm
                float pressure = Forgiving.On ? Mathf.Clamp01((above - h) / 0.05f + 0.35f) : Mathf.Clamp01((0.03f - h) / 0.05f);
                if (Forgiving.On) care.ShowTargetGlow(0.6f + 0.4f * pressure);
                care.AddPressure(pressure);
                Haptics.Tension(input, pressure * 0.7f);
            }
        }

        // ------------------------------------------------------------------ blood sweep
        void Sweep(CasualtyController c, FieldMedic.Input.IHandInput input, float dt)
        {
            float speed = input.Velocity.magnitude;
            if (speed < 0.03f || speed > 0.9f) return; // slow, deliberate strokes only
            Vector3 palm = Grabber.PalmPosition;
            CasualtyBodyPart touched = null;
            float best = sweepContact;
            foreach (var part in c.rig.Parts)
            {
                if (part == null) continue;
                var col = part.GetComponent<Collider>();
                if (col == null) continue;
                Vector3 q = col.ClosestPoint(palm);
                float d = Vector3.Distance(q, palm);
                if (d < best) { best = d; touched = part; }
            }
            if (touched == null) return;
            Swept.Add(touched.region);
            foreach (var s in c.WoundSites)
            {
                if (s == null || s.Wound == null || s.Wound.discovered) continue;
                if (s.Wound.posterior && !c.rolledOnSide) continue; // under his back: roll him first
                if (Vector3.Distance(palm, s.transform.position) > discoverRadius) continue;
                c.Protocol.Discover(s.Wound);
                Haptics.Play(input, Haptics.PressClick);
                if (_blood != null) { _blood.bloodiness = Mathf.Clamp01(_blood.bloodiness + 0.45f); _blood.wetness = 1f; }
                if (c.voice != null) c.voice.OnPainSpike(c, false);
                c.Pain.AddSpike(0.25f);
            }
        }

        // ------------------------------------------------------------------ pulse
        public static Vector3 PulsePoint(CasualtyRig rig, PulseSite site)
        {
            Vector3 fwd = rig.BodyForward, up = rig.BodyUp, right = rig.BodyRight;
            switch (site)
            {
                case PulseSite.Carotid:
                {
                    var neck = rig.Bone(HumanBodyBones.Neck);
                    var head = rig.Bone(HumanBodyBones.Head);
                    Vector3 n = neck != null && head != null ? Vector3.Lerp(neck.position, head.position, 0.35f) : rig.transform.position;
                    return n + fwd * 0.045f + right * 0.03f;
                }
                case PulseSite.RadialL:
                case PulseSite.RadialR:
                {
                    var hand = rig.Bone(site == PulseSite.RadialL ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    var fore = rig.Bone(site == PulseSite.RadialL ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                    if (hand == null || fore == null) return rig.transform.position;
                    // palmar side of the wrist, thumb side, 2–3 cm up the forearm
                    Vector3 axis = (hand.position - fore.position).normalized;
                    return hand.position - axis * 0.03f + Vector3.ProjectOnPlane(up, axis).normalized * 0.018f;
                }
                case PulseSite.PedalL:
                case PulseSite.PedalR:
                {
                    var foot = rig.Bone(site == PulseSite.PedalL ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    var toes = rig.Bone(site == PulseSite.PedalL ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                    if (foot == null) return rig.transform.position;
                    Vector3 t = toes != null ? toes.position : foot.position + fwd * 0.12f;
                    // dorsalis pedis: top of the foot (boot laces), midway to the toes
                    Vector3 shin = rig.Bone(site == PulseSite.PedalL ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
                    Vector3 dorsum = (foot.position - shin).normalized * -1f;
                    return Vector3.Lerp(foot.position, t, 0.45f) + Vector3.ProjectOnPlane(dorsum, (t - foot.position).normalized).normalized * 0.045f;
                }
                case PulseSite.FemoralL:
                case PulseSite.FemoralR:
                {
                    var hip = rig.Bone(site == PulseSite.FemoralL ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                    return hip != null ? hip.position + fwd * 0.09f - up * 0.04f : rig.transform.position;
                }
            }
            return rig.transform.position;
        }

        static readonly PulseSite[] Sites =
            { PulseSite.Carotid, PulseSite.RadialL, PulseSite.RadialR, PulseSite.PedalL, PulseSite.PedalR, PulseSite.FemoralL, PulseSite.FemoralR };

        /// <summary>w10d: contact radius of the carotid / radial / pedal sites outside the exam (any part of the hand).</summary>
        public const float ForgivingPulseRadius = 0.11f;
        /// <summary>w10d: seconds of contact that complete a pulse check outside the exam (exam: 4 s).</summary>
        public const float ForgivingPulseSeconds = 2f;
        /// <summary>Contact time on the current site (tests / UI).</summary>
        public float PulseContact => _pulseContact;

        static bool IsFemoral(PulseSite s) => s == PulseSite.FemoralL || s == PulseSite.FemoralR;

        void Pulse(CasualtyController c, FieldMedic.Input.IHandInput input, float dt)
        {
            bool easy = Forgiving.On;
            Vector3 f = Grabber.FingertipPosition;
            Vector3 palm = Grabber.PalmPosition;
            PulseSite? found = null;
            float best = float.MaxValue;
            foreach (var s in Sites)
            {
                Vector3 site = PulsePoint(c.rig, s);
                // exam / femoral (next to the groin wound): fingertips on the exact site. Elsewhere: any part of the hand nearby
                bool big = easy && !IsFemoral(s);
                float d = big ? Mathf.Min(Vector3.Distance(f, site), Vector3.Distance(palm, site), Vector3.Distance((f + palm) * 0.5f, site))
                              : Vector3.Distance(f, site);
                float r = big ? Mathf.Max(pulseRadius, ForgivingPulseRadius) : pulseRadius;
                if (big && d < 0.3f && d >= r && !AnyPalpating(s))
                    ZoneGlow.Show(GlowKey(c, s), site, 0.08f, ZoneGlow.Gold, 0.4f); // approaching: show where to touch
                if (d < r && d - r < best) { best = d - r; found = s; }
            }
            if (found == null) { PalpatingSite = null; _pulseContact = 0f; return; }
            if (PalpatingSite != found) { _pulseContact = 0f; _beatTimer = 0f; PulseBeats = 0; }
            PalpatingSite = found;
            _pulseSite = found.Value;
            _pulseContact += dt;
            float strength = c.Patient.PulseStrength(_pulseSite);
            LastPulseStrength = strength;
            float need = easy ? ForgivingPulseSeconds : 4f;
            if (easy && !IsFemoral(_pulseSite))
            {
                float k = Mathf.Clamp01(_pulseContact / need);
                ZoneGlow.Show(GlowKey(c, _pulseSite), PulsePoint(c.rig, _pulseSite), 0.1f + 0.04f * k, ZoneGlow.Green, 0.6f + 0.4f * k);
            }
            if (!easy && _pulseContact < 0.3f) return; // fingers need a moment to settle (exam); otherwise the beat starts at once
            float hr = c.Patient.HeartRate;
            if (strength > 0f && hr > 1f)
            {
                _beatTimer -= dt;
                if (_beatTimer <= 0f)
                {
                    _beatTimer += 60f / hr;
                    if (_beatTimer < 0f) _beatTimer = 60f / hr;
                    Haptics.Play(input, Haptics.Heartbeat, 0.3f + 0.7f * strength);
                    PulseBeats++;
                }
            }
            if (_pulseContact > need && (!_lastLogged.TryGetValue(_pulseSite, out float t) || Time.time - t > 30f))
            {
                _lastLogged[_pulseSite] = Time.time;
                c.Protocol.Perform(MedAction.PulseCheck, site: SiteRegion(_pulseSite));
                if (easy) Haptics.Play(input, Haptics.PressClick); // done
            }
        }

        static bool AnyPalpating(PulseSite s)
        {
            foreach (var h in All) if (h != null && h.PalpatingSite.HasValue && h.PalpatingSite.Value == s) return true;
            return false;
        }

        static readonly Dictionary<(int, PulseSite), string> _glowKeys = new Dictionary<(int, PulseSite), string>();

        static string GlowKey(CasualtyController c, PulseSite s)
        {
            var k = (c.GetInstanceID(), s);
            if (!_glowKeys.TryGetValue(k, out var key)) _glowKeys[k] = key = "pulse_" + k.Item1 + "_" + s;
            return key;
        }

        static BodyRegion SiteRegion(PulseSite s) => s switch
        {
            PulseSite.RadialL => BodyRegion.ForearmL, PulseSite.RadialR => BodyRegion.ForearmR,
            PulseSite.PedalL => BodyRegion.FootL, PulseSite.PedalR => BodyRegion.FootR,
            PulseSite.FemoralL => BodyRegion.GroinL, PulseSite.FemoralR => BodyRegion.GroinR,
            _ => BodyRegion.Neck,
        };

        // ------------------------------------------------------------------ gloves
        public void PutOnGloves()
        {
            if (Gloved) return;
            Gloved = true;
            if (_blood != null)
            {
                _blood.cleanColor = Kit.Nitrile;
                _blood.bloodiness = 0f;
                _blood.Apply();
            }
            else
            {
                var mpb = new MaterialPropertyBlock();
                foreach (var r in gloveRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor("_BaseColor", Kit.Nitrile);
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        public static HandTreatment Of(HandGrabber g) => g != null ? g.GetComponent<HandTreatment>() : null;
    }
}
