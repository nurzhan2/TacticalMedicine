using UnityEngine;

namespace FieldMedic.Casualty.Blood
{
    /// <summary>
    /// Put on each player hand. Touching a bleeding wound (or soaked clothing) bloodies the gloves:
    /// base colour goes to dark red, wet-glossy; over a minute the blood dries darker and matte.
    /// </summary>
    public sealed class BloodOnHands : MonoBehaviour
    {
        public Renderer[] gloves;
        public Vector3 palmOffset = new Vector3(0f, -0.01f, 0.06f);
        public float touchRadius = 0.11f;
        [Range(0f, 1f)] public float bloodiness;
        [Range(0f, 1f)] public float wetness;
        public Color cleanColor = new Color(0.12f, 0.12f, 0.1f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothId = Shader.PropertyToID("_Smoothness");
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            if (gloves == null || gloves.Length == 0) gloves = GetComponentsInChildren<Renderer>();
            if (gloves.Length > 0 && gloves[0].sharedMaterial != null && gloves[0].sharedMaterial.HasProperty(BaseColorId))
                cleanColor = gloves[0].sharedMaterial.GetColor(BaseColorId);
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            Vector3 palm = transform.TransformPoint(palmOffset);
            float gain = 0f;
            foreach (var w in WoundSite.Active)
            {
                if (w == null) continue;
                float d = Vector3.Distance(palm, w.transform.position);
                if (d < touchRadius) gain += 0.25f + Mathf.Clamp01(w.FlowMlPerSec / 5f) * 1.5f;
                else if (d < w.SoakRadius + 0.03f) gain += 0.15f; // soaked uniform
            }
            if (gain > 0f) { bloodiness = Mathf.Clamp01(bloodiness + gain * dt); wetness = 1f; }
            else wetness = Mathf.MoveTowards(wetness, 0f, dt / 60f);
            Apply();
        }

        public void Apply()
        {
            if (gloves == null) return;
            _mpb ??= new MaterialPropertyBlock();
            var wet = new Color(0.30f, 0.015f, 0.012f);
            var dry = new Color(0.10f, 0.02f, 0.015f);
            Color blood = Color.Lerp(dry, wet, wetness);
            Color c = Color.Lerp(cleanColor, blood, bloodiness);
            float sm = Mathf.Lerp(0.2f, 0.8f, bloodiness * wetness);
            foreach (var g in gloves)
            {
                if (g == null) continue;
                g.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetFloat(SmoothId, sm);
                g.SetPropertyBlock(_mpb);
            }
        }
    }
}
