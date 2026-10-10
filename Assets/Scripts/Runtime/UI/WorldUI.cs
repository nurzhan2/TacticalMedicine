using System.Text;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Small world-space UI kit (no canvas): TextMesh labels with the built-in dynamic font (Cyrillic and Kazakh
    /// letters come from the OS font fallback on Quest), flat panels and shared unlit materials.
    /// </summary>
    public static class WorldUI
    {
        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        const int FontSize = 72;

        /// <summary>Text whose capital letters are about `height` metres tall.</summary>
        public static TextMesh Text(Transform parent, string name, string text, Vector3 localPos, float height, Color color,
            TextAnchor anchor = TextAnchor.UpperLeft, TextAlignment align = TextAlignment.Left)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMesh>();
            t.font = Font;
            t.fontSize = FontSize;
            t.characterSize = height * 10f / FontSize * 1.45f;
            t.anchor = anchor;
            t.alignment = align;
            t.color = color;
            t.richText = true;
            t.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }

        /// <summary>Greedy word wrap to `width` characters per line.</summary>
        public static string Wrap(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var para in s.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                int line = 0;
                foreach (var w in para.Split(' '))
                {
                    if (w.Length == 0) continue;
                    if (line > 0 && line + 1 + w.Length > width) { sb.Append('\n'); line = 0; }
                    else if (line > 0) { sb.Append(' '); line++; }
                    sb.Append(w); line += w.Length;
                }
            }
            return sb.ToString();
        }

        static Shader _unlit;
        public static Material Unlit(Color c)
        {
            if (_unlit == null) _unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(_unlit);
            m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        public static Material Lit(Color c, float smoothness = 0.2f)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        public static GameObject Quad(Transform parent, string name, Vector3 localPos, Vector2 size, Material m, float z = 0f)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPos + new Vector3(0f, 0f, z);
            q.transform.localRotation = Quaternion.identity;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }

        public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            Object.Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = m;
            return b;
        }

        public static GameObject Disc(Transform parent, string name, Vector3 localPos, float diameter, float thickness, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = name;
            Object.Destroy(c.GetComponent<Collider>());
            c.transform.SetParent(parent, false);
            c.transform.localPosition = localPos;
            // cylinder axis = local Y → turn it to face the panel normal (-Z of the panel = towards the viewer)
            c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            c.transform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
            var r = c.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return c;
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
