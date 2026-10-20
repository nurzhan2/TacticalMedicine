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

        Transform _artCap;
        Vector3 _capRestPos, _capPostedPos;
        Quaternion _capRestRot, _capPostedRot;
        float _capT;
        /// <summary>0 = cap on the tip, 1 = posted on the back end.</summary>
        public float CapOff => _capT;

        /// <summary>Imported marker: the working tip is the real nib; the cap comes off when the marker is taken out
        /// of the kit and is posted on the back end (and goes back on the tip in the kit).</summary>
        public override void OnArtApplied(Transform art)
        {
            var nib = ArtModels.TipOf(transform, art, "Body");
            if (nib.HasValue) tip.localPosition = nib.Value + new Vector3(0f, 0f, 0.002f);
            _artCap = ArtModels.FindPart(art, "Cap");
            var body = ArtModels.FindPart(art, "Body");
            if (_artCap == null || body == null) return;
            _capRestPos = _artCap.localPosition;
            _capRestRot = _artCap.localRotation;
            var bb = body.GetComponent<MeshFilter>().sharedMesh.bounds;
            var cb = _artCap.GetComponent<MeshFilter>().sharedMesh.bounds;
            float backZ = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var c = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                backZ = Mathf.Min(backZ, transform.InverseTransformPoint(body.TransformPoint(c)).z);
            }
            // cap centre (in its parent) goes to the back end, turned around so its open end faces the body
            Vector3 capCenterWorld = _artCap.TransformPoint(cb.center);
            float capZ = transform.InverseTransformPoint(capCenterWorld).z;
            Vector3 capCenterParent = _artCap.parent.InverseTransformPoint(capCenterWorld);
            var turn = Quaternion.AngleAxis(180f, _artCap.parent.InverseTransformDirection(transform.up));
            _capPostedRot = turn * _capRestRot;
            Vector3 shift = _artCap.parent.InverseTransformVector(transform.TransformVector(new Vector3(0f, 0f, backZ + 0.008f - capZ)));
            _capPostedPos = turn * (_capRestPos - capCenterParent) + capCenterParent + shift;
        }

        void Update()
        {
            if (_artCap != null)
            {
                float want = InPouch ? 0f : 1f;
                if (!Mathf.Approximately(_capT, want))
                {
                    _capT = Mathf.MoveTowards(_capT, want, Time.deltaTime / 0.35f);
                    float k = _capT * _capT * (3f - 2f * _capT);
                    _artCap.localPosition = Vector3.Lerp(_capRestPos, _capPostedPos, k);
                    _artCap.localRotation = Quaternion.Slerp(_capRestRot, _capPostedRot, k);
                }
            }
            if (!IsHeld) { _hasLast = false; ResetDwell(null); return; }
            Vector3 t = TipPosition;
            if (Forgiving.On) GlowTargets(t);
            object target = FindTarget(t);
            // w10d (owner: the zones are too small): outside the exam simply holding the tip at the TIME tab / card
            // (within ~7 cm) for DwellNeeded seconds writes; the forehead still needs the trigger squeeze
            if (Forgiving.On) ForgivingDwell(target);
            if (Trigger < 0.5f) { _hasLast = false; return; }
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
            ResetDwell(null);
            Write(target);
        }

        // ------------------------------------------------------------------ w10d forgiving write
        /// <summary>Seconds the tip has to stay at the target outside the exam.</summary>
        public const float DwellNeeded = 0.5f;
        /// <summary>Writing reach outside the exam (exam: the strict 4-6 cm stroke radii).</summary>
        public const float ForgivingReach = 0.07f;
        float _dwell;
        object _dwellTarget;
        /// <summary>0..1 progress of the current dwell write (tests / UI).</summary>
        public float DwellProgress => Mathf.Clamp01(_dwell / DwellNeeded);

        static float Reach(float strict) => Forgiving.Scale(strict, Mathf.Max(strict, ForgivingReach));

        void ResetDwell(object target) { _dwell = 0f; _dwellTarget = target; }

        void ForgivingDwell(object target)
        {
            bool needsSqueeze = target is CasualtyController;
            if (target == null || (needsSqueeze && Trigger < 0.5f)) { ResetDwell(null); return; }
            if (target != _dwellTarget)
            {
                ResetDwell(target);
                Haptics.Play(HolderInput, Haptics.PressStart); // tick: started writing
            }
            _dwell += Time.deltaTime;
            Haptics.Tension(HolderInput, 0.12f + 0.25f * DwellProgress);
            if (_dwell < DwellNeeded) return;
            ResetDwell(null);
            _stroke = 0f; _hasLast = false;
            Write(target); // stronger click inside
        }

        /// <summary>Writable targets glow while the marker is near (~2x the writing reach), brighter when in reach.</summary>
        void GlowTargets(Vector3 t)
        {
            float near = ForgivingReach * 2.2f;
            foreach (var cat in FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None))
                if (cat.State.Secured && !cat.State.TimeMarked) Glow(cat, cat.TimeLabelPoint, t, near);
            foreach (var el in FindObjectsByType<ElasticTourniquetItem>(FindObjectsSortMode.None))
                if (el.CanWriteTime) Glow(el, el.TimeLabelPoint, t, near);
            foreach (var st in FieldMedic.Scenarios.SelfAid.SelfTourniquet.All)
                if (st != null && st.CanWrite) Glow(st, st.TimeLabelPoint, t, near);
            foreach (var card in FindObjectsByType<CardItem>(FindObjectsSortMode.None))
                if (!card.Filled) Glow(card, card.WritePoint, t, near);
        }

        void Glow(object key, Vector3 at, Vector3 tip, float near)
        {
            float d = Vector3.Distance(at, tip);
            if (d > near) return;
            bool inReach = d < ForgivingReach;
            float k = inReach && _dwellTarget == key ? DwellProgress : 0f;
            ZoneGlow.Show(key, at, inReach ? 0.09f + 0.04f * k : 0.07f, inReach ? ZoneGlow.Green : ZoneGlow.Gold, inReach ? 0.7f + 0.3f * k : 0.45f);
        }

        object FindTarget(Vector3 t)
        {
            foreach (var cat in FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None))
                if (cat.State.Secured && !cat.State.TimeMarked && Vector3.Distance(cat.TimeLabelPoint, t) < Reach(0.04f)) return cat;
            foreach (var el in FindObjectsByType<ElasticTourniquetItem>(FindObjectsSortMode.None)) // w9d: Alpha / Esmarch band
                if (el.CanWriteTime && Vector3.Distance(el.TimeLabelPoint, t) < Reach(0.045f)) return el;
            foreach (var st in FieldMedic.Scenarios.SelfAid.SelfTourniquet.All) // w9b: the band on your own arm
                if (st != null && st.CanWrite && Vector3.Distance(st.TimeLabelPoint, t) < Reach(0.045f)) return st;
            foreach (var card in FindObjectsByType<CardItem>(FindObjectsSortMode.None))
                if (!card.Filled && Vector3.Distance(card.WritePoint, t) < Reach(0.06f)) return card;
            var c = NearestCasualty(t, 1.2f);
            if (c != null && c.rig != null && !WroteForehead && Vector3.Distance(Forehead(c.rig), t) < Reach(0.05f)) return c;
            return null;
        }

        public static Vector3 Forehead(CasualtyRig rig)
        {
            NpaItem.Face(rig, out var nostril, out _, out _, out var post, out var up);
            return nostril + up * 0.075f + post * 0.01f;
        }

        /// <summary>Completed strokes (any target) — bots stop writing once it moves.</summary>
        public int Writes { get; private set; }

        void Write(object target)
        {
            Writes++;
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
