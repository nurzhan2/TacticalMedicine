using FieldMedic.Interaction;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// w9d: the real kit items that no scenario uses yet (rolled splint, triangular bandage, eye shield, antiseptic
    /// wipes, duct tape). They sit in their own IFAK slots, can be taken out, dropped and put back — nothing more
    /// (owner: no fake functionality). The splint tells once why it is not needed here.
    /// </summary>
    public sealed class PropItem : MedItem
    {
        public bool HintShown { get; private set; }
        /// <summary>Text of the pickup hint (RU / KZ by the UI language), null = none.</summary>
        public string HintText => kind == MedItemKind.Splint
            ? GameSettings.T("Шина — при переломе. В этом сценарии не нужна.", "Шина — сынған кезде. Бұл сценарийде қажет емес.")
            : null;

        public override void BuildVisual()
        {
            // procedural stand-ins in the real sizes (the Astra models replace them, docs/ART_IMPORT.md)
            switch (kind)
            {
                case MedItemKind.Splint:
                    Body.mass = 0.12f;
                    var orange = Kit.Mat("SplintOrange", new Color(0.85f, 0.42f, 0.1f), 0.3f);
                    var blue = Kit.Mat("SplintBlue", new Color(0.12f, 0.22f, 0.45f), 0.3f);
                    Kit.Part(transform, PrimitiveType.Cylinder, "Roll", Vector3.zero, new Vector3(0.1f, 0.055f, 0.1f), orange, Quaternion.Euler(90f, 0f, 0f));
                    Kit.Part(transform, PrimitiveType.Cylinder, "Core", Vector3.zero, new Vector3(0.05f, 0.056f, 0.05f), blue, Quaternion.Euler(90f, 0f, 0f));
                    break;
                case MedItemKind.TriangularBandage:
                    Body.mass = 0.05f;
                    Kit.Part(transform, PrimitiveType.Cube, "Pack", Vector3.zero, new Vector3(0.1f, 0.015f, 0.14f), Kit.Mat("TriPack", new Color(0.82f, 0.8f, 0.72f), 0.4f));
                    break;
                case MedItemKind.EyeShield:
                    Body.mass = 0.03f;
                    Kit.Part(transform, PrimitiveType.Cube, "Pack", Vector3.zero, new Vector3(0.1f, 0.01f, 0.12f), Kit.Mat("ShieldPack", new Color(0.75f, 0.78f, 0.8f), 0.6f));
                    break;
                case MedItemKind.AntisepticWipes:
                    Body.mass = 0.01f;
                    Kit.Part(transform, PrimitiveType.Cube, "Pack", Vector3.zero, new Vector3(0.05f, 0.005f, 0.08f), Kit.Mat("WipesPack", new Color(0.9f, 0.9f, 0.92f), 0.5f));
                    break;
                default: // duct tape
                    Body.mass = 0.08f;
                    Kit.Part(transform, PrimitiveType.Cylinder, "Roll", Vector3.zero, new Vector3(0.06f, 0.0175f, 0.06f), Kit.Mat("TapeGrey", new Color(0.4f, 0.41f, 0.4f), 0.45f));
                    break;
            }
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            Kit.Merge(transform);
        }

        protected override void OnItemGrabbed(HandGrabber hand)
        {
            if (HintShown || HintText == null) return;
            HintShown = true;
            ItemHint.Show(transform, HintText, 4.5f);
        }
    }

    /// <summary>Small world-space note above a held item (white text on a dark plate), fades after a few seconds.</summary>
    public sealed class ItemHint : MonoBehaviour
    {
        Transform _follow;
        float _until;
        TextMesh _text;
        public static ItemHint Last { get; private set; }

        public static ItemHint Show(Transform follow, string text, float seconds)
        {
            var go = new GameObject("ItemHint");
            var h = go.AddComponent<ItemHint>();
            h._follow = follow;
            h._until = Time.time + seconds;
            var plate = Kit.Part(go.transform, PrimitiveType.Quad, "Plate", new Vector3(0f, 0f, 0.001f), new Vector3(0.3f, 0.07f, 1f),
                Kit.Mat("HintPlate", new Color(0.05f, 0.05f, 0.06f), 0.1f));
            plate.GetComponent<Renderer>().sharedMaterial.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f));
            h._text = Kit.Text(go.transform, "Text", WorldUI.Wrap(text, 34), 0.0028f, Color.white, Vector3.zero, Quaternion.identity);
            h.Place();
            Last = h;
            Debug.Log("[FMVR] item hint: " + text);
            return h;
        }

        public string Text => _text != null ? _text.text : null;

        void LateUpdate()
        {
            if (_follow == null || Time.time > _until) { Destroy(gameObject); return; }
            Place();
        }

        void Place()
        {
            var cam = Camera.main;
            Vector3 p = _follow.position + Vector3.up * 0.14f;
            transform.position = p;
            if (cam != null) transform.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);
        }
    }
}
