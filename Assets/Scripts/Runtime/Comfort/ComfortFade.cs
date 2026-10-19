using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.Comfort
{
    /// <summary>
    /// Head-locked fade to black + comfort vignette (w9 helicopter evacuation). Two quads in front of the eye with a
    /// transparent URP/Unlit material (shader is in Always Included): <see cref="Fade"/> 0..1 = clear..black,
    /// <see cref="Vignette"/> 0..1 = no tunnel..strong tunnel (vehicle motion without artificial camera rotation).
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class ComfortFade : MonoBehaviour
    {
        static ComfortFade _instance;
        public static ComfortFade Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var cam = Camera.main;
                if (cam == null) return null;
                var go = new GameObject("ComfortFade");
                go.transform.SetParent(cam.transform, false);
                _instance = go.AddComponent<ComfortFade>();
                return _instance;
            }
        }

        /// <summary>Wanted values; the shown values ease toward them.</summary>
        public float Fade { get; set; }
        public float Vignette { get; set; }
        public float ShownFade => _fade;
        public float ShownVignette => _vig;
        public float fadeSpeed = 2.2f;

        Renderer _fadeQuad, _vigQuad;
        Material _fadeMat, _vigMat;
        float _fade, _vig;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            if (_instance == null) _instance = this;
            _fadeMat = Transparent("ComfortFade_Black", null);
            _vigMat = Transparent("ComfortFade_Vignette", VignetteTexture());
            _fadeQuad = Quad("Fade", 0.06f, 0.5f, _fadeMat);
            _vigQuad = Quad("Vignette", 0.065f, 0.22f, _vigMat);
            Apply();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        Renderer Quad(string name, float dist, float size, Material m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, dist);
            q.transform.localScale = new Vector3(size, size, 1f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }

        static Material Transparent(string name, Texture tex)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = name };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_ZTest", (float)CompareFunction.Always);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Overlay + 10;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor(BaseColor, new Color(0f, 0f, 0f, 0f));
            return m;
        }

        /// <summary>Black with alpha rising from the centre outward (clear inside ~55 % radius).</summary>
        static Texture2D VignetteTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ComfortVignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, r));
                px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, Mathf.Clamp01(Fade), dt * fadeSpeed);
            _vig = Mathf.MoveTowards(_vig, Mathf.Clamp01(Vignette), dt * 1.2f);
            Apply();
        }

        void Apply()
        {
            if (_fadeQuad == null) return;
            _fadeQuad.enabled = _fade > 0.002f;
            _vigQuad.enabled = _vig > 0.002f && _fade < 0.998f;
            _fadeMat.SetColor(BaseColor, new Color(0f, 0f, 0f, _fade));
            _vigMat.SetColor(BaseColor, new Color(1f, 1f, 1f, _vig));
        }

        /// <summary>Snap to the wanted values now (tests, scene start).</summary>
        public void Snap()
        {
            _fade = Mathf.Clamp01(Fade);
            _vig = Mathf.Clamp01(Vignette);
            Apply();
        }
    }
}
