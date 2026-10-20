using System.Collections.Generic;
using FieldMedic.Input;
using TMPro;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>The controller inputs a hint can name (Quest 3S Touch Plus: X/Y left, A/B right, grip, trigger, stick).</summary>
    public enum ControlIcon { X, Y, A, B, Grip, Trigger, Stick, Hand }

    /// <summary>
    /// w10 one consistent step line (owner: "nothing is clear, what goes where"): the current step in short words + the
    /// controller buttons it needs, RU/KZ, in two places — a card on the left wrist (always there, read it by looking at the
    /// watch) and a line at the bottom of the view for the first seconds of every new step. Icons are drawn as the
    /// physical buttons (round X/Y/A/B, GRIP / КУРОК pills) and a small badge pulses at the hand that has that button.
    /// Fed by the training lessons, the controls tutorial, the self-aid drill and the scenarios (controls only, no
    /// medical answers in Combat/Exam).
    /// </summary>
    public sealed class StepGuide : MonoBehaviour
    {
        public static StepGuide Instance { get; private set; }

        public Transform head;
        public Transform wrist;
        public float bannerSeconds = 7f;
        [Tooltip("Banner distance in front of the eyes and how far below the view centre (m).")]
        public float bannerDistance = 0.62f, bannerDrop = 0.2f;

        public string Ru { get; private set; } = "";
        public string Kz { get; private set; } = "";
        public string Text => GameSettings.KZ && !string.IsNullOrEmpty(Kz) ? Kz : Ru;
        public ControlIcon[] Icons { get; private set; } = new ControlIcon[0];
        public float ShownAt { get; private set; } = -100f;
        public bool BannerVisible => _banner != null && _banner.gameObject.activeSelf && _bannerAlpha > 0.05f;
        public int Version { get; private set; }

        Transform _wristCard, _banner, _wristIcons, _bannerIcons;
        TextMeshPro _wristText, _bannerText;
        Material _wristBg, _bannerBg;
        readonly List<Material> _bannerMats = new List<Material>();
        readonly List<Transform> _badges = new List<Transform>();
        readonly List<ControlIcon> _badgeIcons = new();
        float _bannerAlpha, _yaw, _drop = 0.2f;
        bool _kz;

        const int QCard = 3110, QText = 3112;

        public static StepGuide Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("StepGuide");
            return go.AddComponent<StepGuide>();
        }

        /// <summary>Show a step (same text again = no new banner).</summary>
        public static void Set(string ru, string kz, params ControlIcon[] icons)
        {
            var g = Ensure();
            if (g.Ru == ru && g.Kz == kz && SameIcons(g.Icons, icons)) return;
            g.Ru = ru ?? "";
            g.Kz = kz ?? "";
            g.Icons = icons ?? new ControlIcon[0];
            g.ShownAt = Time.time;
            g.Version++;
            g.Rebuild();
            if (!string.IsNullOrEmpty(ru)) Debug.Log("[FMVR] step guide: " + ru + " [" + string.Join(" ", g.Icons) + "]");
        }

        public static void Clear() { if (Instance != null) Set("", "", new ControlIcon[0]); }

        static bool SameIcons(ControlIcon[] a, ControlIcon[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Icon caption (what is written on it).</summary>
        public static string Caption(ControlIcon i, bool kz) => i switch
        {
            ControlIcon.X => "X", ControlIcon.Y => "Y", ControlIcon.A => "A", ControlIcon.B => "B",
            ControlIcon.Grip => "GRIP",
            ControlIcon.Trigger => kz ? "ШҮРІППЕ" : "КУРОК",
            ControlIcon.Stick => kz ? "СТИК" : "СТИК",
            _ => kz ? "ҚОЛ" : "РУКА",
        };

        /// <summary>Which hand has the button (badge at that hand): X/Y left, A/B right, the rest — both.</summary>
        public static Handedness? HandOf(ControlIcon i) => i switch
        {
            ControlIcon.X or ControlIcon.Y => Handedness.Left,
            ControlIcon.A or ControlIcon.B => Handedness.Right,
            _ => null,
        };

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            Build();
            Rebuild();
        }

        void Build()
        {
            // wrist card: on the forearm side of the watch, face up like the watch
            _wristCard = new GameObject("WristStepCard").transform;
            _wristBg = UiMesh.Mat(Color.white, QCard);
            UiMesh.Add(_wristCard, "Bg", UiMesh.RoundedRect(0.125f, 0.056f, 0.008f, new Color(0.96f, 0.96f, 0.94f, 0.96f), new Color(0.88f, 0.9f, 0.9f, 0.96f)), _wristBg, new Vector3(0f, 0f, 0.001f));
            _wristText = UiMesh.Text(_wristCard, "Text", "", new Vector3(-0.058f, 0.013f, 0f), 0.0042f, new Color(0.1f, 0.12f, 0.14f), 0.116f, QText);
            _wristIcons = new GameObject("Icons").transform;
            _wristIcons.SetParent(_wristCard, false);
            _wristIcons.localPosition = new Vector3(-0.056f, 0.02f, -0.0005f);

            // banner at the bottom of the view
            _banner = new GameObject("StepBanner").transform;
            _bannerBg = UiMesh.Mat(Color.white, QCard);
            _bannerBg.SetFloat("_ZTest", 8f); // always readable (never buried in a wall or in Erlan)
            UiMesh.Add(_banner, "Bg", UiMesh.RoundedRect(0.56f, 0.085f, 0.03f, new Color(0.97f, 0.97f, 0.95f, 0.94f), new Color(0.9f, 0.91f, 0.92f, 0.94f)), _bannerBg, new Vector3(0f, 0f, 0.002f));
            _bannerText = UiMesh.Text(_banner, "Text", "", new Vector3(-0.12f, 0.03f, 0f), 0.0118f, new Color(0.1f, 0.12f, 0.14f), 0.38f, QText, TextAlignmentOptions.TopLeft, true);
            _bannerIcons = new GameObject("Icons").transform;
            _bannerIcons.SetParent(_banner, false);
            _bannerIcons.localPosition = new Vector3(-0.262f, 0f, -0.001f);
            _banner.gameObject.SetActive(false);
        }

        void Rebuild()
        {
            if (_wristText == null) return;
            _kz = GameSettings.KZ;
            string t = Text;
            _wristText.text = t;
            _bannerText.text = t;
            Icons ??= new ControlIcon[0];
            BuildIcons(_wristIcons, 0.0105f, -0.0055f, false, 9);
            BuildIcons(_bannerIcons, 0.022f, 0f, true, 2);
            // the text sits right of / above the icon row
            _wristText.transform.localPosition = new Vector3(-0.058f, Icons.Length > 0 ? 0.008f : 0.022f, 0f);
            _wristIcons.localPosition = new Vector3(-0.056f, 0.02f, -0.0005f);
            _bannerText.text = t;
            // icons in a compact block left of the text (two per row)
            float iconsW = 0f;
            foreach (Transform c in _bannerIcons) iconsW = Mathf.Max(iconsW, c.localPosition.x + 0.04f);
            _bannerText.transform.localPosition = new Vector3(-0.262f + Mathf.Max(0.05f, iconsW) + 0.012f, 0.03f, 0f);
            _bannerText.rectTransform.sizeDelta = new Vector2(0.52f - Mathf.Max(0.05f, iconsW) - 0.012f, 0.5f);
            // badges at the hands
            foreach (var b in _badges) if (b != null) Destroy(b.gameObject);
            _badges.Clear();
            _badgeIcons.Clear();
            foreach (var ic in Icons)
            {
                if (ic == ControlIcon.Hand || ic == ControlIcon.Stick) continue;
                var badge = new GameObject("Badge_" + ic).transform;
                Icon(badge, ic, 0.022f, true);
                badge.name = "Badge_" + ic;
                _badges.Add(badge);
                _badgeIcons.Add(ic);
            }
        }

        void BuildIcons(Transform row, float h, float y, bool banner, int perRow)
        {
            var old = new List<Transform>();
            foreach (Transform c in row) old.Add(c);
            foreach (var c in old) { c.SetParent(null); Destroy(c.gameObject); }
            float x = 0f;
            int n = 0, rows = Mathf.CeilToInt(Icons.Length / (float)Mathf.Max(1, perRow));
            float y0 = y + (rows - 1) * h * 0.65f;
            foreach (var ic in Icons)
            {
                if (n > 0 && n % perRow == 0) x = 0f;
                var t = new GameObject("Icon_" + ic).transform;
                t.SetParent(row, false);
                float w = Icon(t, ic, h, banner);
                t.localPosition = new Vector3(x + w / 2f, y0 - (n / perRow) * h * 1.3f, 0f);
                x += w + h * 0.3f;
                n++;
            }
        }

        /// <summary>A button icon (round letter or a pill with a word) — returns its width.</summary>
        float Icon(Transform t, ControlIcon ic, float h, bool always)
        {
            bool kz = GameSettings.KZ;
            string cap = Caption(ic, kz);
            bool round = ic is ControlIcon.X or ControlIcon.Y or ControlIcon.A or ControlIcon.B;
            float w = round ? h : h * (0.7f + 0.48f * cap.Length);
            Color fill = ic switch
            {
                ControlIcon.X or ControlIcon.Y => new Color(0.12f, 0.13f, 0.15f),
                ControlIcon.A or ControlIcon.B => new Color(0.12f, 0.13f, 0.15f),
                ControlIcon.Grip => new Color(0.18f, 0.42f, 0.28f),
                ControlIcon.Trigger => new Color(0.55f, 0.28f, 0.08f),
                _ => new Color(0.3f, 0.33f, 0.38f),
            };
            var m = UiMesh.Mat(Color.white, QCard + 1);
            if (always) m.SetFloat("_ZTest", 8f);
            UiMesh.Add(t, "Pill", UiMesh.RoundedRect(w, h, h * 0.5f, fill, fill * 0.85f), m, Vector3.zero);
            var txt = UiMesh.Text(t, "Cap", cap, new Vector3(-w / 2f, h * 0.3f, -0.0005f), h * 0.34f, Color.white, w, QText + 1, TextAlignmentOptions.Top, true);
            txt.rectTransform.pivot = new Vector2(0f, 1f);
            return w;
        }

        void LateUpdate()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (wrist == null)
            {
                var w = FindAnyObjectByType<WristWatch>();
                if (w != null) wrist = w.transform;
            }
            if (_wristCard == null) return;
            if (GameSettings.KZ != _kz) Rebuild();
            bool has = !string.IsNullOrEmpty(Ru);

            // wrist: toward the elbow past the watch's pulse display, same face as the watch
            if (wrist != null && has)
            {
                if (!_wristCard.gameObject.activeSelf) _wristCard.gameObject.SetActive(true);
                _wristCard.SetPositionAndRotation(wrist.TransformPoint(new Vector3(0f, -0.105f, -0.004f)), wrist.rotation);
            }
            else if (_wristCard.gameObject.activeSelf) _wristCard.gameObject.SetActive(false);

            // banner: fades in on a new step, out after `bannerSeconds`; lazily follows the view
            float age = Time.time - ShownAt;
            float target = has && age < bannerSeconds ? 1f : 0f;
            _bannerAlpha = Mathf.MoveTowards(_bannerAlpha, target, Time.deltaTime * 3f);
            bool on = _bannerAlpha > 0.01f && head != null;
            if (_banner.gameObject.activeSelf != on) _banner.gameObject.SetActive(on);
            if (on)
            {
                float hy = head.eulerAngles.y;
                if (age < 0.1f) _yaw = hy;
                float diff = Mathf.DeltaAngle(_yaw, hy);
                if (Mathf.Abs(diff) > 12f) _yaw = Mathf.MoveTowardsAngle(_yaw, hy, (Mathf.Abs(diff) - 12f) * 6f * Time.deltaTime + 20f * Time.deltaTime);
                var rot = Quaternion.Euler(0f, _yaw, 0f);
                // the kit tray / radio panel take the lower view: the line moves above the view centre meanwhile
                bool lowBusy = (FieldMedic.Treatment.KitTray.Player != null && FieldMedic.Treatment.KitTray.Player.IsOpen) ||
                               (FieldMedic.Scenarios.RadioPanel.Instance != null && FieldMedic.Scenarios.RadioPanel.Instance.IsOpen);
                _drop = Mathf.MoveTowards(_drop, lowBusy ? -0.17f : bannerDrop, Time.deltaTime * 1.5f);
                Vector3 p = head.position + rot * new Vector3(0f, 0f, bannerDistance) + Vector3.down * _drop;
                _banner.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position));
                _banner.localScale = Vector3.one * (0.92f + 0.08f * _bannerAlpha);
                _bannerBg.SetFloat("_Alpha", _bannerAlpha);
                _bannerText.alpha = _bannerAlpha;
            }

            // badges pulse at the hand with that button (shown with the banner, and while the wrist is raised)
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
            int nLeft = 0, nRight = 0;
            for (int i = 0; i < _badges.Count; i++)
            {
                var b = _badges[i];
                if (b == null) continue;
                // w10 audit: badges skip Hand/Stick, so index the badge's own icon (not Icons[i])
                var ic = i < _badgeIcons.Count ? _badgeIcons[i] : ControlIcon.Grip;
                var hand = HandOf(ic);
                Transform at = null;
                foreach (var r in HandRig.All)
                    if (r != null && r.Input != null && (hand == null ? r.handedness == (GameSettings.LeftHanded ? Handedness.Left : Handedness.Right) : r.handedness == hand.Value)) { at = r.transform; break; }
                bool show = at != null && head != null && has && (on || age < bannerSeconds * 2f);
                if (b.gameObject.activeSelf != show) b.gameObject.SetActive(show);
                if (!show) continue;
                // several badges at one hand sit side by side (GRIP + КУРОК)
                var atRig = at.GetComponent<HandRig>();
                int slot = atRig != null && atRig.handedness == Handedness.Left ? nLeft++ : nRight++;
                Vector3 p = at.TransformPoint(new Vector3(0f, 0.06f, 0.02f)) + head.right * (slot * 0.06f);
                b.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position));
                b.localScale = Vector3.one * pulse;
            }
        }
    }
}
