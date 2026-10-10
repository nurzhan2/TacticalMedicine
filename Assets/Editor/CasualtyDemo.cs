using System;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Medical;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Edit-mode demo of S2 without entering Play mode (works in batch): opens Main, poses Ерлан in his crouch
    /// (s2_erlan_idle), then runs scenario 1's injury — VOG blast, arterial inner-thigh wound + forearm fragment —
    /// stepping Animator + physiology + blood by hand, and renders close-ups (s2_erlan_fall, s2_erlan_wounded,
    /// s2_erlan_wide). The scene is NOT saved.
    /// Batch (needs GPU, no -nographics): -executeMethod FieldMedic.EditorTools.CasualtyDemo.CaptureShots
    /// </summary>
    public static class CasualtyDemo
    {
        const float Dt = 1f / 30f;

        [MenuItem("FieldMedic/Casualty/Capture S2 Screenshots")]
        public static void CaptureShots()
        {
            try
            {
                int n = Run();
                Debug.Log($"[FMVR] CasualtyDemo: {n} screenshots");
                if (Application.isBatchMode) EditorApplication.Exit(n > 0 ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogError("[FMVR] CasualtyDemo failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static int Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            var c = Object.FindAnyObjectByType<CasualtyController>();
            if (c == null) throw new Exception("no CasualtyController in Main");
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var template = cams.FirstOrDefault(x => x.CompareTag("MainCamera"));
            foreach (var cam in cams) cam.enabled = false;
            var rnd = new System.Random(3);
            UnityEngine.Random.InitState(3);

            c.EnsurePatient();
            c.rig.Init();
            // edit-mode rendering: make skinned meshes use the bone pose we just stepped, not a cached one
            foreach (var sk in c.GetComponentsInChildren<SkinnedMeshRenderer>()) sk.forceMatrixRecalculationPerRender = true;
            var anim = c.animator;
            anim.Rebind();
            c.SetPose(CasualtyPose.CrouchIdle, true);
            Step(c, 1.2f, false);

            // temporary illumination flare high above (the trench is lit by a drifting para-flare in S1)
            var flareGo = new GameObject("~DemoFlare");
            var flare = flareGo.AddComponent<Light>();
            flare.type = LightType.Point;
            flare.color = new Color(1f, 0.93f, 0.82f);
            flare.range = 14f;
            flare.intensity = 3.2f;
            flare.shadows = LightShadows.None;
            flareGo.transform.position = c.transform.position + new Vector3(-2f, 5.5f, -1.5f);
            int shots = 0;

            var bodyCenter = c.rig.Bone(HumanBodyBones.Hips).position;
            LogPose(c, "idle");
            shots += Shot("s2_erlan_idle", InTrench(bodyCenter + new Vector3(-1.5f, 0.8f, -1.2f)), bodyCenter + new Vector3(0f, 0.15f, 0f), 50f, template);

            // --- blast: wounds + fall
            var thigh = c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration, "thighR_artery");
            c.AddWound(WoundType.Capillary, BodyRegion.ForearmL, WoundVisual.FragmentHole, "forearmL_frag");
            c.simulationSpeed = 1f;
            c.Hit(HitKind.Explosion);
            Step(c, 1.0f, true);
            LogPose(c, "fall");
            var hips = c.rig.Bone(HumanBodyBones.Hips).position;
            shots += Shot("s2_erlan_fall", InTrench(hips + new Vector3(-1.6f, 1.0f, -0.6f)), hips, 55f, template);
            Step(c, 3.5f, true);
            // let ~70 s of bleeding pass (physiology accelerated, visuals real-time for the last seconds)
            c.simulationSpeed = 12f;
            Step(c, 5.5f, true);
            c.simulationSpeed = 1f;
            // shock deepening: he stops writhing, legs out, fast shallow breathing
            c.poseHold = 999f;
            c.SetPose(CasualtyPose.LyingBreathless, true);
            Step(c, 2.5f, true);
            Debug.Log($"[FMVR] demo: pose={c.CurrentPose} loss={c.Patient.bloodLostMl:F0}ml ({c.Patient.LossFraction:P0}) HR={c.Patient.HeartRate:F0} " +
                      $"SBP={c.Patient.SystolicBP:F0} AVPU={c.Patient.AVPU} pain={c.Pain.level:F2} jets={thigh.JetBursts} pools={c.PoolManager.Pools.Count} poolVol={c.PoolManager.TotalVolumeMl:F0}");

            LogPose(c, "down");
            // advance particles to mid-spurt for the close-up
            Vector3 w = thigh.transform.position;
            Vector3 n = thigh.transform.forward;
            Vector3 side = Vector3.Cross(n, Vector3.up).normalized;
            // from the player's side of the trench, above and a little toward the feet
            var fa = c.rig.Bone(HumanBodyBones.RightUpperLeg).position; var fb = c.rig.Bone(HumanBodyBones.RightLowerLeg).position;
            var ax = (fb - fa).normalized; var rl = w - fa;
            Debug.Log($"[FMVR] demo wound radial from femur axis = {(rl - ax * Vector3.Dot(rl, ax)).magnitude:F3} m");
            Debug.Log($"[FMVR] demo wound at {w:F2} normal {n:F2} soak r={thigh.SoakRadius:F2} flow={thigh.FlowMlPerSec:F1}ml/s sphere0={c.soak.Spheres[0]:F2}");
            var probe = BakeBodyCollider(c);
            {
                var pm = probe.GetComponent<MeshCollider>().sharedMesh; var tr = probe.transform;
                float md = float.MaxValue; foreach (var v in pm.vertices) md = Mathf.Min(md, Vector3.Distance(tr.TransformPoint(v), w));
                Debug.Log($"[FMVR] demo wound distance to nearest skinned vertex = {md:F3} m");
            }
            // the flare drifts over: brighter for the close-up
            flareGo.transform.position = w + new Vector3(-1.2f, 3.2f, -0.6f);
            flare.intensity = 4.5f;
            var cp = BestView(w, n, 0.85f);
            shots += Shot("s2_erlan_wounded", cp, w + Vector3.down * 0.04f, 40f, template);
            Vector3 nh = Vector3.ProjectOnPlane(n, Vector3.up).normalized;
            shots += Shot("s2_erlan_wounded_top", InTrench(w + Vector3.up * 0.85f + nh * 0.25f), w, 45f, template);
            flare.intensity = 3.2f;
            flareGo.transform.position = c.transform.position + new Vector3(-2f, 5.5f, -1.5f);
            Object.DestroyImmediate(probe);
            var smr = c.rig.bodyRenderer;
            Debug.Log($"[FMVR] demo body mats: {string.Join(",", smr.sharedMaterials.Select(m => m.name + ":" + m.shader.name))} hasMPB={smr.HasPropertyBlock()}");
            hips = c.rig.Bone(HumanBodyBones.Hips).position;
            shots += Shot("s2_erlan_wide", InTrench(hips + new Vector3(-2.0f, 1.35f, -0.6f)), hips + Vector3.down * 0.1f, 55f, template);

            Object.DestroyImmediate(flareGo);
            return shots;
        }

        /// <summary>Keeps demo cameras inside the trench (walls at z = ±1.1, parapet top 1.45).</summary>
        static Vector3 InTrench(Vector3 p) => new Vector3(p.x, Mathf.Clamp(p.y, 0.25f, 1.6f), Mathf.Clamp(p.z, -0.95f, 0.95f));

        /// <summary>Unobstructed camera position looking at a point, preferring the surface normal side and above.</summary>
        static Vector3 BestView(Vector3 target, Vector3 normal, float dist)
        {
            Physics.SyncTransforms();
            Vector3 best = target + Vector3.up * dist; float bestScore = float.MinValue;
            for (int az = 0; az < 16; az++)
            for (int el = 20; el <= 75; el += 11)
            {
                var dir = Quaternion.Euler(-el, az * 22.5f, 0f) * Vector3.forward;
                var p = InTrench(target + dir * dist);
                var d = (p - target).normalized;
                // the first thing seen from the camera toward the wound must be the wound itself
                if (Physics.Raycast(p, -d, out var hit, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore)
                    && Vector3.Distance(hit.point, target) > 0.05f) continue;
                float score = Vector3.Dot(d, normal) * 2f + d.y * 0.6f - Mathf.Abs(Vector3.Distance(p, target) - dist) * 3f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        static GameObject BakeBodyCollider(CasualtyController c)
        {
            var smr = c.rig.bodyRenderer;
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var go = new GameObject("~BodyProbe");
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
            return go;
        }

        static void LogPose(CasualtyController c, string tag)
        {
            var h = c.rig.Bone(HumanBodyBones.Hips).position;
            var hd = c.rig.Bone(HumanBodyBones.Head).position;
            Debug.Log($"[FMVR] demo {tag}: pose={c.CurrentPose} hips={h:F2} head={hd:F2} root={c.transform.position:F2}");
        }

        static void Step(CasualtyController c, float seconds, bool down)
        {
            int steps = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                c.animator.Update(Dt);
                c.Simulate(Dt);
                c.rig.ApplyProcedural(Dt);
            }
        }

        static int Shot(string name, Vector3 pos, Vector3 look, float fov, Camera template)
        {
            var pose = new GameObject("~shot").transform;
            pose.position = pos;
            pose.rotation = Quaternion.LookRotation(look - pos);
            var path = System.IO.Path.Combine(ScreenshotTool.OutDir, name + ".png");
            ScreenshotTool.Render(pose, fov, template, 1600, 900, path);
            Object.DestroyImmediate(pose.gameObject);
            Debug.Log("[FMVR] screenshot " + path);
            return 1;
        }
    }
}
