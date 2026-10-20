using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Trauma shears: squeeze (trigger) with the blade tip on the clothing and move along the seam; ~22 cm of
    /// cut opens the region (trouser leg, sleeve, shirt) so the wound can be packed / dressed / banded on skin.
    /// </summary>
    public sealed class ShearsItem : MedItem
    {
        public const float CutNeeded = 0.22f;
        /// <summary>w10d: cut length that opens the clothing outside the exam (shorter, ~15 cm).</summary>
        public const float ForgivingCutNeeded = 0.15f;
        public static float CutNeededNow => Forgiving.Scale(CutNeeded, ForgivingCutNeeded);
        public readonly Dictionary<BodyRegion, float> CutLength = new Dictionary<BodyRegion, float>();
        public Feedback LastFeedback { get; private set; }

        Transform _blade;
        Vector3 _lastTip;
        bool _hasLast;
        float _snip;
        readonly List<Transform> _cutMarks = new List<Transform>();

        public override void BuildVisual()
        {
            Body.mass = 0.09f;
            var handle = Kit.Mat("ShearsHandle", new Color(0.06f, 0.06f, 0.06f), 0.3f);
            var steel = Kit.Mat("Steel", new Color(0.75f, 0.76f, 0.78f), 0.85f, 0.9f);
            // two finger loops (flattened cylinders) and the angled blades
            Kit.Part(transform, PrimitiveType.Cylinder, "LoopA", new Vector3(-0.016f, 0f, -0.05f), new Vector3(0.032f, 0.006f, 0.045f), handle);
            Kit.Part(transform, PrimitiveType.Cylinder, "LoopB", new Vector3(0.016f, 0f, -0.05f), new Vector3(0.028f, 0.006f, 0.04f), handle);
            Kit.Part(transform, PrimitiveType.Cube, "Pivot", new Vector3(0f, 0.002f, -0.015f), new Vector3(0.016f, 0.008f, 0.016f), steel);
            Kit.Part(transform, PrimitiveType.Cube, "LowerBlade", new Vector3(0f, -0.004f, 0.025f), new Vector3(0.014f, 0.003f, 0.08f), steel, Quaternion.Euler(-15f, 0f, 0f));
            Kit.Merge(transform);
            _blade = new GameObject("UpperBlade").transform;
            _blade.SetParent(transform, false);
            _blade.localPosition = new Vector3(0f, 0.002f, -0.015f);
            Kit.Part(_blade, PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, 0.04f), new Vector3(0.012f, 0.003f, 0.078f), steel, Quaternion.Euler(-15f, 0f, 0f));
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, -0.014f, 0.068f);
        }

        Transform _artBlade;
        Quaternion _artBladeRest;
        Vector3 _artAxis;
        float _artSign = 1f, _artAngle;
        /// <summary>Opening of the imported blades (degrees; 0 = closed).</summary>
        public float BladeAngle => _artAngle;

        /// <summary>Imported shears (Astra): BladeA turns about the screw axis (the blades' shared origin), BladeB,
        /// screw and carabiner stay; the blunt tip of BladeA is the working tip.</summary>
        public override void OnArtApplied(Transform art)
        {
            _artBlade = ArtModels.FindPart(art, "BladeA");
            var other = ArtModels.FindPart(art, "BladeB");
            if (_artBlade == null) return;
            _artBladeRest = _artBlade.localRotation;
            _artAxis = _artBlade.parent.InverseTransformDirection(transform.up).normalized;
            // open away from BladeB: the sign that moves BladeA's tip away from BladeB's tip
            var tipA = ArtModels.TipOf(transform, art, "BladeA");
            var tipB = other != null ? ArtModels.TipOf(transform, art, "BladeB") : null;
            if (tipA.HasValue && tipB.HasValue)
            {
                Vector3 pivot = transform.InverseTransformPoint(_artBlade.position);
                Vector3 a = tipA.Value - pivot;
                float plus = Vector3.Distance(Quaternion.AngleAxis(12f, Vector3.up) * a + pivot, tipB.Value);
                float minus = Vector3.Distance(Quaternion.AngleAxis(-12f, Vector3.up) * a + pivot, tipB.Value);
                _artSign = plus >= minus ? 1f : -1f;
                tip.localPosition = tipA.Value + new Vector3(0f, 0f, -0.004f);
            }
            SetArtAngle(0f);
        }

        void SetArtAngle(float deg)
        {
            _artAngle = deg;
            if (_artBlade != null) _artBlade.localRotation = Quaternion.AngleAxis(deg * _artSign, _artAxis) * _artBladeRest;
        }

        void Update()
        {
            float trig = Trigger;
            _snip += Time.deltaTime * (trig > 0.5f ? 14f : 0f);
            _blade.localRotation = Quaternion.Euler(0f, trig > 0.5f ? Mathf.Abs(Mathf.Sin(_snip)) * 14f : 10f, 0f);
            // real blades: closed in the kit, ready-open in the hand, snipping open/close while squeezed (<= 35 deg, Astra)
            if (_artBlade != null)
            {
                float want = InPouch ? 0f : !IsHeld ? 6f : trig > 0.5f ? Mathf.Abs(Mathf.Sin(_snip)) * 30f : 22f;
                SetArtAngle(Mathf.MoveTowards(_artAngle, want, Time.deltaTime * 400f));
            }
            if (!IsHeld || trig < 0.5f) { _hasLast = false; return; }
            var c = NearestCasualty(TipPosition, 1.4f);
            if (c == null || c.rig == null || c.Interaction == null) { _hasLast = false; return; }
            // tip on the clothing of a body region
            CasualtyBodyPart part = null; float best = Forgiving.Scale(0.045f, 0.07f); Vector3 surf = Vector3.zero; // w10d: forgiving 7 cm
            BodyRegion region = BodyRegion.Abdomen;
            // limbs: skeleton geometry of the current pose (sleeve / trouser leg)
            if (LimbMath.Nearest(c.rig, TipPosition, out var limb, out float lpos, out float skin) && skin < Forgiving.Scale(0.04f, 0.07f))
            {
                region = LimbGeometry.RegionAt(LimbMath.Id(limb), Mathf.Min(lpos, 2.4f));
                part = c.rig.Part(region);
                Vector3 ax = LimbMath.PointAt(c.rig, limb, lpos);
                surf = ax + (TipPosition - ax).normalized * LimbMath.RadiusAt(limb, lpos);
            }
            else
            {
                foreach (var p in c.rig.Parts)
                {
                    if (p == null || p.region is BodyRegion.Head or BodyRegion.Neck) continue;
                    var col = p.GetComponent<Collider>();
                    Vector3 q = col.ClosestPoint(TipPosition);
                    float d = Vector3.Distance(q, TipPosition);
                    if (d < best) { best = d; part = p; surf = q; region = p.region; }
                }
            }
            if (part == null) { _hasLast = false; return; }
            Vector3 t = TipPosition;
            if (_hasLast)
            {
                float step = Vector3.Distance(t, _lastTip);
                if (step <= 0.003f) return; // accumulate from the last counted point (frame-rate independent)
                if (step < 0.08f)
                {
                    CutLength.TryGetValue(region, out float len);
                    float before = len;
                    len += step;
                    CutLength[region] = len;
                    Haptics.Play(HolderInput, Haptics.Contact, 1.2f);
                    if (Mathf.Floor(len / 0.03f) > Mathf.Floor(before / 0.03f)) Mark(surf, part);
                    if (before < CutNeededNow && len >= CutNeededNow) Open(c, region);
                }
            }
            _lastTip = t;
            _hasLast = true;
        }

        void Open(CasualtyController c, BodyRegion r)
        {
            c.Interaction.Expose(r);
            LastFeedback = c.Protocol.Perform(MedAction.CutClothing, site: r);
            Haptics.Play(HolderInput, Haptics.PressClick);
        }

        /// <summary>Visible cut line: small slits of skin showing through the cloth.</summary>
        void Mark(Vector3 surf, CasualtyBodyPart part)
        {
            if (_cutMarks.Count > 60) return;
            var col = part.GetComponent<Collider>();
            Vector3 n = (surf - col.bounds.center).normalized;
            var go = new GameObject("CutSlit");
            go.transform.SetParent(part.bone, true);
            go.transform.position = surf + n * 0.004f;
            go.transform.rotation = Quaternion.LookRotation(-n, transform.forward);
            go.transform.localScale = Vector3.one;
            var q = Kit.MeshPart(go.transform, "Q", Casualty.Blood.BloodPoolManager.Quad(), ProceduralTex.ExposedSkinMaterial(), Vector3.zero,
                Quaternion.identity, new Vector3(0.018f, 0.04f, 1f));
            _cutMarks.Add(go.transform);
        }
    }
}
