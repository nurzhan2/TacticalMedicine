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

        [UnityTest]
        public IEnumerator Hands_GloveOpenAndFist()
        {
            ScenarioLaunch.Scenario("S1", ScenarioVariant.Default);
            yield return LoadMain();
            yield return Frames(5);
            var d = ScenarioDirector.Instance;
            var gloves = Object.FindObjectsByType<FieldMedic.Input.GloveHand>(FindObjectsSortMode.None);
            Assert.That(gloves.Length, Is.EqualTo(2));
            var head = d.head;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            foreach (var g in gloves)
            {
                var rig = g.GetComponentInParent<FieldMedic.Input.HandRig>();
                rig.enabled = false; // freeze the input-driven pose
                g.driveFromInput = false;
                bool r = g.side > 0f;
                rig.transform.position = head.position + fwd * 0.4f + right * (r ? 0.13f : -0.13f) + Vector3.down * 0.32f;
                rig.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(r ? 20f : 15f, r ? -12f : 10f, r ? -25f : 15f);
                g.gripCurl = r ? 0.95f : 0.05f; g.indexCurl = r ? 1f : 0f; g.thumbCurl = r ? 0.85f : 0.1f;
                g.Pose();
            }
            yield return Frames(2);
            var radio = GameObject.Find("Radio"); if (radio != null) radio.SetActive(false);
            var ifak = GameObject.Find("PlayerIFAK"); if (ifak != null) ifak.SetActive(false);
            Shot("s7_gloves", head.position + Vector3.up * 0.02f, head.position + fwd * 0.45f + Vector3.down * 0.34f, 62f, 0.35f, true);
        }

        [UnityTest]
        public IEnumerator Hands_GloveIsolated()
        {
            var lg = new GameObject("Key").AddComponent<Light>(); lg.type = LightType.Directional; lg.intensity = 1.2f; lg.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            for (int i = 0; i < 2; i++)
            {
                var hand = new GameObject("H" + i);
                hand.transform.position = new Vector3(i == 0 ? -0.12f : 0.12f, 1f, 0f);
                var v = new GameObject("GloveVisual"); v.transform.SetParent(hand.transform, false);
                var g = v.AddComponent<FieldMedic.Input.GloveHand>();
                g.side = i == 0 ? -1f : 1f; g.driveFromInput = false;
                g.Build();
                g.gripCurl = i == 0 ? 0f : 1f; g.indexCurl = g.gripCurl; g.thumbCurl = g.gripCurl * 0.85f; g.Pose();
            }
            yield return Frames(2);
            Shot("s7_glove_iso_top", new Vector3(0f, 1.45f, 0.05f), new Vector3(0f, 1f, 0.06f), 40f, 0f, true);
            Shot("s7_glove_iso_side", new Vector3(0.5f, 1.05f, 0.06f), new Vector3(0f, 1f, 0.06f), 40f, 0f, true);
            Shot("s7_glove_iso_front", new Vector3(0.02f, 1.12f, 0.5f), new Vector3(0f, 1f, 0.05f), 40f, 0f, true);
        }

        [UnityTest]
        public IEnumerator Field_WrecksAndCraters()
        {
            ScenarioLaunch.Scenario("S1", ScenarioVariant.Default);
            yield return LoadMain();
            yield return Frames(30);
            var d = ScenarioDirector.Instance;
            var bmp = GameObject.Find("Wreck_BMP");
            var tank = GameObject.Find("Wreck_T72");
            Assert.That(bmp, Is.Not.Null);
            Assert.That(tank, Is.Not.Null);
            HidePlayerKit(true);
            var eye = d.head.position + Vector3.up * 1.0f; // peeking over the parapet
            Shot("s7_field_wrecks", eye, Vector3.Lerp(bmp.transform.position, tank.transform.position, 0.35f) + Vector3.up * 1.5f, 70f, 0f, true);
            HidePlayerKit(false);
        }
    }
}
