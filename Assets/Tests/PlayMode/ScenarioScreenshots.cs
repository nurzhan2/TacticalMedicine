using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Renders docs/screens/s5_*.png from the real Main scene: menu board pages, briefing card, every scenario
    /// right after its incident, the debrief board and a training lesson.
    /// Run explicitly (GPU needed): -testFilter FieldMedic.Tests.ScenarioScreenshots
    /// </summary>
    [Category("Screenshots")]
    public class ScenarioScreenshots
    {
        const string Dir = "docs/screens";

        static IEnumerator LoadMain()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Main.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
            var dir = NightLightingDirector.Instance;
            if (dir != null) { dir.autoTracers = false; }
            var threat = ThreatSystem.Instance;
            if (threat != null) threat.enableHits = false;
        }

        static void Shot(string name, Vector3 from, Vector3 at, float fov = 60f, float fill = 0f)
        {
            Light l = null;
            if (fill > 0f)
            {
                var lg = new GameObject("~Fill");
                l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.85f, 0.88f, 1f);
                l.intensity = fill;
                l.range = 4f;
                l.shadows = LightShadows.None;
                lg.transform.position = from + Vector3.up * 0.35f + (at - from).normalized * 0.2f;
            }
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.stereoTargetEye = StereoTargetEyeMask.None; }
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 600f;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            foreach (var b in Object.FindObjectsByType<Billboard>(FindObjectsSortMode.None)) b.Face(from);
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
            if (l != null) Object.Destroy(l.gameObject);
            Debug.Log("[FMVR] screenshot " + name);
        }

        /// <summary>The desktop hands, belt IFAK and radio float in front of the eyes: hide them for presentation shots.</summary>
        static void HidePlayerKit(bool hide)
        {
            foreach (var n in new[] { "Hands", "PlayerIFAK", "Radio" })
            {
                var g = GameObject.Find(n);
                if (g != null) foreach (var r in g.GetComponentsInChildren<Renderer>()) r.enabled = !hide;
            }
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void BoardShot(MenuBoard b, string name)
        {
            var t = b.transform;
            Vector3 from = t.position - t.forward * 1.25f * t.localScale.x + Vector3.up * 0.12f * t.localScale.x;
            Shot(name, from, t.position, 52f);
        }

        [UnityTest]
        public IEnumerator Menu_Pages()
        {
            ScenarioLaunch.Pending = null;
            GameSettings.Language = GameLanguage.RU;
            yield return LoadMain();
            yield return Frames(10);
            var d = ScenarioDirector.Instance;
            Assert.That(d, Is.Not.Null);
            Assert.That(d.InMenu, Is.True);
            var b = d.board;
            HidePlayerKit(true);
            yield return new WaitForSeconds(0.5f);
            BoardShot(b, "s5_menu_main");
            b.ShowScenarios(); yield return Frames(5);
            // hover pulse on card 1 for the shot
            var c1 = RoundButton.All.First(x => x.Label != null && x.Label.text == "1" && x.transform.IsChildOf(b.transform));
            for (int i = 0; i < 20; i++) { c1.Hover(); yield return null; }
            BoardShot(b, "s5_menu_scenarios");
            b.ShowSettings(); yield return Frames(5);
            BoardShot(b, "s5_menu_settings");
            b.ShowTraining(); yield return Frames(5);
            BoardShot(b, "s5_menu_training");
            b.ShowBriefing(ScenarioCatalog.Get("S1"), ScenarioVariant.Default, true); yield return Frames(5);
            BoardShot(b, "s5_menu_briefing");
            GameSettings.Language = GameLanguage.KZ;
            b.ShowScenarios(); yield return Frames(5);
            BoardShot(b, "s5_menu_scenarios_kz");
            GameSettings.Language = GameLanguage.RU;
            // wide: the board in the dugout, candle and lamp
            Shot("s5_menu_dugout", new Vector3(-7.0f, 1.55f, -1.1f), b.transform.position + Vector3.down * 0.1f, 64f);
        }

        static readonly string[] Names = { "", "drop", "sniper", "mine", "mortar", "fpv", "kab", "underfire", "dawn" };

        [UnityTest]
        public IEnumerator Scenarios_AfterIncident([Values("S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8")] string id)
        {
            ScenarioLaunch.Scenario(id, ScenarioVariant.Default);
            yield return LoadMain();
            var d = ScenarioDirector.Instance;
            Assert.That(d, Is.Not.Null);
            Assert.That(d.Run.def.id, Is.EqualTo(id));
            var c = d.casualty;
            if (id == "S1")
            {
                yield return Frames(20);
                var head = d.head;
                Shot("s5_briefing_card", head.position + Vector3.up * 0.02f, head.position + head.forward * 0.85f + Vector3.down * 0.12f, 60f);
            }
            Time.timeScale = 4f;
            float t0 = Time.realtimeSinceStartup;
            while (c.Patient.wounds.Count == 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            while (c.IsFalling && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            yield return new WaitForSeconds(id == "S7" || id == "S8" ? 1f : 6f); // pool grows, blood soaks
            Time.timeScale = 1f;
            var hips = c.rig.Bone(HumanBodyBones.Hips).position;
            // pick a camera spot with a clear line to him: beside, at the feet or at the head end
            Vector3 along = Vector3.ProjectOnPlane(c.rig.BodyUp, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
            Vector3 from = hips - along * 1.0f + Vector3.up * 1.05f;
            foreach (var cand in new[] { hips + side * 0.9f + Vector3.up * 1.0f - along * 0.3f, hips - side * 0.9f + Vector3.up * 1.0f - along * 0.3f,
                                         hips - along * 1.1f + Vector3.up * 1.0f, hips + along * 1.3f + Vector3.up * 1.0f })
                if (!Physics.Linecast(hips + Vector3.up * 0.35f, cand)) { from = cand; break; }
            HidePlayerKit(true);
            Shot($"s5_{id.ToLower()}_{Names[int.Parse(id.Substring(1))]}", from, hips + Vector3.up * 0.05f, 58f, 0.6f);
            HidePlayerKit(false);
            if (id == "S3")
            {
                var h = d.head.position;
                Shot("s5_s3_footprints", h + Vector3.up * 0.1f, d.minePath.points[2], 64f);
            }
            if (id == "S1")
            {
                // debrief: evacuate quickly and render the board that comes to the player
                c.Protocol.Perform(MedAction.MoveToCover);
                yield return Frames(3);
                c.Protocol.Perform(MedAction.MedevacRequest);
                d.Timeline.evacTimeScale = 600f;
                while (d.Report == null && Time.realtimeSinceStartup - t0 < 90f) yield return null;
                yield return Frames(5);
                HidePlayerKit(true);
                BoardShot(d.board, "s5_debrief");
            }
        }

        [UnityTest]
        public IEnumerator Training_Tourniquet()
        {
            ScenarioLaunch.Training("tq");
            yield return LoadMain();
            yield return new WaitForSeconds(2f);
            var t = TrainingDirector.Instance;
            Assert.That(t, Is.Not.Null);
            var c = t.director.casualty;
            var hips = c.rig.Bone(HumanBodyBones.Hips).position;
            var panel = GameObject.Find("TrainingPanel").transform;
            HidePlayerKit(true);
            Vector3 from = panel.position - panel.forward * 1.1f + Vector3.up * 0.25f;
            Shot("s5_training_tq", from, Vector3.Lerp(panel.position, hips, 0.25f), 70f, 1.6f);
            foreach (var d in ScenarioCatalog.All) PlayerPrefs.DeleteKey("fm_best_" + d.id); // screenshot runs are not records
        }
    }
}
