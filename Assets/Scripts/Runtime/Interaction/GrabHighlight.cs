using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>Subtle hover tint (no outline shader, mobile friendly) via MaterialPropertyBlock.
    /// w10: the renderer list is re-read every time the tint switches on (imported art swaps states: sealed pack →
    /// opened pack...), the base colour always comes from the shared material.</summary>
    public sealed class GrabHighlight : MonoBehaviour
    {
        public Color tint = new Color(1f, 0.85f, 0.55f);
        [Range(0f, 1f)] public float amount = 0.35f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        Renderer[] _renderers;
        Color[] _original;
        MaterialPropertyBlock _mpb;
        public bool IsHighlighted { get; private set; }

        void Awake() => Collect();

        void Collect()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _original = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i].sharedMaterial;
                _original[i] = m != null && m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : Color.white;
            }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
        }

        public void SetHighlighted(bool on)
        {
            if (_renderers == null || (on && !IsHighlighted)) Collect();
            if (!on && !IsHighlighted) return;
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
