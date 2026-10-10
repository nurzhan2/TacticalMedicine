using System.Collections;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>S4: threat model, flare physics, sound delay, proximity passthrough, comfort shake, ambience API.</summary>
    public class EnvironmentTests
    {
        static ThreatModel.Situation Exposed(bool fire = true) => new ThreatModel.Situation { headAboveParapet = true, underFire = fire, light = LightMode.Red, standing = true };
        static ThreatModel.Situation Safe() => new ThreatModel.Situation { light = LightMode.Red };

        [Test]
        public void Threat_HeadAboveParapetUnderFire_WarnsThenHits()
        {
            var m = new ThreatModel();
            bool whiz = false, impact = false, hit = false;
            float t = 0f;
            for (; t < 10f && !hit; t += 0.05f)
            {
                var cue = m.Tick(Exposed(), 0.05f);
                if (cue == ThreatCue.BulletWhiz) { Assert.IsFalse(impact); whiz = true; }
                if (cue == ThreatCue.ParapetImpact) { Assert.IsTrue(whiz, "whiz must come first"); impact = true; }
                if (cue == ThreatCue.Hit) hit = true;
            }
            Assert.IsTrue(whiz && impact && hit);
            Assert.That(t, Is.InRange(2f, 4.5f), "about 3 s of exposure under fire is lethal");
            Assert.AreEqual("head_above_parapet", m.HitReason);
            Assert.AreEqual(1, m.Exposures.Count);
            Assert.That(m.Exposures[0].duration, Is.GreaterThan(2f));
        }

        [Test]
        public void Threat_ShortPeek_WarnsButSurvives_AndRiskDecays()
        {
            var m = new ThreatModel();
            for (float t = 0f; t < 1.4f; t += 0.05f) m.Tick(Exposed(), 0.05f);
            Assert.IsFalse(m.IsHit);
            Assert.That(m.Risk, Is.GreaterThan(ThreatModel.WhizThreshold));
            for (float t = 0f; t < 6f; t += 0.05f) m.Tick(Safe(), 0.05f);
            Assert.That(m.Risk, Is.LessThan(0.01f));
            Assert.IsFalse(m.IsHit);
        }

        [Test]
        public void Threat_WhiteLightUnderDrone_IsMuchRiskierThanRed()
        {
            var white = new ThreatModel(); var red = new ThreatModel();
            var s = new ThreatModel.Situation { droneOverhead = true, light = LightMode.White };
            var r = new ThreatModel.Situation { droneOverhead = true, light = LightMode.Red };
            for (int i = 0; i < 60; i++) { white.Tick(s, 0.05f); red.Tick(r, 0.05f); }
            Assert.That(white.Risk, Is.GreaterThan(red.Risk * 8f));
            var cover = new ThreatModel();
            s.inCover = true;
            for (int i = 0; i < 60; i++) cover.Tick(s, 0.05f);
            Assert.AreEqual(0f, cover.Risk, 1e-4f, "inside the dugout the light is hidden");
            Assert.IsNotEmpty(ThreatModel.Explain("white_light_drone", false));
            Assert.IsNotEmpty(ThreatModel.Explain("white_light_drone", true));
        }

        [Test]
        public void SoundDelay_IsDistanceOverSpeedOfSound()
        {
            Assert.AreEqual(1f, EnvMath.SoundDelay(343f), 1e-4f);
            Assert.AreEqual(8.746f, EnvMath.SoundDelay(3000f), 0.01f);
            Assert.AreEqual(0f, EnvMath.SoundDelay(-5f));
        }

        [Test]
        public void Flare_FallsSlowly_BurnsAboutHalfAMinute_AndSwings()
        {
            var start = new Vector3(0f, 80f, 40f);
            var p0 = EnvMath.FlarePosition(start, Vector3.zero, 1.8f, 2f, 0f);
            var p35 = EnvMath.FlarePosition(start, Vector3.zero, 1.8f, 2f, 35f);
            Assert.AreEqual(63f, p0.y - p35.y, 0.01f);
            Assert.Greater(EnvMath.FlareBrightness(10f, 35f), 0.7f);
            Assert.AreEqual(0f, EnvMath.FlareBrightness(36f, 35f));
            Assert.Less(EnvMath.FlareBrightness(0.1f, 35f), 0.3f, "ignition ramps up");
            float minX = 99f, maxX = -99f;
            for (float t = 0f; t < 5f; t += 0.1f) { var p = EnvMath.FlarePosition(start, Vector3.zero, 1.8f, 2f, t); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            Assert.Greater(maxX - minX, 3f, "swings under the parachute");
        }

        [Test]
        public void Passthrough_Ramp_And_PolygonDistance()
        {
            Assert.AreEqual(0f, EnvMath.ProximityOpacity(0.8f));
            Assert.AreEqual(0f, EnvMath.ProximityOpacity(0.6f));
            Assert.AreEqual(1f, EnvMath.ProximityOpacity(0.25f));
            Assert.AreEqual(1f, EnvMath.ProximityOpacity(0.1f));
            float mid = EnvMath.ProximityOpacity(0.425f);
            Assert.That(mid, Is.InRange(0.45f, 0.55f));
            var square = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            Assert.AreEqual(1f, EnvMath.DistanceToPolygonXZ(Vector3.zero, square), 1e-4f);
            Assert.AreEqual(0.3f, EnvMath.DistanceToPolygonXZ(new Vector3(0.7f, 1.6f, 0f), square), 1e-4f);
            Assert.IsTrue(EnvMath.InsidePolygonXZ(Vector3.zero, square));
            Assert.IsFalse(EnvMath.InsidePolygonXZ(new Vector3(2f, 0f, 0f), square));
        }

        [Test]
        public void FallTime_And_HoverPoint()
        {
            Assert.AreEqual(1.749f, EnvMath.FallTime(15f), 0.01f);
            Assert.AreEqual(0f, EnvMath.FallTime(-2f));
            var h = EnvMath.HoverPoint(new Vector3(1f, 0f, 2f), 15f, Vector3.zero);
            Assert.AreEqual(new Vector3(1f, 15f, 2f), h);
            Assert.AreEqual(0.55f + EnvMath.FallTime(15f), CombatAmbience.DropDelay, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Drone_ReconHoversOverAim_DropsVisibleVog_ThenClimbsAway()
        {
            var go = new GameObject("Drone", typeof(AudioSource));
            var d = go.AddComponent<DroneAudio>();
            var clip = AudioClip.Create("hum", 4410, 1, 44100, false);
            var aim = new Vector3(2f, 0.05f, 1f);
            d.Begin(DroneKind.Recon, clip, Vector3.zero, 60f);
            Assert.IsNotNull(d.Visual, "recon drone has a visible body");
            Assert.AreEqual(4, d.Visual.GetComponentsInChildren<Transform>().Count(t => t.name == "Rotor"));
            float far = Vector3.Distance(d.transform.position, aim);
            d.HoverOver(aim, 15f);
            Time.timeScale = 8f;
            float t0 = Time.realtimeSinceStartup;
            while (!d.Hovering && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Time.timeScale = 1f;
            Assert.IsTrue(d.Hovering);
            var pos = d.transform.position;
            Assert.AreEqual(aim.y + 15f, pos.y, 0.6f);
            Assert.That(new Vector2(pos.x - aim.x, pos.z - aim.z).magnitude, Is.LessThan(0.6f), "straight above the aim");
            Assert.That(far, Is.GreaterThan(10f), "it came down from the patrol orbit");

            var vog = FallingMunition.Spawn(pos, aim);
            Assert.AreEqual(EnvMath.FallTime(pos.y - aim.y), vog.Duration, 1e-4f);
            float tf = 0f;
            while (vog != null && tf < vog.Duration * 0.5f) { tf += Time.deltaTime; yield return null; }
            Assert.That(vog.transform.position.y, Is.GreaterThan(aim.y + 15f * 0.6f), "free fall: slow first half");
            d.ReleaseHover();
            Time.timeScale = 8f;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.2f) yield return null;
            Time.timeScale = 1f;
            Assert.IsFalse(d.Hovering);
            Assert.That(d.transform.position.y, Is.GreaterThan(aim.y + 15.5f), "climbs back towards the orbit");
            d.Stop();
            Assert.IsFalse(d.Visual.gameObject.activeSelf);
            Object.Destroy(go);
            if (vog != null) Object.Destroy(vog.gameObject);
        }

        [UnityTest]
        public IEnumerator Drone_FpvDivesAtTarget_ShahedHasNoVisual()
        {
            var go = new GameObject("Fpv", typeof(AudioSource));
            var d = go.AddComponent<DroneAudio>();
            var clip = AudioClip.Create("whine", 4410, 1, 44100, false);
            var target = new Vector3(5f, 1.75f, 40f);
            d.Begin(DroneKind.Fpv, clip, Vector3.zero, 2f);
            d.DiveAt(target);
            float start = Vector3.Distance(d.transform.position, target);
            Assert.That(start, Is.InRange(85f, 95f));
            Assert.IsNotNull(d.Visual);
            float t0 = Time.realtimeSinceStartup;
            while (d.Active && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.That(Vector3.Distance(d.transform.position, target), Is.LessThan(3f), "dive ends on the target");
            d.Begin(DroneKind.Shahed, clip, Vector3.zero, 2f);
            Assert.IsNull(d.Visual, "a Shahed at 450 m is only heard");
            d.Stop();
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ProximityPassthrough_FadesNearEditorBoundary()
        {
            var ts = new GameObject("TS").transform;
            var head = new GameObject("Head").transform; head.SetParent(ts, false);
            var pp = ts.gameObject.AddComponent<ProximityPassthrough>();
            pp.trackingSpace = ts; pp.head = head; pp.logInEditor = false;
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            yield return null;
            Assert.AreEqual(0f, pp.Opacity, 1e-4f, "centre of a 2.5 m play area: full immersion");
            head.localPosition = new Vector3(1.05f, 1.6f, 0f); // 0.2 m from the edge
            yield return null;
            Assert.AreEqual(1f, pp.Opacity, 1e-4f);
            head.localPosition = new Vector3(0.8f, 1.6f, 0f); // 0.45 m
            yield return null;
            Assert.That(pp.Opacity, Is.InRange(0.2f, 0.8f));
            Object.Destroy(ts.gameObject);
        }

        [Test]
        public void Shake_IsComfortLimited()
        {
            Assert.LessOrEqual(EnvMath.ShakeAmplitude(0.5f, 3f, 0.012f), 0.012f);
            Assert.Greater(EnvMath.ShakeAmplitude(3f, 1f, 1f), EnvMath.ShakeAmplitude(15f, 1f, 1f));
        }

        [UnityTest]
        public IEnumerator Ambience_NearBlast_MufflesAndShakes_FarBlast_DoesNot()
        {
            var cam = new GameObject("Cam", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            var rig = new GameObject("Rig").AddComponent<CameraShake>();
            var vfxGo = new GameObject("Vfx");
            vfxGo.AddComponent<CombatVfx>().head = cam.transform;
            var amb = new GameObject("Amb").AddComponent<CombatAmbience>();
            amb.listener = cam.transform;
            amb.autoAmbience = false;
            yield return null;

            amb.Detonate(new Vector3(0f, 1.75f, 3000f), 1f);
            yield return null;
            Assert.AreEqual(0f, amb.MuffleAmount, "3 km away: flash and delayed rumble only");

            amb.Detonate(new Vector3(2f, 1.75f, 4f), 1f);
            yield return null;
            Assert.Greater(amb.MuffleAmount, 0.5f, "4 m: muffled hearing");
            Assert.Greater(CameraShake.CurrentAmplitude, 0f);
            Assert.LessOrEqual(CameraShake.CurrentAmplitude, ComfortSettings.ShakeLimit + 1e-5f);
            Assert.GreaterOrEqual(CombatVfx.Instance.ActiveFlashes, 1);

            amb.PlaySniperCrack();
            amb.PlayIncomingShell(new Vector3(0f, 1.75f, 60f));
            amb.PlayDropGrenade(new Vector3(1f, 1.75f, 8f));
            var d = amb.StartDrone(DroneKind.Recon, 5f);
            Assert.IsNull(d, "no clips assigned in the test → no drone, no exception");
            yield return null;
            Object.Destroy(amb.gameObject); Object.Destroy(vfxGo); Object.Destroy(rig.gameObject); Object.Destroy(cam);
        }

        [UnityTest]
        public IEnumerator ThreatSystem_EndsRunWhenStandingOnTheParapetUnderFire()
        {
            var head = new GameObject("Head").transform;
            head.position = new Vector3(0f, 1.6f, 0f);
            var ts = new GameObject("Threat").AddComponent<ThreatSystem>();
            ts.head = head; ts.parapetHeight = 1.85f; ts.underFire = true;
            string reason = null;
            ts.PlayerHit += r => reason = r;
            for (int i = 0; i < 30; i++) { ts.Tick(0.1f); }
            Assert.IsFalse(ts.Model.IsHit, "crouched below the parapet: safe");
            head.position = new Vector3(0f, 2.1f, 0f); // on the firing step looking over
            for (int i = 0; i < 60 && reason == null; i++) ts.Tick(0.1f);
            Assert.AreEqual("head_above_parapet", reason);
            Assert.IsTrue(RunEndOverlay.Shown);
            yield return null;
            Object.Destroy(RunEndOverlay.Current.gameObject);
            Object.Destroy(ts.gameObject); Object.Destroy(head.gameObject);
        }

        [UnityTest]
        public IEnumerator Headlamp_TogglesAndSwaps()
        {
            var go = new GameObject("Lamp", typeof(Light));
            var lamp = go.AddComponent<Headlamp>();
            yield return null;
            Assert.AreEqual(LightMode.Red, lamp.Mode);
            lamp.Toggle(); Assert.AreEqual(LightMode.Off, lamp.Mode); Assert.IsFalse(go.GetComponent<Light>().enabled);
            lamp.Toggle(); Assert.AreEqual(LightMode.Red, lamp.Mode);
            lamp.SwapColour(); Assert.AreEqual(LightMode.White, lamp.Mode);
            Object.Destroy(go);
        }
    }
}
