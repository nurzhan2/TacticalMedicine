using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Imported art for the IFAK items (w9, docs/ART_IMPORT.md). Editor/ArtImporter turns
    /// SourceAssets/Incoming/&lt;item_id&gt;/ into Resources/Art/&lt;item_id&gt;.prefab (validated sub-part names, 1 unit = 1 m,
    /// box colliders only). At runtime <see cref="Apply"/> hides the procedural renderers in the item's
    /// <see cref="MedItem.Visual"/> child and puts the model there; the logic never changes. No prefab → procedural look.
    /// </summary>
    public static class ArtModels
    {
        public const string ResourceFolder = "Art";

        public struct Spec
        {
            public string id;
            public float length;          // longest side as carried (m)
            public string[] parts;        // required sub-part names
            public string[] hideExtra;    // procedural moving parts the model replaces
            public Spec(string id, float length, string[] parts, params string[] hideExtra)
            { this.id = id; this.length = length; this.parts = parts; this.hideExtra = hideExtra; }
        }

        public static readonly Dictionary<MedItemKind, Spec> ByKind = new Dictionary<MedItemKind, Spec>
        {
            { MedItemKind.Tourniquet, new Spec("tourniquet", 0.17f, new[] { "Strap", "Windlass", "WindlassClip", "TimeTab", "Buckle" }) },
            { MedItemKind.TraumaShears, new Spec("shears", 0.19f, new[] { "BladeA", "BladeB" }, "UpperBlade") },
            { MedItemKind.IsraeliBandage, new Spec("israeli_bandage", 0.17f, new[] { "Pad", "Wrap", "PressureBar", "Clip" }) },
            { MedItemKind.HemostaticGauze, new Spec("hemostatic_gauze", 0.14f, new[] { "Pack" }) },
            { MedItemKind.ChestSeal, new Spec("chest_seal", 0.20f, new[] { "Package" }) },
            { MedItemKind.NasalAirway, new Spec("npa", 0.15f, new[] { "Tube", "Flange" }) },
            { MedItemKind.DecompressionNeedle, new Spec("needle", 0.16f, new[] { "Needle", "Hub", "Cap" }) },
            { MedItemKind.NitrileGloves, new Spec("gloves", 0.15f, new[] { "Gloves" }) },
            { MedItemKind.Marker, new Spec("marker", 0.14f, new[] { "Body", "Cap" }) },
            { MedItemKind.ThermalBlanket, new Spec("blanket", 0.13f, new[] { "Pack" }) },
            { MedItemKind.CasualtyCard, new Spec("casualty_card", 0.14f, new[] { "Card" }) },
            { MedItemKind.Lubricant, new Spec("lubricant", 0.06f, new[] { "Sachet" }) },
        };

        /// <summary>The kit itself (main pouch + quick pouch).</summary>
        public static readonly Spec Pouch = new Spec("ifak", 0.42f, new[] { "Body", "Lid|Flap", "QuickPouch", "Slots" });

        public static IEnumerable<Spec> All()
        {
            foreach (var s in ByKind.Values) yield return s;
            yield return Pouch;
        }

        static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();
        /// <summary>Optional override (tests): returns a prefab for an item id or null.</summary>
        public static System.Func<string, GameObject> Loader;

        public static GameObject Load(string id)
        {
            if (Loader != null) return Loader(id);
            if (Cache.TryGetValue(id, out var p)) return p;
            p = Resources.Load<GameObject>(ResourceFolder + "/" + id);
            Cache[id] = p;
            return p;
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>Sub-part lookup: case-insensitive, suffixes allowed ("Strap.001", "Strap_LOD0"); "A|B" = either.</summary>
        public static Transform FindPart(Transform root, string name)
        {
            foreach (var alt in name.Split('|'))
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if (n.Length < alt.Length || string.Compare(n, 0, alt, 0, alt.Length, System.StringComparison.OrdinalIgnoreCase) != 0) continue;
                    if (n.Length == alt.Length || !char.IsLetter(n[alt.Length])) return t;
                }
            return null;
        }

        /// <summary>Names from `spec.parts` that `model` does not have.</summary>
        public static List<string> MissingParts(Transform model, Spec spec)
        {
            var miss = new List<string>();
            foreach (var p in spec.parts) if (FindPart(model, p) == null) miss.Add(p);
            return miss;
        }

        public static Transform Apply(MedItem item)
        {
            if (item == null || !ByKind.TryGetValue(item.kind, out var spec)) return null;
            var prefab = Load(spec.id);
            if (prefab == null || item.Visual == null) return null;
            var art = Swap(item.transform, item.Visual, prefab, spec.hideExtra);
            if (art != null) item.OnArtApplied(art);
            return art;
        }

        public static Transform ApplyPouch(Transform pouch)
        {
            var prefab = Load(Pouch.id);
            if (prefab == null) return null;
            var vis = pouch.Find("PouchVisual");
            return vis == null ? null : Swap(pouch, vis, prefab, null);
        }

        static Transform Swap(Transform root, Transform visual, GameObject prefab, string[] hideExtra)
        {
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (hideExtra != null)
                foreach (var n in hideExtra)
                {
                    var t = root.Find(n);
                    if (t != null) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                }
            var art = Object.Instantiate(prefab, visual, false);
            art.name = "Art";
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            // the item keeps its own logic collider; the prefab's boxes are for loose display copies only
            foreach (var c in art.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in art.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return art.transform;
        }
    }
}
