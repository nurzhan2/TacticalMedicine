using FieldMedic.Input;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.UI
{
    /// <summary>
    /// Diegetic watch on the left wrist (GDD §9): wall-clock time, time since the injury, the phase, the
    /// evacuation countdown. In «До рассвета» a round "+" button jumps to the next prolonged-care milestone
    /// (30 min / 1 h / 2 h / 4 h after the tourniquet); desktop: T.
    /// </summary>
    public sealed class WristWatch : MonoBehaviour
    {
        public Transform hand;
        public Vector3 localOffset = new Vector3(0.0f, 0.035f, -0.045f);

        /// <summary>Face up on the back of the wrist, 12 o'clock away from the wearer (hand -X).</summary>
        static readonly Quaternion FaceRotation = Quaternion.LookRotation(Vector3.down, Vector3.left);

        TextMesh _time, _info;
        RoundButton _skip;

        void Start()
        {
            if (hand == null) hand = transform.parent;
            WorldUI.Disc(transform, "Case", new Vector3(0f, 0f, 0.004f), 0.044f, 0.012f, WorldUI.Lit(new Color(0.08f, 0.08f, 0.07f), 0.5f));
            WorldUI.Disc(transform, "Face", new Vector3(0f, 0f, -0.0025f), 0.038f, 0.002f, WorldUI.Unlit(new Color(0.02f, 0.03f, 0.02f)));
            WorldUI.Box(transform, "Strap", new Vector3(0f, 0f, 0.012f), new Vector3(0.024f, 0.09f, 0.006f), WorldUI.Lit(new Color(0.12f, 0.12f, 0.08f), 0.1f));
            _time = WorldUI.Text(transform, "Time", "01:40", new Vector3(0f, 0.004f, -0.004f), 0.0075f, new Color(0.55f, 1f, 0.55f), TextAnchor.MiddleCenter, TextAlignment.Center);
            _info = WorldUI.Text(transform, "Info", "", new Vector3(0f, -0.007f, -0.004f), 0.0028f, new Color(0.45f, 0.8f, 0.45f), TextAnchor.MiddleCenter, TextAlignment.Center);
            _skip = RoundButton.Create(transform, "SkipTime", new Vector3(0.036f, 0f, -0.002f), 0.024f, new Color(0.25f, 0.35f, 0.5f), "+", 0.008f);
            _skip.Clicked += Skip;
            _skip.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (hand != null) transform.SetPositionAndRotation(hand.TransformPoint(localOffset), hand.rotation * FaceRotation);
            var d = ScenarioDirector.Instance;
            if (d == null || d.Timeline == null || d.casualty == null || d.casualty.Patient == null)
            {
                _time.text = System.DateTime.Now.ToString("HH:mm");
                _info.text = d != null && d.InMenu ? GameSettings.T("МЕНЮ", "МӘЗІР") : "";
                if (_skip != null) _skip.gameObject.SetActive(false);
                return;
            }
            var p = d.casualty.Patient;
            _time.text = ScenarioClock.Format(p.secondsSinceStart);
            string phase = d.State switch
            {
                ScenarioState.Briefing => GameSettings.T("ТИХО", "ТЫНЫШ"),
                ScenarioState.Incident or ScenarioState.CareUnderFire => GameSettings.T("ПОД ОГНЁМ", "ОҚ АСТЫНДА"),
                ScenarioState.TacticalFieldCare => "MARCH",
                ScenarioState.Evacuation => GameSettings.T("ЭВАК ", "ЭВАК ") + Mathf.CeilToInt(d.Timeline.EvacRemaining / 60f) + GameSettings.T(" мин", " мин"),
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

        void Skip()
        {
            var d = ScenarioDirector.Instance;
            if (d != null) d.TimeSkip();
        }
    }
}
