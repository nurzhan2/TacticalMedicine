using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Casualty.Blood
{
    /// <summary>
    /// Ground pools under a casualty. Blood fed at a point lands on the floor below it (raycast) and grows a
    /// flat decal quad whose area follows the volume (thin film, see <see cref="BloodSettings.poolThickness"/>).
    /// When the source moves away (drag), a new pool starts — leaving a trail.
    /// </summary>
    public sealed class BloodPoolManager : MonoBehaviour
    {
        public LayerMask floorMask = ~0;
        public float newPoolDistance = 0.55f;
        public int maxPools = 12;
        public readonly List<Pool> Pools = new List<Pool>();
        static Mesh _quad;

        public sealed class Pool
        {
            public Transform transform;
            public float volumeMl;
            public float radius;
            public float shownRadius;
        }

        public float TotalVolumeMl
        {
            get { float v = 0; foreach (var p in Pools) v += p.volumeMl; return v; }
        }

        public void Feed(Vector3 source, float ml, float dt)
        {
            if (ml <= 0f) { Animate(dt); return; }
            Vector3 ground = FindGround(source);
            Pool target = null;
            float best = newPoolDistance;
            foreach (var p in Pools)
            {
                float d = Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z), new Vector2(ground.x, ground.z));
                if (d < best && Mathf.Abs(p.transform.position.y - ground.y) < 0.2f) { best = d; target = p; }
            }
            if (target == null) target = Create(ground);
            // the pool spreads toward where the blood keeps landing (the body moves while writhing)
            var tp = target.transform.position;
            float w = ml / Mathf.Max(1f, target.volumeMl + ml);
            target.transform.position = new Vector3(Mathf.Lerp(tp.x, ground.x, w), tp.y, Mathf.Lerp(tp.z, ground.z, w));
            target.volumeMl += ml;
            var s = BloodSettings.Load();
            float h = s != null ? s.poolThickness : 0.0035f;
            float maxR = s != null ? s.maxPoolRadius : 0.9f;
            target.radius = Mathf.Min(maxR, Mathf.Sqrt(target.volumeMl * 1e-6f / (Mathf.PI * h)));
            Animate(dt);
        }

        void Animate(float dt)
        {
            foreach (var p in Pools)
            {
                // blood spreads quickly, then slows (viscosity + absorption)
                p.shownRadius = dt <= 0f ? p.radius : Mathf.MoveTowards(p.shownRadius, p.radius, dt * 0.08f + (p.radius - p.shownRadius) * dt * 1.5f);
                float d = p.shownRadius * 2f;
                p.transform.localScale = new Vector3(d, d, 1f);
            }
        }

        Vector3 FindGround(Vector3 source)
        {
            if (Physics.Raycast(source + Vector3.up * 0.05f, Vector3.down, out var hit, 2.5f, floorMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CasualtyRig>() == null)
                return hit.point;
            return new Vector3(source.x, transform.position.y, source.z);
        }

        Pool Create(Vector3 ground)
        {
            if (Pools.Count >= maxPools)
            {
                var old = Pools[0];
                Pools.RemoveAt(0);
                if (old.transform != null) DestroySafe(old.transform.gameObject);
            }
            var go = new GameObject("BloodPool");
            go.transform.SetParent(null, true);
            go.transform.position = ground + Vector3.up * (0.004f + 0.0007f * Pools.Count);
            go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            go.transform.localScale = Vector3.zero;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            var s = BloodSettings.Load();
            if (s != null) r.sharedMaterial = s.pool;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var p = new Pool { transform = go.transform };
            Pools.Add(p);
            return p;
        }

        public static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "BloodQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _quad.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateBounds();
            return _quad;
        }

        void OnDestroy()
        {
            foreach (var p in Pools) if (p.transform != null) DestroySafe(p.transform.gameObject);
            Pools.Clear();
        }

        static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
