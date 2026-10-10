using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.Treatment
{
    public enum MedItemKind
    {
        Tourniquet, HemostaticGauze, IsraeliBandage, ChestSeal, NasalAirway, Lubricant,
        DecompressionNeedle, TraumaShears, NitrileGloves, Marker, ThermalBlanket, CasualtyCard,
    }

    /// <summary>
    /// Builds the IFAK items at runtime from primitives + small procedural meshes (no texture memory, a few
    /// draw calls each after merging), and creates the right <see cref="MedItem"/> behaviour per kind.
    /// </summary>
    public static class Kit
    {
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static Font _font;

        public static readonly Color CatBlack = new Color(0.035f, 0.035f, 0.035f);
        public static readonly Color CatRed = new Color(0.62f, 0.05f, 0.04f);
        public static readonly Color Olive = new Color(0.27f, 0.28f, 0.17f);
        public static readonly Color Coyote = new Color(0.40f, 0.32f, 0.21f);
        public static readonly Color Gauze = new Color(0.86f, 0.85f, 0.80f);
        public static readonly Color Nitrile = new Color(0.33f, 0.24f, 0.62f);

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Material Mat(string name, Color c, float smooth = 0.3f, float metal = 0f)
        {
            if (Mats.TryGetValue(name, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "Kit_" + name, enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            Mats[name] = m;
            return m;
        }

        public static GameObject Part(Transform parent, PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public static GameObject MeshPart(Transform parent, string name, Mesh mesh, Material m, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public static TextMesh Text(Transform parent, string name, string text, float charSize, Color c, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            var r = go.GetComponent<MeshRenderer>();
            if (Font != null) r.sharedMaterial = Font.material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return tm;
        }

        /// <summary>
        /// Combines all MeshRenderers below `root` (except subtrees in `keep`) into one mesh per material
        /// on a child "Merged" — fewer draw calls for the many small parts of an item.
        /// </summary>
        public static void Merge(Transform root, params Transform[] keep)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var kill = new List<GameObject>();
            var toRoot = root.worldToLocalMatrix;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsKept(mr.transform, keep) || mr.GetComponent<TextMesh>() != null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mat = mr.sharedMaterial;
                if (!groups.TryGetValue(mat, out var list)) groups[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toRoot * mr.transform.localToWorldMatrix });
                kill.Add(mr.gameObject);
            }
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = root.name + "_" + kv.Key.name };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                var go = MeshPart(root, "Merged_" + kv.Key.name, mesh, kv.Key, Vector3.zero, Quaternion.identity, Vector3.one);
                go.transform.SetSiblingIndex(0);
            }
            foreach (var g in kill)
            {
                if (g == root.gameObject) continue;
                // keep transforms that have children we did not merge (e.g. a kept rod under a merged part)
                if (g.transform.childCount > 0)
                {
                    Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(g.GetComponent<MeshFilter>());
                }
                else Object.DestroyImmediate(g);
            }
        }

        static bool IsKept(Transform t, Transform[] keep)
        {
            if (keep == null) return false;
            foreach (var k in keep) if (k != null && (t == k || t.IsChildOf(k))) return true;
            return false;
        }

        /// <summary>Flat band ring around local Z (a tourniquet / bandage wrap): radius, width (along Z), thickness.</summary>
        public static Mesh Ring(float radius, float width, float thickness, int segments = 28, Mesh reuse = null)
        {
            var m = reuse != null ? reuse : new Mesh { name = "Ring" };
            int n = segments + 1;
            var v = new Vector3[n * 4];
            var nr = new Vector3[n * 4];
            var uv = new Vector2[n * 4];
            float ro = radius + thickness, ri = radius, hw = width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i] = d * ro + Vector3.forward * -hw; nr[i] = d;
                v[n + i] = d * ro + Vector3.forward * hw; nr[n + i] = d;
                v[2 * n + i] = d * ri + Vector3.forward * -hw; nr[2 * n + i] = -d;
                v[3 * n + i] = d * ri + Vector3.forward * hw; nr[3 * n + i] = -d;
                float u = i / (float)segments * 6f;
                uv[i] = new Vector2(u, 0); uv[n + i] = new Vector2(u, 1); uv[2 * n + i] = new Vector2(u, 0); uv[3 * n + i] = new Vector2(u, 1);
            }
            var tri = new List<int>(segments * 12);
            for (int i = 0; i < segments; i++)
            {
                int a = i, b = i + 1;
                // outer
                tri.Add(a); tri.Add(n + a); tri.Add(b);
                tri.Add(b); tri.Add(n + a); tri.Add(n + b);
                // inner (facing the limb)
                tri.Add(2 * n + a); tri.Add(2 * n + b); tri.Add(3 * n + a);
                tri.Add(2 * n + b); tri.Add(3 * n + b); tri.Add(3 * n + a);
            }
            m.Clear();
            m.vertices = v; m.normals = nr; m.uv = uv;
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ factory
        public static MedItem Create(MedItemKind kind, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.SetActive(false); // configure before Awake runs
            MedItem item = kind switch
            {
                MedItemKind.Tourniquet => go.AddComponent<TourniquetCAT>(),
                MedItemKind.HemostaticGauze => go.AddComponent<GauzeItem>(),
                MedItemKind.IsraeliBandage => go.AddComponent<BandageItem>(),
                MedItemKind.ChestSeal => go.AddComponent<ChestSealItem>(),
                MedItemKind.NasalAirway => go.AddComponent<NpaItem>(),
                MedItemKind.DecompressionNeedle => go.AddComponent<NeedleItem>(),
                MedItemKind.TraumaShears => go.AddComponent<ShearsItem>(),
                MedItemKind.NitrileGloves => go.AddComponent<GlovesItem>(),
                MedItemKind.Marker => go.AddComponent<MarkerItem>(),
                MedItemKind.ThermalBlanket => go.AddComponent<BlanketItem>(),
                MedItemKind.CasualtyCard => go.AddComponent<CardItem>(),
                _ => go.AddComponent<MedItem>(),
            };
            item.kind = kind;
            go.SetActive(true);
            return item;
        }
    }
}
