using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Scenarios.SelfAid;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9b self-aid drill end-to-end with scripted hands through the real interaction code: the player is hit in the
    /// open, walks into cover, puts the CAT on his own arm one-handed, writes the time, reports on the radio, wraps
    /// the blanket and answers the check-ins until the group comes (full debrief) — plus doing nothing (bleeds out)
    /// and staying in the open (the drone comes back twice), and the "Жгут на себя" lesson.
    /// </summary>
    public class SelfAidTests
    {
        readonly List<Object> _spawned = new List<Object>();
        SelfAidDirector _d;
        SelfAidBot _bot;
        Transform _body, _head;
        DebriefReport _report;
        GlobalStateGuard _guard;
        static readonly Vector3 CoverAt = new Vector3(0f, 0f, -6f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure();
            _guard = new GlobalStateGuard();
            _report = null;
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.LeftHanded = false;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
            _spawned.Add(floor);
            var ear = new GameObject("Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
            var cover = new GameObject("CoverZone_Test");
            cover.transform.position = CoverAt + Vector3.up;
            cover.AddComponent<CoverZone>().size = new Vector3(3f, 2.4f, 3f);
            _spawned.Add(cover);
        }

        [TearDown]
        public void TearDown()
        {
            _guard?.Restore();
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var l in Object.FindObjectsByType<SelfLimb>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.Destroy(l.gameObject);
            foreach (var t in Object.FindObjectsByType<SelfTourniquet>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            foreach (var m in Object.FindObjectsByType<SelfAidReportMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
        }

        HandGrabber MakeHand(Handedness h, Vector3 at)
        {
            var go = new GameObject("Hand_" + h);
            _spawned.Add(go);
            var rig = go.AddComponent<HandRig>();
            rig.handedness = h;
            rig.mode = HandRig.Mode.Scripted;
            var g = go.AddComponent<HandGrabber>();
            rig.UseScripted(at);
            go.transform.position = at;
            return g;
        }

        /// <summary>Body (head 1.6 m, facing the cover at −Z), two hands, worn IFAK, radio, director in test mode.</summary>
        IEnumerator Run(int seed = 3, System.Action<SelfAidConfig> tune = null, bool lesson = false)
        {
            _body = new GameObject("Body").transform;
            _spawned.Add(_body.gameObject);
            _head = new GameObject("Head").transform;
            _head.SetParent(_body, false);
            _head.localPosition = new Vector3(0f, 1.6f, 0f);
            _head.localRotation = Quaternion.Euler(10f, 180f, 0f);
            var l = MakeHand(Handedness.Left, _head.position + new Vector3(0.2f, -0.5f, -0.25f));
            var r = MakeHand(Handedness.Right, _head.position + new Vector3(-0.2f, -0.5f, -0.25f));

            var pgo = new GameObject("PlayerIFAK");
            _spawned.Add(pgo);
            var pouch = pgo.AddComponent<IfakPouch>();
            pouch.buildOnStart = false;
            pouch.bodyLocked = true;
            pouch.head = _head;
            pouch.Build();

            var radio = new GameObject("Radio").AddComponent<RadioSet>();
            radio.head = _head;
            radio.muted = true;
            _spawned.Add(radio.gameObject);

            var dgo = new GameObject("SelfAidDirector");
            _spawned.Add(dgo);
            _d = dgo.AddComponent<SelfAidDirector>();
            _d.testMode = true;
            _d.head = _head;
            _d.playerRig = _body;
            _d.pouch = pouch;
            _d.leftHand = l;
            _d.rightHand = r;
            _d.startPosition = Vector3.zero;
            _d.startYaw = 0f;
            _d.Finished += rep => _report = rep;
            yield return null;
            if (lesson) _d.BeginLesson(TrainingCatalog.Get("selftq"), seed);
            else
            {
                _d.Begin(ScenarioCatalog.Get("SA"), seed);
                tune?.Invoke(_d.Config); // before the hit (the run is created from the config at the hit)
            }
            float t0 = Time.time;
            while (!_d.HitHappened && Time.time - t0 < 5f) yield return null;
            Assert.IsTrue(_d.HitHappened, "the FPV hit the player");
            var bgo = new GameObject("SelfAidBot");
            _spawned.Add(bgo);
            _bot = bgo.AddComponent<SelfAidBot>();
            _bot.Init(_d, _body);
            yield return null;
        }

        string Summary => _report == null ? "no report; bot " + _bot?.lastStep + "; events " + string.Join(" | ", _d.Events.Skip(System.Math.Max(0, _d.Events.Count - 12)))
            : $"end={_report.endReason} score={_report.score} survived={_report.survived} mistakes=[{string.Join(",", _report.mistakes.Select(m => m.code))}] " +
              $"checks=[{string.Join("; ", _report.checks.Select(c => (c.ok ? "+" : "-") + c.text))}] bot={_bot.lastStep}";

        [UnityTest, Timeout(240000)]
        public IEnumerator CorrectSelfAid_CoverTqTimeReportBlanketCheckIns_FullDebrief()
        {
            yield return Run(3, c => { c.rescueAfterReport = 34f; c.firstCheckAfterAck = 7f; c.checkInterval = 12f; });
            Assert.AreEqual("armL", _d.Run.cfg.LimbId, "right-handed: the left (weak) arm is hit");
            Assert.IsTrue(_d.Wounded.gripDisabled, "the wounded hand cannot grip");
            Assert.That(_d.Vision, Is.Not.Null);
            Assert.That(_d.Vision.Red, Is.GreaterThan(0.5f), "red flash at the hit");

            // the wounded hand cannot take the CAT itself
            yield return _bot.hands.Grab(_d.Wounded, _d.KitCat);
            Assert.IsFalse(_d.KitCat.IsHeld, "a disabled hand does not grab");
            yield return _bot.hands.Release(_d.Wounded);

            yield return _bot.WalkTo(CoverAt);
            yield return null;
            Assert.IsTrue(_d.Run.InCover && _d.Run.CoverReachedAt > 0f, "in cover");
            Assert.IsEmpty(_d.Run.Mistakes.Keys, "no exposure mistake on a quick move: " + string.Join(",", _d.Run.Mistakes.Keys));

            float flowBefore = _d.Run.FlowMlPerSec;
            Assert.Greater(flowBefore, 1f, "arterial bleeding");
            yield return _bot.HoldWoundedArm();
            yield return _bot.SelfTourniquet(0.18f);
            Assert.IsTrue(_d.Run.tq.Locked, "locked; bot " + _bot.lastStep + " stage " + _d.Run.tq.stage + " pos " + _d.Run.tq.limbPosition);
            Assert.LessOrEqual(_d.Run.tq.limbPosition, _d.Config.highAndTightMax, "high");
            Assert.IsTrue(_d.Run.wound.IsControlled, "bleeding stopped, occlusion " + _d.Run.tq.Occlusion);
            Assert.IsFalse(_d.Wounded.gripDisabled, "grip back once the band is on");
            yield return null;
            Assert.IsTrue(_d.Run.tq.TimeMarked, "time written; bot " + _bot.lastStep);
            Assert.That(_d.Run.TqLockedAt, Is.LessThan(60f), "TQ within a minute");

            yield return _bot.Report();
            Assert.IsTrue(_d.Run.ReportSent && _d.Run.ReportAllCorrect, "report: " + string.Join(" / ", _d.Run.ReportLog));
            yield return _bot.Blanket();
            Assert.IsTrue(_d.Run.Blanket, "blanket on; bot " + _bot.lastStep);
            Assert.That(_d.Vision.Foil, Is.GreaterThan(0.3f), "foil over the view");

            yield return _bot.Wait(90f);
            Assert.IsNotNull(_report, Summary);
            Assert.AreEqual("rescued", _report.endReason, Summary);
            Assert.IsTrue(_report.survived && _report.selfAid, Summary);
            Assert.That(_bot.checksAnswered, Is.GreaterThanOrEqualTo(2), "check-ins answered");
            Assert.IsEmpty(_report.mistakes, Summary);
            Assert.AreEqual(100, _report.score, Summary);
            Assert.AreEqual(9, _report.checks.Count);
            Assert.IsTrue(_report.checks.All(c => c.ok), Summary);
            Assert.Contains(SelfAidCue.RescueArrive, _d.CueLog);
            Assert.Contains(SelfAidCue.Ack, _d.CueLog);
            Assert.IsTrue(_d.Events.Any(e => e == "radio sa_arrive"), "«Держись, брат, свои!»");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DoingNothing_InCover_BleedsOut()
        {
            yield return Run(5, c => c.lethalSeconds = 24f);
            yield return _bot.WalkTo(CoverAt);
            float t0 = Time.time;
            float dimSeen = 0f, greySeen = 0f;
            while (_report == null && Time.time - t0 < 60f)
            {
                dimSeen = Mathf.Max(dimSeen, _d.Vision.Dim);
                greySeen = Mathf.Max(greySeen, _d.Vision.Grey);
                yield return null;
            }
            Assert.IsNotNull(_report, Summary);
            Assert.AreEqual("died", _report.endReason, Summary);
            Assert.IsFalse(_report.survived);
            Assert.That(_report.mistakes.Select(m => m.code), Has.Member("sa_no_tq"));
            Assert.That(_report.outcomeText, Does.Contain("кровопотер"));
            Assert.Greater(greySeen, 0.3f, "colour drains with the blood loss");
            Assert.Greater(dimSeen, 0.2f, "vision dims past 70 %");
            Assert.IsFalse(_report.checks[1].ok);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator StayingInTheOpen_DroneComesBackTwice_Killed()
        {
            yield return Run(7);
            float t0 = Time.time;
            while (_report == null && Time.time - t0 < 40f) yield return null;
            Assert.IsNotNull(_report, Summary);
            Assert.AreEqual("killed", _report.endReason, Summary);
            Assert.IsFalse(_report.survived);
            int warn = _d.CueLog.IndexOf(SelfAidCue.DroneWarning), pass = _d.CueLog.IndexOf(SelfAidCue.DronePass), kill = _d.CueLog.IndexOf(SelfAidCue.Killed);
            Assert.That(warn, Is.GreaterThanOrEqualTo(0));
            Assert.That(pass, Is.GreaterThan(warn));
            Assert.That(kill, Is.GreaterThan(pass));
            Assert.That(_report.mistakes.Select(m => m.code), Has.Member("sa_stayed_open"));
            Assert.LessOrEqual(_report.score, 20);
            Assert.IsTrue(_report.mistakes.First(m => m.code == "sa_stayed_open").hasPos, "marked on the map");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Lesson_SelfTourniquet_CompletesStepByStep()
        {
            yield return Run(2, null, lesson: true);
            Assert.IsNotNull(_d.Panel, "white hint card");
            Assert.AreEqual("s_grab", _d.Progress.Step.id);
            yield return _bot.HoldWoundedArm();
            yield return _bot.SelfTourniquet(0.18f);
            float t0 = Time.time;
            while (!_d.LessonComplete && Time.time - t0 < 10f) yield return null;
            Assert.IsTrue(_d.LessonComplete, "lesson at step " + _d.Progress.Step?.id + "; bot " + _bot.lastStep);
            Assert.IsFalse(_d.Run.Ended, "training never fails");
        }
    }
}
