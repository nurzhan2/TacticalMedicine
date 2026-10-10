using FieldMedic.Input;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Short briefing in front of the player at the start of a scenario (title, situation, key skills). Stays
    /// world-locked at reading distance, fades out after the incident starts or after `life` seconds.
    /// </summary>
    public sealed class BriefingCard : MonoBehaviour
    {
        public float life = 11f;
        float _t;
        TextMesh[] _texts;
        Renderer _bg;
        Material _bgMat;

        public static BriefingCard Show(ScenarioDefinition def, Transform head)
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return null;
            var go = new GameObject("BriefingCard");
            var c = go.AddComponent<BriefingCard>();
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            go.transform.position = head.position + fwd * 0.85f + Vector3.down * 0.12f;
            go.transform.rotation = Quaternion.LookRotation(fwd);
            c.Build(def);
            return c;
        }

        void Build(ScenarioDefinition def)
        {
            bool kz = GameSettings.KZ;
            _bgMat = WorldUI.Unlit(new Color(0.02f, 0.025f, 0.02f));
            _bg = WorldUI.Quad(transform, "Bg", new Vector3(0f, 0f, 0.01f), new Vector2(0.62f, 0.36f), _bgMat).GetComponent<Renderer>();
            var title = WorldUI.Text(transform, "Title", $"S{def.number}  {def.Title(kz)}", new Vector3(-0.29f, 0.16f, 0f), 0.026f, new Color(1f, 0.8f, 0.4f));
            var body = WorldUI.Text(transform, "Body", WorldUI.Wrap(def.Briefing(kz), 52), new Vector3(-0.29f, 0.11f, 0f), 0.0125f, new Color(0.9f, 0.9f, 0.85f));
            string skills = "";
            var list = kz ? def.skillsKz : def.skillsRu;
            foreach (var s in list) skills += "•  " + s + "\n";
            var sk = WorldUI.Text(transform, "Skills", skills, new Vector3(-0.29f, -0.04f, 0f), 0.0115f, new Color(0.65f, 0.85f, 0.6f));
            _texts = new[] { title, body, sk };
        }

        void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Clamp01(_t / 0.6f) * Mathf.Clamp01((life - _t) / 1.2f);
            foreach (var t in _texts) { var c = t.color; c.a = a; t.color = c; }
            _bg.enabled = a > 0.02f;
            if (_t > life) Destroy(gameObject);
        }
    }
}
