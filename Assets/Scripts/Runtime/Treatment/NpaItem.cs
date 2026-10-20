using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Nasopharyngeal airway (28 Fr). Measure: flange at the nostril, tip at the ear lobe. Lubricate: squeeze the
    /// lube packet (trigger) on the tip. Insert into the right nostril, bevel toward the septum, straight back
    /// "toward the ground" (perpendicular to the face) — aimed up at the forehead it sticks on the turbinates;
    /// twist it to get past resistance. Fully in → flange at the nostril, airway open.
    /// </summary>
    public sealed class NpaItem : MedItem
    {
        public const float Length = 0.13f;
        public NpaProcedure Npa = new NpaProcedure();
        public Feedback LastFeedback { get; private set; }
        public bool Engaged { get; private set; }
        protected override bool HoldsOwnPose => Engaged;

        float _measureHold;
        Vector3 _palmAtEngage;
        float _depthAtEngage;
        Quaternion _prevRot;
        Renderer _tube;
        CasualtyController _c;

        public override void BuildVisual()
        {
            Body.mass = 0.01f;
            var amber = Kit.Mat("NpaAmber", new Color(0.85f, 0.55f, 0.25f), 0.75f);
            Kit.Part(transform, PrimitiveType.Cylinder, "Flange", new Vector3(0f, 0f, -Length * 0.5f), new Vector3(0.026f, 0.002f, 0.02f), amber, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(transform, PrimitiveType.Cube, "Bevel", new Vector3(0.002f, 0f, Length * 0.5f - 0.004f), new Vector3(0.006f, 0.009f, 0.012f), amber, Quaternion.Euler(0f, 30f, 0f));
            Kit.Merge(transform);
            _tube = Kit.Part(transform, PrimitiveType.Cylinder, "Tube", Vector3.zero, new Vector3(0.0095f, Length * 0.5f, 0.0095f), amber, Quaternion.Euler(90f, 0f, 0f)).GetComponent<Renderer>();
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, 0f, Length * 0.5f);
        }

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Attached && base.CanBeGrabbedBy(hand);

        /// <summary>Head frame: nostril (right), ear lobes, posterior direction ("toward the ground" when supine).</summary>
        public static void Face(CasualtyRig rig, out Vector3 nostril, out Vector3 earL, out Vector3 earR, out Vector3 posterior, out Vector3 headUp)
        {
            var head = rig.Bone(HumanBodyBones.Head);
            var chest = rig.ChestBone;
            // the head's own forward/up carried by the animated head bone (bone axes are arbitrary)
            Quaternion rel = head.rotation * Quaternion.Inverse(chest != null ? chest.rotation : rig.transform.rotation);
            Vector3 fwd = (rel * rig.BodyForward).normalized;
            headUp = (rel * rig.BodyUp).normalized;
            Vector3 right = Vector3.Cross(headUp, fwd).normalized;
            // per-model offsets (w10c: measured on the soldier's face by the prefab builder)
            Vector3 n = rig.faceNostril, e = rig.faceEar;
            Vector3 h = head.position;
            nostril = h + right * n.x + headUp * n.y + fwd * n.z;
            earL = h - right * e.x + headUp * e.y + fwd * e.z;
            earR = h + right * e.x + headUp * e.y + fwd * e.z;
            posterior = -fwd;
        }

        /// <summary>Imported NPA: sealed in its package in the kit, the bare tube in the hand; the bevelled end of the
        /// delivered tube is the working tip.</summary>
        public override void OnArtApplied(Transform art)
        {
            var p = ArtModels.TipOf(transform, art, "Tube");
            if (p.HasValue) tip.localPosition = new Vector3(0f, 0f, p.Value.z);
        }

        void Update()
        {
            if (Attached) return;
            if (!IsHeld) { if (Engaged) Disengage(); return; }
            _c = NearestCasualty(TipPosition, 1.2f);
            if (_c == null || _c.rig == null || _c.rig.Bone(HumanBodyBones.Head) == null) return;
            Face(_c.rig, out var nostril, out var earL, out var earR, out var post, out _);
            Vector3 flange = transform.TransformPoint(new Vector3(0f, 0f, -Length * 0.5f));

            if (!Engaged)
            {
                // measuring: flange at the nostril, tip at an ear lobe
                // w10d: forgiving sizes outside the exam (ear 7 cm, flange 7 cm, nostril 5 cm)
                float ear = Forgiving.Scale(0.045f, 0.07f);
                bool atEar = Vector3.Distance(TipPosition, earL) < ear || Vector3.Distance(TipPosition, earR) < ear;
                if (!Npa.measured && atEar && Vector3.Distance(flange, nostril) < Forgiving.Scale(0.04f, 0.07f))
                {
                    _measureHold += Time.deltaTime;
                    if (_measureHold > 0.4f) { Npa.measured = true; Haptics.Play(HolderInput, Haptics.PressClick); }
                }
                else _measureHold = 0f;
                Lube();
                float toNose = Vector3.Distance(TipPosition, nostril);
                if (Forgiving.On && toNose < 0.3f)
                    ZoneGlow.Show(this, nostril, toNose < 0.05f ? 0.07f : 0.05f, toNose < 0.05f ? ZoneGlow.Green : ZoneGlow.Gold, toNose < 0.05f ? 0.9f : 0.5f);
                if (toNose < Forgiving.Scale(0.025f, 0.05f)) Engage(nostril);
                return;
            }
            Advance(nostril, post);
        }

        void Lube()
        {
            if (Npa.lubed) return;
            foreach (var h in HandGrabber.All)
            {
                if (h == null || h.Held == null || h.Held.GetComponent<MedItem>() is not MedItem m || m.kind != MedItemKind.Lubricant) continue;
                if (h.Input.Trigger > 0.6f && Vector3.Distance(m.TipPosition, TipPosition) < Forgiving.Scale(0.05f, 0.09f))
                {
                    Npa.lubed = true;
                    var gloss = Kit.Mat("NpaLubed", new Color(0.9f, 0.62f, 0.3f), 0.95f);
                    if (_tube != null) _tube.sharedMaterial = gloss;
                    Haptics.Play(h.Input, Haptics.Contact, 2f);
                }
            }
        }

        void Engage(Vector3 nostril)
        {
            Engaged = true;
            Body.isKinematic = true;
            _palmAtEngage = Holder.PalmPosition;
            _depthAtEngage = Npa.depthCm;
            _prevRot = Holder.transform.rotation;
            // bevel (+X of the tube) toward the septum = toward the midline from the right nostril
            Face(_c.rig, out _, out var earL, out var earR, out _, out _);
            Npa.bevelToSeptum = Vector3.Dot(transform.right, (earL - earR).normalized) > 0f;
        }

        void Disengage()
        {
            Engaged = false;
            if (!Attached) { Body.isKinematic = IsHeld; Npa.depthCm = 0f; Npa.resistance = false; }
        }

        protected override void FixedUpdate()
        {
            if (Engaged || Attached) return; // pose driven by the nostril constraint
            base.FixedUpdate();
        }

        void Advance(Vector3 nostril, Vector3 post)
        {
            float angle = Vector3.Angle(transform.forward, post);
            // the hand pushes along the tube; the procedure decides how far it actually goes
            float wanted = _depthAtEngage + Vector3.Dot(Holder.PalmPosition - _palmAtEngage, transform.forward) * 100f;
            if (wanted > Npa.depthCm) Npa.Advance(wanted - Npa.depthCm, angle);
            (Holder.transform.rotation * Quaternion.Inverse(_prevRot)).ToAngleAxis(out float a, out _);
            _prevRot = Holder.transform.rotation;
            if (a > 180f) a = 360f - a;
            Npa.Rotate(a);
            if (Npa.resistance) Haptics.Tension(HolderInput, 0.7f);
            // the tube slides along its own axis into the nose
            Vector3 dir = Vector3.Slerp(transform.forward, Holder.transform.rotation * Vector3.forward, 0.2f).normalized;
            if (Npa.depthCm > 1f) dir = transform.forward; // once in, the nose holds the direction
            transform.SetPositionAndRotation(nostril + dir * (Npa.depthCm / 100f - Length * 0.5f), Quaternion.LookRotation(dir, transform.up));
            // pulled back out
            Vector3 handMove = Holder.PalmPosition - _palmAtEngage;
            if (Vector3.Dot(handMove, transform.forward) < -0.05f || Vector3.ProjectOnPlane(handMove, transform.forward).magnitude > Forgiving.Scale(0.1f, 0.15f))
            { Disengage(); return; }
            if (!Npa.Inserted) return;
            var head = _c.rig.Bone(HumanBodyBones.Head);
            var holder = Holder;
            ForceRelease();
            AttachTo(head);
            Engaged = false;
            LastFeedback = _c.Protocol.NasalAirwayInserted(Npa);
            Haptics.Play(holder != null ? holder.Input : null, Haptics.PressClick);
        }
    }
}
