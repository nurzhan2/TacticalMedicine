using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Helicopter landing zone behind the ruins: a VS-17 style marker panel pegged to the ground and a coloured
    /// smoke grenade that the evacuation phase pops (w4). The smoke drifts downwind so the pilot reads the wind.
    /// </summary>
    public sealed class LzMarker : MonoBehaviour
    {
        public static LzMarker Instance { get; private set; }

        public ParticleSystem smoke;
        [Tooltip("Smoke already burning when the scene starts (screenshots / training).")]
        public bool smokeOnStart = true;

        public bool SmokeActive => smoke != null && smoke.isEmitting;
        public Vector3 TouchdownPoint => transform.position;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (smoke == null) return;
            if (smokeOnStart) PopSmoke(); else smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void PopSmoke()
        {
            if (smoke != null && !smoke.isEmitting) smoke.Play(true);
        }

        public void StopSmoke()
        {
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
