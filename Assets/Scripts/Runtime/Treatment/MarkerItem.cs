using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Permanent marker. Squeeze (trigger) and "write" (≥ 4 cm of tip strokes) on: the CAT TIME strap (writes
    /// the time), his forehead ("T 01:43" — any medic sees there is a tourniquet), or the casualty card.
    /// </summary>
    public sealed class MarkerItem : MedItem
    {
        public const float StrokeNeeded = 0.04f;
        public Feedback LastFeedback { get; private set; }
        public bool WroteForehead { get; private set; }

        float _stroke;
        Vector3 _last;
        bool _hasLast;
        object _target;

        public override void BuildVisual()
        {
            Body.mass = 0.015f;
            var black = Kit.Mat("MarkerBlack", new Color(0.04f, 0.04f, 0.04f), 0.5f);
            var grey = Kit.Mat("MarkerGrey", new Color(0.6f, 0.6f, 0.6f), 0.4f);
            Kit.Part(transform, PrimitiveType.Cylinder, "Body", Vector3.zero, new Vector3(0.016f, 0.06f, 0.016f), black, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(transform, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0f, 0.035f), new Vector3(0.0165f, 0.008f, 0.0165f), grey, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(transform, PrimitiveType.Cylinder, "Nib", new Vector3(0f, 0f, 0.066f), new Vector3(0.006f, 0.008f, 0.006f), black, Quaternion.Euler(90f, 0f, 0f));
            Kit.Merge(transform);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.075f);
        }

        void Update()
        {
            if (!IsHeld || Trigger < 0.5f) { _hasLast = false; return; }
            Vector3 t = TipPosition;
            object target = FindTarget(t);
            if (target == null || target != _target) { _target = target; _stroke = 0f; _hasLast = false; }
            if (target == null) return;
            if (_hasLast)
            {
                float step = Vector3.Distance(t, _last);
                if (step < 0.05f) _stroke += step;
                Haptics.Tension(HolderInput, 0.15f);
            }
            _last = t; _hasLast = true;
            if (_stroke < StrokeNeeded) return;
            _stroke = 0f;
            Write(target);
        }

        object FindTarget(Vector3 t)
        {
            foreach (var cat in FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None))
                if (cat.State.Secured && !cat.State.TimeMarked && Vector3.Distance(cat.TimeLabelPoint, t) < 0.04f) return cat;
            foreach (var el in FindObjectsByType<ElasticTourniquetItem>(FindObjectsSortMode.None)) // w9d: Alpha / Esmarch band
                if (el.CanWriteTime && Vector3.Distance(el.TimeLabelPoint, t) < 0.045f) return el;
            foreach (var st in FieldMedic.Scenarios.SelfAid.SelfTourniquet.All) // w9b: the band on your own arm
                if (st != null && st.CanWrite && Vector3.Distance(st.TimeLabelPoint, t) < 0.045f) return st;
            foreach (var card in FindObjectsByType<CardItem>(FindObjectsSortMode.None))
                if (!card.Filled && Vector3.Distance(card.WritePoint, t) < 0.06f) return card;
            var c = NearestCasualty(t, 1.2f);
            if (c != null && c.rig != null && !WroteForehead && Vector3.Distance(Forehead(c.rig), t) < 0.05f) return c;
            return null;
        }

        public static Vector3 Forehead(CasualtyRig rig)
        {
            NpaItem.Face(rig, out var nostril, out _, out _, out var post, out var up);
            return nostril + up * 0.075f + post * 0.01f;
        }

        void Write(object target)
        {
            Haptics.Play(HolderInput, Haptics.PressClick);
            switch (target)
            {
                case TourniquetCAT cat:
                    if (cat.WriteTime()) LastFeedback = cat.LastFeedback;
                    break;
                case ElasticTourniquetItem el:
                    if (el.WriteTime()) LastFeedback = el.LastFeedback;
                    break;
                case CardItem card:
                    card.Fill();
                    break;
                case FieldMedic.Scenarios.SelfAid.SelfTourniquet st:
                    st.WriteTime();
                    break;
                case CasualtyController c:
                    WroteForehead = true;
                    LastFeedback = c.Protocol.Perform(MedAction.MarkForehead);
                    var head = c.rig.Bone(HumanBodyBones.Head);
                    NpaItem.Face(c.rig, out _, out _, out _, out var post, out var up);
                    var tm = Kit.Text(head, "ForeheadMark", "T " + ScenarioClock.Format(c.Patient.secondsSinceStart), 0.0042f,
                        new Color(0.02f, 0.02f, 0.03f), Vector3.zero, Quaternion.identity);
                    tm.transform.position = Forehead(c.rig) - post * 0.004f;
                    tm.transform.rotation = Quaternion.LookRotation(post, up);
                    break;
            }
        }
    }
}
