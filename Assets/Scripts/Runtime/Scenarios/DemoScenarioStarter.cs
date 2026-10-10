using System.Collections;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Environment;
using FieldMedic.Medical;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Minimal Scenario 1 «Сброс» kickoff until the S5 scenario system lands: a recon drone hums overhead, drops
    /// a VOG next to Erlan after <see cref="delay"/> s (desktop: Enter starts it now) → arterial thigh bleed +
    /// fragment in the forearm, he falls. Care under fire (threat system armed) until he is dragged into the
    /// dugout cover zone, then the front calms down (tactical field care).
    /// </summary>
    public sealed class DemoScenarioStarter : MonoBehaviour
    {
        public CasualtyController casualty;
        public float delay = 15f;
        public bool started;

        float _t;

        void Update()
        {
            if (casualty == null) casualty = FindAnyObjectByType<CasualtyController>();
            if (casualty == null) return;
            _t += Time.deltaTime;
            if (!started && (_t >= delay || (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)))
            {
                started = true;
                StartCoroutine(Run());
            }
            if (started && casualty.Protocol != null && casualty.Protocol.phase != Phase.CareUnderFire && ThreatSystem.Instance != null)
            {
                ThreatSystem.Instance.underFire = false;
                if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.5f;
            }
        }

        IEnumerator Run()
        {
            var amb = CombatAmbience.Instance;
            if (amb != null) amb.StartDrone(DroneKind.Recon, 40f);
            yield return new WaitForSeconds(3f);
            var hips = casualty.rig != null ? casualty.rig.Bone(HumanBodyBones.Hips) : casualty.transform;
            Vector3 p = new Vector3(hips.position.x + 0.9f, 0.05f, hips.position.z + 0.2f);
            if (amb != null) amb.PlayDropGrenade(p);
            yield return new WaitForSeconds(2.3f); // click → 1.1 s whistle → blast
            if (amb == null) CombatVfx.Instance?.ExplosionNear(p, 0.6f);
            casualty.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration, "thighR_artery");
            casualty.AddWound(WoundType.Capillary, BodyRegion.ForearmL, WoundVisual.FragmentHole, "forearmL_frag");
            casualty.Hit(HitKind.Explosion);
            Debug.Log("[FMVR] demo scenario: VOG drop, Erlan hit (thigh arterial + forearm fragment)");
        }
    }
}
