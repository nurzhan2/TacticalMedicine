using System.Collections.Generic;
using FieldMedic.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Shell-damaged masonry walls for the V2 ruins. A wall is cut into brick-length strips (0.25 m); every strip
    /// has its own broken top quantised to brick courses (7.5 cm), so the silhouette reads as torn brickwork, not
    /// a box. Openings (windows, shell holes, breaches, the drag doorway) are removed per strip; every boundary
    /// between strips gets jamb faces where the solid ranges differ, tops/sills/lintels get caps — a closed,
    /// two-sided mesh ~0.3–1k triangles per wall. Vertex colours drive <c>FieldMedic/RuinWall</c>: r = plaster cover
    /// (falls off toward broken edges), g = soot, b = AO. MeshCollider on layer/tag Cover, NavMeshObstacle (carve).
    /// </summary>
    public static class RuinBuilder
    {
        public struct Opening
        {
            public float x0, x1, y0, y1;   // wall-local: x along the wall from its start, y up from the ground
            public Opening(float x0, float x1, float y0, float y1) { this.x0 = x0; this.x1 = x1; this.y0 = y0; this.y1 = y1; }
            public static Opening Breach(float x0, float x1) => new Opening(x0, x1, -1f, 99f);
        }

        public sealed class WallSpec
        {
            public string name;
            public Vector3 start, end;
            public float height = 2.4f;
            public float jag = 0.45f;              // amplitude of the broken top (m)
            public float thickness = BattlefieldLayout.WallThickness;
            public Opening[] openings = new Opening[0];
            public (float x0, float x1, float h)[] collapses = new (float, float, float)[0]; // dips to height h
            public float soot = 0.25f;
            public int seed = 1;
        }

        const float Strip = 0.25f, Course = 0.075f, Sink = 0.3f, Row = 0.55f;

        public static GameObject BuildWall(Transform parent, WallSpec w, Material m, string meshDir)
        {
            Vector3 along = w.end - w.start; along.y = 0f;
            float L = along.magnitude;
            Vector3 dir = along / L;
            int n = Mathf.Max(1, Mathf.RoundToInt(L / Strip));
            float sw = L / n;
            var rng = new System.Random(w.seed);

            // ---- per-strip top + solid ranges
            var tops = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = (i + 0.5f) * sw;
                float nz = Mathf.PerlinNoise(w.seed * 3.17f + x * 0.45f, 0.5f) - 0.5f;
                float fine = Mathf.PerlinNoise(w.seed * 1.31f + x * 2.3f, 7.1f) - 0.5f;
                float h = w.height + nz * 2f * w.jag + fine * 0.35f * w.jag;
                foreach (var c in w.collapses)
                {
                    // smooth V-shaped dip with ragged edges
                    float mid = (c.x0 + c.x1) * 0.5f, half = (c.x1 - c.x0) * 0.5f;
                    float t = Mathf.Clamp01(1f - Mathf.Abs(x - mid) / Mathf.Max(0.01f, half));
                    t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f));
                    h = Mathf.Lerp(h, c.h + fine * 0.3f, t);
                }
                if (rng.NextDouble() < 0.12) h -= Course * rng.Next(1, 5); // a missing brick here and there
                tops[i] = Mathf.Max(0.15f, Mathf.Round(h / Course) * Course);
            }
            var solid = new List<Vector2>[n];
            for (int i = 0; i < n; i++)
            {
                float xa = i * sw, xb = (i + 1) * sw, xm = (xa + xb) * 0.5f;
                var r = new List<Vector2> { new Vector2(-Sink, tops[i]) };
                foreach (var o in w.openings)
                {
                    if (xm < o.x0 || xm > o.x1) continue;
                    var next = new List<Vector2>();
                    foreach (var s in r)
                    {
                        if (o.y1 <= s.x || o.y0 >= s.y) { next.Add(s); continue; }
                        if (o.y0 > s.x) next.Add(new Vector2(s.x, Mathf.Round(o.y0 / Course) * Course));
                        if (o.y1 < s.y) next.Add(new Vector2(Mathf.Round(o.y1 / Course) * Course, s.y));
                    }
                    r = next;
                }
                r.RemoveAll(s => s.y - s.x < 0.05f);
                solid[i] = r;
            }

            var mb = new MeshBuild();
            float t2 = w.thickness * 0.5f;

            // ---- front (+z) and back (−z) faces, rows of ≤ Row metres for vertex-colour resolution
            for (int i = 0; i < n; i++)
            {
                float xa = i * sw, xb = (i + 1) * sw;
                foreach (var s in solid[i])
                {
                    int rows = Mathf.Max(1, Mathf.CeilToInt((s.y - s.x) / Row));
                    for (int k = 0; k < rows; k++)
                    {
                        float y0 = Mathf.Lerp(s.x, s.y, (float)k / rows), y1 = Mathf.Lerp(s.x, s.y, (float)(k + 1) / rows);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float z = side * t2;
                            var p = new[] { new Vector3(xa, y0, z), new Vector3(xb, y0, z), new Vector3(xb, y1, z), new Vector3(xa, y1, z) };
                            var cols = new Color[4];
                            for (int q = 0; q < 4; q++) cols[q] = FaceColor(w, p[q], tops, sw, L, side);
                            var uv = side > 0
                                ? new[] { new Vector2(xa, y0), new Vector2(xb, y0), new Vector2(xb, y1), new Vector2(xa, y1) }
                                : new[] { new Vector2(L - xa, y0), new Vector2(L - xb, y0), new Vector2(L - xb, y1), new Vector2(L - xa, y1) };
                            mb.Quad(p, new Vector3(0f, 0f, side), uv, cols);
                        }
                    }
                    // cap on top of the range (wall top or window sill) and under it (lintel / hole ceiling)
                    var capC = new Color(0f, w.soot * 0.6f, 0.9f);
                    mb.Quad(new[] { new Vector3(xa, s.y, -t2), new Vector3(xb, s.y, -t2), new Vector3(xb, s.y, t2), new Vector3(xa, s.y, t2) },
                        Vector3.up, new[] { new Vector2(xa, 0f), new Vector2(xb, 0f), new Vector2(xb, w.thickness), new Vector2(xa, w.thickness) },
                        new[] { capC, capC, capC, capC });
                    if (s.x > -Sink + 0.01f)
                        mb.Quad(new[] { new Vector3(xa, s.x, -t2), new Vector3(xb, s.x, -t2), new Vector3(xb, s.x, t2), new Vector3(xa, s.x, t2) },
                            Vector3.down, new[] { new Vector2(xa, 0f), new Vector2(xb, 0f), new Vector2(xb, w.thickness), new Vector2(xa, w.thickness) },
                            new[] { capC, capC, capC, capC });
                }
            }

            // ---- jambs at strip boundaries: ranges solid on one side only
            for (int k = 0; k <= n; k++)
            {
                float x = k * sw;
                var left = k > 0 ? solid[k - 1] : new List<Vector2>();
                var right = k < n ? solid[k] : new List<Vector2>();
                foreach (var seg in Subtract(right, left)) Jamb(mb, x, seg, -1f, t2, w); // right solid, left empty → faces −x
                foreach (var seg in Subtract(left, right)) Jamb(mb, x, seg, 1f, t2, w);
            }

            var mesh = mb.ToMesh(w.name);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            string path = $"{meshDir}/{w.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(w.name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(w.start.x, BattlefieldLayout.GroundHeight(w.start.x, w.start.z), w.start.z);
            go.transform.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.On;
            MakeCover(go, mesh, true);
            return go;
        }

        static Color FaceColor(WallSpec w, Vector3 p, float[] tops, float sw, float L, int side)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(p.x / sw), 0, tops.Length - 1);
            float top = tops[i];
            float edge = Mathf.Clamp01((top - p.y) / 0.9f);
            edge = Mathf.Min(edge, Mathf.Clamp01(Mathf.Min(p.x, L - p.x) / 0.45f) * 0.6f + 0.4f);
            float soot = w.soot * (0.35f + 0.65f * Mathf.Clamp01(1f - p.y / 2.2f));
            foreach (var o in w.openings)
            {
                float dx = Mathf.Max(0f, Mathf.Max(o.x0 - p.x, p.x - o.x1));
                float dy = Mathf.Max(0f, Mathf.Max(o.y0 - p.y, p.y - o.y1));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                edge = Mathf.Min(edge, Mathf.Clamp01(d / 0.5f) * 0.85f + 0.15f);
                if (p.y > o.y1 - 0.1f && d < 1.2f && o.y0 > 0f) soot = Mathf.Max(soot, (1f - d / 1.2f) * 0.85f); // fire licked out of the window
            }
            // interior faces are less weathered
            if (side < 0) edge = Mathf.Min(1f, edge * 1.15f + 0.05f);
            float ao = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((p.y + 0.05f) / 0.6f));
            return new Color(edge, Mathf.Clamp01(soot), ao, 1f);
        }

        static void Jamb(MeshBuild mb, float x, Vector2 seg, float nx, float t2, WallSpec w)
        {
            var c = new Color(0f, w.soot * 0.5f, 0.85f);
            float y0 = seg.x, y1 = seg.y;
            mb.Quad(new[] { new Vector3(x, y0, -t2), new Vector3(x, y0, t2), new Vector3(x, y1, t2), new Vector3(x, y1, -t2) },
                new Vector3(nx, 0f, 0f), new[] { new Vector2(0f, y0), new Vector2(w.thickness, y0), new Vector2(w.thickness, y1), new Vector2(0f, y1) },
                new[] { c, c, c, c });
        }

        /// <summary>Ranges of <paramref name="a"/> not covered by <paramref name="b"/> (1-D interval difference).</summary>
        public static List<Vector2> Subtract(List<Vector2> a, List<Vector2> b)
        {
            var res = new List<Vector2>();
            foreach (var s in a)
            {
                var parts = new List<Vector2> { s };
                foreach (var cut in b)
                {
                    var next = new List<Vector2>();
                    foreach (var p in parts)
                    {
                        if (cut.y <= p.x || cut.x >= p.y) { next.Add(p); continue; }
                        if (cut.x > p.x) next.Add(new Vector2(p.x, cut.x));
                        if (cut.y < p.y) next.Add(new Vector2(cut.y, p.y));
                    }
                    parts = next;
                }
                foreach (var p in parts) if (p.y - p.x > 0.01f) res.Add(p);
            }
            return res;
        }

        // ------------------------------------------------------------------ floor slab
        /// <summary>Concrete floor of a ruin (slightly raised, ragged-edged grid so it doesn't read as a plate).</summary>
        public static GameObject BuildFloor(Transform parent, string name, Vector2 min, Vector2 max, float y, Material m, string meshDir, int seed)
        {
            var mb = new MeshBuild();
            const float step = 0.5f;
            int nx = Mathf.CeilToInt((max.x - min.x) / step), nz = Mathf.CeilToInt((max.y - min.y) / step);
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x0 = min.x + i * step, x1 = Mathf.Min(max.x, x0 + step), z0 = min.y + j * step, z1 = Mathf.Min(max.y, z0 + step);
                // broken-out patches: skip a few cells near the edges, sink a few (cracked slab)
                float edge = Mathf.Min(Mathf.Min(x0 - min.x, max.x - x1), Mathf.Min(z0 - min.y, max.y - z1));
                float hole = Mathf.PerlinNoise(seed + x0 * 0.6f, z0 * 0.6f);
                if (edge < 0.6f && hole > 0.62f) continue;
                float h(float x, float z) => y + (Mathf.PerlinNoise(seed * 2.1f + x * 1.3f, z * 1.3f) - 0.5f) * 0.03f;
                var p = new[] { new Vector3(x0, h(x0, z0), z0), new Vector3(x0, h(x0, z1), z1), new Vector3(x1, h(x1, z1), z1), new Vector3(x1, h(x1, z0), z0) };
                var uv = new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };
                var c = Color.white;
                mb.Quad(p, Vector3.up, uv, new[] { c, c, c, c });
            }
            var mesh = mb.ToMesh(name);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            string path = $"{meshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        // ------------------------------------------------------------------ cover helpers
        public static void MakeCover(GameObject go, Mesh colliderMesh, bool occluder)
        {
            int layer = LayerMask.NameToLayer(CoverMath.CoverLayerName);
            if (layer >= 0) go.layer = layer;
            go.tag = CoverMath.CoverTag;
            if (colliderMesh != null) go.AddComponent<MeshCollider>().sharedMesh = colliderMesh;
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
            if (occluder) flags |= StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            AddObstacle(go);
        }

        /// <summary>Carving NavMeshObstacle sized to the object's renderers (local space).</summary>
        public static void AddObstacle(GameObject go)
        {
            if (go.GetComponent<NavMeshObstacle>() != null) return;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = new Bounds(go.transform.InverseTransformPoint(rs[0].bounds.center), Vector3.zero);
            foreach (var r in rs)
            {
                if (r.name.Contains("_LOD") && !r.name.EndsWith("_LOD0")) continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    b.Encapsulate(go.transform.InverseTransformPoint(c));
                }
            }
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Box;
            o.center = b.center;
            o.size = b.size;
            o.carving = true;
            o.carveOnlyStationary = true;
        }

        // ------------------------------------------------------------------ mesh helper
        public sealed class MeshBuild
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            /// <summary>Adds a quad; winding is fixed up so the face points along <paramref name="normal"/>.</summary>
            public void Quad(Vector3[] p, Vector3 normal, Vector2[] uvs, Color[] cols)
            {
                int b = v.Count;
                for (int i = 0; i < 4; i++) { v.Add(p[i]); n.Add(normal); uv.Add(uvs[i]); c.Add(cols[i]); }
                // Unity front faces are clockwise seen from the front ⇔ Cross(b−a, c−a) points at the viewer
                Vector3 face = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
                if (Vector3.Dot(face, normal) > 0f) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                return m;
            }
        }
    }
}
