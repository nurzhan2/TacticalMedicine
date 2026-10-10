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
                float y = floor + 0.01f;
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, floor + 1.2f, p.z), Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Collide))
                {
                    bool body = h.collider.GetComponent<CasualtyBodyPart>() != null;
                    bool ground = !h.collider.isTrigger && h.rigidbody == null;
                    if ((body || ground) && h.point.y + (body ? 0.035f : 0.01f) > y) y = h.point.y + (body ? 0.035f : 0.01f);
                }
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
