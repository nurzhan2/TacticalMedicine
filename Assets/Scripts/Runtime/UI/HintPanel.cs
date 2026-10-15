using System.Collections.Generic;
using FieldMedic.Medical;
using TMPro;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// The WHITE hint panel of Training mode (GDD_V2 §3): a rounded white card with a soft shadow that fades and
    /// scales in above Erlan when the player comes within 2 m, billboards to the player, and never covers the wound.
    /// Shows the TCCC phase chip, "Step X of N", the step title with an (animated) pictogram, 2–3 short actions with
    /// checkboxes and the progress pills. Step done → green check + border, then the director advances it.
    /// Wrong action → the card turns yellow and states why. Readable at 1.2 m (caps 3 cm title / 2 cm text).
    /// The panel faces −Z of its transform (towards the viewer).
    /// </summary>
    public sealed class HintPanel : MonoBehaviour
    {
        public enum Mood { Normal, Done, Wrong }

        public const float Width = 0.6f;
        const float Pad = 0.032f, IconSize = 0.088f, CheckSize = 0.034f, RowH = 0.05f;
        const int QShadow = 3100, QCard = 3101, QDeco = 3102, QText = 3104;

        static readonly Color CardWhite = new(1f, 1f, 1f, 0.97f);
        static readonly Color CardWhiteLow = new(0.94f, 0.95f, 0.96f, 0.97f);
        static readonly Color CardYellow = new(1f, 0.9f, 0.45f, 0.98f);
        static readonly Color CardYellowLow = new(1f, 0.82f, 0.3f, 0.98f);
        static readonly Color Ink = new(0.11f, 0.13f, 0.15f);
        static readonly Color Muted = new(0.42f, 0.46f, 0.5f);
        static readonly Color Green = new(0.18f, 0.67f, 0.32f);
        static readonly Color Line = new(0.8f, 0.83f, 0.86f);
        static readonly Color Amber = new(0.55f, 0.33f, 0f);

        public Mood CurrentMood { get; private set; }
        public float Visibility { get; private set; }
        public bool Shown { get; private set; }
        public string Title => _title != null ? _title.text : "";
        public string Counter => _counter != null ? _counter.text : "";
        public string Reason => _reason != null && _reason.gameObject.activeSelf ? _reason.text : "";
        public bool[] Checks { get; private set; } = new bool[0];
        public float Height { get; private set; } = 0.4f;
        /// <summary>Card centre offset above its anchor (computed by <see cref="Follow"/>).</summary>
        public Vector3 LastAnchor { get; private set; }

        Transform _card, _checkPop;
        MeshRenderer _bg, _shadow, _border, _icon, _warnIcon, _checkIcon;
        Material _bgMat, _shadowMat, _borderMat, _iconMat, _warnMat, _checkMat, _chipMat, _lineMat;
        readonly List<Material> _fadeMats = new();
        readonly List<TextMeshPro> _texts = new();
        TextMeshPro _counter, _title, _chip, _reason;
        readonly List<(TextMeshPro text, MeshRenderer box, MeshRenderer tick, Material boxMat, Material tickMat)> _rows = new();
        readonly List<(MeshRenderer r, Material m)> _pills = new();
        float _anim, _moodT, _popT, _iconT;
        Color _bgTop = CardWhite, _bgBottom = CardWhiteLow;
        LessonStep _step;
        bool _kz;
        AudioSource _audio;

        public static HintPanel Create(string name = "TrainingPanel")
        {
            var go = new GameObject(name);
            var p = go.AddComponent<HintPanel>();
            p.Build();
            return p;
        }

        void Build()
        {
            _card = new GameObject("Card").transform;
            _card.SetParent(transform, false);
            _shadowMat = Track(UiMesh.Mat(new Color(0f, 0f, 0f, 1f), QShadow));
            _bgMat = Track(UiMesh.Mat(Color.white, QCard));
            _borderMat = Track(UiMesh.Mat(Line, QDeco));
            _iconMat = Track(UiMesh.Mat(Color.white, QDeco));
            _warnMat = Track(UiMesh.Mat(Color.white, QDeco, UiMesh.Icon("warn")));
            _checkMat = Track(UiMesh.Mat(Color.white, QText + 1, UiMesh.Icon("check")));
            _chipMat = Track(UiMesh.Mat(Color.white, QDeco));
            _lineMat = Track(UiMesh.Mat(Line, QDeco));
            _shadow = UiMesh.Add(_card, "Shadow", null, _shadowMat, new Vector3(0.008f, -0.012f, 0.004f));
            _bg = UiMesh.Add(_card, "Bg", null, _bgMat, Vector3.zero);
            _border = UiMesh.Add(_card, "Border", null, _borderMat, new Vector3(0f, 0f, -0.0005f));
            _icon = UiMesh.Add(_card, "Icon", UiMesh.Quad, _iconMat, Vector3.zero);
            _warnIcon = UiMesh.Add(_card, "Warn", UiMesh.Quad, _warnMat, Vector3.zero);
            _checkPop = new GameObject("DoneCheck").transform;
            _checkPop.SetParent(_card, false);
            _checkIcon = UiMesh.Add(_checkPop, "Check", UiMesh.Quad, _checkMat, Vector3.zero);
            _chip = Txt("Phase", "", 0.016f, Color.white, 0.2f, TextAlignmentOptions.Top, true);
            var chipBg = UiMesh.Add(_chip.transform, "ChipBg", null, _chipMat, Vector3.zero);
            chipBg.name = "ChipBg";
            _counter = Txt("Counter", "", 0.017f, Muted, 0.25f, TextAlignmentOptions.TopRight);
            _title = Txt("Title", "", 0.03f, Ink, Width - Pad * 2 - IconSize - 0.02f, TextAlignmentOptions.TopLeft, true);
            _reason = Txt("Reason", "", 0.019f, Amber, Width - Pad * 2 - 0.05f, TextAlignmentOptions.TopLeft, true);
            UiMesh.Add(_card, "Divider", UiMesh.Quad, _lineMat, Vector3.zero).name = "Divider";
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0.7f;
            _audio.volume = 0.55f;
            _audio.playOnAwake = false;
            SetVisibleImmediate(false);
        }

        Material Track(Material m) { _fadeMats.Add(m); return m; }

        TextMeshPro Txt(string name, string text, float cap, Color c, float width, TextAlignmentOptions a, bool bold = false)
        {
            var t = UiMesh.Text(_card, name, text, Vector3.zero, cap, c, width, QText, a, bold);
            _texts.Add(t);
            return t;
        }

        // ------------------------------------------------------------------ content
        public void SetStep(LessonStep step, int number, int count, bool kz)
        {
            _step = step;
            _kz = kz;
            CurrentMood = Mood.Normal;
            _moodT = 0f;
            _popT = -1f;
            if (step == null) return;
            _title.text = step.Text(kz);
            _counter.text = (kz ? "Қадам " : "Шаг ") + number + (kz ? " / " : " из ") + count;
            _chip.text = step.phase switch
            {
                "CUF" => kz ? "ОҚ АСТЫНДА" : "ПОД ОГНЁМ",
                "TACEVAC" => kz ? "ЭВАКУАЦИЯ" : "ЭВАКУАЦИЯ",
                _ => kz ? "БАСПАНАДА" : "В УКРЫТИИ",
            };
            _chipMat.SetColor("_Color", step.phase switch { "CUF" => new Color(0.78f, 0.17f, 0.13f), "TACEVAC" => new Color(0.2f, 0.42f, 0.75f), _ => new Color(0.24f, 0.5f, 0.28f) });
            var tex = UiMesh.Icon(step.icon);
            _iconMat.SetTexture("_MainTex", tex);
            _icon.enabled = tex != null;
            // rows
            while (_rows.Count < step.actions.Count) AddRow();
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i < step.actions.Count;
                _rows[i].text.gameObject.SetActive(on);
                _rows[i].box.gameObject.SetActive(on);
                if (on) _rows[i].text.text = step.actions[i].Text(kz);
            }
            Checks = new bool[step.actions.Count];
            SetPills(number - 1, count);
            SetReason(null);
            Layout();
        }

        void AddRow()
        {
            int i = _rows.Count;
            var t = Txt("Action" + i, "", 0.0205f, Ink, Width - Pad * 2 - CheckSize - 0.03f, TextAlignmentOptions.TopLeft);
            var boxMat = Track(UiMesh.Mat(new Color(0.55f, 0.6f, 0.65f), QDeco));
            var box = UiMesh.Add(_card, "Box" + i, UiMesh.Ring(CheckSize, CheckSize, 0.008f, 0.004f, Color.white), boxMat, Vector3.zero);
            var tickMat = Track(UiMesh.Mat(Color.white, QDeco + 1, UiMesh.Icon("check")));
            var tick = UiMesh.Add(box.transform, "Tick", UiMesh.Quad, tickMat, new Vector3(0f, 0f, -0.001f));
            tick.transform.localScale = Vector3.one * (CheckSize * 1.05f);
            _rows.Add((t, box, tick, boxMat, tickMat));
        }

        void SetPills(int current, int count)
        {
            foreach (var p in _pills) if (p.r != null) Destroy(p.r.gameObject);
            _pills.Clear();
            for (int i = 0; i < count; i++)
            {
                var m = Track(UiMesh.Mat(i < current ? Green : i == current ? Ink : new Color(0.82f, 0.85f, 0.88f), QDeco));
                var r = UiMesh.Add(_card, "Pill" + i, null, m, Vector3.zero);
                _pills.Add((r, m));
            }
        }

        public void SetChecks(bool[] states)
        {
            if (states == null || CurrentMood == Mood.Done) return;
            for (int i = 0; i < Checks.Length && i < states.Length; i++)
            {
                if (states[i] && !Checks[i]) Play("hint_show", 0.5f);
                Checks[i] = states[i];
            }
        }

        public void SetDone()
        {
            if (CurrentMood == Mood.Done) return;
            CurrentMood = Mood.Done;
            _moodT = 0f;
            _popT = 0f;
            for (int i = 0; i < Checks.Length; i++) Checks[i] = true;
            SetReason(null);
            Play("hint_done", 1f);
        }

        /// <summary>Yellow card with the reason (null → back to white).</summary>
        public void SetWrong(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                if (CurrentMood == Mood.Wrong) { CurrentMood = Mood.Normal; _moodT = 0f; }
                SetReason(null);
                return;
            }
            if (CurrentMood == Mood.Done) return;
            bool fresh = CurrentMood != Mood.Wrong || _reason.text != reason;
            CurrentMood = Mood.Wrong;
            if (fresh) { _moodT = 0f; Play("hint_wrong", 1f); }
            SetReason(reason);
        }

        void SetReason(string r)
        {
            bool on = !string.IsNullOrEmpty(r);
            bool changed = on != _reason.gameObject.activeSelf || (on && _reason.text != r);
            _reason.gameObject.SetActive(on);
            _warnIcon.gameObject.SetActive(on);
            if (on) _reason.text = r;
            if (changed && _step != null) Layout();
        }

        void Play(string clip, float vol)
        {
            if (UiAudio.Muted || _audio == null || Visibility < 0.2f) return;
            var c = Resources.Load<AudioClip>("UI/" + clip);
            if (c != null) _audio.PlayOneShot(c, vol);
        }

        // ------------------------------------------------------------------ layout
        void Layout()
        {
            float x0 = -Width * 0.5f + Pad;
            float y = 0f;
            // header: phase chip (left) + step counter (right)
            float chipW = 0.13f + _chip.text.Length * 0.0075f;
            _chip.rectTransform.sizeDelta = new Vector2(chipW, 0.03f);
            _chip.transform.localPosition = new Vector3(x0 + chipW * 0.5f, y - 0.006f, -0.002f);
            var chipBg = _chip.transform.Find("ChipBg");
            chipBg.localPosition = new Vector3(0f, -0.0105f, 0.0012f);
            chipBg.GetComponent<MeshFilter>().sharedMesh = UiMesh.RoundedRect(chipW, 0.028f, 0.014f, Color.white, Color.white);
            _counter.transform.localPosition = new Vector3(Width * 0.5f - Pad, y - 0.008f, -0.002f);
            y -= 0.046f;
            // icon + title
            _icon.transform.localPosition = new Vector3(x0 + IconSize * 0.5f, y - IconSize * 0.5f, -0.001f);
            _icon.transform.localScale = Vector3.one * IconSize;
            _title.transform.localPosition = new Vector3(x0 + IconSize + 0.02f, y - 0.008f, -0.002f);
            _title.ForceMeshUpdate();
            float titleH = Mathf.Max(IconSize, _title.preferredHeight + 0.01f);
            y -= titleH + 0.012f;
            // reason (wrong action)
            if (_reason.gameObject.activeSelf)
            {
                _warnIcon.transform.localPosition = new Vector3(x0 + 0.017f, y - 0.018f, -0.001f);
                _warnIcon.transform.localScale = Vector3.one * 0.034f;
                _reason.transform.localPosition = new Vector3(x0 + 0.045f, y - 0.004f, -0.002f);
                _reason.ForceMeshUpdate();
                y -= Mathf.Max(0.04f, _reason.preferredHeight + 0.012f);
            }
            // divider
            var div = _card.Find("Divider");
            div.localPosition = new Vector3(0f, y, -0.0008f);
            div.localScale = new Vector3(Width - Pad * 2f, 0.0018f, 1f);
            y -= 0.016f;
            // actions
            int n = _step != null ? _step.actions.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var row = _rows[i];
                row.box.transform.localPosition = new Vector3(x0 + CheckSize * 0.5f, y - CheckSize * 0.5f, -0.001f);
                row.text.transform.localPosition = new Vector3(x0 + CheckSize + 0.018f, y - 0.006f, -0.002f);
                row.text.ForceMeshUpdate();
                y -= Mathf.Max(RowH, row.text.preferredHeight + 0.018f);
            }
            // progress pills
            y -= 0.006f;
            int count = _pills.Count;
            if (count > 0)
            {
                float gap = 0.006f, pw = Mathf.Min(0.04f, (Width - Pad * 2f - gap * (count - 1)) / count);
                float total = pw * count + gap * (count - 1);
                var pill = UiMesh.RoundedRect(pw, 0.009f, 0.0045f, Color.white, Color.white);
                for (int i = 0; i < count; i++)
                {
                    _pills[i].r.GetComponent<MeshFilter>().sharedMesh = pill;
                    _pills[i].r.transform.localPosition = new Vector3(-total * 0.5f + pw * 0.5f + i * (pw + gap), y - 0.006f, -0.001f);
                }
                y -= 0.02f;
            }
            y -= Pad * 0.6f;
            Height = -y + 0.012f;
            // card background centred: shift contents so the card is centred on the transform
            float top = Height * 0.5f - 0.012f;
            // everything above was laid out from y = 0 at the top edge: shift it so the card is centred
            foreach (Transform c in _card)
            {
                if (c == _bg.transform || c == _shadow.transform || c == _border.transform || c == _checkPop) continue;
                c.localPosition = new Vector3(c.localPosition.x, c.localPosition.y + top, c.localPosition.z);
            }
            _bg.GetComponent<MeshFilter>().sharedMesh = UiMesh.RoundedRect(Width, Height, 0.04f, Color.white, Color.white);
            _shadow.GetComponent<MeshFilter>().sharedMesh = UiMesh.Shadow(Width - 0.02f, Height - 0.02f, 0.04f, 0.045f, new Color(0f, 0f, 0f, 0.38f));
            _border.GetComponent<MeshFilter>().sharedMesh = UiMesh.Ring(Width, Height, 0.04f, 0.006f, Color.white);
            _checkPop.localPosition = new Vector3(Width * 0.5f - 0.03f, Height * 0.5f - 0.03f, -0.006f);
            ApplyColors();
        }

        void ApplyColors()
        {
            Color top = CurrentMood == Mood.Wrong ? CardYellow : CardWhite;
            Color bottom = CurrentMood == Mood.Wrong ? CardYellowLow : CardWhiteLow;
            _bgMat.SetColor("_Color", Color.white);
            _bgTop = top; _bgBottom = bottom;
            var bgMesh = _bg.GetComponent<MeshFilter>().sharedMesh;
            if (bgMesh != null)
            {
                var v = bgMesh.vertices;
                var c = new Color[v.Length];
                for (int i = 0; i < v.Length; i++) c[i] = Color.Lerp(top, bottom, Mathf.InverseLerp(Height * 0.5f, -Height * 0.5f, v[i].y));
                bgMesh.colors = c;
            }
            _borderMat.SetColor("_Color", CurrentMood == Mood.Done ? Green : CurrentMood == Mood.Wrong ? new Color(0.85f, 0.55f, 0.05f) : Line);
        }

        // ------------------------------------------------------------------ placement
        /// <summary>
        /// Place the card above `anchor` (Erlan's chest), a little beyond him from the viewer, facing the viewer (yaw
        /// only), and raised until the line of sight from the eyes to `keepVisible` (the wound) passes under it.
        /// </summary>
        public void Follow(Vector3 anchor, Vector3 viewer, Vector3 keepVisible, float dt)
        {
            Vector3 away = anchor - viewer; away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Vector3 target = anchor + away * 0.22f + Vector3.up * (0.62f + Height * 0.5f);
            // keep the wound visible: the ray eye→wound must pass below the card's bottom edge
            for (int k = 0; k < 12; k++)
            {
                Vector3 bottom = target - Vector3.up * (Height * 0.5f + 0.04f);
                Vector3 toW = keepVisible - viewer, toB = bottom - viewer;
                float dw = new Vector2(toW.x, toW.z).magnitude, db = new Vector2(toB.x, toB.z).magnitude;
                if (db < 1e-3f || dw < 1e-3f) break;
                float slopeW = toW.y / dw, slopeB = toB.y / db;
                if (slopeB > slopeW + 0.12f || db > dw + 0.4f) break;
                target += Vector3.up * 0.05f;
            }
            LastAnchor = target;
            transform.position = dt <= 0f || Visibility < 0.01f ? target : Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-dt * 6f));
            Vector3 face = transform.position - viewer; face.y = 0f;
            if (face.sqrMagnitude > 1e-4f)
            {
                var want = Quaternion.LookRotation(face.normalized, Vector3.up);
                // slight tilt back so the card faces the eyes when the player kneels over him
                float pitch = Mathf.Clamp(Mathf.Atan2(viewer.y - transform.position.y, face.magnitude) * Mathf.Rad2Deg, -25f, 25f);
                want *= Quaternion.Euler(-pitch * 0.7f, 0f, 0f);
                transform.rotation = dt <= 0f || Visibility < 0.01f ? want : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-dt * 8f));
            }
        }

        public void SetVisible(bool on) => Shown = on;

        public void SetVisibleImmediate(bool on)
        {
            Shown = on;
            _anim = on ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _anim = Mathf.MoveTowards(_anim, Shown ? 1f : 0f, dt / (Shown ? 0.38f : 0.25f));
            _moodT += dt;
            _iconT += dt;
            if (_popT >= 0f) _popT += dt;
            Apply();
        }

        void Apply()
        {
            Visibility = Shown ? Mathf.SmoothStep(0f, 1f, _anim) : _anim * _anim;
            float s = Shown ? Mathf.Lerp(0.82f, 1f, UiMesh.EaseOutBack(_anim)) : Mathf.Lerp(0.9f, 1f, _anim);
            if (_card != null) _card.localScale = Vector3.one * s;
            foreach (var m in _fadeMats) if (m != null) m.SetFloat("_Alpha", Visibility);
            foreach (var t in _texts) if (t != null) t.alpha = Visibility;
            bool visible = Visibility > 0.002f;
            if (_card != null && _card.gameObject.activeSelf != visible) _card.gameObject.SetActive(visible);
            if (!visible) return;
            // icon: gentle bob + breathe
            if (_icon != null)
            {
                float k = 1f + 0.05f * Mathf.Sin(_iconT * 3.2f);
                _icon.transform.localScale = Vector3.one * IconSize * k;
            }
            // checkboxes
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i < Checks.Length && Checks[i];
                _rows[i].tick.enabled = on;
                _rows[i].boxMat.SetColor("_Color", on ? Green : new Color(0.55f, 0.6f, 0.65f));
                _rows[i].text.color = on ? new Color(0.3f, 0.5f, 0.35f) : Ink;
            }
            // big green check pops on done
            bool done = CurrentMood == Mood.Done && _popT >= 0f;
            _checkPop.gameObject.SetActive(done);
            if (done) _checkPop.localScale = Vector3.one * 0.075f * UiMesh.EaseOutBack(Mathf.Clamp01(_popT / 0.35f));
            // wrong: a short shake
            if (CurrentMood == Mood.Wrong && _moodT < 0.45f) _card.localPosition = new Vector3(Mathf.Sin(_moodT * 60f) * 0.006f * (1f - _moodT / 0.45f), 0f, 0f);
            else _card.localPosition = Vector3.zero;
            if (_bgTop != (CurrentMood == Mood.Wrong ? CardYellow : CardWhite)) ApplyColors();
            _borderMat.SetColor("_Color", CurrentMood == Mood.Done ? Green : CurrentMood == Mood.Wrong ? new Color(0.85f, 0.55f, 0.05f) : Line);
        }
    }
}
