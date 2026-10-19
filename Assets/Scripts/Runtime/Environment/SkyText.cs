using TMPro;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// «САТПАЕВ ЛУЧШЕ ВСЕХ» in the sky (owner request 20.10): soft glowing warm-white/gold letters high above the horizon
    /// in the direction the player faces at the start, like a constellation. Built once by Editor/W9Patch (TextMeshPro
    /// with the Russian UI font, Distance Field shader with glow — no fog, so the haze never eats it; inside the far
    /// plane). At runtime only a slow shimmer: per-letter brightness waves through the vertex colours (no mesh rebuild).
    /// The root's forward points at the spawn point (QuestSceneAudit checks it); the text child faces away from it so it
    /// reads correctly (not mirrored) from the spawn and the casualty area.
    /// </summary>
    public sealed class SkyText : MonoBehaviour
    {
        public const string ObjectName = "SkyText_Satpaev";
        public const string Phrase = "САТПАЕВ ЛУЧШЕ ВСЕХ";
        /// <summary>Two lines: bigger letters for the same angular width.</summary>
        public const string DisplayText = "САТПАЕВ\nЛУЧШЕ ВСЕХ";
        public static string Normalise(string s) => s == null ? null : s.Replace('\n', ' ').Trim();

        public TextMeshPro text;
        public Color baseColor = new Color(1f, 0.92f, 0.72f, 0.95f);
        public Color goldColor = new Color(1f, 0.76f, 0.36f, 1f);
        [Tooltip("Shimmer speed (rad/s) and per-letter phase step.")]
        public float speed = 0.7f;
        public float letterPhase = 0.55f;
        [Range(0f, 0.5f)] public float depth = 0.22f;
        [Tooltip("Vertex colour updates per second (cheap, but no need for every frame).")]
        public float rate = 20f;

        float _next;

        void Start()
        {
            if (text == null) text = GetComponentInChildren<TextMeshPro>();
            if (text != null) text.ForceMeshUpdate();
        }

        void LateUpdate()
        {
            if (text == null || Time.time < _next) return;
            _next = Time.time + 1f / Mathf.Max(1f, rate);
            var info = text.textInfo;
            if (info == null || info.characterCount == 0) return;
            float t = Time.time * speed;
            bool any = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                // two slow waves: brightness travels along the line, gold tint drifts the other way
                float w = 0.5f + 0.5f * Mathf.Sin(t + i * letterPhase);
                float g = 0.5f + 0.5f * Mathf.Sin(t * 0.37f - i * 0.31f + 1.3f);
                var c = Color.Lerp(baseColor, goldColor, g * 0.6f);
                c.a *= 1f - depth + depth * w;
                var c32 = (Color32)c;
                var cols = info.meshInfo[ch.materialReferenceIndex].colors32;
                int v = ch.vertexIndex;
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c32;
                any = true;
            }
            if (any) text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        /// <summary>Pose used by the builder and the audit: `distance` m from the eye, `elevation` deg above the horizon
        /// at compass `yaw`, forward pointing back at the eye.</summary>
        public static Pose PlaceFrom(Vector3 eye, float yaw, float elevation, float distance)
        {
            Vector3 dir = Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward;
            Vector3 p = eye + dir * distance;
            return new Pose(p, Quaternion.LookRotation(-dir, Vector3.up));
        }

        /// <summary>Elevation of the text centre above the horizon seen from `eye` (deg).</summary>
        public static float ElevationFrom(Vector3 eye, Vector3 textPos)
        {
            Vector3 d = textPos - eye;
            return Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        }
    }
}
