using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Battle;
using FieldMedic.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// docs/screens/v2_w3_*.jpg from the running battle (play mode, Quest pipeline + HDR capture): staged moments
    /// so every shot shows the war — the assault seen from house A's window, rounds smacking into the sandbags in
    /// front of a kneeling player, a shell landing in the open field, a jet bomb run with flares + a helicopter
    /// rocket salvo, the smoke screen over the drag line, a tank firing, and the overview. Also measures render
    /// counters with the battle running (docs/PERF_V2.md "w3"). Run explicitly:
    /// -testFilter FieldMedic.Tests.BattleScreenshots (GPU needed, no -nographics).
    /// </summary>
    [Category("Screenshots")]
    public class BattleScreenshots
    {
        GlobalStateGuard _guard;

        [SetUp] public void Save() => _guard = new GlobalStateGuard();

        [TearDown] public void Restore() { _guard?.Restore(); SmokeScreens.Clear(); BattleDirector.PlayerArmed = null; BattleDirector.Mode = BattleMode.Combat; }

        const string Dir = "docs/screens";

        static IEnumerator Seconds(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        /// <summary>Fresh camera (not a copy of the OVR eye camera — copying it brought back the editor-GPU
        /// speckle), HDR, warm-up render, then the capture.</summary>
        static bool NoMsaa;
        static int Median = 1;

        static void Shot(string name, Vector3 from, Vector3 look, float fov)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 450f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.enabled = false;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = NoMsaa ? 1 : 4 };
            if (NoMsaa) cam.allowMSAA = false;
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req); // warm-up
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var frames = new Color32[Median][];
            for (int f = 0; f < Median; f++)
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                frames[f] = tex.GetPixels32();
            }
            if (Median > 1)
            {
                // per-pixel median removes the random speckle the editor's Intel GPU adds (w2 finding)
                var outPx = new Color32[frames[0].Length];
                var r = new byte[Median]; var g = new byte[Median]; var b = new byte[Median];
                for (int i = 0; i < outPx.Length; i++)
                {
                    for (int f = 0; f < Median; f++) { r[f] = frames[f][i].r; g[f] = frames[f][i].g; b[f] = frames[f][i].b; }
                    System.Array.Sort(r); System.Array.Sort(g); System.Array.Sort(b);
                    outPx[i] = new Color32(r[Median / 2], g[Median / 2], b[Median / 2], 255);
                }
                tex.SetPixels32(outPx);
                tex.Apply();
            }
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }

        static Vector3 G(float x, float z, float up = 0f) => new Vector3(x, BattlefieldLayout.GroundHeight(x, z) + up, z);

        [UnityTest]
        public IEnumerator Capture_Battle()
        {
            int quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            bool hdr = rp != null && rp.supportsHDR;
            if (rp != null) rp.supportsHDR = true;
            try { yield return CaptureAll(); }
            finally
            {
                if (rp != null) rp.supportsHDR = hdr;
                QualitySettings.SetQualityLevel(quality, true);
            }
        }

        IEnumerator CaptureAll()
        {
            BattleDirector.Mode = BattleMode.Combat;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            AudioListener.volume = 0f;
            var d = BattleDirector.Instance;
            var fx = BattleFx.Instance;
            Assert.IsNotNull(d);
            d.autoRun = false; // staged moments; the layers keep running on their own
            yield return Seconds(25f); // soldiers in position, crowd advancing, vehicles moving
            // editor play mode initialises the XR loader (no headset): shaders then run their stereo variants and a
            // mono capture camera reads garbage (the speckle / ground noise seen only in play-mode captures)
            Median = 3; // per-pixel median of 3 renders: cuts the editor GPU's random speckle (w2 finding)

            // 1) assault seen over the sandbag line: enemies firing, crowd, tanks, a shell landing out front
            d.Fire(BattleEventType.MgBurst);
            yield return Seconds(1.2f);
            fx.Blast(G(-6f, 47f), 1.6f, true);
            yield return Seconds(0.12f);
            Shot("v2_w3_assault", new Vector3(-2.4f, 1.85f, 5.2f), new Vector3(2f, 1.2f, 50f), 70f);
            Shot("v2_w3_assault_window", new Vector3(0.33f, 1.72f, 3.55f), new Vector3(3f, 2.5f, 60f), 72f);

            // 1b) the enemy line close up (telephoto from our line): kneeling riflemen firing over crater lips
            var inf = NearInfantry.Instance;
            SoldierAgent target = null;
            for (float t = 0f; t < 8f && target == null; t += Time.deltaTime)
            {
                target = inf.enemies.Where(a => a != null && a.State == SoldierState.Kneel).OrderBy(a => a.transform.position.z).FirstOrDefault();
                yield return null;
            }
            if (target != null)
            {
                for (int k = 0; k < 120 && target.BurstRoundsLeft <= 0; k++) yield return null; // mid-burst: muzzle flash
                Vector3 tp = target.transform.position + Vector3.up * 1.0f;
                Shot("v2_w3_enemy_line", new Vector3(tp.x * 0.15f, 1.9f, 6f), tp, 18f);
            }

            // 2) kneeling behind the sandbags: rounds hit the cover in front of the player
            var cp = Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None).Where(p => p.cover != null && p.cover.name.StartsWith("concrete_blockade")).OrderBy(p => p.name).FirstOrDefault();
            if (cp != null)
            {
                var ef = EnemyFire.Instance;
                var head = new GameObject("~head").transform;
                head.position = cp.HeadPosition;
                ef.head = head;
                BattleDirector.PlayerArmed = true;
                var far = cp.HeadPosition + cp.threatDirection.normalized * 90f;
                ef.mgNests = new[] { G(far.x, far.z, 1.0f) };
                ef.useInfantry = false;
                int hits = ef.CoverHits;
                for (float t = 0f; t < 6f && ef.CoverHits < hits + 2; t += Time.deltaTime) yield return null;
                yield return Seconds(0.06f);
                Vector3 eye = cp.HeadPosition + cp.threatDirection.normalized * -0.25f + Vector3.up * 0.5f;
                fx.Blast(G(cp.transform.position.x - 9f, cp.transform.position.z + 34f), 1.2f);
                yield return Seconds(0.15f);
                Shot("v2_w3_cover", eye, eye + cp.threatDirection.normalized * 20f + Vector3.down * 2.2f, 80f);
                BattleDirector.PlayerArmed = false;
                Object.Destroy(head.gameObject);
            }

            // 3) the open ground where Erlan goes down: a shell lands 30 m ahead, the assault behind it
            var fall = BattlefieldLayout.ErlanFallPoint;
            fx.Blast(G(fall.x + 8f, fall.z + 26f), 1.4f);
            yield return Seconds(0.18f);
            Shot("v2_w3_open_field", G(fall.x - 1.5f, fall.z - 3f, 1.55f), G(fall.x + 5f, fall.z + 40f, 2f), 70f);

            // 4) the sky: a jet bomb run over the enemy lines with flares, a helicopter ripple-firing rockets
            var air = AirLayer.Instance;
            air.HeliRun(G(10f, 120f));
            air.JetRun(G(-20f, 110f));
            var skyEye = new Vector3(-1.5f, 1.7f, -1f);
            float waited = 0f;
            while (waited < 14f && air.jet != null && air.jet.gameObject.activeSelf && (Vector3.Distance(air.jet.position, skyEye) > 190f || air.jet.position.z < skyEye.z - 40f))
            { waited += Time.deltaTime; yield return null; }
            if (air.jet != null && air.jet.gameObject.activeSelf)
            {
                Vector3 jet = air.jet.position;
                Vector3 under = new Vector3(jet.x, 0f, jet.z);
                Shot("v2_w3_sky_jet", skyEye, Vector3.Lerp(under, jet, 0.5f), 84f);
            }
            else Debug.Log("[FMVR] jet not in view for the sky shot (skipped)");
            yield return Seconds(Mathf.Max(1f, 18.6f - waited));
            Vector3 heli = air.heli != null && air.heli.gameObject.activeSelf ? air.heli.position : G(30f, -30f, 35f);
            Shot("v2_w3_sky_heli", new Vector3(-1.5f, 1.7f, -6f), Vector3.Lerp(heli, G(10f, 120f), 0.25f), 78f);

            // 5) smoke screen thrown from the doorway toward Erlan to cover the drag
            var sg = Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None).OrderBy(x => x.name).First();
            var door = BattlefieldLayout.DoorwayCentre + new Vector3(0.4f, 1.4f, 0.2f);
            sg.transform.position = door;
            sg.ThrowNow((G(fall.x, fall.z - 4f, 1f) - door).normalized * 9f + Vector3.up * 3f);
            yield return Seconds(11f);
            Shot("v2_w3_smoke", new Vector3(-4.5f, 2.3f, 5.2f), new Vector3(5f, 1.5f, 11f), 72f);

            // 6) a tank firing: muzzle blast + dust
            var tank = VehicleLayer.Instance.vehicles.FirstOrDefault(v => v != null && !v.autocannon);
            if (tank != null)
            {
                int shots = tank.ShotsFired;
                tank.RequestShot(G(-17f, 7.4f, 1.5f));
                for (float t = 0f; t < 15f && tank.ShotsFired == shots; t += Time.deltaTime) yield return null;
                yield return Seconds(0.05f);
                Vector3 tp = tank.transform.position;
                Vector3 side = Vector3.Cross(Vector3.up, tank.transform.forward).normalized;
                Shot("v2_w3_tank", tp + side * 22f - tank.transform.forward * 8f + Vector3.up * 3f, tp + tank.transform.forward * 4f + Vector3.up * 1.5f, 55f);
            }

            // 7) overview: barrage across the field, everything at once
            d.Fire(BattleEventType.ArtilleryBarrage);
            fx.Blast(G(12f, 70f), 2.2f, true);
            fx.Blast(G(-25f, 85f), 1.8f, true);
            yield return Seconds(0.5f);
            fx.Blast(G(-8f, 58f), 1.5f);
            yield return Seconds(0.15f);
            Shot("v2_w3_overview", new Vector3(-24f, 16f, -22f), new Vector3(4f, 0f, 48f), 62f);
            Assert.Greater(fx.Detonations, 4);
        }

        /// <summary>Render counters with the battle running (Quest pipeline asset, 100° FOV, one eye 1440²).</summary>
        [UnityTest]
        public IEnumerator Profile_Battle_Render_Stats()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            int quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            AudioListener.volume = 0f;
            yield return Seconds(20f);
            var cam = Camera.main;
            var keepPos = cam.transform.localPosition; var keepRot = cam.transform.localRotation; float keepFov = cam.fieldOfView;
            var rt = new RenderTexture(1440, 1440, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.fieldOfView = 100f;
            var names = new[] { "Triangles Count", "Draw Calls Count", "Batches Count", "SetPass Calls Count" };
            var recs = names.Select(n => Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, n)).ToArray();
            var spots = new (string, Vector3)[]
            {
                ("player start (house A)", BattlefieldLayout.PlayerStart + Vector3.up * 1.6f),
                ("behind the sandbags (kneeling)", new Vector3(-3.6f, 1.1f, 5.8f)),
                ("Erlan in the open", BattlefieldLayout.ErlanFallPoint + Vector3.up * 1.6f),
                ("LZ", BattlefieldLayout.LandingZone + Vector3.up * 1.6f),
            };
            var sb = new StringBuilder();
            sb.AppendLine("| Spot | worst heading | triangles | draw calls | batches | SetPass |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var (name, eye) in spots)
            {
                long[] worst = new long[names.Length]; float worstYaw = 0f;
                for (int h = 0; h < 8; h++)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, h * 45f, 0f));
                    long[] peak = new long[names.Length];
                    for (int f = 0; f < 6; f++)
                    {
                        yield return null;
                        for (int k = 0; k < names.Length; k++) peak[k] = System.Math.Max(peak[k], recs[k].LastValue);
                    }
                    if (peak[0] > worst[0]) { worst = peak; worstYaw = h * 45f; }
                }
                sb.AppendLine($"| {name} | {worstYaw:F0}° | {worst[0]:N0} | {worst[1]} | {worst[2]} | {worst[3]} |");
            }
            foreach (var r in recs) r.Dispose();
            cam.targetTexture = null;
            cam.transform.localPosition = keepPos; cam.transform.localRotation = keepRot; cam.fieldOfView = keepFov;
            rt.Release();
            QualitySettings.SetQualityLevel(quality, true);
            string table = sb.ToString();
            Debug.Log("[FMVR] w3 render stats\n" + table);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/w3_render_stats.txt", table);
            Assert.Greater(table.Length, 100);
        }
    }
}
