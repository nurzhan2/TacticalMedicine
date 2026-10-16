using System;
using FieldMedic.Audio;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// The hands-on primary survey on Erlan (w8). Nothing is a button:
    /// <list type="bullet">
    /// <item>Pulse — index + middle fingertips held on the carotid / radial point ≥ 2 s (<see cref="HandTreatment"/>
    /// feels the beats as haptics at his heart rate, amplitude from blood pressure, and reports here).</item>
    /// <item>Breathing — the medic's head (HMD) within 25 cm of his mouth ≥ 3 s: the procedural breath
    /// (<see cref="BreathSynth"/>) is heard clearly, the chest rises at the physiology's rate and depth.</item>
    /// <item>AVPU — talking loud near his head (microphone level, <see cref="PlayerVoice"/>) is the voice stimulus;
    /// grabbing his shoulder and shaking it (or squeezing it hard) is the pain stimulus. He answers by his state:
    /// talks, mumbles, groans or nothing; eyes open toward the medic, head turns.</item>
    /// <item>Capillary refill — a fingertip presses one of his nails ≥ 1 s; released, the white nail bed turns pink
    /// again in N s by perfusion.</item>
    /// </list>
    /// Results go to the protocol (debrief: "Пульс проверен в 0:42 — 128, слабый") and, in Training, to a small white
    /// label next to the hands.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class VitalsChecks : MonoBehaviour
    {
        public CasualtyController casualty;
        [Tooltip("Player head (HMD). Default: the scenario's head, else the main camera.")]
        public Transform head;
        public float pulseHoldSeconds = 2f;
        public float breathDistance = 0.25f;
        public float breathHoldSeconds = 3f;
        public float voiceRadius = 1.7f;
        public float voiceHoldSeconds = 0.35f;
        public float stimulusCooldown = 3.5f;
        public float shakeSpeed = 0.3f;
        public int shakeReversals = 3;
        public float squeezeSeconds = 2f;
        public float nailRadius = 0.017f;
        public float nailPressSeconds = 1f;
        [Tooltip("Show the white result label (Training mode only by default).")]
        public bool labelsInTraining = true;
        public static bool ForceLabels;

        public event Action<AssessmentRecord> Assessed;
        public event Action<Stimulus, AvpuResponse> Stimulated;

        public PulseReading? LastPulse { get; private set; }
        public BreathReading? LastBreath { get; private set; }
        public float BreathNearSeconds { get; private set; }
        public bool Listening { get; private set; }
        public Stimulus? LastStimulus { get; private set; }
        public AvpuResponse LastResponse { get; private set; }
        public float LastRefillSeconds { get; private set; } = -1f;
        public int StimulusCount { get; private set; }
        public BreathSynth Breath { get; private set; }
        public CheckLabel Label { get; private set; }
        public float VoiceHeard { get; private set; }

        float _breathRecordedAt = -99f, _lastStimulus = -99f;
        float _shakeWindow, _squeeze;
        int _reversals;
        Vector3 _shakePrevVel;
        readonly float[] _nailPressed = new float[8];
        int _measuringNail = -1;
        float _nailReleasedAt;

        public Transform PlayerHead
        {
            get
            {
                if (head != null) return head;
                var d = FieldMedic.Scenarios.ScenarioDirector.Instance;
                if (d != null && d.head != null) return d.head;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        bool ShowLabels => ForceLabels || (labelsInTraining && FieldMedic.Battle.BattleDirector.Mode == FieldMedic.Battle.BattleMode.Training);

        void Start()
        {
            if (casualty == null) casualty = GetComponent<CasualtyController>();
            var h = casualty != null && casualty.rig != null ? casualty.rig.Bone(HumanBodyBones.Head) : null;
            if (h != null && Breath == null)
            {
                Vector3 mouth = casualty.Face != null && casualty.Face.Built ? casualty.Face.MouthPosition : h.position + casualty.rig.BodyUp * 0.03f + casualty.rig.BodyForward * 0.1f;
                Breath = BreathSynth.Create(h, h.InverseTransformPoint(mouth));
            }
            PlayerVoice.Ensure();
        }

        void OnDestroy() { if (Label != null) Destroy(Label.gameObject); }

        Vector3 Mouth()
        {
            if (casualty.Face != null && casualty.Face.Built) return casualty.Face.MouthPosition;
            var h = casualty.rig.Bone(HumanBodyBones.Head);
            NpaItem.Face(casualty.rig, out var nostril, out _, out _, out _, out var up);
            return nostril - up * 0.03f;
        }

        void Update()
        {
            if (casualty == null || casualty.rig == null || casualty.Patient == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var p = casualty.Patient;
            var ph = PlayerHead;
            if (casualty.rig.lookTarget == null && ph != null) casualty.rig.lookTarget = ph;

            // ---------------------------------------------------------------- breathing
            bool snore = p.AirwayCompromised && p.outcome != Outcome.Died;
            if (Breath != null)
            {
                Breath.Drive(casualty.rig.BreathSignal, p.BreathDepth, snore ? 1f : 0f, dt);
                Breath.gain = Listening ? 0.85f : 0.45f;
            }
            if (ph != null && casualty.IsDown)
            {
                float d = Vector3.Distance(ph.position, Mouth());
                Listening = d < breathDistance;
                BreathNearSeconds = Listening ? BreathNearSeconds + dt : Mathf.Max(0f, BreathNearSeconds - dt * 2f);
                if (Listening && BreathNearSeconds >= breathHoldSeconds && Time.time - _breathRecordedAt > 20f)
                {
                    _breathRecordedAt = Time.time;
                    var b = VitalsAssessment.ReadBreathing(p);
                    LastBreath = b;
                    Record(AssessmentKind.Breathing, null, VitalsAssessment.BreathValue(b, false), VitalsAssessment.BreathValue(b, true),
                        VitalsAssessment.BreathLabel(b, GameSettings.KZ), Mouth());
                }
            }

            // ---------------------------------------------------------------- voice (AVPU V)
            VoiceHeard = 0f;
            var headBone = casualty.rig.Bone(HumanBodyBones.Head);
            if (ph != null && headBone != null && Vector3.Distance(ph.position, headBone.position) < voiceRadius && PlayerVoice.Talking)
                VoiceHeard = 1f;
            _talk = VoiceHeard > 0f ? _talk + dt : Mathf.Max(0f, _talk - dt * 0.5f);
            if (_talk >= voiceHoldSeconds && Time.time - _lastStimulus > stimulusCooldown && casualty.IsDown)
            {
                _talk = 0f;
                Stimulate(Stimulus.Voice, null);
            }

            // ---------------------------------------------------------------- shake / squeeze the shoulder (AVPU P)
            Shake(dt);

            // ---------------------------------------------------------------- capillary refill
            Nails(dt);
        }

        float _talk;

        void Shake(float dt)
        {
            var roll = casualty.Interaction != null ? casualty.Interaction.Roll : null;
            HandGrabber hand = null;
            if (roll != null)
            {
                if (roll.shoulderL != null && roll.shoulderL.Hand != null) hand = roll.shoulderL.Hand;
                else if (roll.shoulderR != null && roll.shoulderR.Hand != null) hand = roll.shoulderR.Hand;
            }
            var drag = casualty.Interaction != null ? casualty.Interaction.Drag : null;
            if (hand == null && drag != null)
                foreach (var sh in drag.strapHandles) if (sh != null && sh.Hand != null) { hand = sh.Hand; break; }
            if (hand == null || hand.Input == null) { _reversals = 0; _shakeWindow = 0f; _squeeze = 0f; return; }
            Vector3 v = hand.Input.Velocity;
            _shakeWindow += dt;
            if (v.magnitude > shakeSpeed && _shakePrevVel.magnitude > shakeSpeed * 0.5f && Vector3.Dot(v, _shakePrevVel) < 0f) { _reversals++; _shakeWindow = 0f; }
            if (v.magnitude > shakeSpeed) _shakePrevVel = v;
            if (_shakeWindow > 0.7f) _reversals = 0;   // a pause ends the shake
            _squeeze = hand.Input.Grip > 0.9f ? _squeeze + dt : 0f;
            bool fire = _reversals >= shakeReversals || _squeeze >= squeezeSeconds;
            if (fire && Time.time - _lastStimulus > stimulusCooldown)
            {
                _reversals = 0; _squeeze = 0f;
                Stimulate(Stimulus.Pain, hand);
            }
        }

        /// <summary>A voice or pain stimulus (also callable by tests / the desktop simulator).</summary>
        public void Stimulate(Stimulus s, HandGrabber byHand)
        {
            var p = casualty.Patient;
            var avpu = p.AVPU;
            var r = VitalsAssessment.Respond(avpu, s);
            _lastStimulus = Time.time;
            LastStimulus = s;
            LastResponse = r;
            StimulusCount++;
            if (casualty.Face != null) casualty.Face.OnStimulus(r);
            if (r.eyesOpen) casualty.Attend(4f);
            var v = casualty.voice;
            if (v != null)
            {
                switch (r.reaction)
                {
                    case AvpuReaction.Talks: v.Say(s == Stimulus.Pain ? "hurts" : Pick("holding", "brother", "help", "blood_much"), true); break;
                    case AvpuReaction.Mumbles: if (!v.Say(Pick("dizzy", "cold", "water"), true)) v.Vocalise(v.bank != null ? v.bank.groan : null, 0.6f, true); break;
                    case AvpuReaction.Groans: v.Vocalise(v.bank != null ? v.bank.groan : null, 0.7f, true); break;
                }
            }
            if (s == Stimulus.Pain && r.withdraws) casualty.PainReaction(0.45f, byHand != null ? byHand.transform : null, "shake");
            var hb = casualty.rig.Bone(HumanBodyBones.Head);
            Record(AssessmentKind.Avpu, s == Stimulus.Voice ? "голос" : "боль", VitalsAssessment.AvpuValue(avpu, s, false), VitalsAssessment.AvpuValue(avpu, s, true),
                VitalsAssessment.AvpuLabel(avpu, s, GameSettings.KZ), hb != null ? hb.position : transform.position);
            Stimulated?.Invoke(s, r);
        }

        string Pick(params string[] ids) => ids[UnityEngine.Random.Range(0, ids.Length)];

        /// <summary>Called by <see cref="HandTreatment"/> when two fingertips have rested on a pulse point long enough.</summary>
        public void PulseFelt(PulseSite site, Vector3 at)
        {
            var r = VitalsAssessment.ReadPulse(casualty.Patient, site);
            LastPulse = r;
            Record(AssessmentKind.Pulse, VitalsAssessment.SiteName(site, false), VitalsAssessment.PulseValue(r, false), VitalsAssessment.PulseValue(r, true),
                VitalsAssessment.PulseLabel(r, GameSettings.KZ), at);
        }

        void Nails(float dt)
        {
            var face = casualty.Face;
            if (face == null || face.Nails.Count == 0) return;
            for (int n = 0; n < face.Nails.Count && n < _nailPressed.Length; n++)
            {
                var nail = face.Nails[n];
                bool touching = false;
                HandGrabber by = null;
                for (int i = 0; i < HandGrabber.All.Count; i++)
                {
                    var h = HandGrabber.All[i];
                    if (h == null || h.Held != null) continue;
                    float d = Mathf.Min(Vector3.Distance(h.IndexTipPosition, nail.Position), Vector3.Distance(h.FingertipPosition, nail.Position));
                    if (d < (nail.pressed ? nailRadius * 1.8f : nailRadius)) { touching = true; by = h; break; }
                }
                if (touching)
                {
                    if (!nail.pressed) HapticsProfile.Current.SealPress(by.Input);
                    face.PressNail(nail, true);
                    _nailPressed[n] += dt;
                }
                else if (nail.pressed)
                {
                    face.PressNail(nail, false);
                    if (_nailPressed[n] >= nailPressSeconds) { _measuringNail = n; _nailReleasedAt = Time.time; }
                    _nailPressed[n] = 0f;
                }
            }
            // the colour is back: report the time the medic actually watched
            if (_measuringNail >= 0 && _measuringNail < face.Nails.Count)
            {
                var nail = face.Nails[_measuringNail];
                float limit = 10f;
                if (!nail.pressed && (nail.blanch <= 0.08f || Time.time - _nailReleasedAt > limit))
                {
                    float sec = nail.blanch <= 0.08f ? Time.time - _nailReleasedAt : float.PositiveInfinity;
                    LastRefillSeconds = sec;
                    _measuringNail = -1;
                    Record(AssessmentKind.CapillaryRefill, nail.name, VitalsAssessment.RefillValue(sec, false), VitalsAssessment.RefillValue(sec, true),
                        VitalsAssessment.RefillLabel(sec, GameSettings.KZ), nail.Position);
                }
            }
        }

        void Record(AssessmentKind k, string site, string ru, string kz, string label, Vector3 at)
        {
            var rec = casualty.Protocol.RecordAssessment(k, site, ru, kz);
            Debug.Log($"[FMVR] check {k}: {ru}");
            if (ShowLabels)
            {
                if (Label == null) Label = CheckLabel.Create("CheckLabel_" + name);
                Label.Show(label, at, PlayerHead);
            }
            Assessed?.Invoke(rec);
        }
    }
}
