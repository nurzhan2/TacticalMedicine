using System.Collections;
using System.Linq;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8: the whole DEMO flow with hands only (no controllers): pinch = small items and tabs, full grip = the CAT,
    /// the strap, the drag handle; poke = buttons. Same runner as <see cref="DemoFlowTests"/>.
    /// </summary>
    public class HandTrackingDemoTests : W8SceneTest
    {
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Demo_FullFlow_WithPinchAndGrip_HandTracking()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(TrainingCatalog.DemoId));
            AudioListener.volume = 0f;
            D.testMode = true;
            if (TacevacController.Instance != null) TacevacController.Instance.approachSeconds = 1.5f;
            run.handTracking = true;
            yield return run.Run();
            Assert.IsTrue(run.bot.handTracking);
            Assert.IsTrue(run.bot.right.TrackedHand, "hands report hand tracking");
            Debug.Log($"[FMVR] w8 hand-tracking demo: pinch grabs {run.bot.pinchGrabs}, grip grabs {run.bot.gripGrabs}, fallback grabs {run.bot.fallbackGrabs}");
            Assert.Greater(run.bot.pinchGrabs, 2, "small items and tabs were pinched");
            Assert.Greater(run.bot.gripGrabs, 2, "handles and the strap were gripped");
        }

        [UnityTest]
        public IEnumerator Pinch_DoesNotTake_DragHandle_Fist_Does()
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(TrainingCatalog.DemoId));
            AudioListener.volume = 0f;
            D.testMode = true;
            yield return DemoFlowRunner.Seconds(1f);
            run.TakeHead(run.HeadAt(1.3f));
            run.handTracking = true;
            run.MakeBot();
            var bot = run.bot;
            var h = bot.right;
            var input = bot.InputOf(h);
            var drag = C.Interaction.Drag;
            yield return bot.Move(h, drag.transform.position);
            input.Trigger = 1f;                    // pinch at the drag strap
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.IsNull(h.Held, "a pinch does not hold a 95 kg man");
            input.Trigger = 0f;
            yield return DemoFlowRunner.Seconds(0.1f);
            input.Grip = 1f;                       // full fist
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.AreEqual(drag, h.Held, "a fist does");
            Assert.AreEqual(GrabStyle.Grip, h.HeldWith);
            input.Grip = 0f;
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.IsNull(h.Held);
            // pinch a small item from the opened pouch
            run.KitNear();
            yield return bot.OpenPouch(run.Pouch, bot.right);
            var gauze = run.Pouch.Find<FieldMedic.Treatment.GauzeItem>();
            yield return bot.Move(h, gauze.transform.position);
            input.Trigger = 1f;
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.IsNotNull(h.Held, "pinch takes a small item (" + gauze.name + ")");
            Assert.AreEqual(GrabStyle.Pinch, h.HeldWith);
            Assert.AreNotEqual(GrabStyle.Grip, h.Held.Style, "only pinch-held things");
            input.Trigger = 0f;
            yield return DemoFlowRunner.Seconds(0.2f);
            Assert.IsNull(h.Held, "opening the pinch lets go");
        }
    }
}
