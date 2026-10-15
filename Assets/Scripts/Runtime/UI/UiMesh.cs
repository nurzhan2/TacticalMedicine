using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Procedural meshes and materials for the world-space cards (white hint panel, wrist display): rounded rectangles,
    /// rings and soft drop shadows built from vertex colours (no textures, crisp at any distance), plus TextMeshPro
    /// labels with the project's Cyrillic + Kazakh SDF font. All quads face −Z (towards the viewer).
    /// </summary>
    public static class UiMesh
    {
        static Shader _shader;
        static TMP_FontAsset _font;

        public static Shader CardShader => _shader != null ? _shader : (_shader = Shader.Find("FieldMedic/UiCard") ?? Shader.Find("Universal Render Pipeline/Unlit"));

        /// <summary>Liberation Sans SDF (Latin, Cyrillic, all Kazakh letters), built by the editor into Resources.</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.Load<TMP_FontAsset>("Fonts/BauyrSans SDF");
                if (_font == null) _font = TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        public static Material Mat(Color c, int queue, Texture tex = null)
        {
            var m = new Material(CardShader) { renderQueue = queue };
            m.SetColor("_Color", c);
            if (tex != null) m.SetTexture("_MainTex", tex);
            return m;
        }

        public static MeshRenderer Add(Transform parent, string name, Mesh mesh, Material m, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        static void Outline(List<Vector3> pts, float w, float h, float r, int seg, float grow = 0f)
        {
            float hw = w * 0.5f + grow, hh = h * 0.5f + grow, rr = Mathf.Max(0.0001f, r + grow);
            Vector2[] centres = { new(hw - rr, hh - rr), new(-hw + rr, hh - rr), new(-hw + rr, -hh + rr), new(hw - rr, -hh + rr) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(centres[c].x + Mathf.Cos(a) * rr, centres[c].y + Mathf.Sin(a) * rr, 0f));
                }
        }

        /// <summary>Filled rounded rectangle centred on the origin, optional vertical gradient (top → bottom).</summary>
        public static Mesh RoundedRect(float w, float h, float r, Color top, Color bottom, int seg = 6)
        {
            var pts = new List<Vector3>();
            Outline(pts, w, h, Mathf.Min(r, Mathf.Min(w, h) * 0.5f), seg);
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { Color.Lerp(top, bottom, 0.5f) };
            var uv = new List<Vector2> { new(0.5f, 0.5f) };
            var t = new List<int>();
            foreach (var p in pts)
            {
                v.Add(p);
                float k = Mathf.InverseLerp(h * 0.5f, -h * 0.5f, p.y);
                c.Add(Color.Lerp(top, bottom, k));
                uv.Add(new Vector2(p.x / w + 0.5f, p.y / h + 0.5f));
            }
            for (int i = 1; i <= pts.Count; i++) { t.Add(0); t.Add(i == pts.Count ? 1 : i + 1); t.Add(i); }
            return Build("RoundRect", v, c, uv, t);
        }

        /// <summary>Ring along a rounded rectangle (borders, checkbox frames).</summary>
        public static Mesh Ring(float w, float h, float r, float thickness, Color col, int seg = 6)
        {
            var inner = new List<Vector3>();
            var outer = new List<Vector3>();
            float ri = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            Outline(inner, w, h, ri, seg, -thickness * 0.5f);
            Outline(outer, w, h, ri, seg, thickness * 0.5f);
            return Strip("Ring", inner, outer, col, col);
        }

        /// <summary>Soft drop shadow: an opaque-ish core that fades to zero over `spread`.</summary>
        public static Mesh Shadow(float w, float h, float r, float spread, Color col, int seg = 6)
        {
            var inner = new List<Vector3>();
            var outer = new List<Vector3>();
            Outline(inner, w, h, r, seg);
            Outline(outer, w, h, r, seg, spread);
            var m = Strip("Shadow", inner, outer, col, new Color(col.r, col.g, col.b, 0f));
            // fill the core
            var v = new List<Vector3>(m.vertices) { Vector3.zero };
            var c = new List<Color>(m.colors) { col };
            var uv = new List<Vector2>(m.uv) { Vector2.zero };
            var t = new List<int>(m.triangles);
            int centre = v.Count - 1, n = inner.Count;
            for (int i = 0; i < n; i++) { t.Add(centre); t.Add((i + 1) % n * 2); t.Add(i * 2); }
            return Build("Shadow", v, c, uv, t);
        }

        static Mesh Strip(string name, List<Vector3> a, List<Vector3> b, Color ca, Color cb)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            int n = a.Count;
            for (int i = 0; i < n; i++) { v.Add(a[i]); v.Add(b[i]); c.Add(ca); c.Add(cb); uv.Add(Vector2.zero); uv.Add(Vector2.one); }
            for (int i = 0; i < n; i++)
            {
                int i0 = i * 2, i1 = (i + 1) % n * 2;
                t.Add(i0); t.Add(i1); t.Add(i0 + 1);
                t.Add(i1); t.Add(i1 + 1); t.Add(i0 + 1);
            }
            return Build(name, v, c, uv, t);
        }

        static Mesh Build(string name, List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetUVs(0, uv);
            // the outlines run counter-clockwise; flip to face −Z (the viewer)
            for (int i = 0; i < t.Count; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh _quad;
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "UiQuad" };
                _quad.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0) });
                _quad.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
                _quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
                _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        /// <summary>World-space TextMeshPro label, top-left anchored at `localPos`, wrapping inside `width`.</summary>
        public static TextMeshPro Text(Transform parent, string name, string text, Vector3 localPos, float capHeight, Color color, float width,
            int queue, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool bold = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = Font;
            // TextMeshPro (3D): 1 point ≈ 0.1 unit of em; caps are ~0.7 em
            t.fontSize = capHeight / 0.07f;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            var rt = t.rectTransform;
            rt.pivot = align == TextAlignmentOptions.TopRight ? new Vector2(1f, 1f) : align == TextAlignmentOptions.Top ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, 0.5f);
            go.transform.localPosition = localPos;
            t.text = text;
            var mr = t.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (t.fontMaterial != null) t.fontMaterial.renderQueue = queue; // instance: drawn above the card
            return t;
        }

        static readonly Dictionary<string, Texture2D> Icons = new();
        public static Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Icons.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("HintIcons/" + name);
            Icons[name] = t;
            return t;
        }

        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x);
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
