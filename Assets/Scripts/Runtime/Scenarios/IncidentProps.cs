using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// What the incident leaves behind, so every scenario reads at a glance: scorched earth and fragments where the
    /// VOG/mortar/mine went off, a broken FPV quad (frame, motors, snapped props) at the entrance, a bullet-torn
    /// sandbag for the sniper, dust and a knocked-off helmet after the glide bomb, a lingering smoke plume.
    /// Primitives only (cheap: ~10 draw calls per incident).
    /// </summary>
    public static class IncidentProps
    {
        static Material _scorch, _metal, _dark, _plastic, _sand, _helmet;

        static void Mats()
        {
            if (_scorch != null) return;
            _scorch = WorldUI.Lit(new Color(0.03f, 0.028f, 0.025f), 0.05f);
            _metal = WorldUI.Lit(new Color(0.25f, 0.24f, 0.22f), 0.5f);
            _dark = WorldUI.Lit(new Color(0.06f, 0.06f, 0.065f), 0.35f);
            _plastic = WorldUI.Lit(new Color(0.55f, 0.18f, 0.08f), 0.4f);
            _sand = WorldUI.Lit(new Color(0.42f, 0.38f, 0.28f), 0.1f);
            _helmet = WorldUI.Lit(new Color(0.27f, 0.29f, 0.2f), 0.25f);
        }

        public static GameObject Spawn(IncidentKind kind, Vector3 blast, Vector3 casualty)
        {
            Mats();
            var root = new GameObject("Incident_" + kind);
            root.transform.position = blast;
            var rng = new System.Random(kind.GetHashCode());
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            switch (kind)
            {
                case IncidentKind.DroneDrop:
                case IncidentKind.Mortar:
                case IncidentKind.Mine:
                    Scorch(root.transform, blast, kind == IncidentKind.Mortar ? 1.1f : 0.75f);
                    for (int i = 0; i < 7; i++) // fragments, splinters of the duckboards
                        Piece(root.transform, blast + new Vector3(R(-0.7f, 0.7f), 0.03f, R(-0.5f, 0.5f)), new Vector3(R(0.02f, 0.05f), 0.01f, R(0.02f, 0.08f)), R(0f, 360f), _metal);
                    if (kind == IncidentKind.Mortar) // the tail fin of the round
                    {
                        Piece(root.transform, blast + new Vector3(0.35f, 0.04f, 0.2f), new Vector3(0.05f, 0.05f, 0.14f), 30f, _dark);
                        for (int f = 0; f < 4; f++)
                            Piece(root.transform, blast + new Vector3(0.35f, 0.04f, 0.2f) + Quaternion.Euler(0f, 30f, f * 90f) * new Vector3(0.035f, 0f, -0.05f), new Vector3(0.004f, 0.05f, 0.05f), 30f, _dark);
                    }
                    if (kind == IncidentKind.Mine) // torn boot by the stump
                        Piece(root.transform, casualty + new Vector3(0.25f, 0.05f, -0.45f), new Vector3(0.1f, 0.09f, 0.22f), 70f, _dark);
                    Smoke(root.transform, blast, 0.5f);
                    break;
                case IncidentKind.Fpv:
                {
                    Scorch(root.transform, blast, 0.9f);
                    // broken quad: X frame, 4 motors, two snapped orange props, battery pack, antenna
                    var q = new GameObject("FpvWreck").transform;
                    q.SetParent(root.transform, false);
                    q.position = blast + new Vector3(-0.4f, 0.04f, -0.15f);
                    q.rotation = Quaternion.Euler(8f, 35f, 14f);
                    Piece(q, q.position, new Vector3(0.32f, 0.012f, 0.03f), 45f, _dark, true);
                    Piece(q, q.position, new Vector3(0.24f, 0.012f, 0.03f), -45f, _dark, true); // one arm snapped short
                    Piece(q, q.position + Vector3.up * 0.02f, new Vector3(0.07f, 0.035f, 0.11f), 35f, _dark, true);
                    for (int m = 0; m < 4; m++)
                    {
                        Vector3 p = q.position + Quaternion.Euler(0f, 35f + 45f + m * 90f, 0f) * Vector3.forward * (m == 1 ? 0.11f : 0.15f);
                        var motor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Object.Destroy(motor.GetComponent<Collider>());
                        motor.transform.SetParent(q, true);
                        motor.transform.position = p + Vector3.up * 0.015f;
                        motor.transform.localScale = new Vector3(0.035f, 0.012f, 0.035f);
                        motor.GetComponent<Renderer>().sharedMaterial = _metal;
                        if (m % 2 == 0) Piece(q, p + Vector3.up * 0.03f, new Vector3(0.12f, 0.003f, 0.018f), R(0f, 180f), _plastic, true);
                    }
                    Piece(root.transform, blast + new Vector3(0.5f, 0.03f, 0.35f), new Vector3(0.06f, 0.004f, 0.025f), 20f, _plastic); // prop blade
                    Smoke(root.transform, blast, 0.7f);
                    break;
                }
                case IncidentKind.SniperShot:
                {
                    // torn sandbag on the parapet, sand spilled on the duckboards under it
                    Vector3 p = new Vector3(casualty.x, 1.7f, casualty.z + 0.65f);
                    Piece(root.transform, p, new Vector3(0.05f, 0.05f, 0.02f), 0f, _scorch);
                    Piece(root.transform, new Vector3(casualty.x + 0.1f, 0.06f, casualty.z + 0.45f), new Vector3(0.22f, 0.02f, 0.14f), 15f, _sand);
                    break;
                }
                case IncidentKind.GlideBomb:
                    // the blast threw his helmet off and covered everything in dust
                    Piece(root.transform, casualty + new Vector3(-0.55f, 0.1f, 0.3f), new Vector3(0.22f, 0.13f, 0.26f), 50f, _helmet);
                    for (int i = 0; i < 6; i++)
                        Piece(root.transform, casualty + new Vector3(R(-0.9f, 0.9f), 0.04f, R(-0.5f, 0.5f)), new Vector3(R(0.06f, 0.14f), R(0.03f, 0.06f), R(0.06f, 0.12f)), R(0f, 360f), _sand);
                    Smoke(root.transform, casualty + Vector3.up * 0.5f, 1.2f);
                    break;
                case IncidentKind.Barrage:
                    for (int i = 0; i < 3; i++) Scorch(root.transform, casualty + new Vector3(R(-3f, 3f), 0f, R(-0.3f, 0.3f)), R(0.4f, 0.8f));
                    break;
            }
            return root;
        }

        static void Piece(Transform parent, Vector3 at, Vector3 size, float yaw, Material m, bool keepWorld = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, true);
            g.transform.position = at;
            g.transform.rotation = (keepWorld ? parent.rotation : Quaternion.identity) * Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Burnt, blackened patch on the floor (irregular: three overlapping flat discs).</summary>
        static void Scorch(Transform parent, Vector3 at, float size)
        {
            for (int i = 0; i < 3; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(d.GetComponent<Collider>());
                d.transform.SetParent(parent, true);
                d.transform.position = new Vector3(at.x + (i - 1) * size * 0.22f, 0.05f + i * 0.002f, at.z + ((i * 7) % 3 - 1) * size * 0.15f);
                float s = size * (1f - i * 0.2f);
                d.transform.localScale = new Vector3(s, 0.002f, s * 0.75f);
                d.transform.rotation = Quaternion.Euler(0f, i * 40f, 0f);
                var r = d.GetComponent<Renderer>();
                r.sharedMaterial = _scorch;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static void Smoke(Transform parent, Vector3 at, float amount)
        {
            var vfx = CombatVfx.Instance;
            if (vfx == null || vfx.smokeMaterial == null) return;
            var go = new GameObject("LingeringSmoke");
            go.transform.SetParent(parent, false);
            go.transform.position = at + Vector3.up * 0.1f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 40f;
            main.loop = false;
            main.startLifetime = 6f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startColor = new Color(0.32f, 0.31f, 0.3f, 0.35f);
            main.maxParticles = 40;
            main.gravityModifier = -0.02f;
            var em = ps.emission;
            em.rateOverTime = 3f * amount;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = vfx.smokeMaterial;
            ps.Play();
        }
    }
}
