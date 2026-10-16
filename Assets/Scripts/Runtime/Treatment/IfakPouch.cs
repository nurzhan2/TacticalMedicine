using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// An IFAK: slots holding <see cref="MedItem"/>s. The player's own pouch is body-locked at the belt
    /// (follows the head's yaw lazily, like the torso, at waist height, opened toward the player);
    /// the casualty's pouch rides on his plate carrier.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class IfakPouch : MonoBehaviour
    {
        public MedItemKind[] contents =
        {
            MedItemKind.Tourniquet, MedItemKind.Tourniquet, MedItemKind.HemostaticGauze, MedItemKind.IsraeliBandage,
            MedItemKind.ChestSeal, MedItemKind.ChestSeal, MedItemKind.NasalAirway, MedItemKind.Lubricant,
            MedItemKind.DecompressionNeedle, MedItemKind.TraumaShears, MedItemKind.NitrileGloves, MedItemKind.Marker,
            MedItemKind.ThermalBlanket, MedItemKind.CasualtyCard,
        };
        public bool buildVisual = true;
        public bool buildOnStart = true;
        [Header("Layout (local)")]
        public Vector3 slotOrigin = new Vector3(-0.165f, 0.02f, 0.045f);
        public float slotSpacing = 0.055f;
        public int columns = 7;
        public float rowSpacing = 0.095f;
        [Header("Body lock (player pouch)")]
        public bool bodyLocked;
        public Transform head;
        public float waistBelowHead = 0.68f;
        public float forward = 0.26f;
        public float tilt = 35f;
        public float yawSlack = 35f;

        [Tooltip("w7: the player's pouch starts closed — pull the red tab to rip the lid open (PouchFlap).")]
        public bool closedLid = true;
        public PouchFlap Flap { get; private set; }
        public bool IsOpen => Flap == null || Flap.IsOpen;

        public readonly List<MedItem> Items = new List<MedItem>();
        public bool Built { get; private set; }
        float _yaw;
        bool _yawInit;

        /// <summary>Pouches with a tray (the player's kit): the physical hand rests on the tray bottom (w8).</summary>
        public static readonly List<IfakPouch> All = new List<IfakPouch>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
        /// <summary>Tray bottom (pouch-local): top at y = 0, half extents x 0.21, z 0.12.</summary>
        public bool HasTray => buildVisual;

        void Start() { if (buildOnStart) Build(); }

        public void Build()
        {
            if (Built) return;
            Built = true;
            if (buildVisual) BuildVisual();
            for (int i = 0; i < contents.Length; i++)
            {
                int row = i / Mathf.Max(1, columns), col = i % Mathf.Max(1, columns);
                var slot = new GameObject("Slot_" + i + "_" + contents[i]).transform;
                slot.SetParent(transform, false);
                slot.localPosition = slotOrigin + new Vector3(col * slotSpacing, 0f, -row * rowSpacing);
                slot.localRotation = Quaternion.identity;
                var item = Kit.Create(contents[i], slot.position, slot.rotation);
                item.PutInPouch(this, slot);
                Items.Add(item);
            }
            if (buildVisual && closedLid) Flap = PouchFlap.Add(this);
            if (bodyLocked) Follow(1f, true);
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
            MeshyModels.ApplyPouch(transform);
        }

        void LateUpdate()
        {
            if (!bodyLocked) return;
            Follow(Time.deltaTime, false);
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
            Vector3 p = head.position + yawRot * Vector3.forward * forward;
            p.y = head.position.y - waistBelowHead;
            transform.SetPositionAndRotation(p, yawRot * Quaternion.Euler(-tilt, 0f, 0f));
        }
    }
}
