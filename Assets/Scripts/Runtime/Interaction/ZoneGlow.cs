using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// w10d: soft pulsing glow that marks an interaction target (pulse site, TIME tab, wound...). Call
    /// <see cref="Show"/> every frame while it should be visible; it hides itself one frame after the last call.
    /// One camera-facing quad per key (1 draw call each, only while shown).
    /// </summary>
    public sealed class ZoneGlow : MonoBehaviour
    {
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f, 0.9f);
        public static readonly Color Green = new Color(0.45f, 1f, 0.55f, 0.9f);
        public static readonly Color Red = new Color(1f, 0.35f, 0.3f, 0.9f);

        static readonly Dictionary<object, ZoneGlow> Glows = new Dictionary<object, ZoneGlow>();
        Material _mat;
        int _shownFrame;
        float _size, _strength;
        Vector3 _at;

        /// <summary>Glow at <paramref name="pos"/> with diameter <paramref name="size"/> (m); strength 0..1 = brightness.</summary>
        public static void Show(object key, Vector3 pos, float size, Color color, float strength = 1f)
        {
            if (key == null || !Application.isPlaying) return;
            if (!Glows.TryGetValue(key, out var g) || g == null)
            {
                var mat = FieldMedic.UI.UiMesh.Mat(color, 3060, FieldMedic.UI.UiMesh.Icon("glow"));
                var r = FieldMedic.UI.UiMesh.Add(null, "ZoneGlow", FieldMedic.UI.UiMesh.Quad, mat, Vector3.zero);
                g = r.gameObject.AddComponent<ZoneGlow>();
                g._mat = mat;
                Glows[key] = g;
            }
            g._mat.SetColor("_Color", color);
            g._shownFrame = Time.frameCount;
            g._at = pos; g._size = size; g._strength = Mathf.Clamp01(strength);
            if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
            g.Place();
        }

        /// <summary>Is a glow for this key visible (tests)?</summary>
        public static bool IsShown(object key) =>
            key != null && Glows.TryGetValue(key, out var g) && g != null && g.gameObject.activeSelf && Time.frameCount - g._shownFrame <= 1;

        void Place()
        {
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : _at + Vector3.back;
            Vector3 toEye = (eye - _at).normalized;
            transform.position = _at + toEye * 0.02f;
            transform.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f);
            transform.localScale = Vector3.one * (_size * pulse);
            _mat.SetFloat("_Alpha", (0.35f + 0.5f * _strength) * pulse);
        }

        void LateUpdate()
        {
            if (Time.frameCount - _shownFrame > 1) { gameObject.SetActive(false); return; }
            Place();
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
