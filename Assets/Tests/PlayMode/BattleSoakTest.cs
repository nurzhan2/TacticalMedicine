using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Battle;
using FieldMedic.Environment;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w3 done-criterion: the Battlefield battle runs continuously for 10 minutes in play mode without errors
    /// (any logged error/exception fails the test) or GC spikes (per-frame managed allocations recorded with the
    /// profiler; spikes and collections bounded), the director keeps firing events and every layer stays alive.
    /// Writes Logs/w3_soak.txt. Run explicitly: -testFilter FieldMedic.Tests.BattleSoakTest (≈ 11 min).
    /// FMVR_SOAK_SECONDS overrides the duration for a quick local check.
    /// </summary>
    [Category("Soak")]
    public class BattleSoakTest
    {
        [UnityTest, Timeout(1500000)]
        public IEnumerator Battle_Runs_TenMinutes_NoErrors_NoGcSpikes()
        {
            float duration = 600f;
            var env = System.Environment.GetEnvironmentVariable("FMVR_SOAK_SECONDS");
            if (!string.IsNullOrEmpty(env) && float.TryParse(env, out float e) && e > 10f) duration = e;
            BattleDirector.Mode = BattleMode.Combat;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            AudioListener.volume = 0f;
            var d = BattleDirector.Instance;
            Assert.IsNotNull(d);
            int quality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true); // the Quest pipeline

            const float Warmup = 20f;
            using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var log = new StringBuilder();
            long frames = 0, spikes = 0, bigFrames = 0;
            double allocSum = 0;
            long allocMax = 0;
            float dtMax = 0f, t = 0f, nextReport = 60f;
            int gc0 = 0;
            int lastEvents = 0;
            float lastEventAt = 0f, maxGap = 0f;
            var dts = new System.Collections.Generic.List<float>(40000);
            while (t < duration)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += Time.deltaTime;
                if (d.EventsFired != lastEvents) { maxGap = Mathf.Max(maxGap, t - lastEventAt); lastEventAt = t; lastEvents = d.EventsFired; }
                if (t < Warmup) { gc0 = System.GC.CollectionCount(0); continue; }
                frames++;
                long a = gc.Valid ? gc.LastValue : 0;
                allocSum += a;
                if (a > allocMax) allocMax = a;
                if (a > 16 * 1024) bigFrames++;
                if (a > 64 * 1024) spikes++;
                if (dt > dtMax) dtMax = dt;
                dts.Add(dt);
                if (t >= nextReport)
                {
                    nextReport += 60f;
                    var inf = NearInfantry.Instance;
                    string line = $"t={t:F0}s events={d.EventsFired} [{string.Join(",", d.EventCounts)}] alive={inf.AliveEnemies} shots={inf.TotalShots} deaths={inf.TotalDeaths} crowd={CrowdLayer.Instance.Alive} veh={VehicleLayer.Instance.TotalShots} jets={AirLayer.Instance.JetRuns} helis={AirLayer.Instance.HeliRuns} blasts={BattleFx.Instance.Detonations} proj={BattleFx.Instance.ActiveProjectiles} audio={BattleAudio.Instance.Played}/{BattleAudio.Instance.Stolen} alloc/frame avg={allocSum / Mathf.Max(1, frames):F0} B max={allocMax} B gc0={System.GC.CollectionCount(0) - gc0}";
                    log.AppendLine(line);
                    Debug.Log("[FMVR] soak " + line);
                }
            }
            QualitySettings.SetQualityLevel(quality, true);
            dts.Sort();
            float p99 = dts.Count > 0 ? dts[(int)(dts.Count * 0.99f)] : 0f;
            int collections = System.GC.CollectionCount(0) - gc0;
            double avg = allocSum / Mathf.Max(1, frames);
            string summary = $"duration {duration:F0} s (warm-up {Warmup:F0} s excluded), frames {frames}, GC alloc/frame avg {avg:F0} B, max {allocMax} B, frames > 16 KB: {bigFrames}, > 64 KB: {spikes}, gen0 collections {collections}, frame time p99 {p99 * 1000f:F1} ms max {dtMax * 1000f:F1} ms (editor, batch), events {d.EventsFired}, longest gap between events {maxGap:F1} s, recorder valid {gc.Valid}";
            log.AppendLine(summary);
            Debug.Log("[FMVR] soak summary: " + summary);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/w3_soak.txt", log.ToString());

            Assert.GreaterOrEqual(d.EventsFired, (int)(duration / 25f), "the director fires an event every 5–20 s");
            Assert.LessOrEqual(maxGap, 45f, "never a long silence between events");
            Assert.Greater(NearInfantry.Instance.AliveEnemies, 0);
            Assert.Greater(CrowdLayer.Instance.Alive, 10);
            Assert.IsTrue(BattleAudio.Instance.BedsPlaying);
            Assert.Less(BattleFx.Instance.ActiveProjectiles, 40, "projectiles are recycled");
            Assert.Less(BattleDirector.Instance.PendingJobs, 80, "scheduled jobs drain");
            if (gc.Valid)
            {
                Assert.Less(avg, 2048, "steady-state managed allocations stay tiny");
                Assert.AreEqual(0, collections, "no garbage collection during the battle (GC spike)");
                Assert.LessOrEqual(spikes, 1, "at most a single one-off frame above 64 KB in 10 minutes");
            }
        }
    }
}
