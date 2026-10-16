using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 deliverable: the "interact1" video and screenshots, scripted hands only. Tutorial (grab, pull, twist,
    /// pinch, two-hand drag) → tourniquet twist: flinch + wrist grab → assessment lesson: voice → eyes open, pulse at
    /// the neck and wrist with the white label, breathing with the ear at his mouth, capillary refill, shock colours,
    /// physical hand with ghost. Frames (30 fps game clock) only with -fmCapture → %TEMP%\fmvr_w8_video;
    /// tools/make_interact_video.py adds captions + sound. Screens: docs/screens/w8_*.jpg.
    /// </summary>
    [Category("Screenshots")]
    public class Interact1Capture : W8SceneTest
    {
        const int VW = 1920, VH = 1080;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _tex;
        string _dir;
        int _frame;
        bool _frames, _recording;
        Vector3 _camPos, _look, _camVel, _lookVel, _wantPos, _wantLook;
        float _smooth = 0.6f;
        int _quality;
        bool _savedTut;

        [SetUp]
        public void CapSetUp()
        {
            _quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            _savedTut = TutorialDirector.Done;
        }

        [TearDown]
        public void CapTearDown()
        {
            Time.captureFramerate = 0;
            QualitySettings.SetQualityLevel(_quality, true);
            TutorialDirector.Done = _savedTut;
            TutorialDirector.AutoStart = false;
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) Object.Destroy(_rt);
            if (_tex != null) Object.Destroy(_tex);
        }

        void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fmvr_w8_video");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            var go = new GameObject("~VideoCam");
            Object.DontDestroyOnLoad(go);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 450f;
            _cam.enabled = false;
            _rt = new RenderTexture(VW, VH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _rt.Create();
            _tex = new Texture2D(VW, VH, TextureFormat.RGB24, false);
        }

        void Render(RenderTexture rt)
        {
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_cam, req);
        }

        void Frame()
        {
            if (!_recording || !_frames) return;
            Render(_rt);
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, VW, VH), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_dir, $"f{_frame++:00000}.jpg"), _tex.EncodeToJPG(90));
        }

        void Shot(string name)
        {
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            float aspect = _cam.aspect;
            _cam.aspect = (float)w / h;
            Render(rt); Render(rt);
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = null;
            _cam.aspect = aspect;
            Directory.CreateDirectory("docs/screens");
            File.WriteAllBytes(Path.Combine("docs/screens", "w8_" + name + ".jpg"), t.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(t);
            Debug.Log("[FMVR] screenshot w8_" + name);
        }

        void Mark(string key)
        {
            if (_dir == null) return;
            File.AppendAllText(Path.Combine(_dir, "moments.txt"), $"{_frame} {key}\n");
            Debug.Log($"[FMVR] w8 video moment {key} at frame {_frame}");
        }

        /// <summary>Camera target (smoothed); `snap` cuts.</summary>
        void Aim(Vector3 pos, Vector3 look, bool snap = false, float smooth = 0.6f)
        {
            _wantPos = pos; _wantLook = look; _smooth = smooth;
            if (snap) { _camPos = pos; _look = look; _camVel = _lookVel = Vector3.zero; Apply(); }
        }

        void Apply() => _cam.transform.SetPositionAndRotation(_camPos, Quaternion.LookRotation(_look - _camPos, Vector3.up));

        IEnumerator Recorder()
        {
            while (true)
            {
                float dt = Time.deltaTime;
                _camPos = Vector3.SmoothDamp(_camPos, _wantPos, ref _camVel, _smooth, 4f, dt);
                _look = Vector3.SmoothDamp(_look, _wantLook, ref _lookVel, _smooth * 0.7f, 6f, dt);
                Apply();
                if (run.head != null && run.headFollowsCamera) run.head.position = _camPos;
                yield return null;
                Frame();
            }
        }

        IEnumerator Hold(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        Coroutine _rec;
        MonoBehaviour _host;

        void StartRec()
        {
            if (_host == null) { var g = new GameObject("~RecHost"); Object.DontDestroyOnLoad(g); _host = g.AddComponent<FrameWatch>(); }
            if (_rec != null) _host.StopCoroutine(_rec);
            _recording = true;
            _rec = _host.StartCoroutine(Recorder());
        }

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator Interact1_Video_And_Screenshots()
        {
            _frames = System.Environment.GetCommandLineArgs().Contains("-fmCapture");
            Setup();
            if (_frames) Time.captureFramerate = 30;

            // ------------------------------------------------------------ 1. tutorial on the practice dummy
            TutorialDirector.Done = false;
            TutorialDirector.AutoStart = true;
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training("tq"));
            AudioListener.volume = 0f;
            D.testMode = true;
            yield return Hold(1f);
            var tut = TutorialDirector.Instance;
            Assert.IsNotNull(tut, "tutorial");
            run.head = new GameObject("~CamHead").transform;
            run.headFollowsCamera = true;
            D.head = run.head;
            Vector3 dummy = tut.Root.position;
            Vector3 toward = tut.Root.forward;   // the dummy faces the player
            Vector3 side = Vector3.Cross(Vector3.up, toward);
            Aim(dummy + toward * 1.5f + side * 0.5f + Vector3.up * 1.45f, dummy + Vector3.up * 0.3f, true);
            run.MakeBot();
            var bot = run.bot;
            bot.handSpeed = 0.8f;
            StartRec();
            Mark("tutorial");
            yield return Hold(1.5f);
            Shot("01_tutorial_dummy");
            yield return bot.Grab(bot.right, tut.Can);
            yield return bot.Move(bot.right, bot.right.PalmPosition + Vector3.up * 0.3f, false, 0.5f);
            yield return Hold(1.0f);
            yield return bot.Move(bot.right, bot.right.PalmPosition - Vector3.up * 0.26f, false, 0.5f);
            yield return bot.Release(bot.right);
            yield return bot.Grab(bot.right, tut.Strap, tut.Strap.transform.position);
            yield return bot.Move(bot.right, bot.right.PalmPosition - tut.Crate.transform.forward * 0.18f, false, 0.3f);
            yield return bot.Release(bot.right);
            yield return Hold(0.8f);
            yield return bot.Grab(bot.right, tut.Rod, tut.Rod.transform.position);
            yield return bot.Twist(bot.right, tut.Crate.transform.up, 200f, 220f);
            yield return bot.Release(bot.right);
            yield return Hold(0.8f);
            yield return bot.Grab(bot.left, tut.Tag);
            yield return bot.Move(bot.left, bot.left.PalmPosition + Vector3.up * 0.2f, false, 0.5f);
            yield return Hold(0.8f);
            yield return bot.Release(bot.left);
            var pts = tut.Crate.GrabPoints;
            yield return bot.Grab(bot.left, tut.Crate, pts[0].transform.position);
            yield return bot.Grab(bot.right, tut.Crate, pts[1].transform.position);
            Shot("02_tutorial_two_hand_drag");
            Vector3 l0 = bot.left.PalmPosition, r0 = bot.right.PalmPosition;
            for (float t = 0f; t < 2.4f; t += Time.deltaTime)
            {
                bot.SetPalm(bot.left, l0 + toward * (0.6f * t) + Vector3.up * 0.06f);
                bot.SetPalm(bot.right, r0 + toward * (0.6f * t) + Vector3.up * 0.06f);
                yield return null;
            }
            yield return bot.Release(bot.left);
            yield return bot.Release(bot.right);
            for (int k = 0; k < 90 && T.Progress == null; k++) yield return null;
            Assert.IsNotNull(T.Progress, "tq lesson after the tutorial");

            // ------------------------------------------------------------ 2. CAT twist: flinch + wrist grab
            Mark("pain");
            yield return Hold(0.6f);
            run.KitNear();
            Vector3 hips = run.Hips, chest = run.Chest;
            var rig = C.rig;
            Aim(hips + rig.BodyRight * -1.5f + rig.BodyUp * 0.2f + Vector3.up * 1.2f, Vector3.Lerp(hips, chest, 0.4f), true);
            yield return run.HandsTo(hips);
            bool shotGrab = false;
            var watch = new GameObject("~grabwatch").AddComponent<FrameWatch>();
            watch.onFrame = () =>
            {
                if (!shotGrab && C.Interaction != null && C.Interaction.GrabbingPlayer && Time.time > 0f)
                {
                    shotGrab = true;
                    _host.StartCoroutine(ShotLater("04_pain_wrist_grab", 0.35f));
                }
            };
            yield return bot.ApplyTourniquet(run.Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 0.12f, 4, null);
            yield return Hold(1.5f);
            Object.Destroy(watch.gameObject);
            Shot("05_cat_tightened");

            // ------------------------------------------------------------ 3. the assessment lesson
            _recording = false;
            TutorialDirector.Done = true;
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training("assess"));
            AudioListener.volume = 0f;
            D.testMode = true;
            yield return Hold(1.2f);
            run.head = new GameObject("~CamHead2").transform;
            run.headFollowsCamera = true;
            D.head = run.head;
            C.Checks.head = run.head;
            run.KitNear();
            run.MakeBot();
            bot = run.bot;
            rig = C.rig;
            var face = C.Face;
            var headBone = rig.Bone(HumanBodyBones.Head);
            Vector3 faceFwd = face.FaceForward;
            Vector3 facePos = headBone.position + rig.BodyUp * 0.06f;
            Aim(facePos + faceFwd * 0.55f + rig.BodyRight * 0.25f + Vector3.up * 0.05f, facePos, true);
            StartRec();
            Mark("voice");
            yield return Hold(1.5f);
            Shot("03a_face_drowsy");
            PlayerVoice.Override = 0.9f;                       // "Ерлан, слышишь меня?"
            yield return Hold(0.9f);
            PlayerVoice.Override = 0f;
            yield return Hold(1.3f);
            Shot("03_voice_eyes_open");
            yield return Hold(1.5f);

            // pulse at the neck, then the wrist
            Mark("pulse");
            Vector3 neck = HandTreatment.PulsePoint(rig, PulseSite.Carotid);
            Aim(neck + faceFwd * 0.25f + rig.BodyRight * 0.35f + Vector3.up * 0.25f, neck, false, 0.5f);
            yield return run.HandsTo(run.Hips);
            yield return bot.CheckPulse(C, PulseSite.Carotid, 3.4f, bot.left);
            Shot("06_pulse_neck_label");
            Vector3 wrist = HandTreatment.PulsePoint(rig, PulseSite.RadialR);
            Aim(wrist + Vector3.up * 0.45f + rig.BodyRight * 0.3f, wrist, false, 0.5f);
            yield return bot.CheckPulse(C, PulseSite.RadialR, 3.4f, bot.right);
            Shot("07_pulse_wrist_label");

            // breathing: the ear at his mouth (first-person view)
            Mark("breath");
            run.headFollowsCamera = false;
            Vector3 mouth = face.MouthPosition;
            Aim(mouth + faceFwd * 0.16f + Vector3.up * 0.04f, rig.ChestBone.position, false, 0.5f);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { run.head.position = _camPos; yield return null; }
            for (float t = 0f; t < 4f; t += Time.deltaTime) { run.head.position = _camPos; yield return null; }
            Shot("08_breathing_check");
            run.headFollowsCamera = true;

            // capillary refill
            Mark("refill");
            var nail = face.Nails[Mathf.Min(4, face.Nails.Count - 1)];
            Aim(nail.Position + Vector3.up * 0.3f + rig.BodyRight * 0.12f + rig.BodyUp * -0.12f, nail.Position, false, 0.5f);
            yield return Hold(1f);
            _host.StartCoroutine(ShotLater("09_capillary_pressed", 1.3f));
            yield return bot.PressNail(nail, bot.left, 1.6f);
            yield return Hold(0.4f);
            Shot("10_capillary_release_white");
            for (float t = 0f; t < 5f && C.Checks.LastRefillSeconds < 0f; t += Time.deltaTime) yield return null;
            yield return Hold(0.8f);

            // shock colours: blood loss ramps up
            Mark("shock");
            Aim(facePos + faceFwd * 0.5f - rig.BodyRight * 0.2f + Vector3.up * 0.05f, facePos, false, 0.6f);
            yield return Hold(1f);
            T.maxLossFraction = 0.6f;
            C.Patient.InsertAirwayAdjunct();
            float l0f = C.Patient.LossFraction;
            Shot("11_shock_before");
            for (float t = 0f; t < 7f; t += Time.deltaTime)
            {
                C.Patient.bloodLostMl = C.Patient.BloodVolumeMl * Mathf.Lerp(l0f, 0.45f, t / 7f);
                yield return null;
            }
            yield return Hold(1.5f);
            Shot("12_shock_grey_blue_lips");

            // physical hand: the glove stops on his chest, the ghost shows the tracked hand
            Mark("ghost");
            Vector3 cs = rig.ChestBone.position;
            Aim(cs + rig.BodyRight * 0.6f + Vector3.up * 0.45f, cs, false, 0.5f);
            yield return bot.Move(bot.right, cs + Vector3.up * 0.25f, false, 1.2f);
            yield return bot.Move(bot.right, cs - Vector3.up * 0.05f, false, 0.3f);
            yield return Hold(1.2f);
            Shot("13_physical_hand_ghost");
            yield return bot.Move(bot.right, cs + Vector3.up * 0.3f, false, 0.8f);
            yield return Hold(1f);
            Mark("end");
            _recording = false;
            Time.captureFramerate = 0;
            Debug.Log($"[FMVR] w8 video: {_frame} frames ({_frame / 30f:F1} s) in {_dir}");
            if (_frames) Assert.Greater(_frame, 30 * 50, "about a minute or more of video");
        }

        IEnumerator ShotLater(string name, float s)
        {
            for (float t = 0f; t < s; t += Time.deltaTime) yield return null;
            Shot(name);
        }
    }
}
