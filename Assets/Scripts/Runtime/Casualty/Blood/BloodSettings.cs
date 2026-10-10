using UnityEngine;

namespace FieldMedic.Casualty.Blood
{
    /// <summary>Shared blood materials/tuning. Lives in Resources/BloodSettings.asset (built by CasualtyAssetBuilder).</summary>
    [CreateAssetMenu(menuName = "FieldMedic/Blood Settings")]
    public sealed class BloodSettings : ScriptableObject
    {
        public Material particle;          // jet / drip droplets
        public Material pool;              // ground pool (transparent, wet)
        public Material woundFragment;     // small fragment hole
        public Material woundRagged;       // ragged laceration
        public Material woundChestEntry;
        public Material woundExit;
        public Material stumpFlesh;
        public GameObject stumpPrefab;     // Assets/Art/Gear/AmputationStump.fbx
        [Tooltip("Film thickness used to turn lost volume into pool area (m). Mud soaks a lot up.")]
        public float poolThickness = 0.0035f;
        public float maxPoolRadius = 0.9f;
        public int maxJetParticles = 400;

        static BloodSettings _cached;
        public static BloodSettings Load()
        {
            if (_cached == null) _cached = Resources.Load<BloodSettings>("BloodSettings");
            return _cached;
        }
    }
}
