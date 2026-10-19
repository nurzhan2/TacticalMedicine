using System;
using System.Collections;
using System.IO;
using System.Linq;
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
    /// w7 backup material for the presentation: the demo flow (<see cref="DemoFlowRunner"/>) filmed by a steady
    /// third-person camera at a fixed 30 fps game clock (Time.captureFramerate) → JPG frames in
    /// %TEMP%\fmvr_video (tools/make_demo_video.py turns them into the MP4), key-moment screenshots (docs/screens/w7_*.jpg)
    /// and the Kazakhstan flag from the front and the back with a pixel check (ornament next to the pole).
    /// Run: -testFilter FieldMedic.Tests.DemoCapture (GPU, no -nographics). Frames only with -fmCapture on the command line.
    /// </summary>
    [Category("Screenshots")]
    public class DemoCapture
    {
        GlobalStateGuard _guard;
        DemoFlowRunner _run;
        int _quality;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _tex;
        string _frameDir;
        int _frame;
        bool _recording;
        string _mode = "follow";
        Vector3 _camPos, _camVel, _look, _lookVel;

        const string Dir = "docs/screens";
        const int VW = 1920, VH = 1080;

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
            Time.captureFramerate = 0;
            QualitySettings.SetQualityLevel(_quality, true);
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            GameSettings.HoldTimeScale = 1f;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            SmokeScreens.Clear();
            if (_run.head != null) Object.Destroy(_run.head.gameObject);
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) Object.Destroy(_rt);
            if (_tex != null) Object.Destroy(_tex);
        }

        static Texture2D Shot(string name, Vector3 from, Vector3 look, float fov)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 450f;
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
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, "w7_" + name + ".jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot w7_" + name);
            return tex;
        }

        // ------------------------------------------------------------------ flag front/back + pixel check
        static bool Yellow(Color c) => c.r > 0.5f && c.g > 0.42f && c.b < 0.36f && c.r > c.b * 1.6f;
        static bool Blue(Color c) => c.b > 0.35f && c.b > c.r * 1.5f && c.g > c.r * 1.2f;

        /// <summary>Normalised x of the yellow centroid inside the blue flag's bounding box (0 = left edge).</summary>
        static float OrnamentSide(Texture2D t, out int blue)
        {
            var px = t.GetPixels();
            int w = t.width, h = t.height, minX = w, maxX = -1;
            blue = 0;
            for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
                if (Blue(px[y * w + x])) { blue++; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            if (maxX <= minX) return 0.5f;
            double sx = 0; int n = 0;
            for (int y = 0; y < h; y += 2)
            for (int x = minX; x <= maxX; x += 2)
                if (Yellow(px[y * w + x])) { sx += x; n++; }
            return n == 0 ? 0.5f : (float)((sx / n - minX) / (maxX - minX));
        }

        [UnityTest]
        public IEnumerator Flag_FrontAndBack_Screenshots_OrnamentAtThePole()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Menu());
            AudioListener.volume = 0f;
            yield return DemoFlowRunner.Seconds(1f);
            var cloth = GameObject.Find("KZ_Flag/Cloth");
            Assert.IsNotNull(cloth, "flag cloth");
            var mr = cloth.GetComponent<MeshRenderer>();
            var flagMat = mr.sharedMaterial;
            float amp = flagMat.GetFloat("_Amplitude");
            flagMat.SetFloat("_Amplitude", 0.03f); // calm frame for the pixel check
            var ct = cloth.transform;
            Vector3 centre = ct.TransformPoint(new Vector3(0.9f, 0.45f, 0f));
            // front: pole on the left → camera on the cloth's −Z side; back: +Z side (the ornament then shows on the right)
            var front = Shot("flag_front", centre - ct.forward * 4.2f - Vector3.up * 1.2f, centre, 34f);
            var back = Shot("flag_back", centre + ct.forward * 4.2f - Vector3.up * 1.2f, centre, 34f);
            Shot("flag_wide", centre - ct.forward * 9f + ct.right * 3f - Vector3.up * 3.4f, centre - Vector3.up * 1.5f, 50f);
            flagMat.SetFloat("_Amplitude", amp);
            float fx = OrnamentSide(front, out int bf), bx = OrnamentSide(back, out int bb);
            Debug.Log($"[FMVR] flag screenshot check: yellow centroid front {fx:F2} (blue px {bf}), back {bx:F2} (blue px {bb})");
            Assert.Greater(bf, 2000, "flag visible in the front shot");
            Assert.Less(fx, 0.47f, "front: ornament (pole side) on the LEFT");
            Assert.Greater(bx, 0.53f, "back: seen through, the ornament is on the right");
            // the emblem: the solid sun is above the eagle → yellow in the upper half outweighs the lower half
            var px = front.GetPixels(); int w = front.width, h = front.height, minY = h, maxY = -1;
            for (int y = 0; y < h; y += 2) for (int x = 0; x < w; x += 2) if (Blue(px[y * w + x])) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            Object.Destroy(front); Object.Destroy(back);
            Assert.Greater(maxY, minY);
        }

        // ------------------------------------------------------------------ video
        void SetupVideo()
        {
            _frameDir = Path.Combine(Path.GetTempPath(), "fmvr_video");
            if (Directory.Exists(_frameDir)) Directory.Delete(_frameDir, true);
            Directory.CreateDirectory(_frameDir);
            var go = new GameObject("~VideoCam");
            Object.DontDestroyOnLoad(go);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.fieldOfView = 58f;
            _cam.nearClipPlane = 0.03f;
            _cam.farClipPlane = 450f;
            _cam.enabled = false;
            _rt = new RenderTexture(VW, VH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _rt.Create();
            _tex = new Texture2D(VW, VH, TextureFormat.RGB24, false);
        }

        bool _frames;

        void Frame()
        {
            if (!_recording || _cam == null || !_frames) return;
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_cam, req);
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, VW, VH), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_frameDir, $"f{_frame++:00000}.jpg"), _tex.EncodeToJPG(90));
        }

        /// <summary>Steady camera: ~1.7 m beside Erlan, eye height, smoothed; the hint panel billboards to it.</summary>
        void UpdateCamera(float dt, bool snap = false)
        {
            if (_cam == null) return;
            var D = ScenarioDirector.Instance;
            if (D == null || D.casualty == null || D.casualty.rig == null) return;
            Vector3 hips = _run.Hips, chest = _run.Chest;
            Vector3 wantPos, wantLook;
            var tac = TacevacController.Instance;
            if (_mode == "heli" && tac != null)
            {
                wantPos = Vector3.Lerp(chest, BattlefieldLayout.LandingZone, 0.2f) + Vector3.up * 1.6f;
                wantLook = (tac.heli != null ? tac.heli.position : BattlefieldLayout.LandingZone) + Vector3.up * 1.2f;
            }
            else if (_mode == "load" && tac != null && tac.Landed)
            {
                // w9: follow the drag to the side door from behind the player's shoulder
                Vector3 door = tac.DoorPoint;
                Vector3 back = chest - door; back.y = 0f;
                if (back.sqrMagnitude < 0.01f) back = Vector3.right;
                wantPos = chest + back.normalized * 1.8f + Vector3.Cross(Vector3.up, back.normalized) * 1.2f + Vector3.up * 1.4f;
                wantLook = Vector3.Lerp(chest, door + Vector3.up * 1.0f, 0.4f);
            }
            else if (_mode == "debrief")
            {
                var board = Object.FindAnyObjectByType<MenuBoard>();
                bool on = board != null && board.isActiveAndEnabled;
                wantPos = on ? board.transform.position - board.transform.forward * 1.2f : _cam.transform.position;
                wantLook = on ? board.transform.position : chest;
            }
            else
            {
                Vector3 toHead = D.playerRig != null ? D.playerRig.position - hips : Vector3.right;
                if (_mode == "drag") { toHead = _run.C.Interaction.Drag.Velocity.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, _run.C.Interaction.Drag.Velocity) : toHead; }
                toHead.y = 0f;
                if (toHead.sqrMagnitude < 0.01f) toHead = Vector3.right;
                Vector3 side = Quaternion.Euler(0f, 20f, 0f) * toHead.normalized;
                float dist = _mode == "drag" ? 2.8f : 1.3f;
                wantPos = new Vector3(hips.x, D.casualty.transform.position.y + (_mode == "drag" ? 1.9f : 1.6f), hips.z) + side * dist;
                wantLook = Vector3.Lerp(hips, chest, 0.6f) + Vector3.up * (_mode == "drag" ? 0.1f : 0.32f);
            }
            if (snap) { _camPos = wantPos; _look = wantLook; _camVel = _lookVel = Vector3.zero; }
            _camPos = Vector3.SmoothDamp(_camPos, wantPos, ref _camVel, 0.9f, 3f, dt);
            _look = Vector3.SmoothDamp(_look, wantLook, ref _lookVel, 0.5f, 6f, dt);
            _cam.transform.SetPositionAndRotation(_camPos, Quaternion.LookRotation(_look - _camPos, Vector3.up));
            if (_run.head != null && _mode != "load") _run.head.position = _camPos;
        }

        IEnumerator Recorder()
        {
            while (_recording)
            {
                UpdateCamera(Time.deltaTime);
                yield return null;
                Frame();
            }
        }

        IEnumerator Hold(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        void Moment(string m)
        {
            Debug.Log($"[FMVR] demo video moment {m} at frame {_frame} ({_frame / 30f:F1} s)");
            File.AppendAllText(Path.Combine(_frameDir, "moments.txt"), $"{_frame} {m}\n");
            if (m == "drag") _mode = "follow";
            if (m == "tq") _mode = "drag";
            if (m == "heli_call") _mode = "heli";
            if (m == "heli") _mode = "load";
            if (m == "debrief") _mode = "debrief";
            // stills
            var D = ScenarioDirector.Instance;
            Vector3 hips = _run.Hips, chest = _run.Chest;
            switch (m)
            {
                case "open": Shot("01_erlan_in_the_open", hips + new Vector3(-4.5f, 2.2f, -3.5f), hips, 50f); break;
                case "hint":
                    var tdp = TrainingDirector.Instance != null ? TrainingDirector.Instance.Panel : null;
                    Vector3 pp = tdp != null ? tdp.transform.position : chest + Vector3.up * 0.6f;
                    Vector3 away = _camPos - pp; away.y = 0f;
                    Shot("02_hint_panel", pp + away.normalized * 1.5f + Vector3.up * 0.25f, Vector3.Lerp(pp, hips, 0.4f), 60f); break;
                case "ifak": Shot("03_ifak_open", _run.Pouch.transform.position + new Vector3(-0.45f, 0.5f, -0.45f), _run.Pouch.transform.position, 50f); break;
                case "tq":
                    Vector3 tqp = LimbMath.PointAt(D.casualty.rig, Limb.RightLeg, 0.12f);
                    Shot("04_cat_locked", tqp + Vector3.up * 0.62f + D.casualty.rig.BodyRight * 0.32f - D.casualty.rig.BodyUp * 0.2f, tqp, 50f); break;
                case "drag": Shot("05_dragged_into_ruins", hips + new Vector3(2.4f, 1.8f, -2.2f), hips, 55f); break;
                case "pack": Shot("06_wound_packed", _camPos, _look, 55f); break;
                case "seal": Shot("07_chest_seal", chest + D.casualty.rig.BodyForward * 0.6f + Vector3.up * 0.3f + D.casualty.rig.BodyRight * 0.3f, chest, 45f); break;
                case "card": Shot("08_blanket_card", hips + Vector3.up * 1.8f + D.casualty.rig.BodyRight * 1.2f, Vector3.Lerp(hips, chest, 0.5f), 55f); break;
                case "heli": Shot("09_tacevac_heli", _camPos, _look, 58f); break;
                case "debrief": break;
            }
        }

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator Demo_Video_And_Screenshots()
        {
            bool frames = System.Environment.GetCommandLineArgs().Contains("-fmCapture");
            _frames = frames;
            // menu with the big ДЕМО button
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Menu());
            AudioListener.volume = 0f;
            yield return DemoFlowRunner.Seconds(1f);
            var board = Object.FindAnyObjectByType<MenuBoard>();
            Assert.IsNotNull(board);
            board.ShowMain();
            yield return null;
            var demo = board.GetComponentsInChildren<RoundButton>(true).First(b => b.name == "Btn_DEMO");
            Vector3 bp = board.transform.position, bn = -board.transform.forward;
            Shot("00_menu_demo_button", bp + bn * 1.25f + Vector3.up * 0.05f, bp, 55f);
            SetupVideo();
            if (frames)
            {
                Time.captureFramerate = 30;
                _recording = true;
                _camPos = bp + bn * 1.3f; _look = bp;
                _cam.transform.SetPositionAndRotation(_camPos, Quaternion.LookRotation(bp - _camPos));
                for (int i = 0; i < 75; i++) { yield return null; Frame(); }
                // zoom toward ДЕМО
                for (int i = 0; i < 30; i++)
                {
                    _cam.transform.position = Vector3.Lerp(bp + bn * 1.3f, demo.transform.position + bn * 0.55f, i / 30f);
                    _cam.transform.rotation = Quaternion.LookRotation(Vector3.Lerp(bp, demo.transform.position, i / 30f) - _cam.transform.position);
                    yield return null; Frame();
                }
                _recording = false;
            }

            // the demo itself
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(TrainingCatalog.DemoId));
            AudioListener.volume = 0f;
            Assert.IsNotNull(ScenarioDirector.Instance);
            _run.headFollowsCamera = true;
            _run.head = new GameObject("~CamHead").transform;
            _run.moment = Moment;
            _mode = "follow";
            UpdateCamera(0f, true);
            if (frames) { Time.captureFramerate = 30; _recording = true; }
            var rec = new GameObject("~Rec").AddComponent<FieldMedic.Scenarios.Bots.MedicBot>();
            _recording = true; rec.StartCoroutine(Recorder()); if (!frames) Debug.Log("[FMVR] demo capture without frames (no -fmCapture)");
            yield return _run.Run();
            yield return Hold(1.5f);
            // the debrief board comes to the viewer
            _mode = "debrief";
            var b2 = Object.FindAnyObjectByType<MenuBoard>();
            if (b2 != null)
            {
                Vector3 p = b2.transform.position, n = -b2.transform.forward;
                _camPos = p + n * 1.15f; _look = p;
                Shot("10_debrief", p + n * 1.1f, p, 55f);
            }
            yield return Hold(5f);
            _recording = false;
            Time.captureFramerate = 0;
            Debug.Log($"[FMVR] demo video: {_frame} frames ({_frame / 30f:F1} s) in {_frameDir}");
            if (frames) Assert.Greater(_frame, 30 * 60, "at least a minute of video");
        }

    }
}
