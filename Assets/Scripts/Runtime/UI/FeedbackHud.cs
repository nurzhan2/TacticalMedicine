using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Desktop/editor feedback: the last protocol entries of the nearest casualty (colour by severity) and the
    /// state of the tourniquet / pulse under the fingers. Hidden in VR (no HUD there: GDD pillar 2).
    /// </summary>
    public sealed class FeedbackHud : MonoBehaviour
    {
        public int lines = 5;
        GUIStyle _style;

        void OnGUI()
        {
            if (DesktopSimulator.XRActive || !GameSettings.Hints) return;
            CasualtyController c = null;
            foreach (var x in CasualtyController.All) if (x != null && x.IsDown) { c = x; break; }
            if (c == null || c.Protocol == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            var log = c.Protocol.log;
            float y = Screen.height - 30f;
            for (int i = log.Count - 1, n = 0; i >= 0 && n < lines; i--)
            {
                var e = log[i];
                if (string.IsNullOrEmpty(e.text)) continue;
                n++;
                GUI.color = e.severity switch
                {
                    Severity.Error => new Color(1f, 0.35f, 0.3f),
                    Severity.Warning => new Color(1f, 0.8f, 0.3f),
                    Severity.Ok => new Color(0.5f, 1f, 0.5f),
                    _ => new Color(0.85f, 0.85f, 0.85f),
                };
                GUI.Label(new Rect(12f, y, Screen.width - 24f, 26f), $"[{Scenarios.ScenarioClock.Format(e.time)}] {e.text}", _style);
                y -= 24f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            string phase = c.Protocol.phase == Phase.CareUnderFire ? "ПОД ОГНЁМ" : c.Protocol.phase == Phase.TacticalFieldCare ? "В УКРЫТИИ (MARCH)" : "ЭВАКУАЦИЯ";
            GUI.Label(new Rect(12f, 56f, 600f, 24f), $"Фаза: {phase}   Время {Scenarios.ScenarioClock.Format(c.Patient.secondsSinceStart)}", _style);
        }
    }
}
