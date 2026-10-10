using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// S3 «Мина»: the footprints you came in on (your only safe ground) drawn as dark boot prints on the floor of
    /// the communication trench. Stepping (head projected on the floor) farther than <see cref="tolerance"/> from
    /// the print line counts as "stepped off the path".
    /// </summary>
    public sealed class MinePath : MonoBehaviour
    {
        public Vector3[] points = new Vector3[0];
        public float tolerance = 0.45f;
        public float printSpacing = 0.36f;

        readonly List<GameObject> _prints = new List<GameObject>();

        void Start() => Build();

        public void Build()
        {
            foreach (var g in _prints) if (g != null) Destroy(g);
            _prints.Clear();
            if (points.Length < 2) return;
            var m = UI.WorldUI.Unlit(new Color(0.035f, 0.03f, 0.025f));
            bool left = true;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                Vector3 dir = (b - a); dir.y = 0f;
                float len = dir.magnitude;
                dir /= Mathf.Max(1e-4f, len);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (float d = 0f; d < len; d += printSpacing)
                {
                    var p = a + dir * d + side * (left ? -0.09f : 0.09f);
                    var q = UI.WorldUI.Quad(transform, "Boot", Vector3.zero, new Vector2(0.1f, 0.27f), m);
                    q.transform.position = new Vector3(p.x, 0.012f + a.y, p.z);
                    q.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                    _prints.Add(q);
                    left = !left;
                }
            }
        }

        public float Distance(Vector3 p)
        {
            float best = float.MaxValue;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = new Vector2(points[i].x, points[i].z);
                var b = new Vector2(points[i + 1].x, points[i + 1].z);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
            }
            return best;
        }

        public bool OffPath(Vector3 headPos) => points.Length >= 2 && Distance(headPos) > tolerance;
    }
}
