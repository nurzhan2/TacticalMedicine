using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// w9d: the player's kit drawn with the delivered FMVR_IFAK model (rip-away two-echelon IFAK). The model's rest pose
    /// is the opened kit; closing folds the side wings over the upper panel (Y axis), the lower extension onto the lower
    /// panel and the flap (with the lower panel) up over the front (X axis), exactly the folding order of Astra's report.
    /// The quick pouch (1st echelon: CAT + shears) is attached to the body's side, the back panel stays on the vest
    /// while the body rips off it. Items sit on "seats": children of the delivered Slot_* empties, oriented so the item's
    /// long side runs along the slot and its printed front faces out of the open kit.
    /// Model axes (Unity): front = −Z (the open face), up = +Y, quick pouch to +X.
    /// </summary>
    public sealed class IfakArt : MonoBehaviour
    {
        public const string VisualName = "PouchVisual";
        /// <summary>Kit frame: tray up (+Y) = the open face, far edge (+Z) = the top of the kit (handle, pull tab).</summary>
        public static readonly Quaternion ModelInKit = Quaternion.Euler(90f, 0f, 0f);
        /// <summary>Model origin (bottom hinge of the body) in the kit frame.</summary>
        public static readonly Vector3 ModelOffset = new Vector3(-0.03f, 0.02f, -0.02f);
        public const float QuickPouchAttachX = -0.107f;   // Astra: subtract from its saved X to attach it at the side
        public const float RipLift = 0.035f;              // body lifts off the back panel when ripped open

        public Transform Root { get; private set; }       // instantiated prefab ("Art")
        public Transform Model { get; private set; }
        public Transform Body { get; private set; }
        public Transform Flap { get; private set; }
        public Transform PullTab { get; private set; }
        public Transform QuickPouch { get; private set; }
        public Transform BackPanel { get; private set; }
        /// <summary>0 = closed (folded, on the back panel), 1 = open (rest pose of the model).</summary>
        public float Open01 { get; private set; } = 1f;

        struct Hinge { public Transform t; public Quaternion rest; public Vector3 axis; public float closedDeg; public float order; }
        readonly List<Hinge> _hinges = new List<Hinge>();
        readonly Dictionary<string, Transform> _slots = new Dictionary<string, Transform>();
        Vector3 _bodyRest;

        /// <summary>Builds the art kit under `pouch` (null when there is no imported IFAK).</summary>
        public static IfakArt Build(Transform pouch)
        {
            var prefab = ArtModels.Load(ArtModels.Pouch.id);
            if (prefab == null) return null;
            var vis = pouch.Find(VisualName);
            if (vis == null) { vis = new GameObject(VisualName).transform; vis.SetParent(pouch, false); }
            var art = pouch.gameObject.GetComponent<IfakArt>() ?? pouch.gameObject.AddComponent<IfakArt>();
            art.Setup(vis, prefab);
            return art.Root != null ? art : null;
        }

        void Setup(Transform vis, GameObject prefab)
        {
            var go = Instantiate(prefab, vis, false);
            go.name = "Art";
            Root = go.transform;
            Root.localPosition = ModelOffset;
            Root.localRotation = ModelInKit;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Model = Root.Find("Model") ?? Root;
            Body = ArtModels.FindPart(Model, "Body");
            Flap = ArtModels.FindPart(Model, "Flap");
            PullTab = ArtModels.FindPart(Model, "PullTab");
            QuickPouch = ArtModels.FindPart(Model, "QuickPouch");
            BackPanel = ArtModels.FindPart(Model, "BackPanel");
            if (Body == null || Flap == null) { Destroy(go); Root = null; return; }
            // the quick pouch rides on the body's side (Astra shows it separated next to the open kit)
            if (QuickPouch != null) QuickPouch.position += Model.TransformVector(new Vector3(QuickPouchAttachX, 0f, 0f));
            _bodyRest = Body.localPosition;
            // hinges in the model's own axes (folding order: wings, extension, flap)
            AddHinge("InnerPanel_LeftWing", Vector3.up, -90f, 0f);
            AddHinge("InnerPanel_RightWing", Vector3.up, 90f, 0f);
            AddHinge("InnerPanel_LowerExtension", Vector3.right, 180f, 0.25f);
            AddHinge("Flap", Vector3.right, 180f, 0.45f);
            foreach (var t in Model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Slot_")) _slots[t.name] = t;
        }

        void AddHinge(string part, Vector3 modelAxis, float closedDeg, float order)
        {
            var t = ArtModels.FindPart(Model, part);
            if (t == null) return;
            Vector3 world = Model.TransformDirection(modelAxis);
            _hinges.Add(new Hinge { t = t, rest = t.localRotation, axis = t.parent.InverseTransformDirection(world).normalized, closedDeg = closedDeg, order = order });
        }

        /// <summary>Folds the kit: 0 closed … 1 open. Wings move first when closing (last when opening).</summary>
        public void SetOpen(float open01)
        {
            Open01 = Mathf.Clamp01(open01);
            float closed = 1f - Open01;
            foreach (var h in _hinges)
            {
                // staggered: each hinge uses its own part of the 0..1 range (order 0 → first 55 %, flap last)
                float k = Mathf.Clamp01(Mathf.InverseLerp(h.order * 0.6f, h.order * 0.6f + 0.55f, closed));
                k = k * k * (3f - 2f * k);
                h.t.localRotation = Quaternion.AngleAxis(h.closedDeg * k, h.axis) * h.rest;
            }
            // ripped off the back panel: the body lifts away from it while opening
            if (Body != null)
            {
                Vector3 lift = Body.parent.InverseTransformDirection(-Model.forward) * RipLift * Mathf.Clamp01(Open01 * 2f);
                Body.localPosition = _bodyRest + lift;
            }
        }

        public Transform Slot(string name) => _slots.TryGetValue(name, out var t) ? t : null;

        /// <summary>A seat on a delivered slot: item Z along the slot's long axis (+Y of the empty), item Y out of the
        /// open face, optionally flipped (tip down), shifted in seat space (x right, y out, z along the slot).</summary>
        public Transform Seat(string slotName, string seatName, bool tipDown, Vector3 offset)
        {
            var slot = Slot(slotName);
            if (slot == null) return null;
            var seat = new GameObject(seatName).transform;
            seat.SetParent(slot, false);
            Vector3 along = slot.up;
            Vector3 outward = -Model.forward; // model front (−Z) = out of the open kit
            if (Mathf.Abs(Vector3.Dot(along, outward)) > 0.95f) outward = Model.up;
            var rot = Quaternion.LookRotation(along, outward);
            if (tipDown) rot = rot * Quaternion.Euler(0f, 180f, 0f);
            seat.rotation = rot;
            seat.position = slot.position + rot * offset;
            return seat;
        }

        /// <summary>Which delivered slot an item goes to (n = how many of this kind were placed before) and how it sits.</summary>
        public static bool Layout(MedItemKind kind, int n, out string slot, out bool tipDown, out Vector3 offset)
        {
            tipDown = false;
            offset = Vector3.zero;
            switch (kind)
            {
                case MedItemKind.Tourniquet: slot = "Slot_TQ"; break;
                case MedItemKind.TraumaShears: slot = "Slot_Shears"; tipDown = true; break;
                case MedItemKind.HemostaticGauze: slot = "Slot_Gauze"; break;
                case MedItemKind.IsraeliBandage: slot = "Slot_Israeli"; break;
                case MedItemKind.ChestSeal: slot = n == 0 ? "Slot_ChestSeal_1" : "Slot_ChestSeal_2"; offset = new Vector3(0f, n == 0 ? 0.004f : 0f, 0f); break;
                case MedItemKind.NasalAirway: slot = "Slot_NPA"; tipDown = true; break;
                case MedItemKind.DecompressionNeedle: slot = "Slot_Needle"; tipDown = true; break;
                case MedItemKind.NitrileGloves: slot = "Slot_Gloves"; break;
                case MedItemKind.Marker: slot = "Slot_Marker"; tipDown = true; break;
                case MedItemKind.ThermalBlanket: slot = "Slot_Blanket"; break;
                case MedItemKind.CasualtyCard: slot = "Slot_Card"; break;
                case MedItemKind.Splint: slot = "Slot_Splint"; break;
                case MedItemKind.DuctTape: slot = "Slot_Tape"; break;
                case MedItemKind.AlphaTourniquet: slot = "Slot_AlphaTQ"; break;
                case MedItemKind.EsmarchTourniquet: slot = "Slot_Esmarch"; break;
                // no slot of their own in the delivered kit: next to related items (see docs/W9_WHATS_NEW.md)
                case MedItemKind.Lubricant: slot = "Slot_NPA"; offset = new Vector3(0f, 0.012f, 0.085f); break;
                case MedItemKind.TriangularBandage: slot = "Slot_Splint"; offset = new Vector3(0f, -0.004f, -0.095f); break;
                case MedItemKind.EyeShield: slot = "Slot_Israeli"; offset = new Vector3(0f, -0.004f, -0.095f); break;
                case MedItemKind.AntisepticWipes: slot = "Slot_Card"; offset = new Vector3(0f, 0.008f, 0f); break;
                default: slot = null; return false;
            }
            return true;
        }
    }
}
