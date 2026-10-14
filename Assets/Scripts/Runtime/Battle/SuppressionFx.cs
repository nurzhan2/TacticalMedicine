using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>Suppression vignette: a quad in front of the eye whose edge darkening follows
    /// <see cref="Amount"/> (smoothed). Hidden when zero so it costs nothing in calm moments.</summary>
    public sealed class SuppressionFx : MonoBehaviour
    {
        public Renderer quad;
        public float Amount { get; set; }
        float _shown;
        MaterialPropertyBlock _mpb;
        static readonly int AmountId = Shader.PropertyToID("_Amount");

        void Awake() => _mpb = new MaterialPropertyBlock();

        void LateUpdate()
        {
            if (quad == null) return;
            _shown = Mathf.MoveTowards(_shown, Mathf.Clamp01(Amount), Time.deltaTime * 1.5f);
            bool on = _shown > 0.01f;
            if (quad.enabled != on) quad.enabled = on;
            if (!on) return;
            _mpb.SetFloat(AmountId, _shown);
            quad.SetPropertyBlock(_mpb);
        }

        public float Shown => _shown;
    }
}
