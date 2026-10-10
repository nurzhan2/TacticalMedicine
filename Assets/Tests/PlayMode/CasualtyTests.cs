using System.Collections;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Medical;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    public class CasualtyTests
    {
        const string PrefabPath = "Assets/Prefabs/Erlan.prefab";
        GameObject _floor;
        CasualtyController _c;

        [SetUp]
        public void SetUp()
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor.transform.localScale = Vector3.one * 2f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_c != null) Object.Destroy(_c.gameObject);
            Object.Destroy(_floor);
            foreach (var p in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "BloodPool"))
                Object.Destroy(p.gameObject);
        }

        CasualtyController Spawn()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null, "Erlan prefab missing: run CasualtyAssetBuilder.BuildAll");
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _c = go.GetComponent<CasualtyController>();
            _c.voice.muted = true;
            return _c;
        }

        IEnumerator RunSim(CasualtyController c, float simSeconds, float speed, float realTimeout = 30f)
        {
            c.simulationSpeed = speed;
            float t0 = Time.realtimeSinceStartup;
            while (c.Patient.secondsSinceStart < simSeconds && c.Patient.outcome == Outcome.Ongoing
                   && Time.realtimeSinceStartup - t0 < realTimeout)
                yield return null;
        }

        [UnityTest]
        public IEnumerator UntreatedArterialThigh_BleedsAndDiesInExpectedWindow()
        {
            var c = Spawn();
            yield return null;
            var site = c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            c.Hit(HitKind.Explosion);
            yield return new WaitForSeconds(1.5f);
            Assert.That(site.JetBursts, Is.GreaterThan(0), "arterial jet pulses with the heart");
            Assert.That(c.PoolManager.Pools.Count, Is.GreaterThan(0), "blood pool on the floor");
            yield return RunSim(c, 900f, 60f);
            Assert.That(c.Patient.outcome, Is.EqualTo(Outcome.Died), "died untreated");
            Assert.That(c.Patient.secondsSinceStart, Is.InRange(180f, 420f), "3-7 min without a tourniquet");
            Assert.That(c.PoolManager.TotalVolumeMl, Is.GreaterThan(1500f));
            yield return new WaitForSeconds(0.3f);
            Assert.That(c.CurrentPose, Is.EqualTo(CasualtyPose.LyingDying));
            Assert.That(site.FlowMlPerSec, Is.EqualTo(0f), "no cardiac output, no flow");
        }

        [UnityTest]
        public IEnumerator TourniquetViaApi_StopsBleedingAndSurvives()
        {
            var c = Spawn();
            yield return null;
            c.startPhase = Phase.CareUnderFire;
            var site = c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            c.Hit(HitKind.Explosion);
            yield return RunSim(c, 40f, 20f);
            var fb = c.ApplyTourniquet(BodyRegion.ThighR);
            Assert.That(fb.code, Is.EqualTo("tq_ok"), fb.text);
            yield return null;
            Assert.That(site.FlowMlPerSec, Is.EqualTo(0f));
            Assert.That(c.Pain.TourniquetHalfTurns("legR"), Is.EqualTo(2));
            yield return RunSim(c, 1200f, 120f);
            Assert.That(c.Patient.outcome, Is.EqualTo(Outcome.Ongoing), c.Patient.causeOfDeath);
            Assert.That(c.Patient.LossFraction, Is.LessThan(0.2f));
            Assert.That(CasualtyPoseMapper.IsLying(c.CurrentPose), Is.True);
        }

        [UnityTest]
        public IEnumerator Rig_HasRegionColliders_WoundOnInnerThighSurface()
        {
            var c = Spawn();
            yield return null;
            var rig = c.rig;
            foreach (var r in new[] { BodyRegion.ThighR, BodyRegion.ThighL, BodyRegion.ChestL, BodyRegion.ChestR, BodyRegion.Head, BodyRegion.GroinR, BodyRegion.FootL })
                Assert.That(rig.Part(r), Is.Not.Null, "collider for " + r);
            var site = c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var femur = rig.Bone(HumanBodyBones.RightUpperLeg);
            var knee = rig.Bone(HumanBodyBones.RightLowerLeg);
            Assert.That(site.transform.IsChildOf(femur), Is.True);
            // on the surface: 4..16 cm from the femur axis, between hip and knee
            Vector3 axis = (knee.position - femur.position).normalized;
            Vector3 rel = site.transform.position - femur.position;
            float along = Vector3.Dot(rel, axis) / Vector3.Distance(knee.position, femur.position);
            float radial = (rel - axis * Vector3.Dot(rel, axis)).magnitude;
            Assert.That(along, Is.InRange(0.15f, 0.65f));
            Assert.That(radial, Is.InRange(0.03f, 0.17f));
            // medial side: toward the left leg
            var other = rig.Bone(HumanBodyBones.LeftUpperLeg);
            Assert.That(Vector3.Dot(site.transform.position - femur.position, other.position - femur.position), Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator HitFall_ThenLyingPose_AndHandlingPainFlinch()
        {
            var c = Spawn();
            yield return null;
            Assert.That(c.CurrentPose, Is.EqualTo(CasualtyPose.CrouchIdle));
            c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            c.Hit(HitKind.Explosion);
            Assert.That(c.IsFalling, Is.True);
            float t0 = Time.time;
            while (c.IsFalling && Time.time - t0 < 8f) yield return null;
            Assert.That(c.IsFalling, Is.False);
            Assert.That(CasualtyPoseMapper.IsLying(c.CurrentPose), Is.True, c.CurrentPose.ToString());
            float before = c.Pain.spike;
            var site = c.WoundSites[0];
            for (int i = 0; i < 10; i++) { c.Handle(site.transform.position + Vector3.up * 0.05f, 1.5f, 0.05f); yield return null; }
            Assert.That(c.Pain.spike, Is.GreaterThan(before + 0.2f));
        }

        [Test]
        public void TwoBoneIK_ReachesTarget_AndRespectsHint()
        {
            var a = new GameObject("a").transform;
            var b = new GameObject("b").transform; b.SetParent(a); b.localPosition = new Vector3(0, -0.45f, 0);
            var t = new GameObject("t").transform; t.SetParent(b); t.localPosition = new Vector3(0, -0.45f, 0);
            a.position = new Vector3(0, 1, 0);
            var target = new Vector3(0.3f, 0.5f, 0.35f);
            var hint = new Vector3(0, 0.9f, 1f);
            TwoBoneIK.Solve(a, b, t, target, hint, 1f);
            Assert.That(Vector3.Distance(t.position, target), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(a.position, b.position), Is.EqualTo(0.45f).Within(1e-3f));
            // knee bends toward the hint (+Z)
            Vector3 mid = (a.position + t.position) * 0.5f;
            Assert.That(b.position.z, Is.GreaterThan(mid.z));
            // unreachable target: chain straightens toward it
            TwoBoneIK.Solve(a, b, t, new Vector3(0, -2f, 0), null, 1f);
            Assert.That(Vector3.Angle(t.position - a.position, Vector3.down), Is.LessThan(2f));
            Object.DestroyImmediate(a.gameObject);
        }

        [Test]
        public void BloodPool_AreaFollowsVolume()
        {
            var go = new GameObject("pm");
            var pm = go.AddComponent<BloodPoolManager>();
            pm.Feed(new Vector3(0, 0.5f, 0), 1000f, 0f);
            Assert.That(pm.Pools.Count, Is.EqualTo(1));
            Assert.That(pm.Pools[0].radius, Is.InRange(0.25f, 0.5f), "1 L spreads to ~0.3 m radius on the floor");
            pm.Feed(new Vector3(1.2f, 0.5f, 0), 50f, 0f);
            Assert.That(pm.Pools.Count, Is.EqualTo(2), "moved source -> new pool (drag trail)");
            Object.DestroyImmediate(go);
        }
    }
}
