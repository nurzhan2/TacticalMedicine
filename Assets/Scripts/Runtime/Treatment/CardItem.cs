using System.Text;
using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Casualty card (first medical card, form 100 style). Write on it with the marker (fills: name, time,
    /// tourniquets with time, wounds, AVPU / pulse), then put it on his chest (plate carrier) and let go.
    /// </summary>
    public sealed class CardItem : MedItem
    {
        public bool Filled { get; private set; }
        public Feedback LastFeedback { get; private set; }
        public Vector3 WritePoint => _text != null ? _text.transform.position : transform.position;

        TextMesh _text;

        public override void BuildVisual()
        {
            Body.mass = 0.01f;
            var paper = Kit.Mat("CardPaper", new Color(0.88f, 0.86f, 0.78f), 0.15f);
            var red = Kit.Mat("CardRed", new Color(0.65f, 0.08f, 0.06f), 0.2f);
            Kit.Part(transform, PrimitiveType.Cube, "Card", Vector3.zero, new Vector3(0.1f, 0.0015f, 0.14f), paper);
            Kit.Part(transform, PrimitiveType.Cube, "StripeL", new Vector3(-0.046f, 0.0009f, 0f), new Vector3(0.008f, 0.0005f, 0.14f), red);
            Kit.Part(transform, PrimitiveType.Cube, "StripeR", new Vector3(0.046f, 0.0009f, 0f), new Vector3(0.008f, 0.0005f, 0.14f), red);
            Kit.Merge(transform);
            _text = Kit.Text(transform, "Text", "КАРТОЧКА РАНЕНОГО\n\n—", 0.0011f, new Color(0.05f, 0.05f, 0.1f),
                new Vector3(0f, 0.0012f, 0f), Quaternion.Euler(90f, 0f, 0f));
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
        }

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Attached && base.CanBeGrabbedBy(hand);

        /// <summary>Imported card (Astra): the title and the field names are printed — the marker writes into the fields.</summary>
        public override void OnArtApplied(Transform art)
        {
            if (_text == null) return;
            _text.text = "";
            _text.anchor = TextAnchor.UpperLeft;
            _text.alignment = TextAlignment.Left;
            _text.characterSize = 0.0007f;
            var b = new Bounds();
            bool any = false;
            foreach (var mf in art.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            // printed fields start ~40 % down the card; the writing goes to the right half of each line
            _text.transform.localPosition = new Vector3(b.min.x + b.size.x * 0.40f, b.max.y + 0.0006f, b.max.z - b.size.z * 0.36f);
            _artPrinted = true;
        }

        bool _artPrinted;

        /// <summary>Written with the marker: current state of the casualty.</summary>
        public void Fill()
        {
            var c = NearestCasualty(transform.position, 3f);
            var sb = new StringBuilder(_artPrinted ? "ряд. Ерлан Сейтқали\n" : "КАРТОЧКА РАНЕНОГО\nряд. Ерлан Сейтқали\n");
            if (c != null)
            {
                var p = c.Patient;
                sb.Append("Время: ").Append(ScenarioClock.Format(p.secondsSinceStart)).Append('\n');
                foreach (var tq in p.tourniquets)
                    if (tq.Locked) sb.Append("ЖГУТ ").Append(RegionRu(tq.Region)).Append(" T ").Append(ScenarioClock.Format(tq.appliedAtSeconds)).Append('\n');
                foreach (var w in p.wounds)
                    if (w.discovered) sb.Append("• ").Append(RegionRu(w.region)).Append(": ").Append(TypeRu(w.type)).Append('\n');
                sb.Append("AVPU: ").Append(p.AVPU.ToString()[0]).Append("  ЧСС ~").Append(Mathf.RoundToInt(p.HeartRate / 10f) * 10);
            }
            _text.text = sb.ToString();
            Filled = true;
        }

        protected override void OnItemReleased(HandGrabber hand)
        {
            if (!Filled) return;
            var c = NearestCasualty(transform.position, 1.5f);
            if (c == null || c.rig == null) return;
            var chest = c.rig.ChestBone;
            if (Vector3.Distance(transform.position, chest.position) > 0.35f) return;
            AttachTo(chest);
            transform.position = chest.position + c.rig.BodyForward * 0.16f + c.rig.BodyUp * 0.05f;
            transform.rotation = Quaternion.LookRotation(c.rig.BodyUp, c.rig.BodyForward);
            LastFeedback = c.Protocol.Perform(MedAction.CasualtyCard);
        }

        public static string RegionRu(BodyRegion r) => r switch
        {
            BodyRegion.Head => "голова", BodyRegion.Neck => "шея", BodyRegion.ChestL => "грудь лев.", BodyRegion.ChestR => "грудь прав.",
            BodyRegion.Abdomen => "живот", BodyRegion.Pelvis => "таз/ягодица", BodyRegion.GroinL => "пах лев.", BodyRegion.GroinR => "пах прав.",
            BodyRegion.AxillaL => "подмышка лев.", BodyRegion.AxillaR => "подмышка прав.",
            BodyRegion.UpperArmL => "плечо лев.", BodyRegion.UpperArmR => "плечо прав.", BodyRegion.ForearmL => "предплечье лев.",
            BodyRegion.ForearmR => "предплечье прав.", BodyRegion.HandL => "кисть лев.", BodyRegion.HandR => "кисть прав.",
            BodyRegion.ThighL => "бедро лев.", BodyRegion.ThighR => "бедро прав.", BodyRegion.ShinL => "голень лев.",
            BodyRegion.ShinR => "голень прав.", BodyRegion.FootL => "стопа лев.", _ => "стопа прав.",
        };

        public static string TypeRu(WoundType t) => t switch
        {
            WoundType.ArterialLimb => "артериальное кровотечение",
            WoundType.TraumaticAmputation => "отрыв",
            WoundType.Junctional => "узловое кровотечение",
            WoundType.Venous => "венозное",
            WoundType.Capillary => "осколочное (мелкое)",
            _ => "проникающее груди",
        };
    }
}
