using System;
using System.Collections.Generic;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Big round animated button (owner request): a dark metal bezel, a coloured cap that breathes softly, pulses
    /// and glows on hover and physically sinks when pressed. Pressed by a fingertip (poke) or by a pointer
    /// (controller ray + trigger, desktop crosshair/mouse + click/Enter — see <see cref="BoardPointer"/>).
    /// Local frame: +Z points into the panel, the viewer is on the -Z side.
    /// </summary>
    public sealed class RoundButton : MonoBehaviour
    {
        public static readonly List<RoundButton> All = new List<RoundButton>();

        public float diameter = 0.12f;
        public Color color = new Color(0.42f, 0.45f, 0.24f);
        public Color glowColor = new Color(1f, 0.78f, 0.35f);
        public bool interactable = true;
        public float pokeTravel = 0.012f;
        public event Action Clicked;

        public TextMesh Label { get; private set; }
        public TextMesh Caption { get; private set; }
        public bool Hovered => Time.unscaledTime - _hoverAt < 0.08f;
        public int ClickCount { get; private set; }

        Transform _cap, _glow;
        Material _capMat, _glowMat;
        float _hoverAt = -1f, _hoverAmt, _press, _pokeDepth, _clickCooldown, _phase;
        bool _pokeArmed = true;

        public static RoundButton Create(Transform parent, string name, Vector3 localPos, float diameter, Color color,
            string label, float labelHeight, string caption = null, float captionHeight = 0.022f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            var b = go.AddComponent<RoundButton>();
            b.diameter = diameter;
            b.color = color;
            b.Build(label, labelHeight, caption, captionHeight);
            return b;
        }

        void Build(string label, float labelHeight, string caption, float captionHeight)
        {
            _phase = UnityEngine.Random.value * 6f;
            _glowMat = WorldUI.Unlit(new Color(glowColor.r, glowColor.g, glowColor.b) * 0.15f);
            _glow = WorldUI.Disc(transform, "Glow", new Vector3(0f, 0f, 0.004f), diameter * 1.16f, 0.004f, _glowMat).transform;
            WorldUI.Disc(transform, "Bezel", new Vector3(0f, 0f, -0.004f), diameter * 1.06f, 0.012f, WorldUI.Lit(new Color(0.09f, 0.09f, 0.08f), 0.55f));
            _capMat = WorldUI.Unlit(color);
            _cap = new GameObject("Cap").transform;
            _cap.SetParent(transform, false);
            WorldUI.Disc(_cap, "CapDisc", Vector3.zero, diameter * 0.88f, 0.016f, _capMat);
            WorldUI.Disc(_cap, "CapRim", new Vector3(0f, 0f, 0.002f), diameter * 0.93f, 0.012f, WorldUI.Unlit(color * 0.55f));
            _cap.localPosition = new Vector3(0f, 0f, -0.016f);
            if (!string.IsNullOrEmpty(label))
                Label = WorldUI.Text(_cap, "Label", label, new Vector3(0f, 0f, -0.0095f), labelHeight, new Color(0.96f, 0.94f, 0.86f),
                    TextAnchor.MiddleCenter, TextAlignment.Center);
            if (!string.IsNullOrEmpty(caption))
                Caption = WorldUI.Text(transform, "Caption", caption, new Vector3(0f, -diameter * 0.62f, -0.006f), captionHeight,
                    new Color(0.88f, 0.86f, 0.78f), TextAnchor.UpperCenter, TextAlignment.Center);
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public Vector3 Normal => -transform.forward; // towards the viewer
        public float Radius => diameter * 0.53f * transform.lossyScale.x;

        /// <summary>Ray test against the button face. Returns distance or -1.</summary>
        public float Raycast(Ray ray)
        {
            if (!interactable || !isActiveAndEnabled) return -1f;
            var plane = new Plane(Normal, transform.position);
            if (!plane.Raycast(ray, out float d) || d < 0f) return -1f;
            return Vector3.Distance(ray.GetPoint(d), transform.position) <= Radius * 1.08f ? d : -1f;
        }

        public void Hover() => _hoverAt = Time.unscaledTime;

        public void Click()
        {
            if (!interactable || _clickCooldown > 0f) return;
            _clickCooldown = 0.35f;
            _press = 1f;
            ClickCount++;
            UiAudio.Click(transform.position);
            Clicked?.Invoke();
        }

        public void SetLabel(string text) { if (Label != null) Label.text = text; }
        public void SetCaption(string text) { if (Caption != null) Caption.text = text; }

        public void SetColor(Color c)
        {
            color = c;
            if (_capMat != null) { _capMat.SetColor("_BaseColor", c); _capMat.color = c; }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _clickCooldown = Mathf.Max(0f, _clickCooldown - dt);
            Poke();
            _hoverAmt = Mathf.MoveTowards(_hoverAmt, Hovered || _pokeDepth > 0f ? 1f : 0f, dt * 6f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 4f);

            // animation: breathing idle, pulse on hover, depth on press
            float t = Time.unscaledTime * 2.2f + _phase;
            float idle = 1f + 0.012f * Mathf.Sin(t);
            float pulse = 1f + _hoverAmt * (0.05f + 0.03f * Mathf.Sin(Time.unscaledTime * 7f));
            float depth = Mathf.Max(_pokeDepth, _press * pokeTravel);
            _cap.localScale = Vector3.one * (idle * pulse);
            _cap.localPosition = new Vector3(0f, 0f, -0.016f + depth);
            float g = interactable ? 0.12f + _hoverAmt * (0.65f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f)) + _press * 0.8f : 0.03f;
            var gc = glowColor * g;
            _glowMat.SetColor("_BaseColor", gc);
            _glow.localScale = new Vector3(diameter * (1.16f + 0.08f * _hoverAmt), 0.002f, diameter * (1.16f + 0.08f * _hoverAmt));
            var cc = interactable ? Color.Lerp(color, color * 1.35f + new Color(0.05f, 0.05f, 0.03f), _hoverAmt) : color * 0.45f;
            _capMat.SetColor("_BaseColor", cc);
        }

        /// <summary>Fingertip press: the cap follows the finger; past 60% of the travel it clicks once.</summary>
        void Poke()
        {
            float best = 0f;
            foreach (var h in HandGrabber.All)
            {
                if (h == null || h.Held != null) continue;
                Vector3 local = transform.InverseTransformPoint(h.FingertipPosition);
                float r = new Vector2(local.x, local.y).magnitude * transform.lossyScale.x;
                if (r > Radius) continue;
                float depth = (local.z * transform.lossyScale.z) + 0.016f + 0.0095f; // 0 at the cap surface
                if (depth > 0f && depth < 0.05f) best = Mathf.Max(best, depth);
            }
            _pokeDepth = Mathf.Min(best, pokeTravel);
            if (_pokeDepth >= pokeTravel * 0.6f) { if (_pokeArmed) { _pokeArmed = false; Click(); } }
            else if (_pokeDepth <= pokeTravel * 0.2f) _pokeArmed = true;
        }
    }

    /// <summary>Procedural UI click (no asset needed): a short damped "tok".</summary>
    public static class UiAudio
    {
        static AudioClip _click;
        public static bool Muted;

        public static void Click(Vector3 at)
        {
            if (Muted) return;
            if (_click == null)
            {
                const int sr = 22050;
                int n = sr / 20;
                var d = new float[n];
                var rng = new System.Random(3);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)sr;
                    d[i] = (Mathf.Sin(2f * Mathf.PI * 820f * t) * 0.6f + (float)(rng.NextDouble() * 2 - 1) * 0.25f) * Mathf.Exp(-t * 90f);
                }
                _click = AudioClip.Create("ui_click", n, 1, sr, false);
                _click.SetData(d, 0);
            }
            AudioSource.PlayClipAtPoint(_click, at, 0.6f);
        }
    }
}
