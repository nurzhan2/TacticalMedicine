using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Hemostatic gauze (z-fold roll, 3.66 m). Touch the end to the bleeding point in an exposed wound, then
    /// push it in with repeated fingertip strokes (either hand); the roll in the hand shrinks, the wound fills.
    /// </summary>
    public sealed class GauzeItem : MedItem
    {
        public float RemainingM { get; private set; } = PackingProcedure.GauzeLengthM;
        public WoundCare PackingInto { get; private set; }
        public string LastHint { get; private set; }

        Transform _roll, _strand;
        float _hintCooldown;

        public override void BuildVisual()
        {
            Body.mass = 0.05f;
            var pack = Kit.Mat("GauzePack", new Color(0.33f, 0.31f, 0.2f), 0.4f);
            var white = Kit.Mat("GauzeWhite", Kit.Gauze, 0.15f);
            var print = Kit.Mat("GauzePrint", new Color(0.08f, 0.2f, 0.45f), 0.3f);
            // opened foil pack behind the roll
            Kit.Part(transform, PrimitiveType.Cube, "Pack", new Vector3(0f, -0.004f, -0.02f), new Vector3(0.075f, 0.004f, 0.1f), pack);
            Kit.Part(transform, PrimitiveType.Cube, "Label", new Vector3(0f, -0.0015f, -0.035f), new Vector3(0.05f, 0.0008f, 0.03f), print);
            Kit.Merge(transform);
            _roll = Kit.Part(transform, PrimitiveType.Cylinder, "Roll", new Vector3(0f, 0.02f, 0.01f), new Vector3(0.05f, 0.036f, 0.05f), white,
                Quaternion.Euler(0f, 0f, 90f)).transform;
            _strand = Kit.Part(transform, PrimitiveType.Cube, "Strand", Vector3.zero, Vector3.one, white).transform;
            _strand.gameObject.SetActive(false);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, 0.02f, 0.045f);
        }

        void Update()
        {
            _hintCooldown -= Time.deltaTime;
            if (IsHeld && PackingInto == null) TryStart();
            UpdateStrand();
        }

        void TryStart()
        {
            var c = NearestCasualty(TipPosition, 1.5f);
            if (c == null) return;
            foreach (var s in c.WoundSites)
            {
                if (s == null) continue;
                var care = s.GetComponent<WoundCare>();
                if (care == null || !care.CanPack || care.Packing.Complete) continue;
                if (care.Lateral(TipPosition) > FieldMedic.Interaction.Forgiving.Scale(0.05f, WoundCare.ForgivingPackRadius) || Mathf.Abs(care.Height(TipPosition)) > FieldMedic.Interaction.Forgiving.Scale(0.04f, 0.08f)) continue;
                if (!care.SkinAccessible)
                {
                    if (_hintCooldown <= 0f)
                    {
                        _hintCooldown = 2f;
                        LastHint = "Через ткань не затампонировать — разрежьте одежду ножницами.";
                        Haptics.Play(HolderInput, Haptics.Contact, 3f);
                    }
                    continue;
                }
                if (care.StartPacking())
                {
                    PackingInto = care;
                    Haptics.Play(HolderInput, Haptics.Grab);
                }
                return;
            }
        }

        /// <summary>Gauze pulled off the roll into the wound.</summary>
        public void Consume(float meters)
        {
            RemainingM = Mathf.Max(0f, RemainingM - meters);
            float k = Mathf.Sqrt(Mathf.Clamp01(RemainingM / PackingProcedure.GauzeLengthM));
            _roll.localScale = new Vector3(0.05f * Mathf.Max(0.35f, k), 0.036f, 0.05f * Mathf.Max(0.35f, k));
        }

        /// <summary>The wound is full: the rest of the roll is torn off and set aside.</summary>
        public void FinishPacking()
        {
            PackingInto = null;
            base.Consume();
        }

        void UpdateStrand()
        {
            bool show = PackingInto != null && !PackingInto.Packing.Complete;
            _strand.gameObject.SetActive(show);
            if (!show) return;
            Vector3 a = _roll.position, b = PackingInto.transform.position;
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            _strand.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(d));
            Vector3 ls = transform.lossyScale;
            _strand.localScale = new Vector3(0.03f / ls.x, 0.003f / ls.y, d.magnitude / ls.z);
        }
    }
}
