using System.Collections;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 living Erlan, checked with scripted hands only: pulse by two fingertips held ≥ 2 s (neck + wrist, haptic
    /// beats at his heart rate, amplitude from blood pressure, radial gone in deep shock), breathing by the head next
    /// to his mouth ≥ 3 s, AVPU by the microphone level and by shaking the shoulder, capillary refill on a nail,
    /// visible shock colours.
    /// </summary>
    public class VitalsChecksTests : W8SceneTest
    {
        int _beats;
        float _beatAmp;
        void OnHaptic(HapticKind k, FieldMedic.Input.IHandInput h, float a) { if (k == HapticKind.Heartbeat) { _beats++; _beatAmp += a; } }

        [UnityTest]
        public IEnumerator Pulse_TwoFingers_Neck_And_Wrist_Beats_By_Pressure()
        {
            yield return LoadLesson("assess");
            yield return Ready();
            Assert.IsNotNull(C.Checks, "VitalsChecks on Erlan");
            var bot = run.bot;
            HapticsProfile.Played += OnHaptic;
            try
            {
                // a touch of 1 s is not a pulse check
                yield return bot.CheckPulse(C, PulseSite.Carotid, 1.0f);
                Assert.IsFalse(C.Protocol.Assessed(AssessmentKind.Pulse), "1 s on the neck is not a check");
                // 2.6 s on the carotid with two fingertips
                _beats = 0; _beatAmp = 0f;
                yield return bot.CheckPulse(C, PulseSite.Carotid, 2.6f);
                var rec = C.Protocol.assessments.LastOrDefault(a => a.kind == AssessmentKind.Pulse);
                Assert.IsNotNull(rec, "carotid pulse recorded");
                Assert.AreEqual("на шее", rec.site);
                var r = C.Checks.LastPulse.Value;
                Assert.AreEqual(PulseSite.Carotid, r.site);
                Assert.That(r.rate, Is.EqualTo(VitalsAssessment.ReadPulse(C.Patient, PulseSite.Carotid).rate).Within(4), "his heart rate");
                Assert.That(r.rate, Is.GreaterThan(110), "shock: fast");
                Assert.GreaterOrEqual(_beats, 3, "haptic beats under the fingers");
                float ampShock = _beatAmp / Mathf.Max(1, _beats);
                Assert.IsTrue(C.Checks.Label != null && C.Checks.Label.Visible, "white result label in Training");
                StringAssert.Contains("Пульс на шее", C.Checks.Label.Text);
                Debug.Log($"[FMVR] w8 test pulse: {C.Checks.Label.Text}, beats {_beats}, amp {ampShock:F2}");

                // wrist
                yield return bot.CheckPulse(C, PulseSite.RadialR, 2.6f);
                Assert.IsTrue(C.Protocol.assessments.Any(a => a.kind == AssessmentKind.Pulse && a.site == "на запястье"), "radial recorded");
                Assert.IsTrue(C.Checks.LastPulse.Value.Present, "radial still there at ~28 % loss");

                // healthy: strong carotid beats
                SetLoss(0f);
                yield return DemoFlowRunner.Seconds(0.3f);
                _beats = 0; _beatAmp = 0f;
                yield return bot.CheckPulse(C, PulseSite.Carotid, 2.6f);
                float ampHealthy = _beatAmp / Mathf.Max(1, _beats);
                Assert.Greater(ampHealthy, ampShock + 0.05f, "beats are stronger with a normal blood pressure");

                // deep shock: the radial pulse disappears (SBP < 80), the carotid stays
                SetLoss(0.4f);
                yield return DemoFlowRunner.Seconds(0.3f);
                Assert.Less(C.Patient.SystolicBP, 80f);
                _beats = 0;
                yield return bot.CheckPulse(C, PulseSite.RadialR, 2.6f);
                Assert.IsFalse(C.Checks.LastPulse.Value.Present, "no radial pulse below SBP 80");
                Assert.AreEqual(0, _beats, "nothing to feel at the wrist");
                StringAssert.Contains("пульса нет", C.Protocol.assessments.Last().ru);
            }
            finally { HapticsProfile.Played -= OnHaptic; }
        }

        [UnityTest]
        public IEnumerator Breathing_ByHeadNearMouth_RateAndDepth_FollowPhysiology()
        {
            yield return LoadLesson("assess");
            yield return Ready(1.2f);
            SetLoss(0.05f);
            C.Patient.InsertAirwayAdjunct();
            yield return DemoFlowRunner.Seconds(3.5f);
            Assert.IsFalse(C.Checks.Listening, "head 1.2 m away is not listening");
            Assert.IsFalse(C.Protocol.Assessed(AssessmentKind.Breathing));
            Assert.IsNotNull(C.Checks.Breath, "3D breath at his mouth");
            Assert.IsTrue(C.Checks.Breath.Source.isPlaying);

            float maxFlow = 0f; int crossings = 0; float prev = C.rig.BreathSignal;
            var watch = new GameObject("~watch").AddComponent<FrameWatch>();
            watch.onFrame = () =>
            {
                maxFlow = Mathf.Max(maxFlow, C.Checks.Breath.Flow);
                float s = C.rig.BreathSignal;
                if (prev <= 0f && s > 0f) crossings++;
                prev = s;
            };
            yield return run.bot.ListenBreath(C, run.head, 3.4f);
            Assert.IsTrue(C.Checks.Listening, "ear within 25 cm of the mouth");
            Assert.IsTrue(C.Protocol.Assessed(AssessmentKind.Breathing), "breathing recorded after 3 s");
            Assert.Greater(maxFlow, 0.05f, "breath heard (procedural airflow)");
            var normal = C.Checks.LastBreath.Value;
            Assert.IsFalse(normal.Fast);

            // tension pneumothorax: fast and shallow (rate counted from the chest itself)
            run.head.position += Vector3.up * 1f;
            yield return DemoFlowRunner.Seconds(1.0f);
            C.Patient.pneumo = PneumoState.Tension;
            C.Patient.tensionSeverity = 0.85f;
            yield return DemoFlowRunner.Seconds(1.0f);
            crossings = 0;
            yield return run.bot.ListenBreath(C, run.head, 6f);
            Object.Destroy(watch.gameObject);
            var t = C.Checks.LastBreath.Value;
            Assert.IsTrue(t.Fast && t.Shallow, "tension: fast and shallow");
            StringAssert.Contains("поверхностное", C.Protocol.assessments.Last(a => a.kind == AssessmentKind.Breathing).ru);
            float counted = crossings / 6f * 60f;
            Debug.Log($"[FMVR] w8 test breathing: normal {normal.rate}/min, tension {t.rate}/min (chest counted {counted:F0}/min), depth {normal.depth:F2} → {t.depth:F2}");
            Assert.That(counted, Is.EqualTo(C.Patient.RespRate).Within(C.Patient.RespRate * 0.35f), "the chest rises at the physiology's rate");
            Assert.Less(C.rig.breathDepth, 0.9f);
        }

        [UnityTest]
        public IEnumerator Avpu_ByVoiceLevel_And_ShoulderShake()
        {
            yield return LoadLesson("assess");
            yield return Ready(0.7f);
            // kneel at his head: the medic talks to him from ~0.6 m
            var hb = C.rig.Bone(HumanBodyBones.Head);
            run.head.position = hb.position + Vector3.up * 0.55f + C.rig.BodyUp * 0.2f;
            var face = C.Face;
            Assert.IsTrue(face != null && face.Built, "face built");
            Assert.AreEqual(Consciousness.Voice, C.Patient.AVPU, "assess lesson: answers the voice");
            // quiet: nothing
            PlayerVoice.Override = 0.1f;
            yield return DemoFlowRunner.Seconds(1.0f);
            Assert.AreEqual(0, C.Checks.StimulusCount, "quiet medic: no stimulus");
            // loud near his head
            PlayerVoice.Override = 0.9f;
            yield return DemoFlowRunner.Seconds(1.0f);
            PlayerVoice.Override = 0f;
            Assert.AreEqual(Stimulus.Voice, C.Checks.LastStimulus);
            Assert.AreEqual(AvpuReaction.Mumbles, C.Checks.LastResponse.reaction);
            yield return DemoFlowRunner.Seconds(0.6f);
            Assert.Greater(face.EyesOpen, 0.6f, "eyes open toward the medic");
            Assert.IsTrue(C.Protocol.assessments.Any(a => a.kind == AssessmentKind.Avpu && a.ru.Contains("(V)")), "AVPU V recorded");
            Assert.IsTrue(C.Attending, "he turns to the medic");

            // deeper shock: P — no answer to the voice, eyes stay shut; a shake makes him groan and flinch
            SetLoss(0.36f);
            C.Patient.InsertAirwayAdjunct();   // at P the tongue would block the airway and hypoxia would make him U
            yield return DemoFlowRunner.Seconds(4.5f);
            Assert.AreEqual(Consciousness.Pain, C.Patient.AVPU);
            int n = C.Checks.StimulusCount;
            PlayerVoice.Override = 0.9f;
            yield return DemoFlowRunner.Seconds(1.0f);
            PlayerVoice.Override = 0f;
            Assert.Greater(C.Checks.StimulusCount, n);
            Assert.AreEqual(AvpuReaction.None, C.Checks.LastResponse.reaction, "P: nothing to the voice");
            Assert.Less(face.EyesOpen, 0.3f, "eyes stay closed");
            yield return DemoFlowRunner.Seconds(4f);
            int pain0 = C.PainReactionCount;
            yield return run.bot.ShakeShoulder(C);
            Assert.AreEqual(Stimulus.Pain, C.Checks.LastStimulus, "the shake is a pain stimulus");
            Assert.AreEqual(AvpuReaction.Groans, C.Checks.LastResponse.reaction);
            Assert.Greater(C.PainReactionCount, pain0, "he flinches");
            Assert.IsTrue(C.Protocol.assessments.Any(a => a.kind == AssessmentKind.Avpu && a.ru.Contains("(P)")), "AVPU P recorded");

            // alert: eyes look at the player's head
            SetLoss(0f);
            yield return DemoFlowRunner.Seconds(2.5f);
            Assert.AreEqual(Consciousness.Alert, C.Patient.AVPU);
            Assert.IsTrue(face.LookingAtPlayer, "conscious: the eyes follow the medic");
            Vector3 eyeFwd = face.LeftEye.forward, to = (run.head.position - face.LeftEye.position).normalized;
            Debug.Log($"[FMVR] w8 test gaze: {Vector3.Angle(eyeFwd, to):F0}° off the medic's head");
        }

        [UnityTest]
        public IEnumerator CapillaryRefill_NailPressed_ColourReturns_ByPerfusion()
        {
            yield return LoadLesson("assess");
            yield return Ready();
            var face = C.Face;
            Assert.GreaterOrEqual(face.Nails.Count, 4, "nail beds on his fingers");
            SetLoss(0f);
            yield return DemoFlowRunner.Seconds(0.5f);
            var nail = face.Nails[4];
            float maxBlanch = 0f;
            var watch = new GameObject("~watch").AddComponent<FrameWatch>();
            watch.onFrame = () => maxBlanch = Mathf.Max(maxBlanch, nail.blanch);
            yield return run.bot.PressNail(nail, run.bot.left, 1.3f);
            for (float t = 0f; t < 8f && C.Checks.LastRefillSeconds < 0f; t += Time.deltaTime) yield return null;
            Assert.Greater(maxBlanch, 0.8f, "pressed nail turns white");
            float healthy = C.Checks.LastRefillSeconds;
            Assert.That(healthy, Is.InRange(0.5f, 2.2f), "normal refill");
            // shock
            SetLoss(0.36f);
            yield return DemoFlowRunner.Seconds(0.5f);
            var nail2 = face.Nails[3];
            yield return run.bot.PressNail(nail2, run.bot.left, 1.3f);
            float t0 = Time.time;
            while (Time.time - t0 < 10f && C.Protocol.assessments.Count(a => a.kind == AssessmentKind.CapillaryRefill) < 2) yield return null;
            Object.Destroy(watch.gameObject);
            float shock = C.Checks.LastRefillSeconds;
            Debug.Log($"[FMVR] w8 test capillary refill: healthy {healthy:F1} s, shock {shock:F1} s (model {VitalsAssessment.CapillaryRefillSeconds(C.Patient):F1})");
            Assert.Greater(shock, 3f, "slow refill in shock");
            Assert.Greater(shock, healthy + 1f);
        }

        [UnityTest]
        public IEnumerator Shock_Colours_Pale_Grey_BlueLips_Sweat_AtMost10Hz()
        {
            yield return LoadLesson("assess");
            yield return Ready();
            var face = C.Face;
            SetLoss(0f);
            yield return DemoFlowRunner.Seconds(1.5f);
            Color s0 = face.CurrentSkin, l0 = face.CurrentLips;
            float sm0 = face.CurrentSmoothness;
            int u0 = face.TintUpdates;
            SetLoss(0.44f);
            C.Patient.InsertAirwayAdjunct();
            float t0 = Time.time;
            yield return DemoFlowRunner.Seconds(3f);
            int updates = face.TintUpdates - u0;
            float hz = updates / (Time.time - t0);
            Color s1 = face.CurrentSkin, l1 = face.CurrentLips;
            Color.RGBToHSV(s0, out _, out float sat0, out _);
            Color.RGBToHSV(s1, out _, out float sat1, out _);
            Debug.Log($"[FMVR] w8 test shock colours: skin {s0} → {s1}, lips {l0} → {l1}, smooth {sm0:F2} → {face.CurrentSmoothness:F2}, tint {hz:F1} Hz");
            Assert.Less(sat1, sat0 - 0.1f, "pale/grey skin");
            Assert.Greater(l1.b - l1.r, l0.b - l0.r + 0.1f, "lips turn blue");
            Assert.LessOrEqual(hz, 10.5f, "material updates ≤ 10 Hz");
            // sweat builds up over time
            for (int i = 0; i < 40 && C.Sweat < 0.5f; i++) yield return DemoFlowRunner.Seconds(0.5f);
            yield return DemoFlowRunner.Seconds(0.3f);
            Assert.Greater(face.CurrentSmoothness, sm0 + 0.05f, "sweat sheen");
        }
    }

    /// <summary>Runs a callback every frame (test instrumentation).</summary>
    public sealed class FrameWatch : MonoBehaviour
    {
        public System.Action onFrame;
        void Update() => onFrame?.Invoke();
    }
}
