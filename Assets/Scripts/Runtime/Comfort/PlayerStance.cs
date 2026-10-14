using UnityEngine;

namespace FieldMedic.Comfort
{
    public enum Stance { Standing, Crouching, Kneeling, Prone }

    /// <summary>
    /// Physical stance from the real head height above the ground under the player (floor-level tracking, no
    /// artificial offsets): standing &gt; 1.35 m, crouching 1.0–1.35, kneeling 0.6–1.0 (sitting on heels),
    /// prone &lt; 0.6. Used by the cover/exposure logic and hints ("get down!").
    /// </summary>
    public sealed class PlayerStance : MonoBehaviour
    {
        public static PlayerStance Instance { get; private set; }

        public Transform head;
        public Transform rigRoot;
        public LayerMask groundMask = ~0;

        public float HeadHeight { get; private set; } = 1.65f;
        public Stance Current { get; private set; } = Stance.Standing;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void LateUpdate()
        {
            if (head == null) { if (Camera.main != null) head = Camera.main.transform; else return; }
            float ground = rigRoot != null ? rigRoot.position.y : 0f;
            if (Physics.Raycast(head.position, Vector3.down, out var hit, 3f, groundMask, QueryTriggerInteraction.Ignore)
                && hit.rigidbody == null)
                ground = Mathf.Max(ground - 0.5f, hit.point.y);
            HeadHeight = head.position.y - ground;
            Current = Classify(HeadHeight);
        }

        public static Stance Classify(float headHeight)
        {
            if (headHeight > 1.35f) return Stance.Standing;
            if (headHeight > 1.0f) return Stance.Crouching;
            if (headHeight > 0.6f) return Stance.Kneeling;
            return Stance.Prone;
        }
    }
}
