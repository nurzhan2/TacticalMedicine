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
        public float trayDistance = 0.42f;
        public float trayBelowEye = 0.34f;
        public float trayTilt = 40f;
        public float followAngle = 50f;
        [Tooltip("Grid: columns, cell size (m), gap, and the largest scale an item is shown at in its cell.")]
        public int columns = 5;
        public float cellWidth = 0.092f, cellDepth = 0.078f, cellGap = 0.01f, maxItemScale = 0.7f;
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
        readonly Dictionary<MedItem, TMPro.TextMeshPro> _labels = new Dictionary<MedItem, TMPro.TextMeshPro>();
        const int QCard = 2980, QText = 2986;
        /// <summary>Item kind to glow in the tray (training step) — null = none.</summary>
        public MedItemKind? Wanted { get; set; }
        public int ToggleCount { get; private set; }
        public event System.Action<bool> Toggled;

        float _yaw, _dist;
        bool _yawInit, _yawChasing, _lastX, _kz;
        Transform _glow, _hipGlow;
        Material _glowMat, _hipGlowMat;
        TMPro.TextMeshPro _title;

        /// <summary>Display order (MARCH-ish): bleeding control first, then chest/airway, then the rest.</summary>
        static readonly MedItemKind[] Order =
        {
            MedItemKind.Tourniquet, MedItemKind.TraumaShears, MedItemKind.HemostaticGauze, MedItemKind.IsraeliBandage, MedItemKind.NitrileGloves,
            MedItemKind.ChestSeal, MedItemKind.DecompressionNeedle, MedItemKind.NasalAirway, MedItemKind.Lubricant, MedItemKind.Marker,
            MedItemKind.CasualtyCard, MedItemKind.ThermalBlanket, MedItemKind.AlphaTourniquet, MedItemKind.EsmarchTourniquet, MedItemKind.Splint,
            MedItemKind.TriangularBandage, MedItemKind.EyeShield, MedItemKind.AntisepticWipes, MedItemKind.DuctTape,
        };

        /// <summary>Extra turn (deg) so the print on the pack faces the reader (measured on docs/screens/w10_tray_close.jpg).</summary>
        static float PrintYaw(MedItemKind k) => k switch
        {
            MedItemKind.ThermalBlanket or MedItemKind.NitrileGloves or MedItemKind.IsraeliBandage => 180f,
            MedItemKind.HemostaticGauze or MedItemKind.CasualtyCard or MedItemKind.ChestSeal => -90f,
            _ => 0f,
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

            // w10: a clear fixed grid (5 × 4). Real-size items would need a 55 × 116 cm board — far too big in front of the
            // eyes — so every item is shown scaled to fit its cell (≤ 70 %, long side across) and is full size again
            // as soon as it is taken out (MedItem.LeavePouch).
            const float margin = 0.022f, header = 0.034f, label = 0.02f;
            int cols = columns, rowsN = Mathf.CeilToInt(entries.Count / (float)cols);
            PanelSize = new Vector2(cols * cellWidth + (cols - 1) * cellGap + 2f * margin, header + rowsN * (cellDepth + label) + (rowsN - 1) * cellGap + margin);

            // background (rounded dark olive card with a coyote rim), lying in the tray plane (local XZ, facing +Y)
            var bg = new GameObject("Background").transform;
            bg.SetParent(Panel, false);
            bg.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bg.localPosition = new Vector3(0f, -0.003f, 0f);
            UiMesh.Add(bg, "Card", UiMesh.RoundedRect(PanelSize.x, PanelSize.y, 0.03f, new Color(0.2f, 0.21f, 0.15f), new Color(0.13f, 0.14f, 0.1f)), UiMesh.Mat(Color.white, QCard), Vector3.zero);
            UiMesh.Add(bg, "Rim", UiMesh.Ring(PanelSize.x, PanelSize.y, 0.03f, 0.008f, new Color(0.45f, 0.36f, 0.22f)), UiMesh.Mat(Color.white, QCard + 1), new Vector3(0f, 0f, -0.001f));
            _title = UiMesh.Text(Panel, "Title", "", new Vector3(-PanelSize.x / 2f + margin, 0.002f, PanelSize.y / 2f - 0.008f), 0.0095f, new Color(1f, 0.82f, 0.45f), PanelSize.x - 2f * margin, QText);
            _title.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var cellMat = UiMesh.Mat(Color.white, QCard + 2);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int r = i / cols, c = i % cols;
                float xc = -PanelSize.x / 2f + margin + cellWidth / 2f + c * (cellWidth + cellGap);
                float zTop = PanelSize.y / 2f - header - r * (cellDepth + label + cellGap);
                float zc = zTop - cellDepth / 2f;
                // a slightly lighter pad under every item (reads as a grid at a glance)
                var pad = new GameObject("Pad_" + e.item.kind).transform;
                pad.SetParent(Panel, false);
                pad.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pad.localPosition = new Vector3(xc, -0.001f, zc - label * 0.35f);
                UiMesh.Add(pad, "Pad", UiMesh.RoundedRect(cellWidth, cellDepth + label * 0.8f, 0.012f, new Color(0.27f, 0.28f, 0.2f), new Color(0.24f, 0.25f, 0.18f)), cellMat, Vector3.zero);

                // long side across the cell (+ a per-kind turn so the printed packs read upright); uniform scale to fit
                bool across = e.size.z > e.size.x;
                var rot = Quaternion.Euler(0f, (across ? 90f : 0f) + PrintYaw(e.item.kind), 0f);
                Vector3 rs = rot * e.size;
                float w = Mathf.Abs(rs.x), d = Mathf.Abs(rs.z);
                float k = Mathf.Min(maxItemScale, (cellWidth - 0.008f) / Mathf.Max(0.01f, w), (cellDepth - 0.008f) / Mathf.Max(0.01f, d));
                var cell = new GameObject("Cell_" + e.item.kind).transform;
                cell.SetParent(Panel, false);
                cell.localRotation = rot;
                cell.localScale = Vector3.one * k;
                Vector3 centre = rot * e.centre * k;
                cell.localPosition = new Vector3(xc - centre.x, (e.size.y * 0.5f - e.centre.y) * k + 0.002f, zc - centre.z);
                Cells[e.item] = cell;
                var lab = UiMesh.Text(Panel, "Label_" + e.item.kind, "", new Vector3(xc, 0.002f, zTop - cellDepth - 0.001f), 0.0068f,
                    new Color(0.97f, 0.95f, 0.88f), cellWidth, QText, TMPro.TextAlignmentOptions.Top, true);
                lab.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _labels[e.item] = lab;
                e.item.PutInPouch(pouch, cell);
            }
            _glowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.85f), QCard + 3, UiMesh.Icon("glow"));
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
                hip.y = head.position.y + hipOffset.y;
                // kneeling / prone: never below the floor (ray down from the head, solid static ground only)
                if (Physics.Raycast(head.position, Vector3.down, out var floor, 2.6f, ~0, QueryTriggerInteraction.Ignore) && floor.rigidbody == null)
                    hip.y = Mathf.Max(hip.y, floor.point.y + hipMinHeight);
                var hipRot = bodyRot * Quaternion.LookRotation(Vector3.up, Vector3.left);
                Hip.SetPositionAndRotation(hip, hipRot);

                // a wall right in front: the tray comes closer instead of sinking into it
                float dist = trayDistance;
                if (Physics.Raycast(head.position + Vector3.down * 0.25f, yawRot * Vector3.forward, out var wall, trayDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore) && wall.rigidbody == null)
                    dist = Mathf.Clamp(wall.distance - 0.3f, 0.26f, trayDistance);
                _dist = Mathf.MoveTowards(_dist <= 0f ? dist : _dist, dist, dt * 0.8f);
                Vector3 tray = head.position + yawRot * Vector3.forward * _dist;
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
                var b = wanted.GetComponent<BoxCollider>();
                Vector3 centre = Panel.InverseTransformPoint(wanted.transform.TransformPoint(b != null ? b.center : Vector3.zero));
                float s = Mathf.Max(cellWidth, cellDepth) * 1.6f;
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
