using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// One near soldier (rigged model + Animator with the generated Run/StandAim clips and the Mixamo kneel/fall
    /// clips). State is driven by <see cref="NearInfantry"/>; this component only holds per-agent data and plays
    /// the animator states (CrossFade by name hash, no transition graph).
    /// </summary>
    public sealed class SoldierAgent : MonoBehaviour
    {
        public Team team;
        public Animator animator;
        public SoldierPoser poser;

        public SoldierState State { get; private set; } = SoldierState.Inactive;
        internal Vector3 goal;            // run destination
        internal Vector3 aim;             // where the rifle points (enemy line / player / their troops)
        internal float stateUntil;        // time the current state ends
        internal float nextBurst;         // time of the next burst
        internal int roundsLeft;          // rounds left in the current burst
        internal float nextRound;
        internal int spotIndex = -1;
        internal float speed = 3.6f;
        internal bool mg;                 // a machine gunner: longer bursts, MG sound
        /// <summary>Set by <see cref="EnemyFire"/> each check: this shooter has an open line to the player.</summary>
        [System.NonSerialized] public bool seesPlayer;
        public int Shots { get; internal set; }
        public int Deaths { get; internal set; }

        static readonly int RunHash = Animator.StringToHash("Run");
        static readonly int KneelHash = Animator.StringToHash("Kneel");
        static readonly int StandHash = Animator.StringToHash("Stand");
        static readonly int[] DieHash = { Animator.StringToHash("Die0"), Animator.StringToHash("Die1"), Animator.StringToHash("Die2") };

        /// <summary>Muzzle position (from the posed rifle when the pose layer is on, else an estimate).</summary>
        public Vector3 Muzzle
        {
            get
            {
                if (poser != null && poser.Weight > 0.5f) return poser.Muzzle;
                var t = transform;
                float h = State == SoldierState.Kneel ? 1.12f : 1.45f;
                return t.position + Vector3.up * h + t.forward * 0.72f + t.right * 0.08f;
            }
        }

        /// <summary>Head position (for our side's fire and the player's view).</summary>
        public Vector3 Head => transform.position + Vector3.up * (State == SoldierState.Kneel ? 1.1f : State == SoldierState.Run || State == SoldierState.Stand ? 1.6f : 0.3f);

        public bool Alive => State == SoldierState.Run || State == SoldierState.Kneel || State == SoldierState.Stand;
        /// <summary>In a firing position (kneeling / standing aim) — a potential shooter at the player.</summary>
        public bool InFiringPosition => State == SoldierState.Kneel || State == SoldierState.Stand;

        internal void Enter(SoldierState s, float fade = 0.25f)
        {
            State = s;
            if (s == SoldierState.Inactive) { gameObject.SetActive(false); return; }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (poser != null)
            {
                poser.pose = s switch { SoldierState.Run => SoldierPose.Run, SoldierState.Kneel => SoldierPose.KneelAim, SoldierState.Stand => SoldierPose.Aim, _ => SoldierPose.None };
                poser.speed = speed;
            }
            if (animator == null || !animator.isActiveAndEnabled) return;
            int hash = s switch
            {
                SoldierState.Run => RunHash,
                SoldierState.Kneel => KneelHash,
                SoldierState.Stand => StandHash,
                SoldierState.Dying => DieHash[Random.Range(0, DieHash.Length)],
                _ => 0,
            };
            if (hash == 0) return;
            animator.CrossFadeInFixedTime(hash, fade, 0, s == SoldierState.Run ? Random.value : 0f);
        }
    }
}
