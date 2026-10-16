using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>Trigger collider on a casualty bone, mapped to a medical body region.</summary>
    public sealed class CasualtyBodyPart : MonoBehaviour
    {
        public BodyRegion region;
        public Transform bone;
        public CasualtyRig rig;
        Collider _col;
        public Collider Collider { get { if (_col == null) _col = GetComponent<Collider>(); return _col; } }
    }
}
