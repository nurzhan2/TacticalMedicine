using System.Collections;
using System.IO;
using FieldMedic.Battle;
using FieldMedic.Environment;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9 TACEVAC screenshots (docs/screens/w9_evac_*.jpg): Erlan dragged up to the open side door, the crew taking him in,
    /// the view from the bench in the cabin, the lift-off seen from the LZ. Also a short check of the whole hand-over:
    /// door zone → aboard → board → seated (rig parented to the helicopter) → flown off.
    /// </summary>
    public class W9EvacScreens
    {
        GlobalStateGuard _guard;
        int _quality;
        DemoFlowRunner _run;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            _quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            _run = new DemoFlowRunner();
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.SetQualityLevel(_quality, true);
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            if (_run.head != null) Object.Destroy(_run.head.gameObject);
        }

        static void Shot(string name, Vector3 from, Vector3 look, float fov)
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Evac_DragToDoor_CrewTakesHimIn_Board_FlyOff()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training("tq"));
            yield return DemoFlowRunner.Seconds(1.5f);
            var tac = TacevacController.Instance;
            Assert.IsNotNull(tac);
            tac.Call(true);
            Assert.IsTrue(tac.Landed);
            yield return null;
            // Erlan a few metres in front of the door (skip the long drag from the ruins — DemoFlowTests covers it)
            var c = _run.C;
            Vector3 door = tac.DoorPoint;
            Vector3 away = door - tac.heli.position; away.y = 0f; away.Normalize();
            Vector3 start = door + away * 3.2f;
            c.transform.position += new Vector3(start.x - _run.Hips.x, 0f, start.z - _run.Hips.z);
            yield return DemoFlowRunner.Seconds(0.3f);
            _run.TakeHead(_run.Hips + away * 1.5f + Vector3.up * 1.6f);
            _run.MakeBot();
            yield return _run.HandsTo(_run.Hips);
            Vector3 side = Vector3.Cross(Vector3.up, away);
            Shot("w9_evac_drag", door + away * 6.5f + side * 3.5f + Vector3.up * 2.2f, door + Vector3.up * 0.8f + away * 1.5f, 62f);
            yield return _run.DragVia(new Vector3(door.x, 0f, door.z), 30f, () => tac.State >= TacevacController.Stage.TakingAboard);
            Assert.AreEqual(TacevacController.Stage.TakingAboard, tac.State, "crew takes him in at the door; chest→door " + tac.CasualtyDoorDistance().ToString("F2"));
            yield return DemoFlowRunner.Seconds(tac.takeAboardSeconds * 0.45f);
            Shot("w9_evac_crew", door + away * 4f + side * 2.4f + Vector3.up * 1.7f, door + Vector3.up * 1.0f - away * 0.6f, 55f);
            float t0 = Time.time;
            while (!tac.CasualtyAboard && Time.time - t0 < 5f) yield return null;
            Assert.IsTrue(tac.CasualtyAboard);
            Assert.AreEqual(tac.heli, c.transform.parent, "Erlan lies in the cabin");
            // the medic walks into the door zone
            _run.TakeHead(door + Vector3.up * 1.6f);
            t0 = Time.time;
            while (!tac.PlayerAboard && Time.time - t0 < 5f) yield return null;
            Assert.IsTrue(tac.PlayerAboard, "boarded");
            Assert.AreEqual(tac.heli, ScenarioDirector.Instance.playerRig.parent, "player rides in the cabin (reference frame)");
            yield return DemoFlowRunner.Seconds(0.8f);
            var eye = Camera.main.transform;
            Shot("w9_evac_cabin", eye.position, tac.CabinPoint + Vector3.up * 0.2f, 85f);
            while (tac.State != TacevacController.Stage.Departing && Time.time - t0 < 12f) yield return null;
            yield return DemoFlowRunner.Seconds(3.5f);
            Shot("w9_evac_liftoff", door + away * 22f + side * 8f + Vector3.up * 1.7f, tac.heli.position + Vector3.up * 1.5f, 60f);
            Assert.Greater(tac.heli.position.y, tac.LandingPoint.y + 1.5f, "lifting off");
            while (!tac.Loaded && Time.time - t0 < 25f) yield return null;
            Assert.IsTrue(tac.Loaded, "flown off");
            Assert.IsNull(ScenarioDirector.Instance.playerRig.parent == tac.heli ? tac.heli : null, "back on the ground after the fade");
        }
    }
}
