using System.Collections;
using FieldMedic.Audio;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Runs once before the PlayMode tests: the w8 hands tutorial would otherwise start in front of every Training
    /// run of the older flows. <see cref="TutorialTests"/> switches it on explicitly.
    /// </summary>
    [SetUpFixture]
    public class W8TestSetup
    {
        [OneTimeSetUp] public void Setup() { TutorialDirector.AutoStart = false; PlayerVoice.Override = null; }
        [OneTimeTearDown] public void Teardown() { TutorialDirector.AutoStart = true; PlayerVoice.Override = null; }
    }

    /// <summary>Shared w8 scene setup: a Training lesson on the battlefield, a test head, the scripted medic.</summary>
    public abstract class W8SceneTest
    {
        protected GlobalStateGuard guard;
        protected DemoFlowRunner run;
        protected ScenarioDirector D => ScenarioDirector.Instance;
        protected CasualtyController C => D.casualty;
        protected TrainingDirector T => TrainingDirector.Instance;

        [SetUp]
        public void BaseSetUp()
        {
            guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            run = new DemoFlowRunner();
            PlayerVoice.Override = null;
            VitalsChecks.ForceLabels = false;
        }

        [TearDown]
        public void BaseTearDown()
        {
            guard?.Restore();
            ScenarioLaunch.Pending = null;
            GameSettings.HoldTimeScale = 1f;
            BattleDirector.PlayerArmed = null;
            BattleDirector.Mode = BattleMode.Combat;
            SmokeScreens.Clear();
            PlayerVoice.Override = null;
            if (run.head != null) Object.Destroy(run.head.gameObject);
        }

        protected IEnumerator LoadLesson(string id)
        {
            yield return DemoFlowRunner.Load(() => ScenarioLaunch.Training(id));
            AudioListener.volume = 0f;
            Assert.IsNotNull(D, "director in the Battlefield scene");
            D.testMode = true;
            yield return DemoFlowRunner.Seconds(1.2f);
        }

        /// <summary>Loss of blood fraction, held there (the trainer's 30 % cap lifted).</summary>
        protected void SetLoss(float f)
        {
            if (T != null) T.maxLossFraction = Mathf.Max(0.3f, f + 0.05f);
            C.Patient.bloodLostMl = C.Patient.BloodVolumeMl * f;
        }

        protected IEnumerator Ready(float headDistance = 0.9f)
        {
            run.TakeHead(run.HeadAt(headDistance));
            run.KitNear();
            run.MakeBot();
            if (C.Checks != null) C.Checks.head = run.head;
            yield return run.HandsTo(run.Hips);
        }
    }
}
