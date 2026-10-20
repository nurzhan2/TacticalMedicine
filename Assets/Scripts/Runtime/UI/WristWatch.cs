using FieldMedic.Input;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.UI
{
    /// <summary>
    /// Diegetic watch + wrist display on the left wrist (GDD §9, GDD_V2 §6): wall-clock time, time since the injury, the
    /// phase, the evacuation countdown, and in every mode a small screen with Erlan's pulse (a red dot beating with his
    /// heart) and the golden-hour countdown (60:00 from the injury, red when it runs out). In «До темноты» a round "+"
    /// button jumps to the next prolonged-care milestone (desktop: T).
    /// </summary>
    public sealed class WristWatch : MonoBehaviour
    {
        public Transform hand;
        public Vector3 localOffset = new Vector3(0.0f, 0.035f, -0.045f);
        public const float GoldenHourSeconds = 3600f;

        /// <summary>Face up on the back of the wrist, 12 o'clock away from the wearer (hand -X).</summary>
        static readonly Quaternion FaceRotation = Quaternion.LookRotation(Vector3.down, Vector3.left);

        TextMesh _time, _info, _pulse, _golden;
        Transform _beat, _screen;
        Material _beatMat;
        RoundButton _skip;
        float _beatPhase;

        public string PulseText => _pulse != null ? _pulse.text : "";
        public string GoldenText => _golden != null ? _golden.text : "";

        void Start()
        {
            if (hand == null) hand = transform.parent;
            WorldUI.Disc(transform, "Case", new Vector3(0f, 0f, 0.004f), 0.044f, 0.012f, WorldUI.Lit(new Color(0.08f, 0.08f, 0.07f), 0.5f));
            WorldUI.Disc(transform, "Face", new Vector3(0f, 0f, -0.0025f), 0.038f, 0.002f, WorldUI.Unlit(new Color(0.02f, 0.03f, 0.02f)));
            WorldUI.Box(transform, "Strap", new Vector3(0f, 0f, 0.012f), new Vector3(0.024f, 0.09f, 0.006f), WorldUI.Lit(new Color(0.12f, 0.12f, 0.08f), 0.1f));
            _time = WorldUI.Text(transform, "Time", "01:40", new Vector3(0f, 0.004f, -0.004f), 0.0075f, new Color(0.55f, 1f, 0.55f), TextAnchor.MiddleCenter, TextAlignment.Center);
            _info = WorldUI.Text(transform, "Info", "", new Vector3(0f, -0.007f, -0.004f), 0.0028f, new Color(0.45f, 0.8f, 0.45f), TextAnchor.MiddleCenter, TextAlignment.Center);
            // wrist display towards the elbow: pulse + golden hour
            _screen = new GameObject("WristDisplay").transform;
            _screen.SetParent(transform, false);
            _screen.localPosition = new Vector3(0f, -0.047f, 0f);
            WorldUI.Box(_screen, "Bezel", new Vector3(0f, 0f, 0.003f), new Vector3(0.05f, 0.03f, 0.006f), WorldUI.Lit(new Color(0.07f, 0.075f, 0.07f), 0.45f));
            WorldUI.Quad(_screen, "Screen", new Vector3(0f, 0f, -0.0005f), new Vector2(0.045f, 0.025f), WorldUI.Unlit(new Color(0.015f, 0.025f, 0.02f)));
            _beatMat = WorldUI.Unlit(new Color(0.95f, 0.15f, 0.12f));
            _beat = WorldUI.Disc(_screen, "Beat", new Vector3(-0.0165f, 0.0055f, -0.0012f), 0.005f, 0.001f, _beatMat).transform;
            _pulse = WorldUI.Text(_screen, "Pulse", "--", new Vector3(-0.012f, 0.0055f, -0.0012f), 0.0042f, new Color(1f, 0.55f, 0.5f), TextAnchor.MiddleLeft, TextAlignment.Left);
            _golden = WorldUI.Text(_screen, "Golden", "60:00", new Vector3(-0.0195f, -0.0055f, -0.0012f), 0.0042f, new Color(1f, 0.82f, 0.4f), TextAnchor.MiddleLeft, TextAlignment.Left);
            _skip = RoundButton.Create(transform, "SkipTime", new Vector3(0.036f, 0f, -0.002f), 0.024f, new Color(0.25f, 0.35f, 0.5f), "+", 0.008f);
            _skip.Clicked += Skip;
            _skip.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (hand != null) transform.SetPositionAndRotation(hand.TransformPoint(localOffset), hand.rotation * FaceRotation);
            var sa = FieldMedic.Scenarios.SelfAid.SelfAidDirector.Instance;
            if (sa != null && sa.Run != null && !sa.Run.Ended)
            {
                // w9b self-aid: your own watch — the time to write on the band and your own racing pulse (no countdown)
                var own = sa.Run.patient;
                UpdateDisplay(own, sa.Run.Clock);
                _golden.text = "";
                _time.text = ScenarioClock.Format(sa.Run.Clock);
                _info.text = !sa.Run.InCover && sa.Run.CoverReachedAt < 0f ? GameSettings.T("ПОД ОГНЁМ", "ОҚ АСТЫНДА")
                    : sa.Run.ArrivedAt >= 0f ? GameSettings.T("СВОИ", "ӨЗІМІЗДІКІ") : GameSettings.T("В УКРЫТИИ", "БАСПАНАДА");
                if (_skip != null) _skip.gameObject.SetActive(false);
                return;
            }
            var d = ScenarioDirector.Instance;
            var c = d != null ? d.casualty : null;
            var p = c != null ? c.Patient : null;
            bool injured = p != null && d.IncidentPatientTime >= 0f && p.wounds.Count > 0;
            UpdateDisplay(p, injured ? p.secondsSinceStart - d.IncidentPatientTime : -1f);
            if (d == null || d.Timeline == null || p == null)
            {
                _time.text = injured ? ScenarioClock.Format(p.secondsSinceStart) : System.DateTime.Now.ToString("HH:mm");
                _info.text = d != null && d.InMenu ? GameSettings.T("МЕНЮ", "МӘЗІР") : TrainingDirector.Instance != null ? GameSettings.T("ОБУЧЕНИЕ", "ОҚЫТУ") : "";
                if (_skip != null) _skip.gameObject.SetActive(false);
                return;
            }
            _time.text = ScenarioClock.Format(p.secondsSinceStart);
            string phase = d.State switch
            {
                ScenarioState.Briefing => GameSettings.T("ТИХО", "ТЫНЫШ"),
                ScenarioState.Incident or ScenarioState.CareUnderFire => GameSettings.T("ПОД ОГНЁМ", "ОҚ АСТЫНДА"),
                ScenarioState.TacticalFieldCare => "MARCH",
                ScenarioState.Evacuation => d.Timeline.EvacArrived ? GameSettings.T("ВЕРТОЛЁТ", "ТІКҰШАҚ") : GameSettings.T("ЭВАК ", "ЭВАК ") + Mathf.CeilToInt(d.Timeline.EvacRemaining / 60f) + GameSettings.T(" мин", " мин"),
                _ => GameSettings.T("РАЗБОР", "ТАЛДАУ"),
            };
            float since = d.IncidentPatientTime >= 0f ? p.secondsSinceStart - d.IncidentPatientTime : 0f;
            _info.text = phase + "\nT+" + Mathf.FloorToInt(since / 60f).ToString("00") + ":" + Mathf.FloorToInt(since % 60f).ToString("00");
            int next = d.NextMilestone;
            bool canSkip = next > 0 && d.State == ScenarioState.TacticalFieldCare;
            _skip.gameObject.SetActive(canSkip);
            if (canSkip) _skip.SetLabel("+");
            var kb = Keyboard.current;
            if (canSkip && !DesktopSimulator.XRActive && kb != null && kb.tKey.wasPressedThisFrame) Skip();
        }

        /// <summary>Pulse (beats per minute, "--" when none is palpable) and the golden-hour countdown.</summary>
        void UpdateDisplay(PatientPhysiology p, float sinceInjury)
        {
            if (_pulse == null) return;
            if (p == null || sinceInjury < 0f)
            {
                _pulse.text = GameSettings.T("пульс --", "пульс --");
                _golden.text = "60:00";
                _beat.localScale = new Vector3(0.005f, 0.0005f, 0.005f);
                return;
            }
            float hr = p.outcome == Outcome.Died ? 0f : p.HeartRate;
            _pulse.text = hr > 1f ? GameSettings.T("пульс ", "пульс ") + Mathf.RoundToInt(hr) : GameSettings.T("пульс --", "пульс --");
            float left = GoldenHourSeconds - sinceInjury;
            int s = Mathf.Abs(Mathf.FloorToInt(left));
            _golden.text = (left < 0f ? "-" : "") + (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
            _golden.color = left < 0f ? new Color(1f, 0.3f, 0.25f) : left < 900f ? new Color(1f, 0.6f, 0.3f) : new Color(1f, 0.82f, 0.4f);
            // heartbeat dot
            if (hr > 1f) _beatPhase += Time.deltaTime * hr / 60f;
            float b = hr > 1f ? Mathf.Exp(-Mathf.Repeat(_beatPhase, 1f) * 9f) : 0f;
            float d = 0.004f + 0.0025f * b;
            _beat.localScale = new Vector3(d, 0.0005f, d);
            _beatMat.SetColor("_BaseColor", Color.Lerp(new Color(0.35f, 0.05f, 0.05f), new Color(1f, 0.2f, 0.15f), b));
        }

        void Skip()
        {
            var d = ScenarioDirector.Instance;
            if (d != null) d.TimeSkip();
        }
    }
}
