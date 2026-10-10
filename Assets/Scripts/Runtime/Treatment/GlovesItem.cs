using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Nitrile gloves (a pair). Hold the pair in one hand, push the other (empty) hand into a glove and pull it
    /// back out: that hand turns purple. Then swap: hold with the gloved hand, glove the other one.
    /// </summary>
    public sealed class GlovesItem : MedItem
    {
        public int Remaining { get; private set; } = 2;
        Transform _gloveA, _gloveB;
        HandGrabber _inserting;
        float _insertedFor;

        public override void BuildVisual()
        {
            Body.mass = 0.02f;
            var m = Kit.Mat("Nitrile", Kit.Nitrile, 0.45f);
            _gloveA = Glove("GloveA", new Vector3(-0.012f, 0f, 0f), m);
            _gloveB = Glove("GloveB", new Vector3(0.012f, 0.008f, 0f), m);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
        }

        Transform Glove(string name, Vector3 pos, Material m)
        {
            var g = new GameObject(name).transform;
            g.SetParent(transform, false);
            g.localPosition = pos;
            Kit.Part(g, PrimitiveType.Cube, "Palm", Vector3.zero, new Vector3(0.05f, 0.006f, 0.06f), m);
            for (int i = 0; i < 4; i++)
                Kit.Part(g, PrimitiveType.Cube, "F" + i, new Vector3(-0.018f + i * 0.012f, 0f, 0.05f), new Vector3(0.01f, 0.005f, 0.045f), m);
            Kit.Part(g, PrimitiveType.Cube, "Cuff", new Vector3(0f, 0f, -0.05f), new Vector3(0.055f, 0.005f, 0.045f), m);
            Kit.Merge(g);
            return g;
        }

        void Update()
        {
            if (!IsHeld || Remaining <= 0) { _inserting = null; return; }
            foreach (var h in HandGrabber.All)
            {
                if (h == null || h == Holder || h.Held != null) continue;
                var ht = HandTreatment.Of(h);
                if (ht == null || ht.Gloved) continue;
                float d = Vector3.Distance(h.PalmPosition, transform.position);
                if (_inserting == null && d < 0.07f) { _inserting = h; _insertedFor = 0f; Haptics.Play(h.Input, Haptics.Contact); }
                if (_inserting == h)
                {
                    _insertedFor += Time.deltaTime;
                    if (d > 0.11f)
                    {
                        // pulled the hand back out with the glove on it
                        if (_insertedFor > 0.15f) PutOn(ht, h);
                        _inserting = null;
                    }
                }
            }
        }

        void PutOn(HandTreatment ht, HandGrabber h)
        {
            ht.PutOnGloves();
            Remaining--;
            Haptics.Play(h.Input, Haptics.PressClick);
            (Remaining == 1 ? _gloveB : _gloveA).gameObject.SetActive(false);
            bool allGloved = true;
            foreach (var x in HandTreatment.All) if (!x.Gloved) allGloved = false;
            if (Remaining > 0 && !allGloved) return;
            var c = NearestCasualty(transform.position, 3f);
            if (c != null) c.Protocol.Perform(MedAction.Gloves);
            Consume();
        }
    }
}
