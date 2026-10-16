using System.Collections;
using System.Linq;
using System.Text;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w2: the V2 steppe battlefield. Cover is real: from the enemy side, the line of fire to a kneeling head
    /// behind every cover piece is blocked by a collider on the Cover layer, while the open stretch where Erlan
    /// falls and a man standing up behind the sandbags are exposed. Plus scene integrity (tags, layers, obstacles,
    /// flag, LZ), locomotion (wall collision, teleport validity, ground following) and a clean desktop start.
    /// </summary>
    public class BattlefieldTests
    {
        [UnitySetUp]
        public IEnumerator LoadBattlefield()
        {
            if (SceneManager.GetActiveScene().name == BattlefieldLayout.SceneName) yield break;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            AudioListener.volume = 0f;
            Physics.SyncTransforms();
        }

        static CoverPoint[] Points => Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None);

        [Test]
        public void Every_Cover_Piece_Has_A_Cover_Point()
        {
            var pts = Points;
            Assert.GreaterOrEqual(pts.Length, 12, "too few cover points");
            foreach (var p in pts) Assert.IsNotNull(p.cover, p.name + ": cover object not linked");
            // walls of both ruined houses, the burnt APC, the sandbagged barrier lines, the wrecks (the pipe became a car wreck in w4)
            foreach (var name in new[] { "HouseA_North", "HouseC_North", "arx_apc", "concrete_blockade", "concrete_barriers_lowpoly", "car_wreck_beetle" })
                Assert.IsTrue(pts.Any(p => p.cover.name == name), "no cover point behind " + name);
        }

        [Test]
        public void Kneeling_Behind_Every_Cover_Is_Shielded_From_The_Enemy()
        {
            var fails = new StringBuilder();
            int n = 0;
            foreach (var p in Points)
            {
                n++;
                Assert.AreEqual(BattlefieldLayout.KneelEyeHeight, p.eyeHeight, 1e-3f);
                if (!CoverMath.Shielded(p.HeadPosition, p.threatDirection, p.arcHalfAngle, out var why))
                    fails.AppendLine($"{p.name} (behind {p.cover.name}) at {p.HeadPosition}: {why}");
            }
            Debug.Log($"[FMVR] cover test: {n} kneeling positions checked");
            Assert.IsEmpty(fails.ToString(), "exposed kneeling positions:\n" + fails);
        }

        [Test]
        public void Lines_Of_Fire_Are_Stopped_By_Cover_Layer_Colliders()
        {
            foreach (var p in Points)
            {
                Vector3 shooter = p.HeadPosition + p.threatDirection.normalized * 120f + Vector3.up * 0.35f;
                Assert.IsTrue(CoverMath.Blocked(shooter, p.HeadPosition, out var hit), p.name);
                Assert.IsTrue(hit.collider.CompareTag(CoverMath.CoverTag), $"{p.name}: blocked by {hit.collider.name} which is not tagged Cover");
                Assert.AreEqual(LayerMask.NameToLayer(CoverMath.CoverLayerName), hit.collider.gameObject.layer, p.name);
            }
        }

        [Test]
        public void Standing_Up_Behind_The_Sandbags_Exposes_The_Head()
        {
            var p = Points.First(x => x.cover.name == "concrete_blockade");
            Vector3 head = p.transform.position + Vector3.up * 1.75f;
            Vector3 shooter = head + Vector3.forward * 120f + Vector3.up * 0.35f;
            Assert.IsFalse(CoverMath.Blocked(shooter, head, out _), "a standing man must show over a 1.5 m sandbag wall");
        }

        [Test]
        public void Erlan_Falls_In_The_Open_10_To_15_m_From_The_Doorway()
        {
            float d = BattlefieldLayout.DragDistance;
            Assert.That(d, Is.InRange(10f, 15f), "drag distance");
            var fall = BattlefieldLayout.ErlanFallPoint;
            Vector3 head = new Vector3(fall.x, BattlefieldLayout.GroundHeight(fall.x, fall.z) + 1.0f, fall.z);
            Assert.IsFalse(CoverMath.Blocked(head + Vector3.forward * 120f + Vector3.up * 0.35f, head, out var hit),
                "Erlan's spot must be in the open, but " + (hit.collider != null ? hit.collider.name : "?") + " covers it");
            // the drag line to the doorway is free of cover (only open ground and a shallow crater)
            Vector3 a = head + Vector3.down * 0.6f, b = BattlefieldLayout.DoorwayCentre + Vector3.up * 0.4f + Vector3.right * 0.6f;
            Assert.IsFalse(Physics.Linecast(a, b, out hit, CoverMath.CoverMask, QueryTriggerInteraction.Ignore),
                "drag line blocked by " + (hit.collider != null ? hit.collider.name : "?"));
        }

        [Test]
        public void Cover_Objects_Have_Collider_Layer_Tag_And_NavMesh_Obstacle()
        {
            int layer = LayerMask.NameToLayer(CoverMath.CoverLayerName);
            Assert.GreaterOrEqual(layer, 0, "Cover layer missing");
            foreach (var cover in Points.Select(p => p.cover).Distinct())
            {
                var cols = cover.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
                Assert.IsNotEmpty(cols, cover.name + " has no collider");
                Assert.IsTrue(cols.All(c => c.gameObject.layer == layer && c.CompareTag(CoverMath.CoverTag)), cover.name + ": collider not on Cover layer/tag");
                Assert.IsNotNull(cover.GetComponentInChildren<NavMeshObstacle>(), cover.name + ": no NavMeshObstacle");
            }
        }

        [Test]
        public void Flag_And_Landing_Zone_Are_In_Place()
        {
            Assert.IsNotNull(GameObject.Find("KZ_Flag/Cloth"), "flag cloth");
            Assert.IsNotNull(LzMarker.Instance, "LZ marker");
            Assert.IsTrue(LzMarker.Instance.SmokeActive, "LZ smoke should burn at start");
            float behind = BattlefieldLayout.HouseAMin.y - LzMarker.Instance.TouchdownPoint.z;
            Assert.That(behind, Is.InRange(35f, 50f), "LZ ~40 m behind the ruins");
            Assert.GreaterOrEqual(Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Count(p => p.name.StartsWith("SmokeColumn")), 5, "horizon smoke columns");
        }

        [UnityTest]
        public IEnumerator Stick_Movement_Slides_Along_Walls_And_Never_Passes_Through()
        {
            var loco = Object.FindAnyObjectByType<Locomotion>();
            Assert.IsNotNull(loco, "Locomotion on the rig");
            var rig = loco.transform;
            rig.position = new Vector3(-2.6f, 0.04f, 2.0f);
            yield return null;
            yield return null;
            Physics.SyncTransforms();
            // push 4 m north into house A's north wall (inner face at z = 4 − 0.19)
            for (int i = 0; i < 40; i++) { loco.MoveBy(new Vector3(0f, 0f, 0.1f)); Physics.SyncTransforms(); }
            float headZ = loco.head.position.z;
            Assert.Less(headZ, BattlefieldLayout.HouseAMax.y - BattlefieldLayout.WallThickness * 0.5f, "walked through the wall");
            Assert.Greater(headZ, 2.9f, "should get close to the wall");
            // free movement in the open
            rig.position = new Vector3(8f, 0f, 30f);
            yield return null;
            Physics.SyncTransforms();
            float moved = loco.MoveBy(new Vector3(1f, 0f, 0f));
            Assert.AreEqual(1f, moved, 0.05f, "open ground movement");
        }

        [UnityTest]
        public IEnumerator Teleport_Only_Onto_Walkable_Ground()
        {
            var loco = Object.FindAnyObjectByType<Locomotion>();
            loco.transform.position = new Vector3(4f, 0f, 6f);
            yield return null;
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(5f, 10f, 10f), Vector3.down, out var ground, 20f, loco.groundMask | loco.blockMask), "ground ray");
            Assert.IsTrue(loco.ValidTarget(ground), "open ground must be a valid target (hit " + ground.collider.name + ")");
            Assert.IsTrue(Physics.Raycast(new Vector3(-3f, 10f, BattlefieldLayout.HouseAMax.y), Vector3.down, out var wallTop, 20f, loco.groundMask | loco.blockMask));
            Assert.IsFalse(loco.ValidTarget(wallTop), "top of a wall must not be a target (hit " + wallTop.collider.name + ")");
            // arc fired from a standing head down-forward lands and is valid
            bool ok = loco.ComputeArc(new Vector3(4f, 1.5f, 6f), new Vector3(0f, -0.2f, 1f), out var p);
            Assert.IsTrue(ok, "arc onto open ground at " + p);
            loco.Teleport(p);
            yield return null;
            Assert.AreEqual(p.x, loco.head.position.x, 0.05f);
            Assert.AreEqual(p.z, loco.head.position.z, 0.05f);
        }

        [UnityTest]
        public IEnumerator Rig_Follows_The_Ground_Into_A_Crater()
        {
            var loco = Object.FindAnyObjectByType<Locomotion>();
            var c = BattlefieldLayout.Craters[3];
            loco.transform.position = new Vector3(c.x, 0f, c.y);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null; // batch frames are tiny: wait real time
            Assert.Less(BattlefieldLayout.GroundHeight(c.x, c.y), -0.3f, "crater floor should be lower");
            var hp = loco.head.position;
            Assert.IsTrue(Physics.Raycast(new Vector3(hp.x, 3f, hp.z), Vector3.down, out var hit, 10f, loco.groundMask), "ground mesh under the head");
            Assert.Less(hit.point.y, -0.3f, "mesh crater floor");
            Assert.AreEqual(hit.point.y, loco.transform.position.y, 0.06f, "rig must stand on the crater floor");
        }

        [Test]
        public void Stance_Thresholds_Match_Real_Head_Heights()
        {
            Assert.AreEqual(Stance.Standing, PlayerStance.Classify(1.65f));
            Assert.AreEqual(Stance.Crouching, PlayerStance.Classify(1.15f));
            Assert.AreEqual(Stance.Kneeling, PlayerStance.Classify(0.9f));
            Assert.AreEqual(Stance.Prone, PlayerStance.Classify(0.35f));
        }

        [UnityTest]
        public IEnumerator Battlefield_Runs_On_Desktop_Path_Without_Errors()
        {
            Assert.IsFalse(DesktopSimulator.XRActive, "test runs on the desktop simulator path");
            Assert.IsNotNull(Object.FindAnyObjectByType<DesktopSimulator>());
            Assert.IsNotNull(Object.FindAnyObjectByType<FieldMedic.Scenarios.ScenarioDirector>());
            for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime) yield return null;
            var stance = Object.FindAnyObjectByType<PlayerStance>();
            Assert.IsNotNull(stance);
            Assert.Greater(stance.HeadHeight, 0.5f);
        }
    }
}
