using System.Collections;
using System.Linq;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 hands tutorial: runs before the first Training lesson (not before the demo), five steps done with the
    /// hands on the practice dummy — grab+lift, pull the strap, twist the windlass, pinch the tag, two-hand drag —
    /// then the lesson starts and the menu remembers it; «ПРОПУСТИТЬ» skips; the menu entry replays it.
    /// </summary>
    public class TutorialTests : W8SceneTest
    {
        bool _savedDone;

        [SetUp] public void TutSetUp() { _savedDone = TutorialDirector.Done; TutorialDirector.AutoStart = true; }
        [TearDown] public void TutTearDown() { TutorialDirector.Done = _savedDone; TutorialDirector.AutoStart = false; }

        IEnumerator LoadTraining(string id)
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(id));
            AudioListener.volume = 0f;
            D.testMode = true;
            yield return DemoFlowRunner.Seconds(1f);
        }

        IEnumerator WaitStep(TutorialDirector t, int step, float timeout = 6f)
        {
            float t0 = Time.time;
            while (t.Step < step && !t.Finished && Time.time - t0 < timeout) yield return null;
            Assert.GreaterOrEqual(t.Step, step, $"tutorial at step {t.Step}, expected {step}");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Tutorial_FiveMotions_ThenLesson_Remembered()
        {
            TutorialDirector.Done = false;
            yield return LoadTraining("tq");
            var tut = TutorialDirector.Instance;
            Assert.IsNotNull(tut, "the tutorial runs before the first Training lesson");
            Assert.IsNull(T.Progress, "the lesson waits for the tutorial");
            Assert.IsNotNull(tut.Panel, "white hint card");
            Assert.IsNotNull(tut.Can); Assert.IsNotNull(tut.Crate); Assert.IsNotNull(tut.Tag);
            run.TakeHead(tut.Root.position + Vector3.up * 1.5f - tut.Root.forward * -0.8f);
            run.MakeBot();
            var bot = run.bot;
            float t0 = Time.time;

            // 1 grab + lift the ammo can
            yield return bot.Grab(bot.right, tut.Can);
            Assert.AreEqual(tut.Can, bot.right.Held);
            yield return bot.Move(bot.right, bot.right.PalmPosition + Vector3.up * 0.3f, false, 0.6f);
            yield return WaitStep(tut, 1);
            yield return bot.Move(bot.right, bot.right.PalmPosition - Vector3.up * 0.25f, false, 0.6f);
            yield return bot.Release(bot.right);

            // 2 pull the strap
            yield return bot.Grab(bot.right, tut.Strap, tut.Strap.transform.position);
            yield return bot.Move(bot.right, bot.right.PalmPosition - tut.Crate.transform.forward * 0.18f, false, 0.4f);
            yield return bot.Release(bot.right);
            yield return WaitStep(tut, 2);

            // 3 twist the windlass half a turn
            yield return bot.Grab(bot.right, tut.Rod, tut.Rod.transform.position);
            yield return bot.Twist(bot.right, tut.Crate.transform.up, 200f);
            yield return bot.Release(bot.right);
            yield return WaitStep(tut, 3);

            // 4 pinch the tag
            yield return bot.Grab(bot.left, tut.Tag);
            Assert.AreEqual(tut.Tag, bot.left.Held);
            yield return WaitStep(tut, 4);
            yield return bot.Move(bot.left, bot.left.PalmPosition + Vector3.up * 0.2f);
            yield return bot.Release(bot.left);

            // 5 two-hand drag by the rope handles
            var pts = tut.Crate.GrabPoints;
            Assert.AreEqual(2, pts.Length, "two rope handles");
            yield return bot.Grab(bot.left, tut.Crate, pts[0].transform.position);
            yield return bot.Grab(bot.right, tut.Crate, pts[1].transform.position);
            Assert.AreEqual(2, tut.Crate.HolderCount, "both hands on the crate");
            Vector3 away = tut.Root.forward;   // toward the player: he walks back pulling it (the crate stands in free space in front of him)
            Vector3 l0 = bot.left.PalmPosition, r0 = bot.right.PalmPosition;
            for (float t = 0f; t < 2.6f; t += Time.deltaTime)
            {
                bot.SetPalm(bot.left, l0 + away * (0.55f * t) + Vector3.up * 0.05f);
                bot.SetPalm(bot.right, r0 + away * (0.55f * t) + Vector3.up * 0.05f);
                yield return null;
            }
            Debug.Log($"[FMVR] w8 test tutorial: crate dragged {tut.CrateDragged:F2} m, strap {tut.StrapPulled:F2} m, twist {tut.TwistAccum:F0}°, {Time.time - t0:F0} s");
            yield return WaitStep(tut, 5);
            yield return bot.Release(bot.left);
            yield return bot.Release(bot.right);
            for (int k = 0; k < 30 && TrainingDirector.Instance.Progress == null; k++) yield return null;
            Assert.IsTrue(tut == null || tut.Finished, "tutorial finished");
            Assert.IsTrue(TutorialDirector.Done, "remembered");
            Assert.IsNotNull(T.Progress, "the lesson starts after the tutorial");
            Assert.AreEqual("tq", T.Lesson.id);
            Assert.IsFalse(TutorialDirector.ShouldRun("tq"), "done: next Training starts directly");
            Assert.IsTrue(TutorialDirector.ShouldRun(TutorialDirector.LessonId), "the menu entry replays it");
        }

        [UnityTest]
        public IEnumerator Tutorial_Skip_ByFinger_StartsLesson()
        {
            TutorialDirector.Done = false;
            yield return LoadTraining("pack");
            var tut = TutorialDirector.Instance;
            Assert.IsNotNull(tut);
            run.TakeHead(tut.Root.position + Vector3.up * 1.5f + tut.Root.forward * 0.9f);
            yield return DemoFlowRunner.Seconds(1.2f);   // the card fades in
            run.MakeBot();
            yield return run.bot.Poke(tut.SkipButton, run.bot.right);
            for (int k = 0; k < 30 && T.Progress == null; k++) yield return null;
            Assert.IsTrue(TutorialDirector.Done, "skipped = remembered");
            Assert.IsNotNull(T.Progress, "lesson started");
            Assert.AreEqual("pack", T.Lesson.id);
        }

        [UnityTest]
        public IEnumerator Tutorial_NotBefore_Demo_Or_WhenDone_MenuEntry()
        {
            TutorialDirector.Done = false;
            yield return LoadTraining(TrainingCatalog.DemoId);
            Assert.IsNull(TutorialDirector.Instance, "the demo never waits for the tutorial");
            Assert.IsNotNull(T.Progress);
            TutorialDirector.Done = true;
            yield return LoadTraining("tq");
            Assert.IsNull(TutorialDirector.Instance, "done: straight into the lesson");
            Assert.IsNotNull(T.Progress);
            // the menu entry
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Menu());
            var board = Object.FindAnyObjectByType<MenuBoard>();
            board.ShowTraining();
            yield return null;
            var b = board.GetComponentsInChildren<RoundButton>(true).FirstOrDefault(x => x.name == "Btn_Tutorial");
            Assert.IsNotNull(b, "«Управление» on the Training page");
            string launched = null;
            board.launchOverride = w => launched = w;
            b.Click();
            board.launchOverride = null;
            Assert.AreEqual("lesson:" + TutorialDirector.LessonId, launched);
            Assert.IsTrue(board.GetComponentsInChildren<RoundButton>(true).Any(x => x.name.Contains("7")) || TrainingCatalog.Topics.Contains("assess"), "assessment topic on the page");
        }
    }
}
