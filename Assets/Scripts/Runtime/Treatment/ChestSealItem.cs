using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Vented chest seal: hold the package, tear it open at the notch with the other hand, peel the backing
    /// by its tab, wipe the blood around the wound with the open palm, place the seal with the palm (ideally at
    /// the end of an exhale — watch the chest), then press the edges all around. "Burp": pinch the edge
    /// (trigger) and lift it to let trapped air out, then press it down again.
    /// </summary>
    public sealed class ChestSealItem : MedItem
    {
        public ChestSealProcedure Proc = new ChestSealProcedure();
        public WoundCare PlacedOn { get; private set; }
        public ProxyHandle TearNotch { get; private set; }
        public ProxyHandle BackingTab { get; private set; }
        public Feedback LastFeedback { get; private set; }
        public string LastHint { get; private set; }

        Transform _package, _seal, _backing, _edgeLift;
        int _sectorsPressed; // bitmask of 6 edge sectors
        bool _reported, _pinching;
        float _pinchStartH;
        float _tearMax, _peelMax;

        public override void BuildVisual()
        {
            Body.mass = 0.04f;
            allowTwoHands = false; // the second hand goes for the notch / tab
            var pack = Kit.Mat("SealPack", new Color(0.12f, 0.13f, 0.1f), 0.5f);
            var label = Kit.Mat("SealLabel", new Color(0.75f, 0.1f, 0.08f), 0.3f);
            var gel = Kit.Mat("SealGel", new Color(0.55f, 0.58f, 0.6f), 0.85f);
            var valve = Kit.Mat("SealValve", new Color(0.08f, 0.08f, 0.08f), 0.5f);
            var backing = Kit.Mat("SealBacking", new Color(0.92f, 0.92f, 0.88f), 0.25f);

            _package = new GameObject("Package").transform;
            _package.SetParent(transform, false);
            Kit.Part(_package, PrimitiveType.Cube, "Foil", Vector3.zero, new Vector3(0.15f, 0.006f, 0.17f), pack);
            Kit.Part(_package, PrimitiveType.Cube, "Label", new Vector3(0f, 0.0035f, 0.02f), new Vector3(0.09f, 0.0006f, 0.05f), label);
            Kit.Merge(_package);

            _seal = new GameObject("Seal").transform;
            _seal.SetParent(transform, false);
            Kit.Part(_seal, PrimitiveType.Cylinder, "Disc", Vector3.zero, new Vector3(0.13f, 0.0012f, 0.13f), gel);
            Kit.Part(_seal, PrimitiveType.Cylinder, "Valve", new Vector3(0f, 0.004f, 0f), new Vector3(0.028f, 0.004f, 0.028f), valve);
            Kit.Part(_seal, PrimitiveType.Cube, "Tab", new Vector3(0f, 0f, 0.072f), new Vector3(0.025f, 0.0012f, 0.02f), valve);
            Kit.Merge(_seal);
            _backing = Kit.Part(_seal, PrimitiveType.Cylinder, "Backing", new Vector3(0f, -0.0015f, 0f), new Vector3(0.132f, 0.0008f, 0.132f), backing).transform;
            _edgeLift = Kit.Part(_seal, PrimitiveType.Cube, "EdgeLift", new Vector3(0.06f, 0.006f, 0f), new Vector3(0.02f, 0.0012f, 0.04f), gel,
                Quaternion.Euler(0f, 0f, 25f)).transform;
            _edgeLift.gameObject.SetActive(false);
            _seal.gameObject.SetActive(false);

            TearNotch = ProxyHandle.Create<ProxyHandle>("Grab_TearNotch", transform, transform.TransformPoint(new Vector3(0.07f, 0f, 0.08f)), 0.035f, 6);
            BackingTab = ProxyHandle.Create<ProxyHandle>("Grab_BackingTab", transform, transform.TransformPoint(new Vector3(0f, -0.002f, -0.075f)), 0.035f, 6);
            BackingTab.gameObject.SetActive(false);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, -0.004f, 0f);
        }

        void Update()
        {
            switch (Proc.step)
            {
                case ChestSealProcedure.Step.Packaged: Tear(); break;
                case ChestSealProcedure.Step.Torn: Peel(); break;
                case ChestSealProcedure.Step.Peeled: if (IsHeld) TryPlace(); break;
                case ChestSealProcedure.Step.Placed:
                case ChestSealProcedure.Step.Sealed: PressAndBurp(); break;
            }
        }

        Transform _artSeal, _artLiner;

        /// <summary>Imported chest seal: the delivered round Seal (valve up) and its Liner replace the procedural disc
        /// once the package is torn; the liner peels with the backing tab.</summary>
        public override void OnArtApplied(Transform art)
        {
            _artSeal = ArtModels.Graft(art, "Seal", _seal, Quaternion.Euler(-90f, 0f, 0f), new Vector3(0f, 0.0005f, 0f), false);
            _artLiner = ArtModels.Graft(art, "Liner", _seal, Quaternion.Euler(-90f, 0f, 0f), new Vector3(0f, -0.0022f, 0.005f), false);
            if (_artSeal != null)
                foreach (Transform c in _seal)
                    if (c.name.StartsWith("Merged_")) foreach (var r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (_artLiner != null) foreach (var r in _backing.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        void Tear()
        {
            TearNotch.gameObject.SetActive(IsHeld);
            if (TearNotch.Hand == null) { _tearMax = 0f; return; }
            _tearMax = Mathf.Max(_tearMax, TearNotch.PalmDelta.magnitude);
            Haptics.Tension(TearNotch.Input, _tearMax / 0.08f * 0.5f);
            if (_tearMax < 0.08f || !Proc.Tear()) return;
            Haptics.Play(TearNotch.Input, Haptics.PressClick);
            TearNotch.ForceRelease();
            TearNotch.gameObject.SetActive(false);
            _package.gameObject.SetActive(false);
            ShowCarriedLook(false);
            _seal.gameObject.SetActive(true);
            BackingTab.gameObject.SetActive(true);
        }

        void Peel()
        {
            if (BackingTab.Hand == null) { _peelMax = 0f; return; }
            _peelMax = Mathf.Max(_peelMax, BackingTab.PalmDelta.magnitude);
            _backing.localPosition = new Vector3(0f, -0.0015f - _peelMax * 0.2f, -_peelMax * 0.5f);
            if (_artLiner != null) _artLiner.localPosition = _backing.localPosition + new Vector3(0f, -0.0007f, 0.005f) - _artLiner.localRotation * _artLiner.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            if (_peelMax < 0.08f || !Proc.Peel()) return;
            Haptics.Play(BackingTab.Input, Haptics.PressClick);
            BackingTab.ForceRelease();
            BackingTab.gameObject.SetActive(false);
            _backing.gameObject.SetActive(false);
            if (_artLiner != null) _artLiner.gameObject.SetActive(false);
        }

        void TryPlace()
        {
            var c = NearestCasualty(TipPosition, 1.5f);
            if (c == null) return;
            foreach (var s in c.WoundSites)
            {
                if (s == null || s.Wound == null || !s.Wound.IsChest) continue;
                var care = s.GetComponent<WoundCare>();
                if (care == null || care.Seal != null) continue;
                if (care.Lateral(TipPosition) > 0.05f || care.Height(TipPosition) > 0.025f) continue;
                Proc.wipes = care.Wipes;
                bool exhaling = c.rig == null || c.rig.BreathSignal <= 0.1f;
                if (!Proc.Place(exhaling))
                {
                    LastHint = "Наклейка не держится на крови — вытрите кожу вокруг раны ладонью.";
                    Haptics.Play(HolderInput, Haptics.Contact, 3f);
                    return;
                }
                PlacedOn = care;
                care.Seal = this;
                var holder = Holder;
                ForceRelease();
                AttachTo(s.transform);
                transform.localPosition = new Vector3(0f, 0f, 0.003f);
                transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // item up (+Y) = wound normal (+Z)
                Haptics.Play(holder != null ? holder.Input : null, Haptics.PressClick);
                LastHint = exhaling ? null : "Лучше накладывать в конце выдоха.";
                return;
            }
        }

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Attached && base.CanBeGrabbedBy(hand);

        void PressAndBurp()
        {
            var care = PlacedOn;
            if (care == null) return;
            foreach (var h in HandGrabber.All)
            {
                if (h == null || h.Input == null || h.Held != null) continue;
                foreach (var p in new[] { h.FingertipPosition, h.PalmPosition })
                {
                    float lat = care.Lateral(p), height = care.Height(p);
                    // edge press: fingers run around the rim
                    if (Proc.step == ChestSealProcedure.Step.Placed && lat > 0.03f && lat < 0.08f && height < 0.012f && h.Input.Trigger < 0.6f)
                    {
                        Vector3 v = Vector3.ProjectOnPlane(p - care.transform.position, care.Normal);
                        float ang = Vector3.SignedAngle(care.transform.up, v, care.Normal) + 180f;
                        int sector = Mathf.Clamp((int)(ang / 60f), 0, 5);
                        if ((_sectorsPressed & (1 << sector)) == 0)
                        {
                            _sectorsPressed |= 1 << sector;
                            Haptics.Play(h.Input, Haptics.Contact, 2f);
                            if (Proc.PressEdge()) OnSealed();
                        }
                    }
                }
                // burp: pinch the edge and lift it
                Vector3 f = h.FingertipPosition;
                float fl = care.Lateral(f), fh = care.Height(f);
                if (Proc.step == ChestSealProcedure.Step.Sealed && h.Input.Trigger > 0.6f && fl > 0.035f && fl < 0.09f)
                {
                    if (!_pinching && fh < 0.02f) { _pinching = true; _pinchStartH = fh; }
                    else if (_pinching && fh - _pinchStartH > 0.015f)
                    {
                        _pinching = false;
                        Proc.Burp();
                        _sectorsPressed = 0;
                        _edgeLift.gameObject.SetActive(true);
                        LastFeedback = care.casualty.Protocol.Perform(MedAction.SealBurp);
                        Haptics.Play(h.Input, Haptics.PressClick);
                        TreatmentAudio.Hiss(care.transform.position, 0.6f);
                    }
                }
                else if (h.Input.Trigger < 0.3f) _pinching = false;
            }
        }

        void OnSealed()
        {
            _edgeLift.gameObject.SetActive(false);
            if (_reported) return;
            _reported = true;
            LastFeedback = PlacedOn.casualty.Protocol.ChestSealApplied(PlacedOn.Wound, Proc);
        }
    }
}
