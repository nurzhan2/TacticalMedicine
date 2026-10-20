using FieldMedic.Casualty.Blood;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// The player's own wounded limb (w9b self-aid). Only the head and the hands are tracked, so the limb is
    /// estimated: three points root (shoulder/hip) → joint (elbow/knee) → distal (wrist/ankle) → tip (hand/foot),
    /// in the same "segment units" as <see cref="LimbGeometry"/> (0 root, 1 joint, 2 wrist/ankle). Draws the sleeve
    /// (tapered tubes), the wound with the soaked cloth and the arterial spurts in rhythm with the heart, and a
    /// blood pool on the ground. <see cref="SelfTourniquet"/> wraps around it.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public abstract class SelfLimb : MonoBehaviour
    {
        public SelfAidRun run;
        public Transform head;
        public bool left = true;
        public float rootRadius = 0.05f, jointRadius = 0.04f, distalRadius = 0.03f;

        protected Vector3 A, B, C, D; // root, joint, distal, tip (world)
        public Vector3 Root => A;
        public Vector3 Joint => B;
        public Vector3 Distal => C;
        public Vector3 Tip => D;
        public float Side => left ? -1f : 1f;

        Transform _upper, _lower, _soak, _woundPatch;
        Mesh _tube;
        Material _cloth, _soakMat;
        ParticleSystem _jet, _drip;
        Transform _pool;
        float _beat, _dripAccum, _poolR;
        bool _built;

        /// <summary>Recompute the four points from the tracked poses (called every frame before anyone reads them).</summary>
        public abstract void Solve();

        protected virtual Color ClothColor => new Color(0.25f, 0.26f, 0.17f);

        protected virtual void Start() => Build();

        public void Build()
        {
            if (_built) return;
            _built = true;
            Solve();
            _tube = Tube(16);
            _cloth = Kit.Mat("SelfSleeve", ClothColor, 0.12f);
            _soakMat = Kit.Mat("SelfSleeveSoak", new Color(0.2f, 0.02f, 0.015f), 0.45f);
            _upper = Kit.MeshPart(transform, "SleeveUpper", _tube, _cloth, Vector3.zero, Quaternion.identity, Vector3.one).transform;
            _lower = Kit.MeshPart(transform, "SleeveLower", _tube, _cloth, Vector3.zero, Quaternion.identity, Vector3.one).transform;
            _soak = Kit.MeshPart(transform, "BloodSoak", _tube, _soakMat, Vector3.zero, Quaternion.identity, Vector3.one).transform;
            var s = BloodSettings.Load();
            var woundMat = s != null && s.woundRagged != null ? s.woundRagged : Kit.Mat("SelfWound", new Color(0.18f, 0.01f, 0.01f), 0.5f);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "WoundPatch";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            var qr = q.GetComponent<MeshRenderer>();
            qr.sharedMaterial = woundMat;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _woundPatch = q.transform;
            _jet = Particles("SelfJet", true, s);
            _drip = Particles("SelfDrip", false, s);
            var pool = Kit.Part(transform, PrimitiveType.Cylinder, "SelfBloodPool", Vector3.zero, Vector3.zero,
                s != null && s.pool != null ? s.pool : Kit.Mat("SelfPool", new Color(0.16f, 0.01f, 0.01f), 0.8f));
            _pool = pool.transform;
            _pool.SetParent(null, true); // stays on the ground where he bled
            Refresh();
        }

        void OnDestroy() { if (_pool != null) Destroy(_pool.gameObject); }

        ParticleSystem Particles(string name, bool jet, BloodSettings s)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.startLifetime = jet ? 0.8f : 1.2f;
            main.startSize = jet ? 0.012f : 0.008f;
            main.maxParticles = jet ? 240 : 60;
            main.startColor = new Color(0.45f, 0.02f, 0.02f, 1f);
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.dampen = 1f; col.bounce = 0f; col.lifetimeLoss = 1f;
            col.enableDynamicColliders = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (jet) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.008f; r.lengthScale = 1.2f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (s != null && s.particle != null) r.sharedMaterial = s.particle;
            return ps;
        }

        // ------------------------------------------------------------------ geometry (segment units)
        public Vector3 PointAt(float pos)
        {
            if (pos <= 1f) return Vector3.Lerp(A, B, Mathf.Max(0f, pos));
            if (pos <= 2f) return Vector3.Lerp(B, C, pos - 1f);
            return Vector3.LerpUnclamped(C, D, (pos - 2f) / 0.6f);
        }

        public Vector3 AxisAt(float pos)
        {
            Vector3 v = pos < 1f ? B - A : pos < 2f ? C - B : D - C;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.down;
        }

        public float RadiusAt(float pos) =>
            pos <= 1f ? Mathf.Lerp(rootRadius, jointRadius, pos) : pos <= 2f ? Mathf.Lerp(jointRadius, distalRadius, pos - 1f) : distalRadius * 0.9f;

        /// Outward direction on the limb surface that faces the eyes (where the band's windlass sits, where the wound shows).
        public Vector3 TopAt(float pos)
        {
            Vector3 p = PointAt(pos), axis = AxisAt(pos);
            Vector3 toEye = head != null ? head.position - p : Vector3.up;
            Vector3 up = Vector3.ProjectOnPlane(toEye.normalized + Vector3.up * 0.3f, axis);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, axis);
            return up.sqrMagnitude < 1e-6f ? Vector3.Cross(axis, Vector3.right).normalized : up.normalized;
        }

        /// <summary>Distance from a point to the limb axis and its position (segment units, 0..2.6).</summary>
        public float Project(Vector3 p, out float pos)
        {
            float best = float.MaxValue; pos = 0f;
            Seg(p, A, B, 0f, 1f, ref best, ref pos);
            Seg(p, B, C, 1f, 2f, ref best, ref pos);
            Seg(p, C, D, 2f, 2.6f, ref best, ref pos);
            return best;
        }

        static void Seg(Vector3 p, Vector3 a, Vector3 b, float p0, float p1, ref float best, ref float pos)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = Vector3.Distance(p, a + ab * t);
            if (d < best) { best = d; pos = Mathf.Lerp(p0, p1, t); }
        }

        // ------------------------------------------------------------------ per frame
        protected virtual void LateUpdate()
        {
            if (!_built) return;
            Solve();
            Refresh();
            Bleed(Time.deltaTime);
        }

        void Refresh()
        {
            Place(_upper, A, B, rootRadius, jointRadius * 1.02f);
            Place(_lower, B, C, jointRadius, distalRadius);
            if (run == null) { _soak.gameObject.SetActive(false); _woundPatch.gameObject.SetActive(false); return; }
            float wp = run.cfg.WoundPosition;
            // soaked cloth: spreads along the sleeve from the wound with the blood lost
            float spread = Mathf.Clamp(0.12f + run.wound.bloodLostMl / 900f, 0.12f, 0.85f);
            float s0 = Mathf.Max(0f, wp - spread * 0.6f), s1 = Mathf.Min(2f, wp + spread);
            _soak.gameObject.SetActive(true);
            Place(_soak, PointAt(s0), PointAt(s1), RadiusAt(s0) + 0.0025f, RadiusAt(s1) + 0.0025f);
            Vector3 top = TopAt(wp);
            Vector3 c = PointAt(wp) + top * (RadiusAt(wp) + 0.004f);
            _woundPatch.gameObject.SetActive(true);
            _woundPatch.SetPositionAndRotation(c, Quaternion.LookRotation(-top, AxisAt(wp)));
            _woundPatch.localScale = new Vector3(0.045f, 0.07f, 1f);
        }

        void Place(Transform t, Vector3 a, Vector3 b, float r0, float r1)
        {
            Vector3 ab = b - a;
            float len = Mathf.Max(0.001f, ab.magnitude);
            t.SetPositionAndRotation(a, Quaternion.LookRotation(ab / len, Vector3.up));
            // the tube mesh is radius 1 at z = 0 and radius `taper` at z = 1: approximate with the mean radius scale
            float r = (r0 + r1) * 0.5f;
            t.localScale = new Vector3(r, r, len);
        }

        void Bleed(float dt)
        {
            if (run == null || _jet == null) return;
            var p = run.patient;
            float flow = run.FlowMlPerSec;
            // the cap that keeps training safe zeroes the volume: keep the look of an open wound
            if (run.cfg.training && !run.wound.IsControlled) flow = Mathf.Max(flow, 4f * run.wound.tourniquetFactor);
            float wp = run.cfg.WoundPosition;
            Vector3 top = TopAt(wp);
            Vector3 at = PointAt(wp) + top * RadiusAt(wp);
            float norm = Mathf.Clamp01(flow / 12f);
            if (flow > 0.3f)
            {
                float hz = Mathf.Max(0.5f, p.HeartRate / 60f);
                float prev = _beat;
                _beat += dt * hz;
                if (Mathf.FloorToInt(_beat) != Mathf.FloorToInt(prev)) Spurt(at, top, norm, p.Perfusion);
            }
            float dripRate = Mathf.Clamp(flow * 1.2f, 0f, 18f) + (run.wound.bloodLostMl > 150f && !run.wound.IsControlled ? 1.5f : 0f);
            _dripAccum += dripRate * dt;
            Vector3 low = D; // blood runs down to the fingertips and drips off
            while (_dripAccum >= 1f)
            {
                _dripAccum -= 1f;
                _drip.Emit(new ParticleSystem.EmitParams
                {
                    position = (Random.value < 0.5f ? low : at) + Random.insideUnitSphere * 0.01f,
                    velocity = Random.insideUnitSphere * 0.03f,
                    startSize = Random.Range(0.005f, 0.01f),
                    startLifetime = 1.4f,
                    applyShapeToPosition = false,
                }, 1);
            }
            // pool on the ground under the hand (grows with the volume, never shrinks)
            float target = Mathf.Clamp(0.06f + Mathf.Sqrt(run.patient.bloodLostMl / 1500f) * 0.55f, 0.06f, 0.75f);
            _poolR = Mathf.MoveTowards(_poolR, target, dt * 0.05f);
            if (Physics.Raycast(low + Vector3.up * 0.05f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) && _poolR > 0.07f)
            {
                if (_pool.localScale.x < 0.01f) _pool.position = hit.point + Vector3.up * 0.004f;
                _pool.localScale = new Vector3(_poolR * 2f, 0.002f, _poolR * 1.6f);
            }
        }

        void Spurt(Vector3 at, Vector3 outward, float norm, float perfusion)
        {
            float speed = Mathf.Lerp(0.6f, 2.6f, norm) * Mathf.Lerp(0.5f, 1f, perfusion);
            int n = Mathf.RoundToInt(Mathf.Lerp(6, 26, norm));
            for (int i = 0; i < n; i++)
                _jet.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 0.006f,
                    velocity = (outward + Random.insideUnitSphere * 0.2f).normalized * speed * Random.Range(0.75f, 1.1f),
                    startSize = Random.Range(0.007f, 0.016f),
                    startLifetime = Random.Range(0.5f, 0.9f),
                    applyShapeToPosition = false,
                }, 1);
        }

        /// <summary>Unit tube along +Z (radius 1, slight taper to 0.9 at z = 1), open ends.</summary>
        static Mesh Tube(int seg)
        {
            var m = new Mesh { name = "SelfSleeveTube" };
            var v = new Vector3[(seg + 1) * 2];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i * 2] = dir * 1.05f;
                v[i * 2 + 1] = dir * 0.92f + Vector3.forward;
                n[i * 2] = n[i * 2 + 1] = dir;
                uv[i * 2] = new Vector2(i / (float)seg, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)seg, 1f);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 6, a = i * 2;
                tri[k] = a; tri[k + 1] = a + 2; tri[k + 2] = a + 1;
                tri[k + 3] = a + 1; tri[k + 4] = a + 2; tri[k + 5] = a + 3;
            }
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// The wounded arm. Upper arm is not tracked: the shoulder is placed from the head pose (torso yaw follows the
    /// head), the wrist comes from the tracked hand, the elbow from a two-bone IK with a down-and-out bend hint.
    /// </summary>
    public sealed class SelfArm : SelfLimb
    {
        public Transform hand;           // the wounded hand (HandRig transform)
        public float upperLength = 0.30f, forearmLength = 0.27f;
        public Vector3 shoulderOffset = new Vector3(0.19f, -0.25f, -0.07f); // x mirrored for the left side
        float _yaw;
        bool _yawInit;

        public override void Solve()
        {
            if (head == null || hand == null) return;
            float hy = head.eulerAngles.y;
            if (!_yawInit) { _yaw = hy; _yawInit = true; }
            float diff = Mathf.DeltaAngle(_yaw, hy);
            if (Mathf.Abs(diff) > 30f) _yaw += (Mathf.Abs(diff) - 30f) * Mathf.Sign(diff); // shoulders lag the head
            var yaw = Quaternion.Euler(0f, _yaw, 0f);
            A = head.position + yaw * new Vector3(shoulderOffset.x * Side, shoulderOffset.y, shoulderOffset.z);
            C = hand.TransformPoint(new Vector3(0f, -0.005f, -0.035f));  // wrist, behind the palm
            D = hand.TransformPoint(new Vector3(0f, -0.01f, 0.09f));     // fingertips
            Vector3 hint = A + yaw * new Vector3(0.22f * Side, -0.32f, -0.12f);
            B = TwoBone(A, C, hint, upperLength, forearmLength);
        }

        /// <summary>Elbow/knee position for a two-bone chain root→end with a bend hint (law of cosines).</summary>
        public static Vector3 TwoBone(Vector3 root, Vector3 end, Vector3 hint, float l1, float l2)
        {
            Vector3 d = end - root;
            float dist = d.magnitude;
            if (dist < 1e-5f) return root + Vector3.down * l1;
            Vector3 dir = d / dist;
            Vector3 bend = Vector3.ProjectOnPlane(hint - root, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            float reach = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.001f);
            float a = (l1 * l1 - l2 * l2 + reach * reach) / (2f * reach);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            return root + dir * a + bend * h;
        }
    }
}
