using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Imported art for the IFAK items (w9, docs/ART_IMPORT.md). Editor/ArtImporter turns
    /// SourceAssets/Incoming/&lt;folder&gt;/ into Resources/Art/&lt;item_id&gt;.prefab (validated sub-part names, 1 unit = 1 m,
    /// box colliders only). At runtime <see cref="Apply"/> hides the procedural renderers in the item's
    /// <see cref="MedItem.Visual"/> child and puts the model there; the logic never changes. No prefab → procedural look.
    /// w9d: one delivery folder may hold several items (npa → NPA + lubricant sachet, small → card, tape, wipes...)
    /// and several states of one item (TQ_Stowed / TQ_Open parts): <see cref="Spec.objects"/> picks the item's objects,
    /// <see cref="Spec.carried"/> the ones shown in the kit, the rest stays in the prefab (inactive) for the item's states.
    /// </summary>
    public static class ArtModels
    {
        public const string ResourceFolder = "Art";

        public sealed class Spec
        {
            public string id;
            public float length;          // longest side as carried (m)
            public string[] parts;        // required sub-part names ("A|B" = either)
            public string[] hideExtra;    // procedural moving parts the model replaces
            /// <summary>Delivery folder in SourceAssets/Incoming (null = same as id).</summary>
            public string source;
            /// <summary>Root objects of the delivered file that belong to this item (null = all).</summary>
            public string[] objects;
            /// <summary>Objects shown as carried / in the kit (null = all of <see cref="objects"/>); the others start inactive.</summary>
            public string[] carried;
            /// <summary>Objects shown while the item is out of the kit (null = same as carried).</summary>
            public string[] held;
            /// <summary>Model axes → item axes (item: longest side along Z, thinnest along Y, printed front up). Astra's
            /// FBX fronts (Blender −Y) arrive as −Z in Unity, so flat packs use (90, 0|90, 0).</summary>
            public Vector3 euler;
            /// <summary>Objects re-posed onto the carried group in the importer: name → extra rotation about its centre
            /// (Astra laid some states out side by side, e.g. the NPA package next to the tube).</summary>
            public Dictionary<string, Vector3> stack;

            public Spec(string id, float length, string[] parts, params string[] hideExtra)
            { this.id = id; this.length = length; this.parts = parts; this.hideExtra = hideExtra; }

            public string Source => string.IsNullOrEmpty(source) ? id : source;
        }

        public static readonly Dictionary<MedItemKind, Spec> ByKind = new Dictionary<MedItemKind, Spec>
        {
            { MedItemKind.Tourniquet, new Spec("tourniquet", 0.17f, new[] { "TQ_Stowed", "Strap", "Windlass", "WindlassClip", "TimeTab", "Buckle" })
                { source = "tq", carried = new[] { "TQ_Stowed" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.TraumaShears, new Spec("shears", 0.19f, new[] { "BladeA", "BladeB" }, "UpperBlade")
                { source = "shears", euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.IsraeliBandage, new Spec("israeli_bandage", 0.17f, new[] { "Pack", "Pad", "Wrap", "PressureBar", "Clip" })
                { source = "israeli", carried = new[] { "Pack" }, held = new[] { "Pack_Open" }, euler = new Vector3(90f, 90f, 0f),
                  stack = new Dictionary<string, Vector3> { { "Pack_Open", Vector3.zero } } } },
            { MedItemKind.HemostaticGauze, new Spec("hemostatic_gauze", 0.14f, new[] { "Pack_Closed", "Pack_OpenBottom", "Gauze_Roll" })
                { source = "gauze", carried = new[] { "Pack_Closed" }, held = new[] { "Pack_OpenBottom", "Gauze_Roll" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.ChestSeal, new Spec("chest_seal", 0.20f, new[] { "Pack", "Seal", "Liner" })
                { source = "chestseal", carried = new[] { "Pack" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.NasalAirway, new Spec("npa", 0.15f, new[] { "Tube" })
                { source = "npa", objects = new[] { "Tube", "Package" }, carried = new[] { "Package", "Tube" }, held = new[] { "Tube" },
                  euler = new Vector3(180f, 0f, 0f), stack = new Dictionary<string, Vector3> { { "Package", new Vector3(-90f, 0f, 0f) } } } },
            { MedItemKind.DecompressionNeedle, new Spec("needle", 0.16f, new[] { "Needle", "Case", "Cap" })
                { source = "needle", carried = new[] { "Case", "Cap", "Needle" }, held = new[] { "Needle" }, euler = new Vector3(180f, 0f, 0f) } },
            { MedItemKind.NitrileGloves, new Spec("gloves", 0.25f, new[] { "Pack_Closed" })
                { source = "gloves", carried = new[] { "Pack_Closed" }, held = new[] { "Pack_Open" }, euler = new Vector3(90f, 90f, 0f) } },
            { MedItemKind.Marker, new Spec("marker", 0.14f, new[] { "Body", "Cap" })
                { source = "marker", euler = new Vector3(180f, 90f, 0f) } },
            { MedItemKind.ThermalBlanket, new Spec("blanket", 0.13f, new[] { "Blanket_Pack" })
                { source = "blanket", objects = new[] { "Blanket_Pack" }, euler = new Vector3(90f, 90f, 0f) } },
            { MedItemKind.CasualtyCard, new Spec("casualty_card", 0.175f, new[] { "CasualtyCard" })
                { source = "small", objects = new[] { "CasualtyCard" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.Lubricant, new Spec("lubricant", 0.05f, new[] { "LubeSachet" })
                { source = "npa", objects = new[] { "LubeSachet" }, euler = new Vector3(90f, 0f, 0f) } },
            // w9d: items the game did not have before
            { MedItemKind.AlphaTourniquet, new Spec("alpha_tq", 0.12f, new[] { "Alpha_Stowed", "Band", "Hook_1", "Hook_2" })
                { source = "alpha", carried = new[] { "Alpha_Stowed" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.EsmarchTourniquet, new Spec("esmarch_tq", 0.10f, new[] { "Esmarch_Stowed", "Band", "Ball_1", "Ball_2" })
                { source = "esmarch", carried = new[] { "Esmarch_Stowed" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.Splint, new Spec("splint", 0.11f, new[] { "Splint_Rolled" })
                { source = "splint", carried = new[] { "Splint_Rolled" }, euler = new Vector3(90f, 0f, 0f) } },
            { MedItemKind.TriangularBandage, new Spec("tri_bandage", 0.14f, new[] { "TriBandage_Pack" })
                { source = "small", objects = new[] { "TriBandage_Pack" }, euler = new Vector3(90f, 90f, 0f) } },
            { MedItemKind.EyeShield, new Spec("eye_shield", 0.12f, new[] { "EyeShield_Pack", "EyeShield" })
                { source = "small", objects = new[] { "EyeShield_Pack", "EyeShield" }, carried = new[] { "EyeShield_Pack" }, euler = new Vector3(90f, 90f, 0f) } },
            { MedItemKind.AntisepticWipes, new Spec("wipes", 0.08f, new[] { "Wipes_Pack" })
                { source = "small", objects = new[] { "Wipes_Pack" }, euler = new Vector3(90f, 90f, 0f) } },
            { MedItemKind.DuctTape, new Spec("tape", 0.06f, new[] { "Tape_Roll" })
                { source = "small", objects = new[] { "Tape_Roll" }, euler = new Vector3(90f, 0f, 0f) } },
        };

        /// <summary>The kit itself: rip-away two-echelon IFAK (FMVR_IFAK), hinged panels and Slot_* empties.</summary>
        public static readonly Spec Pouch = new Spec("ifak", 0.30f, new[] { "Body", "Lid|Flap", "QuickPouch", "InnerPanel_Upper", "InnerPanel_Lower", "PullTab", "Slot_TQ", "Slot_Shears" })
        { source = "ifak" };

        public static IEnumerable<Spec> All()
        {
            foreach (var s in ByKind.Values) yield return s;
            yield return Pouch;
        }

        public static Spec ById(string id)
        {
            foreach (var s in All()) if (s.id == id) return s;
            return null;
        }

        static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();
        /// <summary>Optional override (tests): returns a prefab for an item id or null.</summary>
        public static System.Func<string, GameObject> Loader;
        /// <summary>Tests / diagnostics: run the items with their procedural look only (also env FMVR_NO_ART=1).</summary>
        public static bool Disabled = System.Environment.GetEnvironmentVariable("FMVR_NO_ART") == "1";

        public static GameObject Load(string id)
        {
            if (Disabled) return null;
            if (Loader != null) return Loader(id);
            if (Cache.TryGetValue(id, out var p)) return p;
            p = Resources.Load<GameObject>(ResourceFolder + "/" + id);
            Cache[id] = p;
            return p;
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>Sub-part lookup: case-insensitive; an exact name wins, then suffixes ("Strap.001", "Strap_LOD0");
        /// "A|B" = either.</summary>
        public static Transform FindPart(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var alt in name.Split('|'))
                foreach (var t in all)
                    if (string.Equals(t.name, alt, System.StringComparison.OrdinalIgnoreCase)) return t;
            foreach (var alt in name.Split('|'))
                foreach (var t in all)
                {
                    string n = t.name;
                    if (n.Length <= alt.Length || string.Compare(n, 0, alt, 0, alt.Length, System.StringComparison.OrdinalIgnoreCase) != 0) continue;
                    if (IsSuffix(n.Substring(alt.Length))) return t;
                }
            return null;
        }

        /// <summary>Export suffixes only (".001", "_LOD0", "_low", "1") — "Pack_Open" is not "Pack".</summary>
        static bool IsSuffix(string s)
        {
            if (s.Length == 0) return true;
            if (char.IsLetter(s[0])) return false;
            string t = s.TrimStart('.', '_', ' ', '-');
            if (t.Length == 0) return true;
            if (char.IsDigit(t[0])) return true;
            return t.StartsWith("LOD", System.StringComparison.OrdinalIgnoreCase) || t.StartsWith("low", System.StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("mesh", System.StringComparison.OrdinalIgnoreCase) || t.StartsWith("geo", System.StringComparison.OrdinalIgnoreCase);
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
            if (art == null) return null;
            ShowState(art, spec, true);
            item.ArtRoot = art;
            item.OnArtApplied(art);
            return art;
        }

        /// <summary>Carried (in the kit) or held look of an imported item: toggles the state objects of the spec.</summary>
        public static void ShowState(Transform art, Spec spec, bool inKit)
        {
            if (art == null || spec == null || spec.held == null) return;
            var show = inKit || spec.held == null ? spec.carried : spec.held;
            var all = new HashSet<string>();
            if (spec.carried != null) all.UnionWith(spec.carried);
            all.UnionWith(spec.held);
            foreach (var n in all)
            {
                var t = FindPart(art, n);
                if (t != null) t.gameObject.SetActive(System.Array.IndexOf(show, n) >= 0);
            }
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

        /// <summary>Item-local point at the +Z end (the working tip) of an imported part, null if missing.</summary>
        public static Vector3? TipOf(Transform item, Transform art, string part)
        {
            var t = FindPart(art, part);
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return null;
            var b = mf.sharedMesh.bounds;
            float bz = float.MinValue; Vector3 sum = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = item.InverseTransformPoint(t.TransformPoint(c));
                sum += p;
                if (p.z > bz) bz = p.z;
            }
            var mid = sum / 8f;
            return new Vector3(mid.x, mid.y, bz);
        }

        /// <summary>Takes an imported sub-part out of the carried look and re-parents it onto a logic transform
        /// (e.g. the art windlass onto the CAT's rod pivot). The procedural renderers under `onto` are hidden.</summary>
        public static Transform Graft(Transform art, string part, Transform onto, Quaternion localRot, Vector3 localPos, bool hideProcedural = true)
        {
            var src = FindPart(art, part);
            if (src == null || onto == null) return null;
            if (hideProcedural) foreach (var r in onto.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var copy = Object.Instantiate(src.gameObject, onto, false).transform;
            copy.name = "Art_" + part;
            copy.gameObject.SetActive(true);
            foreach (Transform c in copy) Object.Destroy(c.gameObject); // only this part's mesh
            // centre the mesh on its own bounds, then apply the wanted pose
            var mf = copy.GetComponent<MeshFilter>();
            var center = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
            copy.localRotation = localRot;
            copy.localScale = Vector3.one;
            copy.localPosition = localPos - localRot * center;
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return copy;
        }
    }
}
