using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Body-locked chest rig on the player's vest (follows the torso yaw lazily, like the IFAK) holding the smoke
    /// grenades in their pouch slots.
    /// </summary>
    public sealed class VestMount : MonoBehaviour
    {
        public Transform head;
        public float belowHead = 0.42f;
        public float forward = 0.2f;
        public float side = -0.16f;   // left chest
        float _yaw;
        bool _init;

        void LateUpdate() => Follow(false);

        public void Follow(bool snap)
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;
            float headYaw = head.eulerAngles.y;
            if (!_init || snap) { _yaw = headYaw; _init = true; }
            float diff = Mathf.DeltaAngle(_yaw, headYaw);
            if (Mathf.Abs(diff) > 35f) _yaw = Mathf.MoveTowardsAngle(_yaw, headYaw, (Mathf.Abs(diff) - 35f) * Time.deltaTime * 4f + 0.5f);
            var rot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 p = head.position + rot * new Vector3(side, 0f, forward);
            p.y = head.position.y - belowHead;
            transform.SetPositionAndRotation(p, rot);
        }
    }
}
