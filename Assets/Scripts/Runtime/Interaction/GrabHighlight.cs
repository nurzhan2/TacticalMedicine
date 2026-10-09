using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>Subtle hover tint (no outline shader, mobile friendly) via MaterialPropertyBlock.</summary>
    public sealed class GrabHighlight : MonoBehaviour
    {
        public Color tint = new Color(1f, 0.85f, 0.55f);
        [Range(0f, 1f)] public float amount = 0.35f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        Renderer[] _renderers;
        Color[] _original;
        MaterialPropertyBlock _mpb;
        public bool IsHighlighted { get; private set; }

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _original = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i].sharedMaterial;
                _original[i] = m != null && m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : Color.white;
            }
            _mpb = new MaterialPropertyBlock();
        }

        public void SetHighlighted(bool on)
        {
            if (_renderers == null) Awake();
            IsHighlighted = on;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, on ? Color.Lerp(_original[i], tint, amount) : _original[i]);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }
    }
}
