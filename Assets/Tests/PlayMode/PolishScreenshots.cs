using System.Collections;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static FieldMedic.Tests.ScenarioScreenshots;

namespace FieldMedic.Tests
{
    /// <summary>
    /// S7 polish shots (docs/screens/s7_*.jpg): the recon drone hovering over the trench before the drop, the VOG
    /// falling, the FPV in its terminal dive. Run explicitly (GPU needed): -testFilter FieldMedic.Tests.PolishScreenshots
    /// </summary>
    [Category("Screenshots")]
    public class PolishScreenshots
    {
        static DroneAudio FindDrone(DroneKind k) =>
            Object.FindObjectsByType<DroneAudio>(FindObjectsSortMode.None).FirstOrDefault(x => x.Active && x.kind == k);

        [UnityTest]
        public IEnumerator S1_DroneHover_AndFallingVog()
        {
            ScenarioLaunch.Scenario("S1", ScenarioVariant.Default);
            yield return LoadMain();
            var d = ScenarioDirector.Instance;
            Assert.That(d, Is.Not.Null);
            float t0 = Time.realtimeSinceStartup;
            DroneAudio drone = null;
            while ((drone == null || !drone.Hovering) && Time.realtimeSinceStartup - t0 < 40f) { drone = FindDrone(DroneKind.Recon); yield return null; }
            Assert.That(drone, Is.Not.Null);
            Assert.That(drone.Hovering, Is.True);
            HidePlayerKit(true);
            var head = d.head.position;
            var dp = drone.transform.position;
            Shot("s7_drone_hover_zoom", head, dp, 9f, 0f, true);
            FallingMunition vog = null;
            while (vog == null && Time.realtimeSinceStartup - t0 < 50f) { vog = Object.FindFirstObjectByType<FallingMunition>(); yield return null; }
            Assert.That(vog, Is.Not.Null, "the drop is visible");
            float tf = Time.time;
            while (vog != null && Time.time - tf < vog.Duration * 0.55f) yield return null;
            if (vog != null) Shot("s7_vog_falling", head, vog.transform.position + Vector3.up * 1.5f, 34f, 0f, true);
            HidePlayerKit(false);
        }

        [UnityTest]
        public IEnumerator S5_FpvTerminalDive()
        {
            ScenarioLaunch.Scenario("S5", ScenarioVariant.Default);
            yield return LoadMain();
            var d = ScenarioDirector.Instance;
            float t0 = Time.realtimeSinceStartup;
            DroneAudio fpv = null;
            while (fpv == null && Time.realtimeSinceStartup - t0 < 40f) { fpv = FindDrone(DroneKind.Fpv); yield return null; }
            Assert.That(fpv, Is.Not.Null);
            HidePlayerKit(true);
            float ts = Time.time;
            while (fpv.Active && Time.time - ts < 3.9f) yield return null;
            var head = d.head.position + Vector3.up * 1.2f; // over the parapet: from the trench floor it is hidden until the last second
            Shot("s7_fpv_dive", head, fpv.transform.position, 30f, 0f, true);
            HidePlayerKit(false);
        }
    }
}
