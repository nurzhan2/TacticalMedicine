using UnityEngine;

namespace FieldMedic.Startup
{
    /// <summary>
    /// On the OVRCameraRig in Main_Core (w0b): tracking origin is the floor, so the rig root must sit exactly on the
    /// ground. At start it raycasts down from the spawn point and puts the rig on the first solid surface; if the
    /// head capsule starts inside a collider it is logged (a camera inside a mesh renders black/backfaces).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class RigGrounding : MonoBehaviour
    {
        public float probeAbove = 2.5f;
        public float probeDepth = 30f;
        public LayerMask groundMask = ~0;
        public static Vector3 LastGround { get; private set; }

        void Start() => Ground(transform, probeAbove, probeDepth, groundMask);

        /// <summary>Snaps <paramref name="rig"/> to the ground below it. Returns false if nothing was hit.</summary>
        public static bool Ground(Transform rig, float above = 2.5f, float depth = 30f, int mask = ~0)
        {
            var from = rig.position + Vector3.up * above;
            var hits = Physics.RaycastAll(from, Vector3.down, above + depth, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = default;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(rig)) continue; // own hands / body colliders
                if (h.distance < best) { best = h.distance; point = h.point; }
            }
            if (best == float.MaxValue)
            {
                Debug.LogWarning($"[FMVR] RigGrounding: no ground under the rig at {rig.position}");
                return false;
            }
            float dy = point.y - rig.position.y;
            if (Mathf.Abs(dy) > 0.01f)
            {
                rig.position += Vector3.up * dy;
                Debug.Log($"[FMVR] RigGrounding: rig moved {dy:+0.00;-0.00} m onto '{FirstHitName(hits, best)}' at {point}");
            }
            LastGround = point;
            // head volume (standing 1.7 m) must be free
            var overlaps = Physics.OverlapCapsule(rig.position + Vector3.up * 0.9f, rig.position + Vector3.up * 1.6f, 0.15f, mask, QueryTriggerInteraction.Ignore);
            foreach (var c in overlaps)
                if (!c.transform.IsChildOf(rig)) { Debug.LogWarning($"[FMVR] RigGrounding: player head volume overlaps '{c.name}'"); break; }
            return true;
        }

        static string FirstHitName(RaycastHit[] hits, float dist)
        {
            foreach (var h in hits) if (Mathf.Approximately(h.distance, dist)) return h.collider.name;
            return "?";
        }
    }
}
