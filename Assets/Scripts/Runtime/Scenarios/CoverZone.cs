using FieldMedic.Casualty;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Covered spot (dugout corner): when a casualty's hips are dragged inside the box during care under fire,
    /// the protocol moves to tactical field care (MARCH). Polls the bounds — no trigger/rigidbody pairing needed.
    /// </summary>
    public sealed class CoverZone : MonoBehaviour
    {
        public Vector3 size = new Vector3(2.2f, 2f, 2.2f);

        public bool Contains(Vector3 p)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.y) <= size.y * 0.5f && Mathf.Abs(l.z) <= size.z * 0.5f;
        }

        void Update()
        {
            foreach (var c in CasualtyController.All)
            {
                if (c == null || !c.IsDown || c.Protocol == null || c.Protocol.phase != Phase.CareUnderFire) continue;
                var hips = c.rig != null ? c.rig.Bone(HumanBodyBones.Hips) : null;
                if (hips != null && Contains(hips.position)) c.Protocol.Perform(MedAction.MoveToCover);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
