using System.Collections.Generic;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// w10 (owner: "the kit should sit aside and not get in the way; when I need it I press it and it is in front of my
    /// eyes"). The player's IFAK hangs CLOSED on the left hip (the FMVR_IFAK model, folded) — visible when looking down,
    /// never in front of the hands. Left X (desktop K), grabbing the hip kit with either hand, or the «АПТЕЧКА» button on
    /// the watch opens it as a TRAY ~40 cm in front of the eyes at chest height, tilted toward the face: every item lies
    /// flat in a clear grid with a RU/KZ label under it; the item the current training step needs glows. The tray
    /// follows the head lazily (only after a turn of more than ~50°). X again folds it back to the hip. Items released
    /// within 25 cm of the open tray (or at the hip kit) go back to their place; anywhere else they drop (w9a rule).
    /// The pouch transform IS the tray (items are parented to its cells); when stowed it is parked at the hip with the
    /// items hidden and not grabbable. A pouch that is not body-locked (tests that put the kit on the ground) keeps
    /// its own pose and only opens/closes.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public sealed class KitTray : MonoBehaviour
    {
        /// <summary>The player's tray (null when none).</summary>
        public static KitTray Player { get; private set; }

        public IfakPouch pouch;
        public Transform head;
        [Header("Tray (open)")]
        public float trayDistance = 0.40f;
        public float trayBelowEye = 0.34f;
        public float trayTilt = 40f;
        public float followAngle = 50f;
        public float maxWidth = 0.50f;
        [Header("Hip (stowed)")]
        public Vector3 hipOffset = new Vector3(-0.21f, -0.70f, -0.04f);
        public float hipMinHeight = 0.18f;
        [Tooltip("Released this close to the open tray panel / the hip kit → back into its cell.")]
        public float snapBackDistance = 0.25f, hipReturnDistance = 0.18f;

        public bool IsOpen { get; private set; }
        /// <summary>0 stowed … 1 fully open (animation).</summary>
        public float Open01 { get; private set; }
        public Transform Hip { get; private set; }
        public Transform Panel { get; private set; }
        public ProxyHandle HipHandle { get; private set; }
        public Vector2 PanelSize { get; private set; }
        public readonly Dictionary<MedItem, Transform> Cells = new Dictionary<MedItem, Transform>();
        readonly Dictionary<MedItem, TextMesh> _labels = new Dictionary<MedItem, TextMesh>();
        /// <summary>Item kind to glow in the tray (training step) — null = none.</summary>
        public MedItemKind? Wanted { get; set; }
        public int ToggleCount { get; private set; }
        public event System.Action<bool> Toggled;

        float _yaw;
        bool _yawInit, _yawChasing, _lastX, _kz;
        Transform _glow, _hipGlow;
        Material _glowMat, _hipGlowMat;
        TextMesh _title;

        /// <summary>Display order (MARCH-ish): bleeding control first, then chest/airway, then the rest.</summary>
        static readonly MedItemKind[] Order =
        {
            MedItemKind.Tourniquet, MedItemKind.TraumaShears, MedItemKind.HemostaticGauze, MedItemKind.IsraeliBandage, MedItemKind.NitrileGloves,
            MedItemKind.ChestSeal, MedItemKind.DecompressionNeedle, MedItemKind.NasalAirway, MedItemKind.Lubricant, MedItemKind.Marker,
            MedItemKind.CasualtyCard, MedItemKind.ThermalBlanket, MedItemKind.AlphaTourniquet, MedItemKind.EsmarchTourniquet, MedItemKind.Splint,
            MedItemKind.TriangularBandage, MedItemKind.EyeShield, MedItemKind.AntisepticWipes, MedItemKind.DuctTape,
        };

        public static string Label(MedItemKind k, bool kz) => k switch
        {
            MedItemKind.Tourniquet => kz ? "CAT жгуты" : "Жгут CAT",
            MedItemKind.TraumaShears => kz ? "Қайшы" : "Ножницы",
            MedItemKind.HemostaticGauze => "Гемостатик",
            MedItemKind.IsraeliBandage => kz ? "Израиль бинті" : "Израильский бинт",
            MedItemKind.ChestSeal => kz ? "Жапсырма" : "Наклейка",
            MedItemKind.NasalAirway => kz ? "Түтік (НФВ)" : "Воздуховод",
            MedItemKind.Lubricant => kz ? "Майлағыш" : "Смазка",
            MedItemKind.DecompressionNeedle => kz ? "Ине" : "Игла",
            MedItemKind.NitrileGloves => kz ? "Қолғап" : "Перчатки",
            MedItemKind.Marker => "Маркер",
            MedItemKind.ThermalBlanket => kz ? "Көрпе" : "Одеяло",
            MedItemKind.CasualtyCard => "Карточка",
            MedItemKind.AlphaTourniquet => kz ? "«Альфа» жгуты" : "Жгут «Альфа»",
            MedItemKind.EsmarchTourniquet => kz ? "Эсмарх жгуты" : "Жгут Эсмарха",
            MedItemKind.Splint => "Шина",
            MedItemKind.TriangularBandage => kz ? "Үшбұрышты таңғыш" : "Косынка",
            MedItemKind.EyeShield => kz ? "Көз қалқаны" : "Щиток глаза",
            MedItemKind.AntisepticWipes => kz ? "Сүрткіштер" : "Салфетки",
            MedItemKind.DuctTape => "Скотч",
            _ => k.ToString(),
        };

        public static KitTray Attach(IfakPouch p)
        {
            var t = p.GetComponent<KitTray>() ?? p.gameObject.AddComponent<KitTray>();
            t.pouch = p;
            t.head = p.head;
            p.Tray = t; // before the items move into their cells (they turn into triggers there)
            t.Setup();
            return t;
        }

        void OnDestroy()
        {
            if (Player == this) Player = null;
            if (Hip != null) Destroy(Hip.gameObject);
        }

        void Setup()
        {
            if (Panel != null) return;
            if (pouch.bodyLocked) Player = this;
            // ---- the closed kit on the hip: the pouch's own look moves to a separate body-locked root
            Hip = new GameObject("PlayerKit_Hip").transform;
            foreach (var name in new[] { IfakArt.VisualName, "QuickVisual" })
            {
                var v = FindDeep(pouch.transform, name);
                if (v != null) v.SetParent(Hip, false);
            }
            if (pouch.Art != null) { pouch.Art.SetOpen(0f); pouch.Art.Settle(true); }
            if (pouch.Flap != null)
            {
                // the rip-away tab belongs to the worn kit; the tray replaces it
                if (pouch.Flap.Tab != null) { foreach (var c in pouch.Flap.Tab.GetComponents<Collider>()) c.enabled = false; pouch.Flap.Tab.enabled = false; }
                pouch.Flap.enabled = false;
            }
            HipHandle = ProxyHandle.Create<ProxyHandle>("Grab_KitHip", Hip, Hip.position, 0.1f, 1);
            HipHandle.Grabbed += OnHipGrabbed;
            _hipGlowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.8f), 3060, UiMesh.Icon("glow"));
            _hipGlow = UiMesh.Add(null, "KitHipGlow", UiMesh.Quad, _hipGlowMat, Vector3.zero).transform;
            _hipGlow.gameObject.SetActive(false);

            BuildPanel();
            Debug.Log($"[FMVR] kit tray: {Cells.Count} items, panel {PanelSize.x * 100f:0}×{PanelSize.y * 100f:0} cm, stowed on the left hip");
            Drive(0f, true);
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t)
            {
                if (c.name == name) return c;
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------------ panel + grid
        struct Entry { public MedItem item; public Vector3 size, centre; public bool turn; }

        void BuildPanel()
        {
            Panel = new GameObject("TrayPanel").transform;
            Panel.SetParent(pouch.transform, false);
            var entries = new List<Entry>();
            foreach (var it in pouch.Items)
            {
                if (it == null) continue;
                var col = it.GetComponent<BoxCollider>();
                Vector3 size = col != null ? col.size : new Vector3(0.05f, 0.02f, 0.1f);
                Vector3 centre = col != null ? col.center : Vector3.zero;
                entries.Add(new Entry { item = it, size = size, centre = centre, turn = size.x > size.z * 1.15f });
            }
            entries.Sort((a, b) =>
            {
                int ia = System.Array.IndexOf(Order, a.item.kind), ib = System.Array.IndexOf(Order, b.item.kind);
                if (ia < 0) ia = 99; if (ib < 0) ib = 99;
                return ia != ib ? ia.CompareTo(ib) : pouch.Items.IndexOf(a.item).CompareTo(pouch.Items.IndexOf(b.item));
            });

            // shelf packing: rows from the far edge (+z) toward the player, each item standing "long side away"
            const float gap = 0.018f, label = 0.024f, margin = 0.025f, header = 0.035f;
            var rows = new List<List<(Entry e, float w, float l)>>();
            var row = new List<(Entry, float, float)>();
            float rowW = 0f;
            foreach (var e in entries)
            {
                float w = Mathf.Max(e.turn ? e.size.z : e.size.x, 0.045f);
                float l = Mathf.Min(e.turn ? e.size.x : e.size.z, 0.24f);
                if (row.Count > 0 && rowW + gap + w > maxWidth) { rows.Add(row); row = new List<(Entry, float, float)>(); rowW = 0f; }
                rowW += (row.Count > 0 ? gap : 0f) + w;
                row.Add((e, w, l));
            }
            if (row.Count > 0) rows.Add(row);

            float totalL = header;
            var rowLen = new List<float>();
            foreach (var r in rows) { float m = 0f; foreach (var x in r) m = Mathf.Max(m, x.l); rowLen.Add(m); totalL += m + label + gap; }
            PanelSize = new Vector2(maxWidth + 2f * margin, totalL + margin);

            // background (rounded dark olive card with a coyote rim), lying in the tray plane (local XZ, facing +Y)
            var bg = new GameObject("Background").transform;
            bg.SetParent(Panel, false);
            bg.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bg.localPosition = new Vector3(0f, -0.003f, 0f);
            UiMesh.Add(bg, "Card", UiMesh.RoundedRect(PanelSize.x, PanelSize.y, 0.03f, new Color(0.2f, 0.21f, 0.15f), new Color(0.13f, 0.14f, 0.1f)), UiMesh.Mat(Color.white, 3000), Vector3.zero);
            UiMesh.Add(bg, "Rim", UiMesh.Ring(PanelSize.x, PanelSize.y, 0.03f, 0.008f, new Color(0.45f, 0.36f, 0.22f)), UiMesh.Mat(Color.white, 3001), new Vector3(0f, 0f, -0.001f));
            _title = WorldUI.Text(Panel, "Title", "", new Vector3(-PanelSize.x / 2f + margin, 0.002f, PanelSize.y / 2f - 0.008f), 0.0115f, new Color(1f, 0.82f, 0.45f));
            _title.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            float z = PanelSize.y / 2f - header;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var r = rows[ri];
                float width = 0f; foreach (var x in r) width += x.w; width += gap * (r.Count - 1);
                float x0 = -width / 2f;
                float zc = z - rowLen[ri] / 2f;
                foreach (var (e, w, l) in r)
                {
                    float xc = x0 + w / 2f;
                    x0 += w + gap;
                    var cell = new GameObject("Cell_" + e.item.kind).transform;
                    cell.SetParent(Panel, false);
                    var rot = e.turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                    cell.localRotation = rot;
                    // the item's collider centre over the cell centre, its underside on the card
                    Vector3 c = rot * e.centre;
                    cell.localPosition = new Vector3(xc - c.x, e.size.y * 0.5f - e.centre.y + 0.002f, zc - c.z);
                    Cells[e.item] = cell;
                    var lab = WorldUI.Text(Panel, "Label_" + e.item.kind, "", new Vector3(xc, 0.002f, z - rowLen[ri] - 0.004f), 0.0085f,
                        new Color(0.93f, 0.91f, 0.84f), TextAnchor.UpperCenter, TextAlignment.Center);
                    lab.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    _labels[e.item] = lab;
                    e.item.PutInPouch(pouch, cell);
                }
                z -= rowLen[ri] + label + gap;
            }
            _glowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.85f), 3002, UiMesh.Icon("glow"));
            _glow = UiMesh.Add(Panel, "WantedGlow", UiMesh.Quad, _glowMat, Vector3.zero).transform;
            _glow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _glow.gameObject.SetActive(false);
            RefreshTexts(true);
        }

        void RefreshTexts(bool force)
        {
            bool kz = GameSettings.KZ;
            if (!force && kz == _kz) return;
            _kz = kz;
            foreach (var kv in _labels) if (kv.Key != null && kv.Value != null) kv.Value.text = Label(kv.Key.kind, kz);
            if (_title != null) _title.text = GameSettings.T("АПТЕЧКА   ·   X — закрыть   ·   GRIP / КУРОК — взять", "ДӘРІ ҚОРАБЫ   ·   X — жабу   ·   GRIP / ШҮРІППЕ — алу");
        }

        // ------------------------------------------------------------------ open / close
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            ToggleCount++;
            _yawInit = false; // face the current view
            TreatmentAudio.Velcro(Hip != null ? Hip.position : transform.position, 0.7f);
            HapticBoth(0.25f);
            Toggled?.Invoke(true);
            Debug.Log("[FMVR] kit tray: open");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ToggleCount++;
            TreatmentAudio.Velcro(Hip != null ? Hip.position : transform.position, 0.45f);
            HapticBoth(0.15f);
            Toggled?.Invoke(false);
            Debug.Log("[FMVR] kit tray: closed");
        }

        static void HapticBoth(float a)
        {
            foreach (var h in HandGrabber.All) if (h != null) Haptics.Play(h.Input, new Haptics.Preset(a, 0.05f));
        }

        void OnHipGrabbed(Grabbable g, HandGrabber hand)
        {
            Open();
            g.ForceRelease(); // the hand stays free to take an item from the tray
        }

        /// <summary>Released here → back into its cell (open tray within 25 cm of the card, or the hip kit).</summary>
        public bool AcceptsReturn(Vector3 p)
        {
            if (Hip != null && Vector3.Distance(p, Hip.position) < hipReturnDistance) return true;
            if (!IsOpen || Panel == null) return false;
            Vector3 l = Panel.InverseTransformPoint(p);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - PanelSize.x / 2f), dz = Mathf.Max(0f, Mathf.Abs(l.z) - PanelSize.y / 2f);
            return new Vector3(dx, l.y, dz).magnitude < snapBackDistance;
        }

        /// <summary>Where a guide should point: the open tray's wanted cell, or the hip kit.</summary>
        public Vector3 TargetPoint => IsOpen && Panel != null ? Panel.position : Hip != null ? Hip.position : transform.position;

        // ------------------------------------------------------------------ per frame (called by IfakPouch.LateUpdate)
        public void Drive(float dt, bool snap = false)
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (pouch.head == null) pouch.head = head;
            RefreshTexts(false);
            // left X: the controller button (edge)
            var left = HandRig.Find(Handedness.Left);
            bool x = left != null && left.PrimaryButton;
            if (x && !_lastX && Player == this) Toggle();
            _lastX = x;

            float goal = IsOpen ? 1f : 0f;
            Open01 = snap ? goal : Mathf.MoveTowards(Open01, goal, dt / 0.22f);
            float e = Open01 * Open01 * (3f - 2f * Open01);

            if (head != null && pouch.bodyLocked)
            {
                float hy = head.eulerAngles.y;
                if (!_yawInit || snap) { _yaw = hy; _yawInit = true; _yawChasing = false; }
                float diff = Mathf.DeltaAngle(_yaw, hy);
                if (Mathf.Abs(diff) > followAngle) _yawChasing = true;
                if (_yawChasing)
                {
                    _yaw = Mathf.MoveTowardsAngle(_yaw, hy, 140f * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(_yaw, hy)) < 4f) _yawChasing = false;
                }
                var yawRot = Quaternion.Euler(0f, _yaw, 0f);
                // hip: the torso yaw follows the head loosely (no chase needed: it is out of sight)
                var bodyRot = Quaternion.Euler(0f, IsOpen ? _yaw : hy, 0f);
                Vector3 hip = head.position + bodyRot * new Vector3(hipOffset.x, 0f, hipOffset.z);
                hip.y = Mathf.Max(head.position.y + hipOffset.y, head.parent != null ? head.parent.position.y + hipMinHeight : head.position.y + hipOffset.y);
                var hipRot = bodyRot * Quaternion.LookRotation(Vector3.up, Vector3.left);
                Hip.SetPositionAndRotation(hip, hipRot);

                Vector3 tray = head.position + yawRot * Vector3.forward * trayDistance;
                tray.y = head.position.y - trayBelowEye;
                var trayRot = yawRot * Quaternion.Euler(-trayTilt, 0f, 0f);
                // stowed: parked inside the hip kit (items hidden there); opening flies it up in front of the eyes
                pouch.transform.SetPositionAndRotation(Vector3.Lerp(hip, tray, e), Quaternion.Slerp(hipRot * Quaternion.Euler(-90f, 0f, 0f), trayRot, e));
            }
            else if (Hip != null)
            {
                // a kit placed somewhere (tests, ground): the closed body next to the tray
                Hip.SetPositionAndRotation(pouch.transform.TransformPoint(new Vector3(PanelSize.x / 2f + 0.1f, 0.05f, 0f)), pouch.transform.rotation * Quaternion.LookRotation(Vector3.up, Vector3.left));
            }
            Panel.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, e);
            bool panelOn = Open01 > 0.01f;
            if (Panel.gameObject.activeSelf != panelOn) Panel.gameObject.SetActive(panelOn);
            if (HipHandle != null && HipHandle.enabled == IsOpen) HipHandle.enabled = !IsOpen;
            UpdateGlow();
        }

        void UpdateGlow()
        {
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            MedItem wanted = null;
            if (Wanted.HasValue)
                foreach (var kv in Cells)
                    if (kv.Key != null && kv.Key.kind == Wanted.Value && kv.Key.InPouch) { wanted = kv.Key; break; }
            bool cell = wanted != null && IsOpen && Open01 > 0.9f;
            _glow.gameObject.SetActive(cell);
            if (cell)
            {
                var c = Cells[wanted];
                var b = wanted.GetComponent<BoxCollider>();
                Vector3 centre = Panel.InverseTransformPoint(wanted.transform.TransformPoint(b != null ? b.center : Vector3.zero));
                float s = b != null ? Mathf.Max(b.size.x, b.size.z) * 1.5f : 0.12f;
                _glow.localPosition = new Vector3(centre.x, 0.0005f, centre.z);
                _glow.localScale = Vector3.one * s * (0.9f + 0.15f * pulse);
                _glowMat.SetFloat("_Alpha", pulse);
            }
            // tray closed and a step needs an item: the hip kit glows (press X)
            bool hipGlow = wanted != null && !IsOpen && Hip != null;
            _hipGlow.gameObject.SetActive(hipGlow);
            if (hipGlow)
            {
                var cam = Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : Hip.position + Vector3.up;
                Vector3 to = (eye - Hip.position).normalized;
                _hipGlow.position = Hip.position + to * 0.08f;
                _hipGlow.rotation = Quaternion.LookRotation(-to, Vector3.up);
                _hipGlow.localScale = Vector3.one * 0.22f * (0.9f + 0.2f * pulse);
                _hipGlowMat.SetFloat("_Alpha", pulse);
            }
        }

        /// <summary>The label under an item (tests).</summary>
        public string LabelOf(MedItem it) => _labels.TryGetValue(it, out var t) && t != null ? t.text : null;
    }
}
