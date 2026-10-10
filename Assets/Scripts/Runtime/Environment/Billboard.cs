using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>Keeps a quad facing the main camera (flare glow sprite).</summary>
    public sealed class Billboard : MonoBehaviour
    {
        public Transform target;

        void LateUpdate()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;
            if (target != null) Face(target.position);
        }

        public void Face(Vector3 viewer)
        {
            var d = transform.position - viewer;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
