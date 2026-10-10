using FieldMedic.Audio;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// The drone you can actually see. A dark silhouette built from primitives (body merged into one mesh, 4 spinning
    /// rotors) that rides on the <see cref="DroneAudio"/> transform. No navigation lights — combat drones fly dark;
    /// you spot it against the sky glow or when a flare lights it from below. Recon = Mavic-class grey quad with
    /// a gimbal and a drop clamp; FPV = 7" carbon X frame with orange props and the RPG warhead strapped under it.
    /// ~5 draw calls, only active while the drone is.
    /// </summary>
    public sealed class DroneVisual : MonoBehaviour
    {
        static Material _body, _dark, _prop, _orange, _olive;
        readonly Transform[] _rotors = new Transform[4];
        float _spin;
        public DroneKind Kind { get; private set; }

        public static DroneVisual Build(Transform parent, DroneKind kind)
        {
            Mats();
            var go = new GameObject("DroneVisual_" + kind);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<DroneVisual>();
            v.Kind = kind;
            if (kind == DroneKind.Recon) v.BuildRecon(); else v.BuildFpv();
            return v;
        }

        static void Mats()
        {
            if (_body != null) return;
            _body = WorldUI.Lit(new Color(0.16f, 0.16f, 0.17f), 0.35f);
            _dark = WorldUI.Lit(new Color(0.04f, 0.04f, 0.045f), 0.45f);
            _prop = WorldUI.Lit(new Color(0.09f, 0.09f, 0.1f), 0.3f);
            _orange = WorldUI.Lit(new Color(0.55f, 0.2f, 0.06f), 0.35f);
            _olive = WorldUI.Lit(new Color(0.2f, 0.22f, 0.14f), 0.25f);
        }

        void BuildRecon()
        {
            // ~35 cm diagonal: body, 4 folded-out arms, motors, gimbal camera, and the 3D-printed drop clamp
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>();
            Add(parts, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.09f, 0.06f, 0.2f));
            for (int i = 0; i < 4; i++)
            {
                float yaw = 45f + i * 90f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Add(parts, PrimitiveType.Cube, dir * 0.09f, new Vector3(0f, yaw, 0f), new Vector3(0.018f, 0.014f, 0.16f));
                Add(parts, PrimitiveType.Cylinder, dir * 0.17f + Vector3.up * 0.012f, Vector3.zero, new Vector3(0.03f, 0.012f, 0.03f));
                _rotors[i] = Rotor(dir * 0.17f + Vector3.up * 0.03f, 0.2f, _prop);
            }
            Add(parts, PrimitiveType.Sphere, new Vector3(0f, -0.045f, 0.09f), Vector3.zero, new Vector3(0.045f, 0.04f, 0.045f)); // gimbal
            Add(parts, PrimitiveType.Cube, new Vector3(0f, -0.05f, -0.02f), Vector3.zero, new Vector3(0.05f, 0.025f, 0.06f));    // drop clamp
            Merge(parts, _body);
        }

        void BuildFpv()
        {
            // 7" X frame (~30 cm), stack, battery on top, camera up front, warhead slung under the belly
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>();
            Add(parts, PrimitiveType.Cube, Vector3.zero, new Vector3(0f, 45f, 0f), new Vector3(0.025f, 0.006f, 0.34f));
            Add(parts, PrimitiveType.Cube, Vector3.zero, new Vector3(0f, -45f, 0f), new Vector3(0.025f, 0.006f, 0.34f));
            Add(parts, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.04f, 0.03f, 0.08f));
            Add(parts, PrimitiveType.Cube, new Vector3(0f, 0.045f, -0.01f), Vector3.zero, new Vector3(0.04f, 0.028f, 0.1f));
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward;
                Add(parts, PrimitiveType.Cylinder, dir * 0.16f + Vector3.up * 0.01f, Vector3.zero, new Vector3(0.028f, 0.01f, 0.028f));
                _rotors[i] = Rotor(dir * 0.16f + Vector3.up * 0.025f, 0.17f, _orange);
            }
            Merge(parts, _dark);
            // PG-7 style warhead: olive cylinder + nose cone under the frame, pointing forward
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(w.GetComponent<Collider>());
            w.transform.SetParent(transform, false);
            w.transform.localPosition = new Vector3(0f, -0.05f, 0.03f);
            w.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            w.transform.localScale = new Vector3(0.07f, 0.14f, 0.07f);
            var r = w.GetComponent<Renderer>();
            r.sharedMaterial = _olive;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Add(System.Collections.Generic.List<(Mesh, Matrix4x4)> parts, PrimitiveType t, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            parts.Add((PrimitiveMesh(t), Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale)));
        }

        static readonly System.Collections.Generic.Dictionary<PrimitiveType, Mesh> _meshes = new System.Collections.Generic.Dictionary<PrimitiveType, Mesh>();

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            if (_meshes.TryGetValue(t, out var m) && m != null) return m;
            var g = GameObject.CreatePrimitive(t);
            m = g.GetComponent<MeshFilter>().sharedMesh;
            Destroy(g);
            _meshes[t] = m;
            return m;
        }

        void Merge(System.Collections.Generic.List<(Mesh, Matrix4x4)> parts, Material mat)
        {
            var ci = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++) ci[i] = new CombineInstance { mesh = parts[i].Item1, transform = parts[i].Item2 };
            var mesh = new Mesh { name = "DroneBody" };
            mesh.CombineMeshes(ci, true, true);
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Transform Rotor(Vector3 at, float span, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = "Rotor";
            g.transform.SetParent(transform, false);
            g.transform.localPosition = at;
            g.transform.localScale = new Vector3(span, 0.003f, 0.016f);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        /// <summary>Orient along the flight velocity: nose forward, pitched down into the motion (FPV dives nose-first).</summary>
        public void Pose(Vector3 velocity, float dt)
        {
            _spin += dt * (Kind == DroneKind.Fpv ? 2900f : 2300f);
            for (int i = 0; i < 4; i++)
                if (_rotors[i] != null) _rotors[i].localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? _spin : -_spin) + i * 37f, 0f);
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude < 1e-4f) return;
            float tilt = Kind == DroneKind.Fpv
                ? Mathf.Clamp(Mathf.Atan2(-velocity.y, flat.magnitude) * Mathf.Rad2Deg + 25f, 10f, 70f)
                : Mathf.Clamp(flat.magnitude * 2.5f, 0f, 22f);
            var want = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(tilt, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, Mathf.Clamp01(dt * 4f));
        }
    }

    /// <summary>
    /// The VOG-17 dropped by the recon drone: a small olive grenade with a white 3D-printed tail, falling freely
    /// from the clamp to the impact point (t = √(2h/g)), slight wobble. Destroyed by <see cref="CombatAmbience"/>
    /// at detonation.
    /// </summary>
    public sealed class FallingMunition : MonoBehaviour
    {
        Vector3 _from, _to;
        float _t, _dur;

        public static FallingMunition Spawn(Vector3 from, Vector3 to)
        {
            var root = new GameObject("FallingVOG");
            var f = root.AddComponent<FallingMunition>();
            f._from = from; f._to = to; f._dur = EnvMath.FallTime(from.y - to.y);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.03f, 0.04f, 0.03f);
            body.GetComponent<Renderer>().sharedMaterial = WorldUI.Lit(new Color(0.22f, 0.24f, 0.16f), 0.3f);
            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(tail.GetComponent<Collider>());
            tail.transform.SetParent(root.transform, false);
            tail.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            tail.transform.localScale = new Vector3(0.032f, 0.05f, 0.032f);
            tail.GetComponent<Renderer>().sharedMaterial = WorldUI.Lit(new Color(0.62f, 0.6f, 0.55f), 0.2f);
            root.transform.position = from;
            return f;
        }

        public float Duration => _dur;

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _dur));
            var p = Vector3.Lerp(_from, _to, k);
            p.y = _from.y - (_from.y - _to.y) * k * k; // free fall: distance ∝ t²
            transform.position = p;
            transform.rotation = Quaternion.Euler(Mathf.Sin(_t * 9f) * 8f, _t * 120f, Mathf.Cos(_t * 7f) * 8f);
            if (_t > _dur + 0.5f) Destroy(gameObject);
        }
    }
}
