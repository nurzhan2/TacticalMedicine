using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>Designer knobs of the continuous battle (asset: Assets/Data/BattleSettings.asset).</summary>
    [CreateAssetMenu(menuName = "FieldMedic/Battle Settings", fileName = "BattleSettings")]
    public sealed class BattleSettings : ScriptableObject
    {
        [Header("Event director")]
        [Tooltip("Seconds between director events at intensity 1.")]
        public Vector2 eventInterval = new Vector2(5f, 20f);
        [Tooltip("Weights by BattleEventType: Wave, ArtilleryBarrage, AirStrike, MgBurst, DroneDive, TankShot, HelicopterRun.")]
        public float[] eventWeights = { 2.5f, 2f, 1.2f, 3f, 1.5f, 2.2f, 1f };
        public int seed = 20261015;

        [Header("Intensity / focus")]
        public float swellAmplitude = 0.3f;
        public float swellPeriod = 75f;
        [Tooltip("Near intensity during a medical focus window (3–5 s).")]
        public float focusLevel = 0.3f;
        public float focusSeconds = 4f;
        [Tooltip("The background never gets quieter than this.")]
        public float backgroundFloor = 0.45f;
        [Tooltip("No director blast closer than this to the player during focus (m).")]
        public float focusSafeRadius = 60f;
        [Tooltip("Closest a director blast may land to the player outside focus (m).")]
        public float nearBlastMin = 16f;

        [Header("Enemy fire at the player (Combat / Exam)")]
        public float exposureWindow = 10f;
        [Tooltip("Cumulative exposed seconds inside the window that mean the player is hit.")]
        public float exposureLimit = 4f;
        [Tooltip("Line-of-fire checks per second.")]
        public float checkRate = 10f;
        [Tooltip("Near misses per second while exposed (cracks, impacts next to the player).")]
        public Vector2 nearMissRate = new Vector2(1.5f, 4f);
        [Tooltip("Suppressive impacts per second on the player's cover while he is hidden.")]
        public float coverHitRate = 0.6f;
        [Tooltip("Suppression decay per second (vignette, breathing).")]
        public float suppressionDecay = 0.3f;

        [Header("Layers")]
        public int nearEnemies = 8;
        public int nearFriendlies = 3;
        public int crowdSoldiers = 44;
        public int horizonFigures = 160;
        public float soldierRespawnDelay = 7f;
        [Tooltip("Probability per second that an exposed near enemy is hit by our fire.")]
        public float enemyCasualtyRate = 0.025f;
    }
}
