using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// An IFAK: slots holding <see cref="MedItem"/>s. The player's own kit is worn on the body (rig-relative: body-locked
    /// at the belt, follows the head's yaw lazily like the torso, opened toward the player) as a real two-echelon kit
    /// (w9): a small always-open quick-access pouch on the right (1st echelon: CAT + shears) and the main rip-away pouch
    /// (2nd echelon). The casualty's pouch rides on his plate carrier. Every item not held sits exactly in its own
    /// slot (parented, kinematic, no gravity); <see cref="LateUpdate"/> enforces that after the kit moved.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class IfakPouch : MonoBehaviour
    {
        /// <summary>Main (2nd echelon) pouch. w9d: the real kit — the windlass CAT rides in the quick pouch, the main
        /// pouch carries the elastic Alpha and Esmarch tourniquets and every item Astra modelled.</summary>
        public MedItemKind[] contents =
        {
            MedItemKind.HemostaticGauze, MedItemKind.IsraeliBandage,
            MedItemKind.ChestSeal, MedItemKind.ChestSeal, MedItemKind.NasalAirway, MedItemKind.Lubricant,
            MedItemKind.DecompressionNeedle, MedItemKind.NitrileGloves, MedItemKind.Marker,
            MedItemKind.ThermalBlanket, MedItemKind.CasualtyCard,
            MedItemKind.AlphaTourniquet, MedItemKind.EsmarchTourniquet, MedItemKind.Splint,
            MedItemKind.TriangularBandage, MedItemKind.EyeShield, MedItemKind.AntisepticWipes, MedItemKind.DuctTape,
        };
        [Tooltip("1st echelon, quick-access pouch next to the main one (always open). Empty = no quick pouch.")]
        public MedItemKind[] quickContents = { MedItemKind.Tourniquet, MedItemKind.TraumaShears };
        public bool buildVisual = true;
        [Tooltip("w9d: the casualty's pouch — its items are inside it and show only when a hand reaches in (the real models are bigger than the pouch on his carrier).")]
        public bool revealOnReach;
        public float revealDistance = 0.16f;
        public bool buildOnStart = true;
        [Header("Layout (local)")]
        public Vector3 slotOrigin = new Vector3(-0.165f, 0.02f, 0.045f);
        public float slotSpacing = 0.055f;
        public int columns = 7;
        public float rowSpacing = 0.095f;
        [Tooltip("Quick pouch centre relative to the main pouch (right of the tray, on the side away from a casualty the kit is put down next to).")]
        public Vector3 quickOffset = new Vector3(0.285f, 0.0f, -0.01f);
        [Header("Body lock (player pouch)")]
        public bool bodyLocked;
        public Transform head;
        public float waistBelowHead = 0.68f;
        public float forward = 0.26f;
        public float tilt = 35f;
        [Tooltip("w9d: the real kit opens into a 28 × 50 cm layout — worn a bit further out and steeper (clipboard-like).")]
        public float artForward = 0.34f, artWaistBelowHead = 0.56f, artTilt = 45f;
        public float yawSlack = 35f;

        [Tooltip("w7: the player's pouch starts closed — pull the red tab to rip the lid open (PouchFlap).")]
        public bool closedLid = true;
        public PouchFlap Flap { get; private set; }
        /// <summary>w10: the player's kit is stowed on the hip and summoned as a tray (<see cref="KitTray"/>).</summary>
        public KitTray Tray { get; internal set; }
        /// <summary>w10: body-locked kits become a summonable tray (tests of the legacy worn kit turn it off).</summary>
        public static bool UseTray = true;
        bool TrayWanted => bodyLocked && UseTray;
        public bool IsOpen => Tray != null ? Tray.IsOpen : Flap == null || Flap.IsOpen;
        public Transform QuickPouch { get; private set; }
        /// <summary>w9d: the imported FMVR_IFAK (null = procedural tray).</summary>
        public IfakArt Art { get; private set; }

        public readonly List<MedItem> Items = new List<MedItem>();
        public bool Built { get; private set; }
        float _yaw;
        bool _yawInit;

        void Start() { if (buildOnStart) Build(); }

        public void Build()
        {
            if (Built) return;
            Built = true;
            if (buildVisual && BuildArt()) return;
            if (buildVisual) BuildVisual();
            if (quickContents != null && quickContents.Length > 0) BuildQuickPouch();
            for (int i = 0; i < contents.Length; i++)
            {
                int row = i / Mathf.Max(1, columns), col = i % Mathf.Max(1, columns);
                var slot = new GameObject("Slot_" + i + "_" + contents[i]).transform;
                slot.SetParent(transform, false);
                slot.localPosition = slotOrigin + new Vector3(col * slotSpacing, 0f, -row * rowSpacing);
                slot.localRotation = Quaternion.identity;
                AddItem(contents[i], slot);
            }
            if (buildVisual && closedLid && !TrayWanted) Flap = PouchFlap.Add(this);
            if (bodyLocked) Follow(1f, true);
            if (TrayWanted) Tray = KitTray.Attach(this);
        }

        void AddItem(MedItemKind kind, Transform slot)
        {
            var item = Kit.Create(kind, slot.position, slot.rotation);
            item.PutInPouch(this, slot);
            Items.Add(item);
        }

        /// <summary>Open-top elastic pouch: CAT standing ready on the left, shears next to it. Items come first in
        /// <see cref="Items"/> (so <c>Find&lt;TourniquetCAT&gt;()</c> is the quick one).</summary>
        void BuildQuickPouch()
        {
            QuickPouch = new GameObject("QuickPouch").transform;
            QuickPouch.SetParent(transform, false);
            QuickPouch.localPosition = quickOffset;
            if (buildVisual)
            {
                var coyote = Kit.Mat("PouchCoyote", Kit.Coyote, 0.12f);
                var dark = Kit.Mat("PouchDark", new Color(0.16f, 0.13f, 0.09f), 0.1f);
                var red = Kit.Mat("PouchRed", new Color(0.5f, 0.06f, 0.05f), 0.2f);
                var v = new GameObject("QuickVisual").transform;
                v.SetParent(QuickPouch, false);
                Kit.Part(v, PrimitiveType.Cube, "Base", new Vector3(0f, -0.005f, 0f), new Vector3(0.11f, 0.01f, 0.22f), dark);
                Kit.Part(v, PrimitiveType.Cube, "WallBack", new Vector3(0f, 0.022f, 0.105f), new Vector3(0.11f, 0.054f, 0.012f), coyote);
                Kit.Part(v, PrimitiveType.Cube, "WallFront", new Vector3(0f, 0.012f, -0.105f), new Vector3(0.11f, 0.034f, 0.012f), coyote);
                Kit.Part(v, PrimitiveType.Cube, "WallL", new Vector3(-0.055f, 0.022f, 0f), new Vector3(0.012f, 0.054f, 0.22f), coyote);
                Kit.Part(v, PrimitiveType.Cube, "WallR", new Vector3(0.055f, 0.022f, 0f), new Vector3(0.012f, 0.054f, 0.22f), coyote);
                Kit.Part(v, PrimitiveType.Cube, "Elastic", new Vector3(0f, 0.014f, 0.03f), new Vector3(0.108f, 0.003f, 0.014f), dark);
                Kit.Part(v, PrimitiveType.Cube, "TabTQ", new Vector3(0f, 0.03f, -0.112f), new Vector3(0.05f, 0.022f, 0.004f), red);
                Kit.Merge(v);
            }
            for (int i = 0; i < quickContents.Length; i++)
            {
                var slot = new GameObject("QuickSlot_" + i + "_" + quickContents[i]).transform;
                slot.SetParent(QuickPouch, false);
                slot.localPosition = new Vector3(quickContents.Length == 1 ? 0f : -0.024f + 0.048f * i / (quickContents.Length - 1), 0.02f, 0f);
                slot.localRotation = Quaternion.identity;
                AddItem(quickContents[i], slot);
            }
        }

        /// <summary>w9d: the real kit (FMVR_IFAK): items on the delivered Slot_* empties, rip-away lid = the model's
        /// folding panels (<see cref="PouchFlap"/> in art mode). False → no imported kit, procedural tray.</summary>
        bool BuildArt()
        {
            Art = IfakArt.Build(transform);
            if (Art == null) return false;
            QuickPouch = Art.QuickPouch;
            var count = new Dictionary<MedItemKind, int>();
            int extra = 0;
            void Place(MedItemKind kind)
            {
                count.TryGetValue(kind, out int n);
                count[kind] = n + 1;
                Transform seat = null;
                if (IfakArt.Layout(kind, n, out var slot, out bool tipDown, out var off))
                    seat = Art.Seat(slot, "Seat_" + kind + (n > 0 ? "_" + n : ""), tipDown, off);
                // a kind the kit has no slot for (e.g. a second CAT): in the middle of the upper panel, stacked
                if (seat == null) seat = Art.Seat("Slot_ChestSeal_1", "Seat_Extra" + extra, false, new Vector3(0f, 0.02f + 0.012f * extra++, 0f));
                if (seat == null) { seat = new GameObject("Seat_" + kind).transform; seat.SetParent(transform, false); }
                AddItem(kind, seat);
            }
            if (quickContents != null) foreach (var k in quickContents) Place(k);
            foreach (var k in contents) Place(k);
            if (closedLid && !TrayWanted) Flap = PouchFlap.Add(this);
            if (bodyLocked) Follow(1f, true);
            if (TrayWanted) Tray = KitTray.Attach(this);
            return true;
        }

        /// <summary>Can an item in this slot be taken / put back? Quick pouch: always; main pouch: once the lid is open.</summary>
        public bool IsSlotOpen(Transform slot) => IsOpen || (slot != null && QuickPouch != null && slot.IsChildOf(QuickPouch));

        /// <summary>Released at `item`'s position: does it go back into its slot? Tray: within 25 cm of the open tray or at
        /// the hip kit. Worn/ground kit (w9a): its own slot open and within the item's return distance.</summary>
        public bool AcceptsReturn(MedItem item)
        {
            if (item == null || item.homeSlot == null) return false;
            if (Tray != null) return Tray.AcceptsReturn(item.transform.position);
            return IsSlotOpen(item.homeSlot) && Vector3.Distance(item.transform.position, item.homeSlot.position) < item.returnDistance;
        }

        public T Find<T>(int index = 0) where T : MedItem
        {
            int n = 0;
            foreach (var it in Items) if (it is T t && n++ == index) return t;
            return null;
        }

        public MedItem Find(MedItemKind kind, int index = 0)
        {
            int n = 0;
            foreach (var it in Items) if (it != null && it.kind == kind && n++ == index) return it;
            return null;
        }

        void BuildVisual()
        {
            var coyote = Kit.Mat("PouchCoyote", Kit.Coyote, 0.12f);
            var dark = Kit.Mat("PouchDark", new Color(0.16f, 0.13f, 0.09f), 0.1f);
            var red = Kit.Mat("PouchRed", new Color(0.5f, 0.06f, 0.05f), 0.2f);
            var v = new GameObject("PouchVisual").transform;
            v.SetParent(transform, false);
            // open admin-style pouch: base tray, side walls, folded-down flap with the red cross
            Kit.Part(v, PrimitiveType.Cube, "Base", new Vector3(0f, -0.005f, 0f), new Vector3(0.42f, 0.01f, 0.24f), dark);
            Kit.Part(v, PrimitiveType.Cube, "WallBack", new Vector3(0f, 0.02f, 0.115f), new Vector3(0.42f, 0.05f, 0.012f), coyote);
            Kit.Part(v, PrimitiveType.Cube, "WallFront", new Vector3(0f, 0.012f, -0.115f), new Vector3(0.42f, 0.034f, 0.012f), coyote);
            Kit.Part(v, PrimitiveType.Cube, "WallL", new Vector3(-0.205f, 0.02f, 0f), new Vector3(0.012f, 0.05f, 0.24f), coyote);
            Kit.Part(v, PrimitiveType.Cube, "WallR", new Vector3(0.205f, 0.02f, 0f), new Vector3(0.012f, 0.05f, 0.24f), coyote);
            Kit.Part(v, PrimitiveType.Cube, "Flap", new Vector3(0f, -0.03f, -0.16f), new Vector3(0.42f, 0.012f, 0.1f), coyote, Quaternion.Euler(-40f, 0f, 0f));
            Kit.Part(v, PrimitiveType.Cube, "CrossH", new Vector3(0f, -0.022f, -0.165f), new Vector3(0.06f, 0.004f, 0.018f), red, Quaternion.Euler(-40f, 0f, 0f));
            Kit.Part(v, PrimitiveType.Cube, "CrossV", new Vector3(0f, -0.022f, -0.165f), new Vector3(0.018f, 0.004f, 0.06f), red, Quaternion.Euler(-40f, 0f, 0f));
            // elastic loops across the tray
            for (int i = 0; i < 2; i++)
                Kit.Part(v, PrimitiveType.Cube, "Elastic", new Vector3(0f, 0.012f, 0.045f - i * rowSpacing), new Vector3(0.41f, 0.003f, 0.012f), dark);
            Kit.Merge(v);
            if (ArtModels.ApplyPouch(transform) == null) MeshyModels.ApplyPouch(transform);
        }

        void LateUpdate()
        {
            if (Tray != null) Tray.Drive(Time.deltaTime);
            else if (bodyLocked) Follow(Time.deltaTime, false);
            // after the kit moved: slotted items exactly in place, loose ones never hover (owner bug, w9)
            bool shut = Art != null && Art.Open01 < 0.6f;
            if (Tray != null)
            {
                bool stowed = Tray.Open01 < 0.05f;
                foreach (var it in Items)
                {
                    if (it == null) continue;
                    it.Enforce();
                    it.SetHiddenInKit(stowed && it.InPouch);
                }
                return;
            }
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                if (it == null) continue;
                it.Enforce();
                // w9d: the real kit closed — the main-pouch items are inside it (they are bigger than the folded body)
                if (Art != null) it.SetHiddenInKit(shut && it.InPouch && !IsSlotOpen(it.homeSlot));
                else if (revealOnReach) it.SetHiddenInKit(it.InPouch && !(it is TourniquetCAT) && !HandNear(it.transform.position));
            }
        }

        bool HandNear(Vector3 p)
        {
            foreach (var h in FieldMedic.Interaction.HandGrabber.All)
                if (h != null && h.isActiveAndEnabled && Vector3.Distance(h.PalmPosition, p) < revealDistance) return true;
            return false;
        }

        void Follow(float dt, bool snap)
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;
            float headYaw = head.eulerAngles.y;
            if (!_yawInit || snap) { _yaw = headYaw; _yawInit = true; }
            float diff = Mathf.DeltaAngle(_yaw, headYaw);
            // the torso turns only when the head looks far to the side (and then smoothly)
            if (Mathf.Abs(diff) > yawSlack) _yaw += (Mathf.Abs(diff) - yawSlack) * Mathf.Sign(diff);
            var yawRot = Quaternion.Euler(0f, _yaw, 0f);
            bool art = Art != null;
            Vector3 p = head.position + yawRot * Vector3.forward * (art ? artForward : forward);
            p.y = head.position.y - (art ? artWaistBelowHead : waistBelowHead);
            transform.SetPositionAndRotation(p, yawRot * Quaternion.Euler(-(art ? artTilt : tilt), 0f, 0f));
        }
    }
}
