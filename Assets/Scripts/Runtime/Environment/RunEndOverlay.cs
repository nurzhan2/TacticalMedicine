using FieldMedic.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Run-ending screen when the player is hit: the view fades to near black, a head-locked title + explanation
    /// (why it happened, what to do instead). A / R restarts the scene. The S5 debrief can replace this.
    /// </summary>
    public sealed class RunEndOverlay : MonoBehaviour
    {
        public static RunEndOverlay Current { get; private set; }
        public static bool Shown => Current != null;

        public Material fadeMaterial;   // FieldMedic/Particle (alpha) — assigned by the scene builder
        string _title, _body;
        float _t;
        Renderer _fade;
        TextMesh _text;

        public static RunEndOverlay Show(string title, string body)
        {
            if (Current != null) return Current;
            var cam = Camera.main;
            var go = new GameObject("RunEndOverlay");
            var o = go.AddComponent<RunEndOverlay>();
            o._title = title; o._body = body;
            var template = FindAnyObjectByType<RunEndTemplate>();
            if (template != null) o.fadeMaterial = template.fadeMaterial;
            if (cam != null)
            {
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
                o.Build();
            }
            Current = o;
            return o;
        }

        void Build()
        {
            if (fadeMaterial != null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                q.transform.localScale = new Vector3(3f, 3f, 1f);
                _fade = q.GetComponent<Renderer>();
                _fade.sharedMaterial = new Material(fadeMaterial);
            }
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(transform, false);
            _text = tgo.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tgo.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.fontSize = 48;
            _text.characterSize = 0.006f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(1f, 0.85f, 0.8f, 0f);
            _text.text = _title + "\n\n" + Wrap(_body, 46) + "\n\n" + "A / R — заново";
        }

        static string Wrap(string s, int width)
        {
            var words = s.Split(' ');
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var w in words)
            {
                if (line + w.Length > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w); line += w.Length;
            }
            return sb.ToString();
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(_t / 1.2f);
            if (_fade != null) _fade.sharedMaterial.SetColor("_TintColor", new Color(0.06f, 0f, 0f, a * 0.93f));
            if (_text != null) { var c = _text.color; c.a = Mathf.Clamp01((_t - 0.8f) / 0.8f); _text.color = c; }
            bool restart = (DesktopSimulator.XRActive && OVRInput.GetDown(OVRInput.Button.One)) ||
                           (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame);
            if (_t > 2f && restart)
            {
                Current = null;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void OnGUI()
        {
            if (DesktopSimulator.XRActive || _t < 0.8f) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.85f, 0.8f, Mathf.Clamp01((_t - 0.8f) / 0.8f));
            GUI.Label(new Rect(Screen.width * 0.15f, Screen.height * 0.3f, Screen.width * 0.7f, Screen.height * 0.4f),
                _title + "\n\n" + _body + "\n\nR — заново", st);
        }
    }
}
