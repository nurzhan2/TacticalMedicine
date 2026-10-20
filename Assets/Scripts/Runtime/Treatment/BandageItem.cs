using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Israeli (emergency) bandage: press the pad onto the wound, wrap the roll around the limb (hand circles
    /// the limb), reverse direction through the pressure bar (or twist the wrist at the bar), at least two
    /// more tight wraps, then let go to hook the closure bar.
    /// </summary>
    public sealed class BandageItem : MedItem
    {
        public WoundCare ApplyingTo { get; private set; }
        public string LastHint { get; private set; }

        Transform _pad;
        float _prevAngle, _dirAccum, _wristAccum;
        bool _angleValid;
        int _dir;
        Quaternion _prevRot;

        public override void BuildVisual()
        {
            Body.mass = 0.12f;
            var cream = Kit.Mat("BandageWrap", new Color(0.78f, 0.76f, 0.66f), 0.12f);
            var padM = Kit.Mat("BandagePad", new Color(0.85f, 0.84f, 0.78f), 0.1f);
            var bar = Kit.Mat("BandageBar", new Color(0.12f, 0.12f, 0.12f), 0.3f);
            Kit.Part(transform, PrimitiveType.Cylinder, "Roll", Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f), cream, Quaternion.Euler(0f, 0f, 90f));
            Kit.Merge(transform);
            _pad = new GameObject("PadGroup").transform;
            _pad.SetParent(transform, false);
            _pad.localPosition = new Vector3(0f, -0.005f, 0.075f);
            Kit.Part(_pad, PrimitiveType.Cube, "Pad", Vector3.zero, new Vector3(0.09f, 0.014f, 0.1f), padM);
            Kit.Part(_pad, PrimitiveType.Cube, "Bar", new Vector3(0f, 0.01f, 0f), new Vector3(0.05f, 0.008f, 0.016f), bar);
            Kit.Part(_pad, PrimitiveType.Cube, "Strip", new Vector3(0f, 0.0f, -0.045f), new Vector3(0.09f, 0.004f, 0.03f), cream);
            Kit.Merge(_pad);
            tip = new GameObject("Tip").transform;
            tip.SetParent(_pad, false);
            tip.localPosition = new Vector3(0f, -0.008f, 0f);
        }

        /// <summary>Imported Israeli bandage: sealed pack in the kit, opened pack in the hand with the pad pulled out
        /// (the procedural pad group = the logic part that goes onto the wound).</summary>
        protected override void OnPouchState(bool inPouch)
        {
            base.OnPouchState(inPouch);
            if (ArtRoot != null && _pad != null && ApplyingTo == null) _pad.gameObject.SetActive(!inPouch);
        }

        void Update()
        {
            if (!IsHeld) return;
            if (ApplyingTo == null) TryPlacePad();
            else Wrap();
        }

        void TryPlacePad()
        {
            var c = NearestCasualty(TipPosition, 1.5f);
            if (c == null) return;
            foreach (var s in c.WoundSites)
            {
                if (s == null) continue;
                var care = s.GetComponent<WoundCare>();
                if (care == null || care.Wound.IsChest || care.Bandage.step != BandageProcedure.Step.None) continue;
                if (care.Lateral(TipPosition) > 0.06f || care.Height(TipPosition) > 0.015f) continue;
                if (!care.SkinAccessible) { LastHint = "Повязка накладывается на рану — сначала разрежьте одежду."; continue; }
                if (!care.Bandage.PlacePad()) continue;
                ApplyingTo = care;
                _pad.gameObject.SetActive(false);
                _angleValid = false;
                _dir = 0; _dirAccum = 0f; _wristAccum = 0f;
                _prevRot = Holder.transform.rotation;
                Haptics.Play(HolderInput, Haptics.PressClick);
                return;
            }
        }

        void Wrap()
        {
            var care = ApplyingTo;
            var bd = care.Bandage;
            Vector3 axis = care.WrapAxis(out Vector3 center, out _);
            Vector3 palm = Holder.PalmPosition;
            Vector3 rel = Vector3.ProjectOnPlane(palm - center, axis);
            if (rel.magnitude > 0.03f && rel.magnitude < 0.45f)
            {
                Vector3 refDir = Vector3.ProjectOnPlane(care.Normal, axis).normalized;
                float ang = Vector3.SignedAngle(refDir, rel, axis);
                if (_angleValid)
                {
                    float d = Mathf.DeltaAngle(_prevAngle, ang);
                    if (Mathf.Abs(d) > 0.01f)
                    {
                        int sign = d > 0 ? 1 : -1;
                        if (_dir == 0) _dir = sign;
                        if (sign != _dir)
                        {
                            _dirAccum += Mathf.Abs(d);
                            // reversal at the pressure bar: the classic Israeli-bandage move
                            if (_dirAccum > 30f && bd.TwistBar()) { Haptics.Play(HolderInput, Haptics.PressClick); care.casualty.Pain.AddSpike(0.2f); }
                            if (_dirAccum > 30f) { _dir = sign; _dirAccum = 0f; }
                        }
                        else _dirAccum = 0f;
                        bd.Wrap(d);
                        Haptics.Tension(HolderInput, bd.Pressure * 0.6f);
                    }
                }
                _prevAngle = ang;
                _angleValid = true;
            }
            else _angleValid = false;

            // alternative: twist the wrist at the bar
            Quaternion hr = Holder.transform.rotation;
            (hr * Quaternion.Inverse(_prevRot)).ToAngleAxis(out float a, out Vector3 ax);
            _prevRot = hr;
            if (a > 180f) a -= 360f;
            if (!float.IsNaN(ax.x) && Vector3.Distance(palm, care.transform.position) < 0.2f)
            {
                _wristAccum = Mathf.Max(0f, _wristAccum + Mathf.Abs(a) - Time.deltaTime * 60f);
                if (_wristAccum > 120f && bd.TwistBar()) Haptics.Play(HolderInput, Haptics.PressClick);
            }
        }

        protected override void OnItemReleased(Interaction.HandGrabber hand)
        {
            if (ApplyingTo == null) return;
            if (ApplyingTo.Bandage.Clip())
            {
                Haptics.Play(hand.Input, Haptics.PressClick);
                ApplyingTo.FinishBandage();
                ApplyingTo = null;
                Consume();
            }
            else LastHint = "Повязка не закреплена: оберните, проведите через рычаг (смена направления), ещё 2 тугих витка.";
        }
    }
}
