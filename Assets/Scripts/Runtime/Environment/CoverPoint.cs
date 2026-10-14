using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// A spot right behind a cover piece where a kneeling player's head is shielded from the enemy side.
    /// Placed by the battlefield builder for every solid cover object; the PlayMode cover test (and later the
    /// battle director's exposure logic) ray-cast from the enemy side to <see cref="HeadPosition"/>.
    /// </summary>
    public sealed class CoverPoint : MonoBehaviour
    {
        [Tooltip("The cover object this point hides behind (for messages).")]
        public GameObject cover;
        [Tooltip("Direction the threat comes FROM, i.e. from the head toward the enemy (world, horizontal).")]
        public Vector3 threatDirection = Vector3.forward;
        [Tooltip("Eye height above the ground at this point (kneeling 1.0–1.2 m).")]
        public float eyeHeight = BattlefieldLayout.KneelEyeHeight;
        [Tooltip("Half-angle of the protected arc (degrees) — narrow pieces protect a narrow arc.")]
        public float arcHalfAngle = 10f;

        public Vector3 HeadPosition => transform.position + Vector3.up * eyeHeight;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(HeadPosition, 0.12f);
            Gizmos.DrawLine(HeadPosition, HeadPosition + threatDirection.normalized * 1.5f);
        }
    }

    /// <summary>Line-of-fire tests against the "Cover" layer (shared by tests, w3 enemy fire and debug tools).</summary>
    public static class CoverMath
    {
        public const string CoverLayerName = "Cover";
        public const string CoverTag = "Cover";

        public static int CoverMask
        {
            get
            {
                int l = LayerMask.NameToLayer(CoverLayerName);
                return l >= 0 ? 1 << l : 0;
            }
        }

        /// <summary>True when something on the Cover layer lies between the shooter and the target point.</summary>
        public static bool Blocked(Vector3 shooter, Vector3 target, out RaycastHit hit, int mask = -1)
        {
            if (mask == -1) mask = CoverMask;
            Vector3 d = target - shooter;
            float len = d.magnitude;
            hit = default;
            if (len < 1e-3f) return false;
            return Physics.Raycast(shooter, d / len, out hit, len - 0.02f, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Shooter positions on the enemy side for a head at <paramref name="head"/>: the threat
        /// direction and ±arc at the given range, a little above the head (enemy standing/kneeling on the flat).</summary>
        public static IEnumerable<Vector3> ThreatOrigins(Vector3 head, Vector3 threatDir, float arcHalfAngle, float range = 120f, float rise = 0.35f)
        {
            threatDir.y = 0f;
            threatDir.Normalize();
            foreach (float a in new[] { -arcHalfAngle, 0f, arcHalfAngle })
                yield return head + Quaternion.Euler(0f, a, 0f) * threatDir * range + Vector3.up * rise;
        }

        /// <summary>True when every threat origin's line of fire to the head is blocked by cover.</summary>
        public static bool Shielded(Vector3 head, Vector3 threatDir, float arcHalfAngle, out string detail)
        {
            foreach (var o in ThreatOrigins(head, threatDir, arcHalfAngle))
            {
                if (!Blocked(o, head, out _))
                {
                    detail = $"open line of fire from {o}";
                    return false;
                }
            }
            detail = "ok";
            return true;
        }
    }
}
