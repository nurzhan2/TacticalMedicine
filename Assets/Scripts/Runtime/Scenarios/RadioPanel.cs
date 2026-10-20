using System;
using System.Collections.Generic;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>What the radio can send right now: a title and the lines to choose from (one goes out per Y hold).</summary>
    public sealed class RadioReport
    {
        public string title;
        public readonly List<string> options = new List<string>();
        public readonly List<bool> enabled = new List<bool>();
        public int cursor;
        public Action<int> send;

        public RadioReport Add(string text, bool on = true) { options.Add(text); enabled.Add(on); return this; }
    }

    /// <summary>
    /// w10 push-to-talk on the controller (owner: "and this radio with the button" — the shoulder button was confusing).
    /// Hold left Y (desktop: hold M): the radio keys up and a panel with the report lines floats in front (same style as
    /// the kit tray). Choose a line with the right thumbstick up/down + trigger, or point at it and pull the trigger
    /// (or poke it); release Y = send the chosen line (default: the highlighted next line). Nothing to choose (a check-in
    /// call, report already sent): holding Y transmits the answer at once. The shoulder radio stays as a visual and
    /// still works by touch.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class RadioPanel : MonoBehaviour
    {
        public static RadioPanel Instance { get; private set; }

        public RadioSet radio;
        public Transform head;
        public bool IsOpen { get; private set; }
        public RadioReport Report { get; private set; }
        public int Cursor { get; private set; }
        public int Marked { get; private set; } = -1;
        public int SentCount { get; private set; }
        public int HoldCount { get; private set; }
        public readonly List<RoundButton> Buttons = new List<RoundButton>();
        public event Action<int> Sent;

        Transform _content, _bar;
        TextMesh _title, _foot;
        readonly List<TextMesh> _texts = new List<TextMesh>();
        bool _lastY, _stickArmed = true;
        readonly bool[] _lastTrig = new bool[2];
        float _yaw;
        const float W = 0.42f, RowH = 0.062f;

        public static RadioPanel Ensure(RadioSet radio)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RadioPanel");
            var p = go.AddComponent<RadioPanel>();
            p.radio = radio;
            p.head = radio != null ? radio.head : null;
            return p;
        }

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputLocks.RightStickCaptured = false;
        }

        void Update()
        {
            if (radio == null) radio = RadioSet.Instance;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            var left = HandRig.Find(Handedness.Left);
            bool y = left != null && left.SecondaryButton;
            if (y && !_lastY) Begin();
            else if (!y && _lastY) End();
            _lastY = y;
            if (!IsOpen) return;
            Follow(false);
            Navigate();
        }

        /// <summary>Y pressed: key up, show the lines (tests may call it directly).</summary>
        public void Begin()
        {
            if (radio == null) return;
            HoldCount++;
            Haptics.Play(HandRig.Find(Handedness.Left), Haptics.PressStart);
            UiAudio.Click(radio.transform.position);
            Report = radio.CurrentReport();
            if (Report == null || Report.options.Count == 0)
            {
                // nothing to choose (answer a check-in, "on the air"): the press itself transmits
                radio.PressToTalk();
                Show(null);
                return;
            }
            Cursor = Mathf.Clamp(Report.cursor, 0, Report.options.Count - 1);
            Marked = -1;
            Show(Report);
            Debug.Log($"[FMVR] radio panel: {Report.options.Count} lines, cursor {Cursor} — {Report.title}");
        }

        /// <summary>Y released: send the chosen (or highlighted) line.</summary>
        public void End()
        {
            if (!IsOpen) return;
            var r = Report;
            int i = Marked >= 0 ? Marked : Cursor;
            Hide();
            if (r == null || r.send == null || i < 0 || i >= r.options.Count || !r.enabled[i]) return;
            SentCount++;
            Debug.Log($"[FMVR] radio panel: sent line {i} — {r.options[i]}");
            r.send(i);
            Sent?.Invoke(i);
            Haptics.Play(HandRig.Find(Handedness.Left), Haptics.PressClick);
        }

        /// <summary>Choose a line (pointer click, poke, stick + trigger).</summary>
        public void Mark(int i)
        {
            if (Report == null || i < 0 || i >= Report.options.Count || !Report.enabled[i]) return;
            Marked = i;
            Cursor = i;
            Refresh();
        }

        public void MoveCursor(int delta)
        {
            if (Report == null || Report.options.Count == 0) return;
            int n = Report.options.Count;
            for (int k = 0; k < n; k++)
            {
                Cursor = (Cursor + delta + n) % n;
                if (Report.enabled[Cursor]) break;
            }
            Refresh();
        }

        void Navigate()
        {
            var right = HandRig.Find(Handedness.Right);
            if (right != null && Report != null)
            {
                float sy = right.Thumbstick.y;
                if (_stickArmed && Mathf.Abs(sy) > 0.6f) { MoveCursor(sy > 0f ? -1 : 1); _stickArmed = false; Haptics.Play(right, Haptics.Hover, 2f); }
                else if (Mathf.Abs(sy) < 0.3f) _stickArmed = true;
            }
            // trigger on either hand marks the highlighted line (a pointer click on a line marks that line instead)
            for (int h = 0; h < 2; h++)
            {
                var inp = HandRig.Find((Handedness)h);
                bool t = inp != null && inp.Trigger > 0.7f;
                if (t && !_lastTrig[h] && Report != null)
                {
                    bool pointing = false;
                    foreach (var b in Buttons) if (b != null && b.Hovered) pointing = true;
                    if (!pointing) Mark(Cursor);
                }
                _lastTrig[h] = t;
            }
        }

        // ------------------------------------------------------------------ view
        void Show(RadioReport r)
        {
            IsOpen = true;
            InputLocks.RightStickCaptured = r != null;
            for (int h = 0; h < 2; h++) { var inp = HandRig.Find((Handedness)h); _lastTrig[h] = inp != null && inp.Trigger > 0.7f; }
            Build(r);
            Follow(true);
        }

        void Hide()
        {
            IsOpen = false;
            Report = null;
            InputLocks.RightStickCaptured = false;
            if (_content != null) Destroy(_content.gameObject);
            _content = null;
            Buttons.Clear();
            _texts.Clear();
        }

        void Build(RadioReport r)
        {
            if (_content != null) Destroy(_content.gameObject);
            Buttons.Clear();
            _texts.Clear();
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);
            bool kz = GameSettings.KZ;
            int n = r != null ? r.options.Count : 0;
            float h = 0.1f + Mathf.Max(1, n) * RowH;
            var bg = UiMesh.Add(_content, "Card", UiMesh.RoundedRect(W, h, 0.025f, new Color(0.11f, 0.13f, 0.1f), new Color(0.07f, 0.08f, 0.07f)), UiMesh.Mat(Color.white, 3000), new Vector3(0f, -h / 2f + 0.045f, 0.004f));
            bg.name = "Card";
            UiMesh.Add(_content, "Rim", UiMesh.Ring(W, h, 0.025f, 0.005f, new Color(0.45f, 0.36f, 0.22f)), UiMesh.Mat(Color.white, 3001), new Vector3(0f, -h / 2f + 0.045f, 0.003f));
            _title = WorldUI.Text(_content, "Title", "", new Vector3(-W / 2 + 0.018f, 0.032f, 0f), 0.0135f, new Color(1f, 0.8f, 0.42f));
            if (r == null)
            {
                _title.text = GameSettings.T("РАЦИЯ  ·  передача…", "РАЦИЯ  ·  жіберілуде…");
                var line = radio != null ? radio.Subtitle : null;
                _texts.Add(WorldUI.Text(_content, "Line", WorldUI.Wrap(line ?? GameSettings.T("«На связи.»", "«Байланыстамын.»"), 40), new Vector3(-W / 2 + 0.02f, -0.012f, 0f), 0.0115f, new Color(0.93f, 0.91f, 0.84f)));
                return;
            }
            _title.text = WorldUI.Wrap(r.title, 46);
            _bar = UiMesh.Add(_content, "Cursor", UiMesh.RoundedRect(W - 0.02f, RowH - 0.008f, 0.012f, new Color(0.35f, 0.3f, 0.16f), new Color(0.3f, 0.26f, 0.14f)), UiMesh.Mat(Color.white, 3001), Vector3.zero).transform;
            for (int i = 0; i < n; i++)
            {
                float y = -0.02f - i * RowH;
                var b = RoundButton.Create(_content, "Line" + i, new Vector3(-W / 2 + 0.035f, y, 0f), 0.04f,
                    r.enabled[i] ? new Color(0.24f, 0.32f, 0.4f) : new Color(0.2f, 0.2f, 0.2f), (i + 1).ToString(), 0.014f);
                b.interactable = r.enabled[i];
                int k = i;
                b.Clicked += () => Mark(k);
                Buttons.Add(b);
                _texts.Add(WorldUI.Text(_content, "Text" + i, WorldUI.Wrap(r.options[i], 40), new Vector3(-W / 2 + 0.065f, y + 0.014f, 0f), 0.0112f,
                    r.enabled[i] ? new Color(0.93f, 0.91f, 0.84f) : new Color(0.55f, 0.55f, 0.52f)));
            }
            _foot = WorldUI.Text(_content, "Foot", GameSettings.T("стик ↑↓ / луч + КУРОК — выбрать   ·   отпусти Y — отправить",
                "стик ↑↓ / сәуле + ШҮРІППЕ — таңдау   ·   Y жібер — жіберу"), new Vector3(0f, -0.02f - n * RowH + 0.012f, 0f), 0.0085f,
                new Color(0.75f, 0.78f, 0.7f), TextAnchor.UpperCenter, TextAlignment.Center);
            Refresh();
        }

        void Refresh()
        {
            if (Report == null || _bar == null) return;
            _bar.localPosition = new Vector3(0f, -0.02f - Cursor * RowH, 0.002f);
            for (int i = 0; i < _texts.Count && i < Report.options.Count; i++)
                _texts[i].text = WorldUI.Wrap((i == Marked ? "» " : "") + Report.options[i], 40);
        }

        void Follow(bool snap)
        {
            if (head == null) return;
            float hy = head.eulerAngles.y;
            if (snap) _yaw = hy;
            float diff = Mathf.DeltaAngle(_yaw, hy);
            if (Mathf.Abs(diff) > 30f) _yaw += (Mathf.Abs(diff) - 30f) * Mathf.Sign(diff);
            var rot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 p = head.position + rot * new Vector3(-0.06f, -0.16f, 0.48f);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position));
        }
    }
}
