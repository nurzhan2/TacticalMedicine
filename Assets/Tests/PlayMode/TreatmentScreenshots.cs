using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Loads the real Main scene, lets the medic bot play Scenario 1 (and the chest / groin skills) with the
    /// player's own hands, and renders docs/screens/s3_*.png at each step. Run explicitly:
    /// -testFilter FieldMedic.Tests.TreatmentScreenshots (needs the GPU: no -nographics).
    /// </summary>
    [Category("Screenshots")]
    public class TreatmentScreenshots
    {
        const string Dir = "docs/screens";
        Light _flare;

        static IEnumerator LoadMain()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Main.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
        }

        MedicBot SetupBot(out IfakPouch pouch)
        {
            var hands = Object.FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);
            var l = hands.First(h => h.Handedness == Handedness.Left);
            var r = hands.First(h => h.Handedness == Handedness.Right);
            l.Rig.UseScripted(l.transform.position);
            r.Rig.UseScripted(r.transform.position);
            var bot = new GameObject("Bot").AddComponent<MedicBot>();
            bot.Init(l, r);
            pouch = Object.FindAnyObjectByType<IfakPouch>(FindObjectsInactive.Exclude);
            foreach (var p in Object.FindObjectsByType<IfakPouch>(FindObjectsSortMode.None)) if (p.bodyLocked) pouch = p;
            // a drifting para-flare lights the scene for the pictures (S4 brings the real night lighting)
            var fgo = new GameObject("~ShotFlare");
            _flare = fgo.AddComponent<Light>();
            _flare.type = LightType.Point;
            _flare.color = new Color(1f, 0.93f, 0.82f);
            _flare.range = 12f;
            _flare.intensity = 3.4f;
            _flare.shadows = LightShadows.None;
            return bot;
        }

        /// <summary>Kneel the player rig beside the casualty so the body-locked IFAK is within reach.</summary>
        static void Kneel(Vector3 at, Vector3 lookAt)
        {
            var sim = Object.FindAnyObjectByType<DesktopSimulator>();
            if (sim != null) sim.enabled = false;
            var cam = Camera.main.transform;
            var root = cam.root;
            Vector3 fwd = lookAt - at; fwd.y = 0f;
            root.SetPositionAndRotation(new Vector3(at.x, 0f, at.z), Quaternion.LookRotation(fwd));
            var ts = cam.parent;
            ts.localPosition = new Vector3(0f, 0.95f, 0f); // kneeling eye height
            ts.localRotation = Quaternion.Euler(35f, 0f, 0f);
        }

        IEnumerator Shot(string name, Vector3 from, Vector3 at, float fov = 55f)
        {
            yield return new WaitForEndOfFrame();
            _flare.transform.position = at + new Vector3(-1.4f, 3.4f, -0.8f);
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.stereoTargetEye = StereoTargetEyeMask.None; }
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }

        static Vector3 Side(CasualtyController c, Vector3 p, float right, float up, float along = 0f)
        {
            Vector3 r = Vector3.ProjectOnPlane(c.rig.BodyRight, Vector3.up).normalized;
            Vector3 a = Vector3.ProjectOnPlane(c.rig.BodyUp, Vector3.up).normalized;
            Vector3 v = p + r * right + Vector3.up * up + a * along;
            return new Vector3(v.x, v.y, Mathf.Clamp(v.z, -0.95f, 0.95f)); // stay inside the trench
        }

        [UnityTest]
        public IEnumerator Scenario1_Tourniquet_Drag_TFC()
        {
            GameSettings.HoldTimeScale = 30f;
            TreatmentAudio.Muted = true;
            yield return LoadMain();
            var c = Object.FindAnyObjectByType<CasualtyController>();
            c.voice.muted = true;
            var thigh = c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration, "thighR_artery");
            c.AddWound(WoundType.Capillary, BodyRegion.ForearmL, WoundVisual.FragmentHole, "forearmL_frag");
            c.Hit(HitKind.Explosion);
            float t0 = Time.time;
            while (c.IsFalling && Time.time - t0 < 9f) yield return null;
            yield return new WaitForSeconds(3f); // fall -> lying crossfade settles
            c.simulationSpeed = 6f; // ~40 s of bleeding before the medic gets there
            yield return new WaitForSeconds(5f);
            c.simulationSpeed = 1f;
            var hips = c.rig.Bone(HumanBodyBones.Hips).position;
            Kneel(Side(c, hips, 0.75f, 0f, -0.2f), hips);
            var bot = SetupBot(out var pouch);
            yield return null;
            var w = thigh.transform.position;
            yield return Shot("s3_01_wounded", Side(c, w, 0.9f, 0.9f, -0.3f), w);

            // his own CAT from his IFAK, over the boot and up the leg, high & tight
            var cat = c.Interaction.Ifak.Find<TourniquetCAT>();
            var marker = pouch.Find<MarkerItem>();
            var run = bot.StartCoroutine(bot.ApplyTourniquet(cat, c, Limb.RightLeg, 0.12f, 4, marker));
            while (!cat.State.OnLimb) yield return null;
            yield return new WaitForSeconds(0.4f);
            var foot = c.rig.Bone(HumanBodyBones.RightFoot).position;
            yield return Shot("s3_02_tq_over_foot", Side(c, foot, 0.7f, 0.6f, -0.4f), cat.transform.position, 50f);
            while (cat.State.stage < TourniquetStage.StrapPulled) yield return null;
            yield return new WaitForSeconds(0.3f);
            while (cat.State.HalfTurns < 2) yield return null;
            yield return Shot("s3_03_tq_twist", Side(c, cat.transform.position, 0.55f, 0.55f, 0.1f), cat.transform.position, 45f);
            while (cat.State.HalfTurns < 4) yield return null;
            while (cat.State.stage < TourniquetStage.TimeMarked && bot.lastStep != "tq: done") yield return null;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("s3_04_tq_locked_time", Side(c, cat.transform.position, 0.35f, 0.32f, 0.05f), cat.transform.position, 40f);
            Debug.Log($"[FMVR] S1 TQ: stage {cat.State.stage} turns {cat.State.HalfTurns} flow {thigh.FlowMlPerSec:F2} pulse {c.Patient.PulseStrength(PulseSite.PedalR):F2} fallback {bot.fallbackGrabs}");

            // drag 3.5 m toward the dugout
            Vector3 dest = c.rig.Bone(HumanBodyBones.Hips).position + new Vector3(-3.5f, 0f, 0f);
            var drag = bot.StartCoroutine(bot.DragTo(c, dest, 20f));
            yield return new WaitForSeconds(2.2f);
            hips = c.rig.Bone(HumanBodyBones.Hips).position;
            yield return Shot("s3_05_drag", new Vector3(hips.x + 2.4f, 1.5f, Mathf.Clamp(hips.z - 0.6f, -0.9f, 0.9f)), hips + Vector3.left * 0.3f, 60f);
            yield return drag;
            c.Protocol.phase = Phase.TacticalFieldCare;
            hips = c.rig.Bone(HumanBodyBones.Hips).position;
            Kneel(Side(c, hips, 0.75f, 0f, -0.1f), hips);
            yield return null;

            // TFC: gloves, cut the trouser leg, sweep, pulse, mark forehead, blanket
            yield return bot.Gloves(pouch.Find<GlovesItem>());
            yield return Shot("s3_06_gloves", Side(c, bot.right.PalmPosition, 0.5f, 0.35f), bot.right.PalmPosition, 50f);
            yield return bot.CutClothing(pouch.Find<ShearsItem>(), c, Limb.RightLeg, 0.9f, 0.25f);
            w = thigh.transform.position;
            yield return Shot("s3_07_cut_exposed", Side(c, w, 0.55f, 0.6f, -0.15f), w, 45f);
            yield return bot.BloodSweep(c);
            var arm = c.rig.Bone(HumanBodyBones.LeftLowerArm).position;
            yield return bot.CheckPulse(c, PulseSite.PedalR, 1.2f);
            yield return Shot("s3_08_pulse_check_foot", Side(c, foot, 0.6f, 0.45f, -0.2f), HandTreatment.PulsePoint(c.rig, PulseSite.PedalR), 45f);
            yield return bot.Write(marker, MarkerItem.Forehead(c.rig));
            var head = c.rig.Bone(HumanBodyBones.Head).position;
            yield return Shot("s3_09_forehead_T", Side(c, head, 0.45f, 0.45f, 0.15f), head, 40f);
            var blanket = pouch.Find<BlanketItem>();
            hips = c.rig.Bone(HumanBodyBones.Hips).position;
            blanket.Drape(c);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("s3_10_blanket", Side(c, hips, 1.0f, 1.0f, -0.4f), hips, 55f);
            Debug.Log($"[FMVR] S1 run: score {c.Protocol.Score()} loss {c.Patient.LossFraction:P0} log {string.Join(" | ", c.Protocol.log.Select(e => e.code))}");
            Assert.That(cat.State.Locked, Is.True);
        }

        [UnityTest]
        public IEnumerator Chest_Seal_Needle_And_Groin_Packing()
        {
            GameSettings.HoldTimeScale = 30f;
            TreatmentAudio.Muted = true;
            yield return LoadMain();
            var c = Object.FindAnyObjectByType<CasualtyController>();
            c.voice.muted = true;
            var chest = c.AddWound(WoundType.PenetratingChest, BodyRegion.ChestR, WoundVisual.ChestEntry, "chest");
            var groin = c.AddWound(WoundType.Junctional, BodyRegion.GroinL, WoundVisual.RaggedLaceration, "groin");
            c.Hit(HitKind.Shot);
            float t0 = Time.time;
            while (c.IsFalling && Time.time - t0 < 9f) yield return null;
            yield return new WaitForSeconds(3f); // fall -> lying crossfade settles
            c.Protocol.phase = Phase.TacticalFieldCare;
            var hips = c.rig.Bone(HumanBodyBones.Hips).position;
            Kneel(Side(c, hips, 0.75f, 0f, 0.2f), hips);
            var bot = SetupBot(out var pouch);
            yield return null;

            // groin: cut, pack with fingers, hold, Israeli bandage
            var care = c.Interaction.Care(groin.Wound);
            yield return bot.CutClothing(pouch.Find<ShearsItem>(), c, Limb.LeftLeg, 0.8f, 0.1f);
            var pack = bot.StartCoroutine(bot.Pack(pouch.Find<GauzeItem>(), care));
            while (care.Packing.Fill < 0.5f) yield return null;
            var g = groin.transform.position;
            yield return Shot("s3_11_packing", Side(c, g, 0.45f, 0.5f, -0.1f), g, 45f);
            yield return pack;
            yield return bot.HoldPressure(care, 6.5f);
            yield return bot.Bandage(pouch.Find<BandageItem>(), care);
            yield return Shot("s3_12_israeli_bandage", Side(c, g, 0.5f, 0.55f, -0.2f), g, 45f);

            // chest: seal (tear, peel, wipe, place, edges), then needle at the 5th ICS AAL
            var cc = c.Interaction.Care(chest.Wound);
            yield return bot.ChestSeal(pouch.Find<ChestSealItem>(), cc);
            var p = chest.transform.position;
            yield return Shot("s3_13_chest_seal", Side(c, p, 0.4f, 0.5f, 0.1f), p, 45f);
            c.Patient.pneumo = PneumoState.Tension; c.Patient.tensionSeverity = 0.5f; // fast-forward to tension
            var needle = pouch.Find<NeedleItem>();
            var nrun = bot.StartCoroutine(bot.Needle(needle, c, NeedleSite.FifthIcsAal, true));
            while (needle.DepthCm < 3f) yield return null;
            yield return Shot("s3_14_needle", Side(c, needle.transform.position, 0.45f, 0.4f, 0.05f), needle.transform.position, 45f);
            yield return nrun;
            Debug.Log($"[FMVR] chest/groin run: groin {groin.Wound.control} controlled={groin.Wound.IsControlled}, seal {cc.Seal?.Proc.step}, pneumo {c.Patient.pneumo}, log {string.Join(" | ", c.Protocol.log.Select(e => e.code))}");
            Assert.That(groin.Wound.IsControlled, Is.True);
        }
    }
}
