using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Scenarios.SelfAid;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9b self-aid screenshots on the real battlefield (docs/screens/w9b_*.jpg): the hit in the open (first person,
    /// red flash), the bleeding arm in cover, the band high on the arm with the time, the radio report menu, the
    /// blanket, deep shock (grey + tunnel + dim), the comrades arriving, the debrief checklist, the menu pages.
    /// Hands are scripted (<see cref="SelfAidBot"/>); the run goes through the real scenario launch.
    /// </summary>
    public class SelfAidScreens
    {
        GlobalStateGuard _guard;
        int _quality;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            GameSettings.LeftHanded = false;
            _quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.SetQualityLevel(_quality, true);
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
        }

        static void Shot(string name, Vector3 from, Vector3 look, float fov, Light fill = null)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 900f;
            cam.enabled = false;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from, Vector3.up));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory("docs/screens");
            File.WriteAllBytes(Path.Combine("docs/screens", name + ".jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }

        /// First person: the eye turns to `look` (the overlays are children of the eye and turn with it).
        static void Eye(string name, Transform head, Vector3 look, float fov = 90f)
        {
            head.rotation = Quaternion.LookRotation(look - head.position, Vector3.up);
            Shot(name, head.position, look, fov);
        }

        static Light Fill(Vector3 at)
        {
            var l = new GameObject("~Fill").AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.5f;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.96f, 0.9f);
            l.shadows = LightShadows.None;
            l.transform.position = at;
            return l;
        }

        static void BoardShot(MenuBoard b, string name)
        {
            var t = b.transform;
            float s = t.lossyScale.x;
            Shot(name, t.position - t.forward * 1.25f * s + Vector3.up * 0.05f, t.position, 62f);
        }

        [UnityTest]
        [Timeout(400000)]
        public IEnumerator SelfAid_Screens()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Scenario("SA", new ScenarioVariant { seed = 3 }));
            SelfAidDirector d = null;
            for (int i = 0; i < 600 && (d = SelfAidDirector.Instance) == null; i++) yield return null;
            Assert.IsNotNull(d, "self-aid director started by the scenario launch");
            d.hitDelay = 2.5f;
            foreach (var hr in HandRig.All.ToList()) hr.UseScripted(hr.transform.position);
            var bot = new GameObject("~SelfAidBot").AddComponent<SelfAidBot>();
            bot.Init(d, d.playerRig);
            var head = d.head;
            Assert.IsNotNull(head);
            yield return bot.HoldWoundedArm();
            float t0 = Time.time;
            while (!d.HitHappened && Time.time - t0 < 15f) yield return null;
            Assert.IsTrue(d.HitHappened);
            yield return null;
            yield return null;
            float wp = d.Config.WoundPosition;
            Eye("w9b_01_hit_first_person", head, d.Limb.PointAt(wp) + Vector3.up * 0.05f, 95f);
            yield return DemoFlowRunner.Seconds(2.5f);
            Vector3 wound = d.Limb.PointAt(wp);
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Shot("w9b_02_hit_open_ground", head.position - fwd * 2.6f + side * 1.4f + Vector3.up * 0.4f, head.position + Vector3.down * 0.5f, 60f);

            // into the ruins through the doorway
            var door = BattlefieldLayout.DoorwayCentre;
            yield return bot.WalkTo(door + new Vector3(1.0f, 0f, 0f), 3f);
            yield return bot.WalkTo(door + new Vector3(-1.3f, 0f, 0.1f), 2f);
            yield return null;
            Assert.IsTrue(d.Run.InCover, "in cover inside house A (" + d.CoverName + ")");
            yield return bot.HoldWoundedArm();
            yield return DemoFlowRunner.Seconds(1.2f);
            wound = d.Limb.PointAt(wp);
            fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            side = Vector3.Cross(Vector3.up, fwd) * (d.Config.left ? -1f : 1f);
            var fill = Fill(wound + Vector3.up * 0.5f - fwd * 0.2f);
            Shot("w9b_03_arm_bleeding", wound + side * 0.55f + Vector3.up * 0.25f + fwd * 0.15f, wound, 50f);
            Eye("w9b_04_arm_first_person", head, wound + Vector3.up * 0.03f, 85f);

            yield return bot.SelfTourniquet(0.18f);
            Assert.IsTrue(d.Run.tq.Locked, "band locked; bot " + bot.lastStep);
            yield return null;
            Vector3 band = d.Limb.PointAt(d.Run.tq.limbPosition);
            fill.transform.position = band + Vector3.up * 0.4f - fwd * 0.2f;
            Shot("w9b_05_tq_high_and_tight", band + side * 0.42f + Vector3.up * 0.22f + fwd * 0.25f, band, 48f);
            Eye("w9b_06_tq_first_person", head, d.Tq.TimeLabelPoint, 80f);

            d.Config.rescueAfterReport = 14f;
            d.Config.firstCheckAfterAck = 5f;
            yield return bot.PressRadio();
            yield return null;
            Assert.IsTrue(d.Menu.Open, "report menu");
            Eye("w9b_07_report_menu", head, d.Menu.transform.position, 80f);
            yield return bot.Report();
            Assert.IsTrue(d.Run.ReportSent);
            yield return bot.Blanket();
            yield return null;
            Eye("w9b_08_blanket", head, head.position + fwd * 2f + Vector3.down * 0.9f, 90f);

            // deep shock for a moment (what the eyes see near the end without the band)
            float lost = d.Run.patient.bloodLostMl;
            d.Run.patient.bloodLostMl = d.Run.LethalMl * 0.82f;
            yield return null;
            yield return null;
            Eye("w9b_09_shock_vision", head, head.position + fwd * 3f + Vector3.down * 0.5f, 90f);
            d.Run.patient.bloodLostMl = lost;
            yield return null;

            Object.Destroy(fill.gameObject);
            t0 = Time.time;
            while (!d.CueLog.Contains(SelfAidCue.RescueArrive) && Time.time - t0 < 60f)
            {
                if (d.Run.CheckPending) d.PressRadio();
                yield return null;
            }
            Assert.Contains(SelfAidCue.RescueArrive, d.CueLog);
            yield return DemoFlowRunner.Seconds(3.2f);
            Shot("w9b_10_comrades_arrive", head.position + fwd * 3.5f + Vector3.up * 0.6f, head.position + Vector3.down * 0.6f, 62f);
            t0 = Time.time;
            while (d.Report == null && Time.time - t0 < 20f) yield return null;
            Assert.IsNotNull(d.Report, "debrief");
            yield return DemoFlowRunner.Seconds(0.5f);
            var b = ScenarioDirector.Instance.board;
            BoardShot(b, "w9b_11_debrief");
            Debug.Log($"[FMVR] w9b screens debrief: {d.Report.endReason} score {d.Report.score} checks {string.Join("; ", d.Report.checks.Select(c => (c.ok ? "+" : "-") + c.text))}");
            b.ShowScenarios();
            BoardShot(b, "w9b_12_menu_scenarios");
            b.ShowTraining();
            BoardShot(b, "w9b_13_menu_training");
            GameSettings.Language = GameLanguage.KZ;
            b.ShowDebrief(SelfAidDebrief.Build(d.Def, d.Run, ScenarioClock.Format, true));
            BoardShot(b, "w9b_14_debrief_kz");
            GameSettings.Language = GameLanguage.RU;
        }
    }
}
