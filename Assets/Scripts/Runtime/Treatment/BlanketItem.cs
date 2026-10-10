using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Thermal (space) blanket. Grab the folded pack with both hands and pull them apart to unfold it, carry it
    /// over him and let go: it drapes over the body (mesh conformed to the body once) and stops heat loss.
    /// </summary>
    public sealed class BlanketItem : MedItem
    {
        public bool Unfolded { get; private set; }
        public bool Draped { get; private set; }
        public Feedback LastFeedback { get; private set; }

        Transform _folded, _sheet;
        const float W = 1.4f, L = 2.1f;

        public override void BuildVisual()
        {
            Body.mass = 0.06f;
            allowTwoHands = true;
            var gold = Kit.Mat("BlanketGold", new Color(0.78f, 0.6f, 0.2f), 0.85f, 0.85f);
            var silver = Kit.Mat("BlanketSilver", new Color(0.75f, 0.76f, 0.78f), 0.85f, 0.85f);
            _folded = new GameObject("Folded").transform;
            _folded.SetParent(transform, false);
            Kit.Part(_folded, PrimitiveType.Cube, "Pack", Vector3.zero, new Vector3(0.1f, 0.016f, 0.12f), gold);
            Kit.Part(_folded, PrimitiveType.Cube, "Fold", new Vector3(0f, 0.0085f, 0.01f), new Vector3(0.095f, 0.002f, 0.1f), silver);
            Kit.Merge(_folded);
            _sheet = new GameObject("Sheet").transform;
            _sheet.SetParent(transform, false);
            Kit.Part(_sheet, PrimitiveType.Cube, "Gold", new Vector3(0f, 0.001f, 0f), new Vector3(W, 0.001f, L * 0.5f), gold);
            Kit.Part(_sheet, PrimitiveType.Cube, "Silver", new Vector3(0f, -0.001f, 0f), new Vector3(W, 0.001f, L * 0.5f), silver);
            _sheet.gameObject.SetActive(false);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
        }

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Draped && base.CanBeGrabbedBy(hand);

        void Update()
        {
            if (Draped || Unfolded || HolderCount < 2) return;
            var a = holders[0].Hand; var b = holders[1].Hand;
            if (Vector3.Distance(a.PalmPosition, b.PalmPosition) < 0.55f) return;
            Unfolded = true;
            _folded.gameObject.SetActive(false);
            _sheet.gameObject.SetActive(true);
            GetComponent<BoxCollider>().size = new Vector3(0.6f, 0.02f, 0.4f);
            Haptics.Play(a.Input, Haptics.Grab);
            Haptics.Play(b.Input, Haptics.Grab);
        }

        protected override void OnItemReleased(HandGrabber hand)
        {
            if (!Unfolded || Draped || IsGrabbed) return;
            var c = NearestCasualty(transform.position, 1.6f);
            if (c == null || c.rig == null) return;
            var hips = c.rig.Bone(HumanBodyBones.Hips);
            Vector3 flat = transform.position - hips.position; flat.y = 0f;
            if (flat.magnitude > 0.9f || transform.position.y < hips.position.y) return;
            Drape(c);
        }

        static readonly (HumanBodyBones a, HumanBodyBones b, float r)[] Capsules =
        {
            (HumanBodyBones.Hips, HumanBodyBones.Spine, 0.16f), (HumanBodyBones.Spine, HumanBodyBones.Chest, 0.17f),
            (HumanBodyBones.Chest, HumanBodyBones.Neck, 0.17f), (HumanBodyBones.Neck, HumanBodyBones.Head, 0.08f),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.09f), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.09f),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.065f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.065f),
            (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 0.06f), (HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 0.06f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.06f), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.06f),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.05f), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.05f),
        };

        /// <summary>Top of the body under a point, from the current bone pose (capsules around the bones).</summary>
        static float BodyTop(CasualtyRig rig, Vector3 p)
        {
            float top = float.MinValue;
            foreach (var (ba, bb, r) in Capsules)
            {
                var ta = rig.Bone(ba); var tb = rig.Bone(bb);
                if (ta == null || tb == null) continue;
                Vector2 a = new Vector2(ta.position.x, ta.position.z), b = new Vector2(tb.position.x, tb.position.z), q = new Vector2(p.x, p.z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(q, a + ab * t);
                if (d >= r) continue;
                float y = Mathf.Lerp(ta.position.y, tb.position.y, t) + Mathf.Sqrt(r * r - d * d);
                if (y > top) top = y;
            }
            var head = rig.Bone(HumanBodyBones.Head);
            if (head != null)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(head.position.x, head.position.z));
                if (d < 0.12f) top = Mathf.Max(top, head.position.y + Mathf.Sqrt(0.0144f - d * d));
            }
            return top;
        }

        /// <summary>Conform the sheet to the body: a grid dropped onto the casualty (body colliders) and ground.</summary>
        public void Drape(CasualtyController c)
        {
            var rig = c.rig;
            var hips = rig.Bone(HumanBodyBones.Hips);
            var head = rig.Bone(HumanBodyBones.Head);
            Vector3 along = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up).normalized;
            if (along.sqrMagnitude < 0.01f) along = Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
            // from the shoulders down to the feet (head stays out, but it is wrapped like a hood at the top)
            Vector3 center = hips.position - along * 0.2f;
            const int nx = 12, nz = 18;
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[verts.Length];
            float floor = c.transform.position.y;
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                Vector3 p = center + across * ((x / (float)nx - 0.5f) * W) + along * ((z / (float)nz - 0.5f) * L * 0.95f);
                float y = Mathf.Max(floor + 0.01f, BodyTop(rig, p) + 0.03f);
                verts[z * (nx + 1) + x] = new Vector3(p.x, y, p.z);
                uv[z * (nx + 1) + x] = new Vector2(x / (float)nx, z / (float)nz);
            }
            // smooth once so the sheet tents between limbs instead of following every collider edge
            var sm = (Vector3[])verts.Clone();
            for (int z = 1; z < nz; z++)
            for (int x = 1; x < nx; x++)
            {
                int i = z * (nx + 1) + x;
                float avg = (verts[i - 1].y + verts[i + 1].y + verts[i - nx - 1].y + verts[i + nx + 1].y) * 0.25f;
                sm[i].y = Mathf.Max(verts[i].y, (verts[i].y + avg) * 0.5f);
            }
            var tris = new List<int>();
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * (nx + 1) + x;
                tris.AddRange(new[] { i, i + nx + 1, i + 1, i + 1, i + nx + 1, i + nx + 2 });
            }
            var mesh = new Mesh { name = "DrapedBlanket" };
            var local = new Vector3[sm.Length];
            for (int i = 0; i < sm.Length; i++) local[i] = hips.InverseTransformPoint(sm[i]);
            mesh.vertices = local;
            mesh.uv = uv;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            ForceRelease();
            AttachTo(hips);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(1f / hips.lossyScale.x, 1f / hips.lossyScale.y, 1f / hips.lossyScale.z);
            _sheet.gameObject.SetActive(false);
            var gold = Kit.Mat("BlanketGold", new Color(0.78f, 0.6f, 0.2f), 0.85f, 0.85f);
            var go = Kit.MeshPart(transform, "Draped", mesh, gold, Vector3.zero, Quaternion.identity, Vector3.one);
            go.transform.localScale = hips.lossyScale; // undo the counter-scale for the hips-local mesh
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GetComponent<BoxCollider>().enabled = false;
            Draped = true;
            LastFeedback = c.Protocol.Perform(MedAction.ThermalBlanket);
        }
    }
}
