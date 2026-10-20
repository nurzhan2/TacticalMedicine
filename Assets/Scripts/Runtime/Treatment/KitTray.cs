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
    /// the watch summons it: w10d (owner: "no rectangle — the previous one with the model, with everything, that is the
    /// top") the REAL kit flies up from the hip and unfolds its hinged panels (<see cref="IfakArt.SetOpen"/>) ~42 cm in
    /// front of the eyes at chest height, tilted toward the face; every item sits in its own Slot_* seat of the model
    /// (<see cref="IfakArt.Layout"/>, exactly like the w9 worn kit) with a small RU/KZ label under it; the item the
    /// current training step needs glows. The open kit follows the head lazily (only after a turn of more than ~50°).
    /// X again folds it and puts it back on the hip. Items released within 25 cm of the open kit (or at the hip kit) go
    /// back to their seat; anywhere else they drop (w9a rule). The kit body has no colliders (never blocks the hands);
    /// the only collider of its own is the hip proxy handle; every item in the open kit gets a ≥ 6 cm grab zone.
    /// The pouch transform IS the centre of the open kit; stowed, it is parked on the hip (items hidden, not
    /// grabbable). A pouch that is not body-locked (tests that put the kit on the ground) keeps its own pose and only
    /// unfolds / folds in place.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public sealed class KitTray : MonoBehaviour
    {
        /// <summary>The player's kit (null when none).</summary>
        public static KitTray Player { get; private set; }

        public IfakPouch pouch;
        public Transform head;
        [Header("Open kit (in front of the eyes)")]
        public float trayDistance = 0.42f;
        public float trayBelowEye = 0.36f;
        [Tooltip("Tilt of the open face toward the eyes (deg from vertical-facing-up).")]
        public float trayTilt = 46f;
        public float followAngle = 50f;
        [Tooltip("Seconds for the fly-up + unfold.")]
        public float openTime = 0.35f;
        [Tooltip("Smallest grab zone (m) of an item in the open kit.")]
        public float minGrabZone = 0.065f;
        [Header("Hip (stowed)")]
        public Vector3 hipOffset = new Vector3(-0.21f, -0.70f, -0.04f);
        public float hipMinHeight = 0.18f;
        [Tooltip("Released this close to the open kit / the hip kit → back into its seat.")]
        public float snapBackDistance = 0.25f, hipReturnDistance = 0.18f;

        public bool IsOpen { get; private set; }
        /// <summary>0 stowed … 1 fully open (animation).</summary>
        public float Open01 { get; private set; }
        /// <summary>Items drawn (the kit unfolded far enough that they do not stick out of the folded body).</summary>
        public bool ItemsShown => Open01 > (pouch != null && pouch.Art != null ? 0.6f : 0.05f);
        public Transform Hip { get; private set; }
        /// <summary>Frame of the open kit's face (centre, +Y out of the face, +Z to the kit's top edge). Labels / glow live here.</summary>
        public Transform Panel { get; private set; }
        public ProxyHandle HipHandle { get; private set; }
        /// <summary>Open kit footprint (x across, y = along the kit, m).</summary>
        public Vector2 PanelSize { get; private set; }
        /// <summary>Item → its seat (the anchor it sits on in the open kit).</summary>
        public readonly Dictionary<MedItem, Transform> Cells = new Dictionary<MedItem, Transform>();
        readonly Dictionary<MedItem, TMPro.TextMeshPro> _labels = new Dictionary<MedItem, TMPro.TextMeshPro>();
        // w10b: stacked items with the same label (CAT / Alpha / Esmarch in one pocket): one label until the first is taken
        readonly Dictionary<MedItem, MedItem> _dupOf = new Dictionary<MedItem, MedItem>();
        readonly Dictionary<MedItem, MeshFilter> _plates = new Dictionary<MedItem, MeshFilter>();
        readonly Dictionary<MedItem, BoxCollider> _zones = new Dictionary<MedItem, BoxCollider>();
        const int QPlate = 2984, QText = 2986, QGlow = 2983;
        const float LabelCap = 0.0058f;
        /// <summary>Item kind to glow in the kit (training step) — null = none.</summary>
        public MedItemKind? Wanted { get; set; }
        public int ToggleCount { get; private set; }
        public event System.Action<bool> Toggled;

        float _yaw, _dist;
        bool _yawInit, _yawChasing, _lastX, _kz;
        int _settled = -2; // art merged snapshot: -1 moving, 0 closed, 1 open
        Vector3 _closedCentre; // folded kit centre in pouch space
        Transform _glow, _hipGlow;
        Material _glowMat, _hipGlowMat;
        MedItem _wantedShown;
        static readonly Color LabelColor = new Color(0.97f, 0.95f, 0.88f), WantedColor = new Color(1f, 0.85f, 0.35f);

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
            p.Tray = t; // before the items are re-seated (they turn into triggers in the kit)
            t.Setup();
            return t;
        }

        void OnDestroy()
        {
            if (Player == this) Player = null;
            if (Hip != null) Destroy(Hip.gameObject);
            if (_hipGlow != null) Destroy(_hipGlow.gameObject);
        }

        void Setup()
        {
            if (Panel != null) return;
            if (pouch.bodyLocked) Player = this;
            if (pouch.Flap != null)
            {
                // the rip-away tab belongs to the worn kit; X / the hip handle open this one
                if (pouch.Flap.Tab != null) { foreach (var c in pouch.Flap.Tab.GetComponents<Collider>()) c.enabled = false; pouch.Flap.Tab.enabled = false; }
                pouch.Flap.enabled = false;
            }
            // no solid colliders on the kit body: it must never block / shove the hands
            foreach (var c in pouch.GetComponentsInChildren<Collider>(true))
                if (c != null && c.GetComponentInParent<MedItem>(true) == null) c.isTrigger = true;

            // ---- measure the kit opened (rest pose of the model) and make the pouch origin its centre
            var art = pouch.Art;
            if (art != null) { art.Settle(false); art.SetOpen(1f); }
            var open = KitBounds();
            var shift = open.center;
            foreach (Transform c in pouch.transform) c.localPosition -= shift;
            PanelSize = new Vector2(Mathf.Max(0.1f, open.size.x), Mathf.Max(0.1f, open.size.z));
            Panel = new GameObject("OpenKitFace").transform;
            Panel.SetParent(pouch.transform, false);
            BuildLabelsAndZones(open.extents.y);
            // folded: where the closed body is (the hip handle sits on it)
            if (art != null) art.SetOpen(0f);
            var closed = KitBounds();
            _closedCentre = closed.center;

            // ---- the hip: a body-locked anchor with the grab handle (the folded kit itself is parked on it)
            Hip = new GameObject("PlayerKit_Hip").transform;
            HipHandle = ProxyHandle.Create<ProxyHandle>("Grab_KitHip", Hip, Hip.position, 0.1f, 1);
            HipHandle.Grabbed += OnHipGrabbed;
            _hipGlowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.8f), 3060, UiMesh.Icon("glow"));
            _hipGlow = UiMesh.Add(null, "KitHipGlow", UiMesh.Quad, _hipGlowMat, Vector3.zero).transform;
            _hipGlow.gameObject.SetActive(false);

            Debug.Log($"[FMVR] kit: {Cells.Count} items in the real IFAK ({(art != null ? "FMVR_IFAK model" : "procedural")}), open {PanelSize.x * 100f:0}×{PanelSize.y * 100f:0} cm, folded {closed.size.x * 100f:0}×{closed.size.y * 100f:0}×{closed.size.z * 100f:0} cm, stowed on the left hip");
            Drive(0f, true);
        }

        /// <summary>The kit's own meshes (not the items on its seats) in pouch space.</summary>
        Bounds KitBounds()
        {
            var toPouch = pouch.transform.worldToLocalMatrix;
            var b = new Bounds();
            bool any = false;
            foreach (var r in pouch.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.GetComponentInParent<MedItem>(true) != null || r.GetComponent<TMPro.TextMeshPro>() != null) continue;
                if (Panel != null && r.transform.IsChildOf(Panel)) continue; // labels / glow
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = toPouch * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, new Vector3(0.28f, 0.04f, 0.4f));
            return b;
        }

        /// <summary>Item bounds (its own box collider) in the open kit's face frame.</summary>
        Bounds ItemBounds(MedItem it)
        {
            var col = it.GetComponent<BoxCollider>();
            Vector3 size = col != null ? col.size : Vector3.one * 0.05f, centre = col != null ? col.center : Vector3.zero;
            var m = Panel.worldToLocalMatrix * it.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(centre), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(centre + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return b;
        }

        // ------------------------------------------------------------------ labels, grab zones, glow
        void BuildLabelsAndZones(float faceHalfThickness)
        {
            var plateMat = UiMesh.Mat(Color.white, QPlate);
            foreach (var it in pouch.Items)
            {
                if (it == null) continue;
                Cells[it] = it.homeSlot;
                // every item stays on its real seat (IfakArt.Layout) — re-seat so it is exact in the measured pose
                if (it.InPouch) it.ReturnToSlot();

                // grab zone: at least ~6.5 cm in every direction around the item (easy to take, also with hand tracking)
                var col = it.GetComponent<BoxCollider>();
                if (col != null)
                {
                    var zgo = new GameObject("KitGrabZone");
                    zgo.layer = it.gameObject.layer;
                    zgo.transform.SetParent(it.transform, false);
                    zgo.transform.localPosition = col.center;
                    var z = zgo.AddComponent<BoxCollider>();
                    z.isTrigger = true;
                    var ls = it.transform.lossyScale;
                    float s = Mathf.Max(1e-4f, Mathf.Abs(ls.x));
                    z.size = Vector3.Max(col.size, Vector3.one * (minGrabZone / s));
                    z.enabled = false;
                    _zones[it] = z;
                }

                // small label just under the item's lower edge, lying on top of it (reads toward the kit's top edge)
                var ib = ItemBounds(it);
                float y = Mathf.Max(ib.max.y, faceHalfThickness * 0.2f) + 0.004f;
                var plate = new GameObject("LabelPlate_" + it.kind).transform;
                plate.SetParent(Panel, false);
                plate.localRotation = Quaternion.Euler(90f, 0f, 0f);
                plate.localPosition = new Vector3(ib.center.x, y - 0.001f, ib.min.z - 0.004f - LabelCap);
                var pr = UiMesh.Add(plate, "Plate", UiMesh.Quad, plateMat, Vector3.zero);
                _plates[it] = pr.GetComponent<MeshFilter>();
                var lab = UiMesh.Text(Panel, "Label_" + it.kind, "", new Vector3(ib.center.x, y, ib.min.z - 0.004f), LabelCap,
                    LabelColor, 0.12f, QText, TMPro.TextAlignmentOptions.Top, true);
                lab.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _labels[it] = lab;
            }
            _glowMat = UiMesh.Mat(new Color(1f, 0.85f, 0.35f, 0.85f), QGlow, UiMesh.Icon("glow"));
            _glow = UiMesh.Add(Panel, "WantedGlow", UiMesh.Quad, _glowMat, Vector3.zero).transform;
            _glow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _glow.gameObject.SetActive(false);
            RefreshTexts(true);
            Declutter();
        }

        /// <summary>w10b: items in the real kit sit close together (CAT + shears in the side pouch) → their labels
        /// overlapped. Push overlapping label plates apart in the panel plane (sized for the longer of RU/KZ).</summary>
        void Declutter()
        {
            var items = new List<MedItem>();
            var c = new List<Vector2>();
            var half = new List<Vector2>();
            _dupOf.Clear();
            var firstByText = new List<MedItem>();
            foreach (var kv in _labels)
            {
                if (kv.Key == null || kv.Value == null) continue;
                string ru = Label(kv.Key.kind, false);
                Vector3 lp = kv.Value.transform.localPosition;
                MedItem twin = null;
                foreach (var f in firstByText)
                    if (Label(f.kind, false) == ru && Vector3.Distance(_labels[f].transform.localPosition, lp) < 0.12f) { twin = f; break; }
                if (twin != null) _dupOf[kv.Key] = twin; else firstByText.Add(kv.Key);
            }
            foreach (var kv in _labels)
            {
                if (kv.Key == null || kv.Value == null || _dupOf.ContainsKey(kv.Key) || !_plates.TryGetValue(kv.Key, out var mf) || mf == null) continue;
                int len = Mathf.Max(Label(kv.Key.kind, false).Length, Label(kv.Key.kind, true).Length);
                var pp = mf.transform.parent.localPosition;
                items.Add(kv.Key);
                c.Add(new Vector2(pp.x, pp.z));
                half.Add(new Vector2((len * LabelCap * 0.78f + 0.008f) * 0.5f + 0.002f, LabelCap * 1.05f + 0.002f));
            }
            var start = new List<Vector2>(c);
            for (int iter = 0; iter < 60; iter++)
            {
                bool moved = false;
                for (int i = 0; i < c.Count; i++)
                    for (int j = i + 1; j < c.Count; j++)
                    {
                        Vector2 d = c[j] - c[i];
                        float ox = half[i].x + half[j].x - Mathf.Abs(d.x), oz = half[i].y + half[j].y - Mathf.Abs(d.y);
                        if (ox <= 0f || oz <= 0f) continue;
                        moved = true;
                        // move along the axis that needs less push (labels are wide: usually up/down)
                        Vector2 push = oz <= ox ? new Vector2(0f, (d.y >= 0f ? 1f : -1f) * oz * 0.5f + (d.y >= 0f ? 0.0005f : -0.0005f))
                                                : new Vector2((d.x >= 0f ? 1f : -1f) * ox * 0.5f + (d.x >= 0f ? 0.0005f : -0.0005f), 0f);
                        c[i] -= push; c[j] += push;
                    }
                if (!moved) break;
            }
            for (int i = 0; i < items.Count; i++)
            {
                Vector2 delta = c[i] - start[i];
                if (delta.sqrMagnitude < 1e-8f) continue;
                var off = new Vector3(delta.x, 0f, delta.y);
                _labels[items[i]].transform.localPosition += off;
                _plates[items[i]].transform.parent.localPosition += off;
            }
            foreach (var kv in _dupOf)
            {
                _labels[kv.Key].transform.localPosition = _labels[kv.Value].transform.localPosition;
                if (_plates.TryGetValue(kv.Key, out var a) && a != null && _plates.TryGetValue(kv.Value, out var b) && b != null)
                    a.transform.parent.localPosition = b.transform.parent.localPosition;
            }
        }

        void RefreshTexts(bool force)
        {
            bool kz = GameSettings.KZ;
            if (!force && kz == _kz) return;
            _kz = kz;
            foreach (var kv in _labels)
            {
                if (kv.Key == null || kv.Value == null) continue;
                string text = Label(kv.Key.kind, kz);
                kv.Value.text = text;
                // dark rounded plate under the text (white print stays readable on coyote / olive / foil)
                if (_plates.TryGetValue(kv.Key, out var mf) && mf != null)
                {
                    float w = text.Length * LabelCap * 0.78f + 0.008f, h = LabelCap * 2.1f;
                    var old = mf.sharedMesh;
                    mf.sharedMesh = UiMesh.RoundedRect(w, h, h * 0.45f, new Color(0.08f, 0.08f, 0.06f, 0.78f), new Color(0.06f, 0.06f, 0.05f, 0.78f));
                    if (old != null && old != UiMesh.Quad) Destroy(old);
                }
            }
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
            Debug.Log("[FMVR] kit: open (summoned in front)");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ToggleCount++;
            TreatmentAudio.Velcro(Hip != null ? Hip.position : transform.position, 0.45f);
            HapticBoth(0.15f);
            Toggled?.Invoke(false);
            Debug.Log("[FMVR] kit: folded back to the hip");
        }

        static void HapticBoth(float a)
        {
            foreach (var h in HandGrabber.All) if (h != null) Haptics.Play(h.Input, new Haptics.Preset(a, 0.05f));
        }

        void OnHipGrabbed(Grabbable g, HandGrabber hand)
        {
            Open();
            g.ForceRelease(); // the hand stays free to take an item from the kit
        }

        /// <summary>Released here → back into its seat (open kit within 25 cm of its footprint, or the hip kit).</summary>
        public bool AcceptsReturn(Vector3 p)
        {
            if (Hip != null && Vector3.Distance(p, Hip.position) < hipReturnDistance) return true;
            if (!IsOpen || Panel == null) return false;
            Vector3 l = Panel.InverseTransformPoint(p);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - PanelSize.x / 2f), dz = Mathf.Max(0f, Mathf.Abs(l.z) - PanelSize.y / 2f);
            return new Vector3(dx, l.y, dz).magnitude < snapBackDistance;
        }

        /// <summary>Where a guide should point: the wanted item in the open kit (else the kit), or the hip kit.</summary>
        public Vector3 TargetPoint
        {
            get
            {
                if (IsOpen && Panel != null) return _wantedShown != null && _wantedShown.InPouch ? _wantedShown.transform.position : Panel.position;
                return Hip != null ? Hip.position : transform.position;
            }
        }

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
            Open01 = snap ? goal : Mathf.MoveTowards(Open01, goal, dt / Mathf.Max(0.05f, openTime));
            float e = Open01 * Open01 * (3f - 2f * Open01);

            // the real kit unfolds / folds (merged into one draw per material while it rests)
            var art = pouch.Art;
            if (art != null)
            {
                int state = Open01 >= 1f ? 1 : Open01 <= 0f ? 0 : -1;
                if (state != _settled || snap)
                {
                    if (state < 0) art.Settle(false);
                    art.SetOpen(state < 0 ? e : state);
                    if (state >= 0) art.Settle(true);
                    _settled = state;
                }
                else if (state < 0) art.SetOpen(e);
            }

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
                // folded kit on the left hip: its top edge up, the face outward (to the left)
                var hipRot = bodyRot * Quaternion.LookRotation(Vector3.up, Vector3.left);
                Hip.SetPositionAndRotation(hip, hipRot);

                // a wall right in front: the kit comes closer instead of sinking into it
                float dist = trayDistance;
                if (Physics.Raycast(head.position + Vector3.down * 0.25f, yawRot * Vector3.forward, out var wall, trayDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore) && wall.rigidbody == null)
                    dist = Mathf.Clamp(wall.distance - 0.3f, 0.26f, trayDistance);
                _dist = Mathf.MoveTowards(_dist <= 0f ? dist : _dist, dist, dt * 0.8f);
                Vector3 front = head.position + yawRot * Vector3.forward * _dist;
                front.y = head.position.y - trayBelowEye;
                var frontRot = yawRot * Quaternion.Euler(-trayTilt, 0f, 0f);
                // stowed: the folded kit's centre on the hip anchor; opening flies it up in front of the eyes while it unfolds
                Vector3 stowPos = hip - hipRot * _closedCentre;
                pouch.transform.SetPositionAndRotation(Vector3.Lerp(stowPos, front, e), Quaternion.Slerp(hipRot, frontRot, e));
            }
            else if (Hip != null)
            {
                // a kit placed somewhere (tests, ground): it unfolds in place; the hip anchor is the folded body
                Hip.SetPositionAndRotation(pouch.transform.TransformPoint(_closedCentre), pouch.transform.rotation);
            }
            bool faceOn = Open01 > 0.9f;
            if (Panel.gameObject.activeSelf != faceOn) Panel.gameObject.SetActive(faceOn);
            if (HipHandle != null && HipHandle.enabled == IsOpen) HipHandle.enabled = !IsOpen;
            bool shown = ItemsShown;
            foreach (var kv in _zones)
            {
                if (kv.Key == null || kv.Value == null) continue;
                bool on = IsOpen && shown && kv.Key.InPouch;
                if (kv.Value.enabled != on) kv.Value.enabled = on;
            }
            UpdateGlow();
        }

        void UpdateGlow()
        {
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            MedItem wanted = null;
            if (Wanted.HasValue)
                foreach (var kv in Cells)
                    if (kv.Key != null && kv.Key.kind == Wanted.Value && kv.Key.InPouch) { wanted = kv.Key; break; }
            // labels: only for what is still in the kit; the wanted one in amber
            if (wanted != _wantedShown || Time.frameCount % 15 == 0)
            {
                foreach (var kv in _labels)
                {
                    if (kv.Key == null || kv.Value == null) continue;
                    bool on = kv.Key.InPouch && !(_dupOf.TryGetValue(kv.Key, out var twin) && twin != null && twin.InPouch);
                    if (kv.Value.gameObject.activeSelf != on) kv.Value.gameObject.SetActive(on);
                    if (_plates.TryGetValue(kv.Key, out var pl) && pl != null && pl.transform.parent.gameObject.activeSelf != on) pl.transform.parent.gameObject.SetActive(on);
                    kv.Value.color = kv.Key == wanted ? WantedColor : LabelColor;
                }
                _wantedShown = wanted;
            }
            bool cell = wanted != null && IsOpen && Open01 > 0.9f;
            _glow.gameObject.SetActive(cell);
            if (cell)
            {
                var b = ItemBounds(wanted);
                float s = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 1.35f + 0.04f, 0.09f, 0.32f);
                _glow.localPosition = new Vector3(b.center.x, b.center.y, b.center.z);
                _glow.localScale = Vector3.one * s * (0.9f + 0.15f * pulse);
                _glowMat.SetFloat("_Alpha", pulse);
            }
            // kit closed and a step needs an item: the hip kit glows (press X)
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
