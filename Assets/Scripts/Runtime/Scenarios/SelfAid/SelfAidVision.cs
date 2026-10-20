using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// What blood loss does to the player's eyes (w9b), without post-processing (Quest budget): head-locked transparent
    /// quads (URP/Unlit, Always Included) in front of the eye — a grey wash that drains the colour, a dark tunnel that
    /// closes in, a hard dim past ~70 % of the lethal loss, the red flash of the hit, and the gold foil edge of the
    /// rescue blanket. No camera motion at all (comfort). <see cref="Comfort.ComfortFade"/> stays on top for fades.
    /// </summary>
    [DefaultExecutionOrder(890)]
    public sealed class SelfAidVision : MonoBehaviour
    {
        /// 0..1 of the lethal blood loss (set every frame by the director).
        public float bloodLoss;
        public float Grey { get; private set; }
        public float Tunnel { get; private set; }
        public float Dim { get; private set; }
        public float Red { get; private set; }
        public float Foil { get; private set; }

        Material _grey, _tunnel, _dim, _red, _foil;
        Renderer _rGrey, _rTunnel, _rDim, _rRed, _rFoil;
        float _flash, _foilFlash, _foilBase;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static SelfAidVision Attach(Transform head)
        {
            var go = new GameObject("SelfAidVision");
            go.transform.SetParent(head, false);
            return go.AddComponent<SelfAidVision>();
        }

        void Awake()
        {
            _grey = Mat("SA_Grey", null, 1);
            _tunnel = Mat("SA_Tunnel", Vignette(new Color32(0, 0, 0, 255), 0.25f, 0.95f), 2);
            _red = Mat("SA_Red", Vignette(new Color32(150, 6, 4, 255), 0.15f, 0.85f, 0.22f), 3);
            _foil = Mat("SA_Foil", FoilEdge(), 4);
            _dim = Mat("SA_Dim", null, 5);
            _rGrey = Quad("Grey", 0.074f, 0.5f, _grey);
            _rTunnel = Quad("Tunnel", 0.072f, 0.24f, _tunnel);
            _rRed = Quad("RedFlash", 0.07f, 0.26f, _red);
            _rFoil = Quad("BlanketFoil", 0.068f, 0.25f, _foil);
            _rDim = Quad("Dim", 0.066f, 0.5f, _dim);
            Apply();
        }

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

        static Material Mat(string name, Texture tex, int order)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
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
            m.renderQueue = (int)RenderQueue.Overlay + order;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor(BaseColor, new Color(0f, 0f, 0f, 0f));
            return m;
        }

        /// Colour with alpha rising from `inner` to `outer` radius (plus a flat `floor` alpha everywhere).
        static Texture2D Vignette(Color32 c, float inner, float outer, float floor = 0f)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SA_Vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Max(floor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, r)));
                px[y * n + x] = new Color32(c.r, c.g, c.b, (byte)(Mathf.Clamp01(a) * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// Crinkled gold/silver foil along the lower edge of the view (the blanket pulled up to the chin): opaque below
        /// v ≈ 0.3 with a ragged, crumpled top edge (the visible view spans v ≈ 0.23..0.77 at this distance).
        static Texture2D FoilEdge()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SA_Foil" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float top = 0.31f + 0.03f * Mathf.Sin(u * 17f) + 0.025f * (Mathf.PerlinNoise(u * 9f, 3.3f) - 0.5f) * 2f + 0.04f * Mathf.Abs(u - 0.5f);
                float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(top - 0.03f, top + 0.012f, v));
                float crinkle = Mathf.PerlinNoise(u * 14f, v * 14f) * 0.6f + Mathf.PerlinNoise(u * 41f + 7f, v * 37f) * 0.4f;
                float gold = Mathf.Clamp01(crinkle * 1.4f - 0.15f);
                var c = Color.Lerp(new Color(0.55f, 0.42f, 0.12f), new Color(1f, 0.86f, 0.45f), gold);
                if (crinkle > 0.72f) c = Color.Lerp(c, new Color(0.9f, 0.92f, 0.95f), 0.6f); // silver glints
                px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(edge) * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>The hit: a red flash that bleeds out of the view in ~1 s.</summary>
        public void Flash(float strength = 1f) => _flash = Mathf.Max(_flash, Mathf.Clamp01(strength));

        /// <summary>The blanket unfolds over you: strong foil edge for a moment, then a thin rim at the bottom.</summary>
        public void Blanket() { _foilFlash = 1f; _foilBase = 0.92f; _rise = 0f; }
        float _rise = 1f;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float f = Mathf.Clamp01(bloodLoss);
            Grey = Mathf.SmoothStep(0f, 0.62f, Mathf.InverseLerp(0.08f, 0.95f, f));
            Tunnel = Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.95f, f)) * 0.95f;
            Dim = f > 0.7f ? Mathf.SmoothStep(0f, 0.82f, Mathf.InverseLerp(0.7f, 1f, f)) : 0f;
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 0.9f);
            Red = _flash;
            _foilFlash = Mathf.MoveTowards(_foilFlash, 0f, dt * 0.6f);
            Foil = Mathf.Max(_foilBase, _foilFlash);
            // the blanket comes up from below the view (pulled up to the chin)
            _rise = Mathf.MoveTowards(_rise, 1f, dt * 1.6f);
            if (_rFoil != null) _rFoil.transform.localPosition = new Vector3(0f, Mathf.Lerp(-0.03f, 0f, Mathf.SmoothStep(0f, 1f, _rise)), 0.068f);
            Apply();
        }

        void Apply()
        {
            if (_rGrey == null) return;
            Set(_rGrey, _grey, new Color(0.33f, 0.32f, 0.31f, Grey * 0.85f));
            Set(_rTunnel, _tunnel, new Color(1f, 1f, 1f, Tunnel));
            Set(_rRed, _red, new Color(1f, 1f, 1f, Red * 0.85f));
            Set(_rFoil, _foil, new Color(1f, 1f, 1f, Foil));
            Set(_rDim, _dim, new Color(0f, 0f, 0f, Dim));
        }

        static void Set(Renderer r, Material m, Color c)
        {
            r.enabled = c.a > 0.004f;
            m.SetColor(BaseColor, c);
        }
    }
}
