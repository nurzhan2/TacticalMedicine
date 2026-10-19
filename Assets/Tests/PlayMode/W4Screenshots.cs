using System;
using System.Collections;
using System.IO;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FieldMedic.Tests
{
    /// <summary>
    /// docs/screens/v2_w4_*.jpg (play mode, Quest pipeline, Vulkan editor device): the white hint card in its states
    /// (step, done, wrong, Kazakh), the full TCCC run in the open with the ghost glove and glows, the wrist display, the
    /// TACEVAC helicopter + litter, the menu pages, the debrief with the mini map, and the battlefield re-shoots for the
    /// supervisor notes (two-storey ruins + bombed block, infantry from our line, smoke columns, cover view).
    /// Run explicitly: -testFilter FieldMedic.Tests.W4Screenshots (GPU needed, no -nographics).
    /// </summary>
    [Category("Screenshots")]
    public class W4Screenshots
    {
        GlobalStateGuard _guard;
        Transform _head;
        MedicBot _bot;
        int _quality;

        [SetUp]
        public void SetUp()
        {
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            _quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true); // the Quest pipeline asset
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.SetQualityLevel(_quality, true);
            _guard?.Restore();
            ScenarioLaunch.Pending = null;
            GameSettings.Language = GameLanguage.RU;
            GameSettings.HoldTimeScale = 1f;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            SmokeScreens.Clear();
            if (_head != null) Object.Destroy(_head.gameObject);
            foreach (var d in ScenarioCatalog.All) PlayerPrefs.DeleteKey("fm_best_" + d.id);
        }

        const string Dir = "docs/screens";
        static IEnumerator Seconds(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        static void Shot(string name, Vector3 from, Vector3 look, float fov, Vector3? up = null)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 450f;
            cam.allowMSAA = true;
            cam.enabled = false;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from, up ?? Vector3.up));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req); // warm-up
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }

        IEnumerator Load(Action request)
        {
            request();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
        }

        ScenarioDirector D => ScenarioDirector.Instance;
        CasualtyController C => D.casualty;
        Vector3 Hips => C.rig.Bone(HumanBodyBones.Hips).position;
        Vector3 Chest => C.rig.ChestBone.position;
        static Vector3 G(float x, float z, float up = 0f) => new Vector3(x, BattlefieldLayout.GroundHeight(x, z) + up, z);

        void TakeHead(Vector3 at)
        {
            if (_head == null) _head = new GameObject("~ShotHead").transform;
            _head.position = at;
            D.head = _head;
        }

        Vector3 HeadAt(float dist, float height = 1.6f, float sideDeg = 0f)
        {
            Vector3 dir = D.playerRig.position - Hips; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
            dir = Quaternion.Euler(0f, sideDeg, 0f) * dir.normalized;
            return new Vector3(Hips.x, C.transform.position.y + height, Hips.z) + dir * dist;
        }

        IfakPouch Pouch => Object.FindObjectsByType<IfakPouch>(FindObjectsSortMode.None).First(p => C.Interaction == null || p != C.Interaction.Ifak);

        void KitNear()
        {
            var p = Pouch;
            p.bodyLocked = false;
            p.transform.position = Hips + new Vector3(0.7f, 0.1f, 0.3f);
            Physics.SyncTransforms();
        }

        void MakeBot()
        {
            var hands = HandGrabber.All.Where(h => h != null).ToList();
            var l = hands.FirstOrDefault(h => h.name.Contains("_L")) ?? hands[0];
            var r = hands.FirstOrDefault(h => h != l) ?? hands[1];
            _bot = new GameObject("~Bot").AddComponent<MedicBot>();
            _bot.Init(l, r);
        }

        void HideRigVisuals()
        {
            // the screenshot is taken from the trainee's eyes: the desktop-sim hands are elsewhere
            foreach (var g in Object.FindObjectsByType<FieldMedic.Input.GloveHand>(FindObjectsSortMode.None))
                if (g.name != "Glove") g.gameObject.SetActive(false);
        }

        /// <summary>Camera at the trainee's eyes, looking between the card and the wound.</summary>
        void CardShot(string name, float fov = 72f)
        {
            var t = TrainingDirector.Instance;
            var site = C.WoundSites.First(s => s != null);
            Vector3 look = Vector3.Lerp(t.Panel.transform.position, site.transform.position, 0.45f);
            Shot(name, _head.position, look, fov);
        }

        // ================================================================== hint card states
        [UnityTest]
        public IEnumerator Capture_HintCard_States()
        {
            yield return Load(() => ScenarioLaunch.Training("tq"));
            yield return Seconds(2f);
            TakeHead(HeadAt(1.25f, 1.55f, 25f));
            KitNear();
            yield return Seconds(1.2f);
            CardShot("v2_w4_hint_panel");
            GameSettings.Language = GameLanguage.KZ;
            yield return Seconds(0.3f);
            CardShot("v2_w4_hint_panel_kz");
            GameSettings.Language = GameLanguage.RU;
            yield return Seconds(0.3f);
            MakeBot();
            yield return _bot.Move(_bot.left, Hips + new Vector3(0.35f, 0.45f, -0.12f), false, 6f);
            yield return _bot.Move(_bot.right, Hips + new Vector3(0.35f, 0.45f, 0.12f), false, 6f);
            var t = TrainingDirector.Instance;
            yield return _bot.Grab(_bot.right, Pouch.Find<TourniquetCAT>());
            float t0 = Time.time;
            while (t.Panel.CurrentMood != HintPanel.Mood.Done && Time.time - t0 < 2f) yield return null;
            yield return Seconds(0.4f);
            CardShot("v2_w4_hint_done");
            yield return Seconds(1.2f);
            yield return _bot.Release(_bot.right);
            yield return _bot.ApplyTourniquet(Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 1.0f, 4, null);
            yield return Seconds(0.8f);
            CardShot("v2_w4_hint_wrong");
        }

        [UnityTest]
        public IEnumerator Capture_FullTccc_InTheOpen_Ghost_Wrist_Tacevac()
        {
            yield return Load(() => ScenarioLaunch.Training("full"));
            yield return Seconds(3f);
            var t = TrainingDirector.Instance;
            // the trainee reaches Erlan in the open: card "under fire", ghost glove from the CAT to the thigh, glows
            TakeHead(HeadAt(1.3f, 1.25f, -20f));
            KitNear();
            yield return Seconds(2.5f);
            Assert.AreEqual("tq_lock", t.Progress.Step.id);
            yield return Seconds(0.9f); // ghost glove mid-motion
            Vector3 look = Vector3.Lerp(t.Panel.transform.position, Hips, 0.55f);
            Shot("v2_w4_full_cuf", _head.position, look, 78f);
            // wrist display close-up
            MakeBot();
            var watch = Object.FindAnyObjectByType<WristWatch>();
            yield return _bot.Move(_bot.left, _head.position + Vector3.down * 0.35f + (Hips - _head.position).normalized * 0.32f, false, 6f);
            yield return Seconds(0.3f);
            var wt = watch.transform;
            Shot("v2_w4_wrist", wt.position - wt.forward * 0.16f + wt.up * 0.02f, wt.position + wt.up * -0.02f, 50f, wt.up);
            // TACEVAC: the helicopter comes in over the steppe and lands at the LZ
            var tac = TacevacController.Instance;
            Assert.IsNotNull(tac);
            tac.approachSeconds = 12f;
            tac.Call();
            yield return Seconds(9.5f);
            var lz = BattlefieldLayout.LandingZone;
            Shot("v2_w4_tacevac_approach", G(-1.5f, -6f, 1.7f), tac.heli.position + Vector3.down * 3f, 70f);
            yield return Seconds(3.5f);
            Assert.IsTrue(tac.Landed);
            Shot("v2_w4_tacevac_lz", G(lz.x + 9f, lz.z + 13f, 2.2f), lz + Vector3.up * 2f, 62f);
            // w9: no litter — the open side door the medic drags Erlan to
            var door = tac.DoorPoint;
            Vector3 side = door - tac.heli.position; side.y = 0f;
            Shot("v2_w4_tacevac_door", door + side.normalized * 3.2f + Vector3.up * 1.5f + tac.heli.forward * 1.5f, door + Vector3.up * 1.1f, 68f);
        }

        // ================================================================== menu + debrief
        void BoardShot(MenuBoard b, string name)
        {
            var t = b.transform;
            float s = t.lossyScale.x;
            Shot(name, t.position - t.forward * 1.25f * s + Vector3.up * 0.05f, t.position, 62f);
        }

        [UnityTest]
        public IEnumerator Capture_Menu_And_Debrief()
        {
            yield return Load(() => ScenarioLaunch.Menu());
            yield return Seconds(1f);
            var b = D.board;
            b.ShowMain();
            yield return Seconds(0.3f);
            BoardShot(b, "v2_w4_menu");
            b.ShowTraining();
            yield return Seconds(0.3f);
            BoardShot(b, "v2_w4_menu_training");

            // a Combat run with mistakes → debrief with the map markers and the timeline
            yield return Load(() => ScenarioLaunch.Scenario("S1", ScenarioVariant.Default));
            D.testMode = true;
            BattleDirector.PlayerArmed = false;
            float t0 = Time.time;
            while (D.State != ScenarioState.CareUnderFire && Time.time - t0 < 25f) yield return null;
            while (C.IsFalling && Time.time - t0 < 30f) yield return null;
            yield return Seconds(1.5f);
            TakeHead(HeadAt(1.2f));
            KitNear();
            MakeBot();
            yield return _bot.Move(_bot.right, Hips + new Vector3(0.35f, 0.45f, 0.12f), false, 6f);
            // the classic mistake: the band on the knee; then too slow, no time mark, no sweep
            yield return _bot.ApplyTourniquet(C.Interaction.Ifak != null ? C.Interaction.Ifak.Find<TourniquetCAT>() : Pouch.Find<TourniquetCAT>(), C, Limb.RightLeg, 1.0f, 4, null);
            C.simulationSpeed = 30f;
            yield return Seconds(4f);
            C.simulationSpeed = 1f;
            D.EndNow("evacuated");
            t0 = Time.time;
            while (D.Report == null && Time.time - t0 < 5f) yield return null;
            yield return Seconds(0.5f);
            Assert.IsNotNull(D.Report);
            Debug.Log("[FMVR] w4 debrief shot: mistakes " + string.Join(",", D.Report.mistakes.Select(m => m.number + ":" + m.code + (m.hasPos ? "@" : ""))));
            BoardShot(D.board, "v2_w4_debrief");
        }

        // ================================================================== battlefield re-shoots (supervisor notes)
        [UnityTest]
        public IEnumerator Capture_Battlefield_Reshoots()
        {
            yield return Load(() => ScenarioLaunch.Menu());
            BattleDirector.Mode = BattleMode.Combat;
            var bd = BattleDirector.Instance;
            var fx = BattleFx.Instance;
            if (bd != null) bd.autoRun = false;
            if (D.board != null) D.board.gameObject.SetActive(false);
            HideRigVisuals();
            yield return Seconds(25f); // soldiers in position, vehicles moving, smoke columns grown

            // 1) overview (same framing as w3) — two-storey ruins, the bombed block, no fake horizon boxes
            fx.Blast(G(12f, 70f), 2.2f, true);
            yield return Seconds(0.3f);
            Shot("v2_w4_battlefield_overview", new Vector3(-24f, 16f, -22f), new Vector3(4f, 0f, 48f), 62f);
            // 2) the ruins from the open field: house A (two storeys) with the apartment block behind
            Shot("v2_w4_battlefield_ruins", G(9f, 16f, 1.7f), G(-8f, -8f, 3.5f), 66f);
            // 3) the right flank: farmhouse, burnt APC
            Shot("v2_w4_battlefield_flank", G(4f, -3f, 1.7f), G(24f, -13f, 2f), 66f);

            // 4) infantry from our line: behind a friendly firing over the sandbags, the enemy line beyond
            var inf = NearInfantry.Instance;
            bd.Fire(BattleEventType.MgBurst);
            var friend = inf.friendlies.Where(a => a != null && a.Alive).OrderBy(a => Vector3.Distance(a.transform.position, Vector3.zero)).FirstOrDefault();
            for (int k = 0; k < 180 && friend != null && friend.BurstRoundsLeft <= 0; k++) yield return null;
            var enemies = inf.enemies.Where(a => a != null && a.Alive).ToList();
            Vector3 enemyCentre = enemies.Count > 0 ? enemies.Aggregate(Vector3.zero, (s, a) => s + a.transform.position) / enemies.Count : G(0f, 60f);
            Debug.Log($"[FMVR] w4 infantry: {enemies.Count} enemies alive, centre {enemyCentre}, friendly {(friend != null ? friend.transform.position.ToString() : "none")}");
            if (friend != null)
            {
                Vector3 fp = friend.transform.position;
                Vector3 toE = enemyCentre - fp; toE.y = 0f;
                Vector3 eye = fp - toE.normalized * 2.2f + Vector3.Cross(Vector3.up, toE.normalized) * 0.9f + Vector3.up * 1.75f;
                Shot("v2_w4_battlefield_infantry", eye, enemyCentre + Vector3.up * 1.2f, 58f);
            }
            // 4b) the enemy line close up (telephoto from our line) with tracers
            bd.Fire(BattleEventType.MgBurst);
            yield return Seconds(0.6f);
            Shot("v2_w4_battlefield_enemy", new Vector3(-1f, 1.9f, 6f), enemyCentre + Vector3.up * 1.0f, 24f);

            // 5) smoke columns (soft continuous sheets) over the front
            Shot("v2_w4_battlefield_smoke", new Vector3(0f, 2.2f, 6f), new Vector3(-20f, 30f, 160f), 64f);

            // 6) kneeling behind the sandbags (cover view)
            var cp = Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None).Where(p => p.cover != null && p.cover.name.StartsWith("concrete_blockade")).OrderBy(p => p.name).FirstOrDefault();
            if (cp != null)
            {
                Vector3 e = cp.HeadPosition + cp.threatDirection.normalized * -0.25f + Vector3.up * 0.45f;
                fx.Blast(G(cp.transform.position.x - 9f, cp.transform.position.z + 34f), 1.2f);
                yield return Seconds(0.2f);
                Shot("v2_w4_battlefield_cover_view", e, e + cp.threatDirection.normalized * 20f + Vector3.down * 2.2f, 80f);
            }
            // 7) inside house A: the treatment corner under the broken first-floor slab
            Shot("v2_w4_battlefield_inside", new Vector3(1.2f, 1.6f, -1.2f), new Vector3(-4.5f, 1.6f, 3.2f), 78f);
        }
    }
}
