using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>w10 screenshots on the real battlefield: the kit tray in front of the eyes (wanted item glowing), the
    /// closed kit on the hip, the hold-Y radio panel, the step line (bottom of view + wrist card) and the controls
    /// tutorial in the menu. docs/screens/w10_*.jpg</summary>
    [Category("Screenshots")]
    public class W10Screens
    {
        GlobalStateGuard _guard;
        MedicBot _bot;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
        }

        [TearDown]
        public void TearDown()
        {
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            GameSettings.HoldTimeScale = 1f;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            SmokeScreens.Clear();
            if (_bot != null) Object.Destroy(_bot.gameObject);
        }

        static IEnumerator Seconds(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        IEnumerator Load(System.Action request)
        {
            request?.Invoke();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
            Assert.IsNotNull(ScenarioDirector.Instance);
        }

        void MakeBot()
        {
            var hands = HandGrabber.All.Where(h => h != null).ToList();
            var l = hands.First(h => h.Handedness == Handedness.Left);
            var r = hands.First(h => h.Handedness == Handedness.Right);
            _bot = new GameObject("~Bot").AddComponent<MedicBot>();
            _bot.Init(l, r);
            _bot.handSpeed = 2f;
        }

        static Transform Eye => Camera.main.transform;

        static void Shot(string name, Vector3 from, Vector3 look, float fov = 80f)
        {
            const int w = 1600, h = 1000;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 450f;
            cam.enabled = false;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
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
        public IEnumerator Tray_Hip_Radio_StepLine()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            yield return Seconds(1.5f);
            MakeBot();
            var tray = KitTray.Player;
            Assert.IsNotNull(tray, "the player's kit tray");
            // hands low and out of the view
            yield return _bot.Move(_bot.left, Eye.position + Eye.rotation * new Vector3(-0.3f, -0.55f, 0.05f), false, 4f);
            yield return _bot.Move(_bot.right, Eye.position + Eye.rotation * new Vector3(0.3f, -0.55f, 0.05f), false, 4f);
            yield return Seconds(0.5f);
            // 1) the step line at the bottom of the view (first seconds of the step)
            Shot("w10_step_banner", Eye.position, Eye.position + Eye.forward * 2f + Vector3.down * 0.35f, 90f);
            // 2) the closed kit on the left hip, looking down (it glows: the step needs the CAT)
            Shot("w10_hip_kit", Eye.position, tray.Hip.position, 80f);
            // 3) X → the tray in front of the eyes, the CAT cell glowing
            yield return _bot.OpenPouch(tray.pouch, _bot.left);
            yield return Seconds(0.6f);
            Vector3 c = tray.Panel.position;
            Shot("w10_tray_view", Eye.position, c + Vector3.down * 0.02f, 82f);
            Shot("w10_tray_close", c + (Eye.position - c).normalized * 0.42f + Vector3.up * 0.05f, c, 62f);
            // 4) the CAT taken from the tray (full size in the hand)
            var cat = tray.pouch.Find<TourniquetCAT>();
            yield return _bot.Grab(_bot.right, cat);
            yield return _bot.Move(_bot.right, Eye.position + Eye.rotation * new Vector3(0.12f, -0.18f, 0.35f), false, 2f);
            yield return Seconds(0.3f);
            Shot("w10_tray_cat_in_hand", Eye.position, c + Vector3.up * 0.08f, 82f);
            yield return _bot.Move(_bot.right, cat.homeSlot.position + tray.pouch.transform.up * 0.1f, false, 2f);
            yield return _bot.Release(_bot.right);
            yield return _bot.OpenPouch(tray.pouch, _bot.left); // no-op: open
            // 5) hold Y: the radio panel
            var left = (ScriptedHandInput)HandRig.Find(Handedness.Left);
            left.PrimaryButton = true; yield return null; left.PrimaryButton = false; yield return Seconds(0.4f); // fold the tray
            left.SecondaryButton = true;
            yield return Seconds(0.4f);
            Assert.IsTrue(RadioPanel.Instance != null && RadioPanel.Instance.IsOpen, "radio panel open while Y is held");
            Shot("w10_radio_panel", Eye.position, RadioPanel.Instance.transform.position, 70f);
            left.SecondaryButton = false;
            yield return Seconds(0.3f);
            // 6) the wrist card: raise the left wrist with the watch toward the eyes
            Vector3 at = Eye.position + Eye.rotation * new Vector3(-0.05f, -0.2f, 0.28f);
            Vector3 toEye = (Eye.position - at).normalized;
            left.Rotation = Quaternion.LookRotation(Vector3.Cross(Eye.right, toEye), toEye);
            left.Position = at;
            yield return Seconds(0.3f);
            var watch = Object.FindAnyObjectByType<WristWatch>();
            Shot("w10_wrist_card", Eye.position, watch.transform.position + (watch.transform.TransformPoint(new Vector3(0f, -0.1f, 0f)) - watch.transform.position) * 0.5f, 50f);
        }

        [UnityTest]
        public IEnumerator Menu_And_Tutorial()
        {
            yield return Load(() => ScenarioLaunch.Menu());
            yield return Seconds(1f);
            var board = ScenarioDirector.Instance.board;
            Shot("w10_menu", Eye.position, board.transform.position, 70f);
            var tut = ControlsTutorial.Begin(ScenarioDirector.Instance);
            yield return Seconds(0.5f);
            Assert.IsFalse(board.gameObject.activeSelf, "the board hides during the tutorial");
            Shot("w10_tutorial", Eye.position, tut.Canteen.transform.position + Vector3.up * 0.35f, 85f);
            bool seen = GameSettings.TutorialSeen;
            tut.Skip();
            GameSettings.TutorialSeen = seen;
            yield return null;
            Assert.IsTrue(board.gameObject.activeSelf, "the board is back after skipping");
        }
    }
}
