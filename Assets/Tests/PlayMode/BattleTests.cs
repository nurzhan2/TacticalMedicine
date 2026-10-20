using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Battle;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w3 battle in the real Battlefield scene: enemy fire exposure (open → hit after ~4 s cumulative, cover →
    /// never, standing up behind sandbags → hit, Training → never targeted, smoke → blocked), the hands-on smoke
    /// grenade (pin pulled by the other hand, throw, 20 s screen blocks rays), director focus keeps blasts away,
    /// the player's safe spots are not exposed, and every layer is alive after a minute.
    /// </summary>
    public class BattleTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        GlobalStateGuard _guard;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _guard = new GlobalStateGuard();
            BattleDirector.Mode = BattleMode.Combat;
            BattleDirector.PlayerArmed = null;
            SmokeScreens.Clear();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
            Assert.IsNotNull(BattleDirector.Instance, "BattleDirector missing in the Battlefield scene");
            Assert.IsNotNull(EnemyFire.Instance, "EnemyFire missing");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            _guard?.Restore();
            BattleDirector.Mode = BattleMode.Combat;
            BattleDirector.PlayerArmed = null;
            SmokeScreens.Clear();
        }

        static Vector3 G(float x, float z, float up) => new Vector3(x, BattlefieldLayout.GroundHeight(x, z) + up, z);

        Transform TestHead(Vector3 at)
        {
            var go = new GameObject("TestHead");
            _spawned.Add(go);
            go.transform.position = at;
            return go.transform;
        }

        /// <summary>Isolated enemy fire: one MG nest, no infantry, no random director events touching the player.</summary>
        EnemyFire Isolate(Transform head, params Vector3[] nests)
        {
            var ef = EnemyFire.Instance;
            ef.head = head;
            ef.useInfantry = false;
            ef.mgNests = nests;
            ef.targetableOverride = null;
            ef.armedWithoutScenario = true;
            BattleDirector.PlayerArmed = true; // the scene's menu disarms it on Start
            ef.ResetExposure();
            BattleDirector.Instance.autoRun = false;
            if (ThreatSystem.Instance != null) ThreatSystem.Instance.enableHits = false; // overlay off; EnemyFire event still fires
            return ef;
        }

        IEnumerator WaitUntilHit(EnemyFire ef, float max, System.Action<float> done)
        {
            float t = 0f;
            bool hit = false;
            System.Action<string> h = _ => hit = true;
            ef.PlayerHit += h;
            while (!hit && t < max) { t += Time.deltaTime; yield return null; }
            ef.PlayerHit -= h;
            done(hit ? t : -1f);
        }

        [UnityTest]
        public IEnumerator EnemyFire_OpenGround_PlayerHitAfterAboutFourSeconds()
        {
            BattleDirector.Mode = BattleMode.Exam; // w10b: the strict 4 s limit is the exam's (Combat allows 2.2x longer)
            var fall = BattlefieldLayout.ErlanFallPoint;
            var head = TestHead(G(fall.x, fall.z, 1.65f));
            var ef = Isolate(head, G(fall.x, fall.z + 60f, 1.1f));
            Assert.IsTrue(ef.LineOpen(G(fall.x, fall.z + 60f, 1.1f), head.position), "test setup: the nest must see the open ground");
            float hitAt = -1f;
            yield return WaitUntilHit(ef, 10f, t => hitAt = t);
            Assert.Greater(hitAt, 0f, "standing in the open under fire must get the player hit");
            Assert.That(hitAt, Is.InRange(3.7f, 5.2f), "hit after ~4 s cumulative exposure");
            Assert.Greater(ef.NearMisses, 0, "near misses (cracks, impacts) warn the player first");
            Assert.IsTrue(ef.PlayerWasHit);
        }

        /// <summary>The sandbag-barrier cover point of the w2 cover line (kneeling eye, threat direction).</summary>
        static CoverPoint SandbagPoint()
        {
            var pts = Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None);
            var cp = pts.Where(p => p.cover != null && p.cover.name.StartsWith("concrete_blockade")).OrderBy(p => p.name).FirstOrDefault();
            Assert.IsNotNull(cp, "no sandbag cover point");
            return cp;
        }

        [UnityTest]
        public IEnumerator EnemyFire_KneelingBehindSandbags_NeverHit_RoundsHitTheCover()
        {
            var cp = SandbagPoint();
            var head = TestHead(cp.HeadPosition);
            var far = cp.HeadPosition + cp.threatDirection.normalized * 85f;
            var nest = G(far.x, far.z, 1.0f);
            var ef = Isolate(head, nest);
            float hitAt = -1f;
            yield return WaitUntilHit(ef, 12f, t => hitAt = t);
            Assert.Less(hitAt, 0f, "behind cover the player must never be hit");
            Assert.IsFalse(ef.Exposed);
            Assert.Greater(ef.CoverHits, 0, "the enemy keeps hammering the cover in front of the player");
        }

        [UnityTest]
        public IEnumerator EnemyFire_StandingUpBehindSandbags_IsExposed()
        {
            var cp = SandbagPoint();
            var head = TestHead(cp.transform.position + Vector3.up * 1.75f);
            var far = cp.HeadPosition + cp.threatDirection.normalized * 85f;
            var ef = Isolate(head, G(far.x, far.z, 1.0f));
            float hitAt = -1f;
            yield return WaitUntilHit(ef, 8f, t => hitAt = t);
            Assert.Greater(hitAt, 0f, "standing tall behind 1.5 m sandbags exposes the head");
        }

        [UnityTest]
        public IEnumerator EnemyFire_Training_EnemyNeverTargetsThePlayer()
        {
            BattleDirector.Mode = BattleMode.Training;
            var fall = BattlefieldLayout.ErlanFallPoint;
            var head = TestHead(G(fall.x, fall.z, 1.65f));
            var ef = Isolate(head, G(fall.x, fall.z + 60f, 1.1f));
            ef.useInfantry = true;
            float hitAt = -1f;
            yield return WaitUntilHit(ef, 8f, t => hitAt = t);
            Assert.Less(hitAt, 0f);
            Assert.IsFalse(ef.PlayerTargetable);
            Assert.AreEqual(0, ef.NearMisses);
            Assert.AreEqual(0f, ef.Tracker.TotalExposed);
            // their rifles aim at our line, never at the player
            var inf = NearInfantry.Instance;
            Assert.IsNull(inf.playerAimProvider(inf.enemies[0]));
        }

        [UnityTest]
        public IEnumerator EnemyFire_DenseSmoke_BlocksTheLineOfFire()
        {
            var fall = BattlefieldLayout.ErlanFallPoint;
            var head = TestHead(G(fall.x, fall.z, 1.65f));
            var nest = G(fall.x, fall.z + 60f, 1.1f);
            var ef = Isolate(head, nest);
            int id = SmokeScreens.Add(Vector3.Lerp(head.position, nest, 0.15f), 5f, 1f);
            Assert.IsFalse(ef.LineOpen(nest, head.position));
            float hitAt = -1f;
            yield return WaitUntilHit(ef, 8f, t => hitAt = t);
            Assert.Less(hitAt, 0f, "hidden by smoke");
            SmokeScreens.Remove(id);
        }

        [UnityTest]
        public IEnumerator CoverPoints_AreNotExposed_ToShootersInTheirArc_TreatmentSpotHidden()
        {
            var ef = EnemyFire.Instance;
            var shooters = NearInfantry.Instance.enemySpots.Select(p => p + Vector3.up * 1.12f).Concat(ef.mgNests).ToArray();
            Assert.Greater(shooters.Length, 10);
            var bad = new List<string>();
            int checkedLines = 0;
            foreach (var cp in Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None))
            {
                Vector3 h = cp.HeadPosition;
                foreach (var s in shooters)
                {
                    Vector3 d = s - h; d.y = 0f;
                    if (Vector3.Angle(d, new Vector3(cp.threatDirection.x, 0f, cp.threatDirection.z)) > cp.arcHalfAngle) continue;
                    checkedLines++;
                    if (ef.LineOpen(s, h)) bad.Add($"{cp.name} ({(cp.cover != null ? cp.cover.name : "?")}) seen from {s}");
                }
            }
            // the treatment spot inside house A (kneeling over Erlan) is hidden from every shooter position
            Vector3 t = BattlefieldLayout.TreatmentSpot + Vector3.up * 1.1f;
            foreach (var s in shooters) if (ef.LineOpen(s, t) || ef.LineOpen(s, t + Vector3.down * 0.42f)) bad.Add($"treatment spot seen from {s}");
            yield return null;
            Debug.Log($"[FMVR] cover check: {checkedLines} in-arc lines of fire");
            Assert.IsEmpty(bad, string.Join("; ", bad));
        }

        // ------------------------------------------------------------------ smoke grenade
        (HandGrabber, ScriptedHandInput) MakeHand(Handedness h, Vector3 palm)
        {
            var go = new GameObject("TestHand_" + h);
            _spawned.Add(go);
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var g = go.AddComponent<HandGrabber>();
            var input = rig.UseScripted(palm - g.palmOffset);
            input.Grip = 0f; // tests close the grip right before TryGrab (the grabber releases when it opens)
            go.transform.position = input.Position;
            return (g, input);
        }

        static void MovePalm(HandGrabber g, ScriptedHandInput input, Vector3 palm) => input.Position = palm - input.Rotation * g.palmOffset;

        [UnityTest]
        public IEnumerator SmokeGrenade_GrabPullPinThrow_DenseScreenBlocksEnemyFire()
        {
            var grenades = Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None);
            Assert.AreEqual(2, grenades.Length, "two smoke grenades on the vest");
            var sg = grenades.OrderBy(x => x.name).First();
            Assert.AreEqual(SmokeGrenade.GState.OnVest, sg.State);
            yield return null;

            var (right, rin) = MakeHand(Handedness.Right, sg.transform.position + Vector3.up * 0.06f);
            var (left, lin) = MakeHand(Handedness.Left, sg.pin.transform.position);
            yield return null;
            Assert.IsFalse(sg.pin.CanBeGrabbedBy(left), "the ring cannot be pulled while the grenade sits in the pouch");
            rin.Grip = 1f;
            Assert.IsTrue(right.TryGrab(sg), "grab the grenade");
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(SmokeGrenade.GState.Held, sg.State);
            MovePalm(left, lin, sg.pin.transform.position);
            yield return new WaitForFixedUpdate();
            lin.Grip = 1f;
            Assert.IsTrue(left.TryGrab(sg.pin), "hook the ring with the other hand");
            Vector3 start = left.PalmPosition;
            for (int i = 1; i <= 10 && !sg.pin.Pulled; i++)
            {
                MovePalm(left, lin, start + (left.transform.position - right.transform.position).normalized * 0.012f * i + Vector3.left * 0.01f * i);
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsTrue(sg.pin.Pulled, "pulling the ring ≥ 6 cm frees the pin");

            // throw: swing the hand forward and open it
            Vector3 p0 = right.PalmPosition;
            for (int i = 1; i <= 6; i++) { MovePalm(right, rin, p0 + new Vector3(0f, 0.04f * i, 0.12f * i)); rin.Grip = 1f; yield return new WaitForFixedUpdate(); yield return null; }
            rin.Grip = 0f;
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(SmokeGrenade.GState.Armed, sg.State, "released with the pin out → the lever flies, fuse burns");
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(SmokeGrenade.GState.Smoking, sg.State);
            yield return new WaitForSeconds(5f);
            Assert.Greater(sg.CloudRadius, 3f, "the cloud grows to several metres");
            Assert.GreaterOrEqual(SmokeScreens.ActiveCount, 1);
            // a shooter on the far side of the cloud cannot see a head on this side
            Vector3 c = sg.CloudCentre;
            Assert.IsTrue(SmokeScreens.Blocks(c + new Vector3(0f, 0f, 40f), c + new Vector3(0f, 0f, -6f)), "dense smoke blocks the line through it");
            Assert.IsFalse(sg.CanBeGrabbedBy(right), "a burning smoke grenade is not picked up");
        }

        [UnityTest]
        public IEnumerator SmokeGrenade_PinIn_GoesBackToTheVestOrDrops()
        {
            var sg = Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None).OrderBy(x => x.name).Last();
            yield return null;
            var (right, rin) = MakeHand(Handedness.Right, sg.transform.position + Vector3.up * 0.06f);
            yield return null;
            rin.Grip = 1f;
            Assert.IsTrue(right.TryGrab(sg));
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(SmokeGrenade.GState.Held, sg.State);
            MovePalm(right, rin, sg.slot.position + Vector3.up * 0.06f); // keep the hand at the pouch
            rin.Grip = 0f; // open the hand
            for (int i = 0; i < 3; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.AreEqual(SmokeGrenade.GState.OnVest, sg.State, "released at the pouch → back on the vest");
            rin.Grip = 1f;
            Assert.IsTrue(right.TryGrab(sg));
            Vector3 p0 = right.PalmPosition;
            for (int i = 1; i <= 8; i++) { MovePalm(right, rin, p0 + Vector3.forward * 0.07f * i); yield return new WaitForFixedUpdate(); yield return null; }
            rin.Grip = 0f;
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(SmokeGrenade.GState.Dropped, sg.State, "pin in, away from the vest → a dud on the ground, no smoke");
            Assert.IsFalse(sg.pin.Pulled);
        }

        // ------------------------------------------------------------------ director
        [UnityTest]
        public IEnumerator Director_Focus_KeepsBlastsAwayFromThePlayer()
        {
            var d = BattleDirector.Instance;
            d.autoRun = false;
            var fx = BattleFx.Instance;
            var head = Camera.main.transform;
            float safe = d.settings.focusSafeRadius;
            fx.ResetStats();
            for (int k = 0; k < 4; k++)
            {
                BattleDirector.RequestFocus(5f);
                Assert.IsTrue(d.InFocus);
                d.Fire(BattleEventType.ArtilleryBarrage);
                d.Fire(BattleEventType.TankShot);
                for (float t = 0f; t < 4.5f; t += Time.deltaTime) { BattleDirector.RequestFocus(5f); yield return null; }
            }
            Assert.Greater(fx.Detonations, 0, "the barrages happened");
            Assert.GreaterOrEqual(fx.ClosestBlast, safe * 0.85f, $"during focus no blast within {safe} m of the player");
            Assert.Greater(d.Curve.Background(Time.time), 0.4f, "the background never goes silent");
        }

        [UnityTest]
        public IEnumerator Director_FocusIsRequestedByMedicalGrabs()
        {
            var d = BattleDirector.Instance;
            int before = d.Curve.FocusRequests;
            var erlan = GameObject.Find("Erlan");
            Assert.IsNotNull(erlan);
            var g = erlan.GetComponentsInChildren<Grabbable>(true).FirstOrDefault(x => x.isActiveAndEnabled);
            Assume.That(g, Is.Not.Null, "no grabbable on the casualty");
            var (hand, hin) = MakeHand(Handedness.Right, g.transform.position);
            yield return new WaitForSeconds(2.2f); // the director hooks new grabbers every 2 s
            hin.Grip = 1f;
            hand.TryGrab(g);
            yield return null;
            Assert.Greater(d.Curve.FocusRequests, before, "a hand on the casualty requests focus");
            Assert.IsTrue(d.InFocus);
        }

        [UnityTest]
        public IEnumerator Battle_AllLayersAlive_AfterAMinute()
        {
            Time.timeScale = 3f;
            var d = BattleDirector.Instance;
            for (float t = 0f; t < 60f; t += Time.deltaTime) yield return null;
            Time.timeScale = 1f;
            var inf = NearInfantry.Instance;
            Assert.GreaterOrEqual(d.EventsFired, 3, "events every 5–20 s");
            Assert.Greater(inf.TotalShots, 50, "near infantry fires");
            Assert.Greater(inf.AliveEnemies, 0);
            Assert.Greater(CrowdLayer.Instance.Alive, 15, "mid crowd advancing");
            Assert.Greater(CrowdLayer.Instance.DrawCalls, 0);
            Assert.IsTrue(BattleAudio.Instance.BedsPlaying, "artillery/battle beds never stop");
            Assert.Greater(BattleAudio.Instance.Played, 20);
            Assert.Greater(BattleFx.Instance.Detonations + (CombatAmbience.Instance != null ? CombatAmbience.Instance.EventsPlayed : 0), 0);
            Assert.IsNotNull(Object.FindAnyObjectByType<HorizonLayer>().figures.sharedMaterial.GetTexture("_MainTex"), "impostor sheet assigned");
            Debug.Log($"[FMVR] battle 60 s: events {d.EventsFired} [{string.Join(",", d.EventCounts)}], shots {inf.TotalShots}, deaths {inf.TotalDeaths}, respawns {inf.Respawns}, crowd alive {CrowdLayer.Instance.Alive} killed {CrowdLayer.Instance.Killed}, vehicle shots {VehicleLayer.Instance.TotalShots}, jet runs {AirLayer.Instance.JetRuns}, heli runs {AirLayer.Instance.HeliRuns}, blasts {BattleFx.Instance.Detonations}, audio played {BattleAudio.Instance.Played} stolen {BattleAudio.Instance.Stolen}");
        }

        [UnityTest]
        public IEnumerator EveryEventType_RunsWithoutErrors()
        {
            var d = BattleDirector.Instance;
            d.autoRun = false;
            foreach (BattleEventType e in System.Enum.GetValues(typeof(BattleEventType)))
            {
                d.Fire(e);
                for (int i = 0; i < 30; i++) yield return null;
            }
            for (float t = 0f; t < 30f; t += Time.deltaTime) yield return null;
            Assert.AreEqual(System.Enum.GetValues(typeof(BattleEventType)).Length, d.EventCounts.Count(c => c > 0));
            Assert.Greater(AirLayer.Instance.JetRuns, 0);
            Assert.Greater(AirLayer.Instance.HeliRuns, 0);
            Assert.Greater(AirLayer.Instance.BombsDropped + AirLayer.Instance.RocketsFired, 0);
            Assert.Greater(VehicleLayer.Instance.TotalShots, 0, "a tank fired");
        }
    }
}
