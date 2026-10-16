using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// A medic bot with two scripted hands performs procedures on Erlan through the real interaction code
    /// (grabs, strap pulls, wrist twists, finger strokes). Correct technique saves him; wrong technique fails
    /// the way it fails in reality.
    /// </summary>
    public class TreatmentBotTests
    {
        readonly List<Object> _spawned = new List<Object>();
        CasualtyController _c;
        MedicBot _bot;
        IfakPouch _pouch;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Make();
            GameSettings.HoldTimeScale = 60f; // training speed-up of the 3-minute hold
            TreatmentAudio.Muted = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            _spawned.Add(floor);
            var ear = new GameObject("Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.HoldTimeScale = 1f;
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var p in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "BloodPool"))
                Object.Destroy(p.gameObject);
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
            go.AddComponent<HandTreatment>();
            return g;
        }

        /// <summary>Erlan wounded and down, a bot kneeling at his right side, the player's IFAK beside him.</summary>
        IEnumerator Scene(WoundType type, BodyRegion region, WoundVisual visual, HitKind hit = HitKind.Explosion, Phase phase = Phase.CareUnderFire)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _spawned.Add(go);
            _c = go.GetComponent<CasualtyController>();
            _c.voice.muted = true;
            yield return null;
            _c.AddWound(type, region, visual);
            _c.Hit(hit);
            float t0 = Time.time;
            while (_c.IsFalling && Time.time - t0 < 9f) yield return null;
            Assert.That(_c.IsFalling, Is.False, "fell and lies");
            // the fall -> lying crossfade still moves the body for a moment: let it settle
            Vector3 last = _c.rig.Bone(HumanBodyBones.Hips).position;
            for (float s = 0f; s < 6f; s += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                Vector3 now = _c.rig.Bone(HumanBodyBones.Hips).position;
                if (s > 1f && Vector3.Distance(now, last) < 0.004f) break;
                last = now;
            }
            _c.Protocol.phase = phase;
            yield return null;

            var hips = _c.rig.Bone(HumanBodyBones.Hips).position;
            var l = MakeHand(Handedness.Left, hips + new Vector3(0.4f, 0.5f, -0.1f));
            var r = MakeHand(Handedness.Right, hips + new Vector3(0.4f, 0.5f, 0.1f));
            var bgo = new GameObject("Bot");
            _spawned.Add(bgo);
            _bot = bgo.AddComponent<MedicBot>();
            _bot.Init(l, r);
            var pgo = new GameObject("PlayerIFAK");
            _spawned.Add(pgo);
            pgo.transform.position = hips + new Vector3(0.75f, 0.05f, 0.25f);
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.buildOnStart = false;
            _pouch.Build();
            yield return null;
        }

        IEnumerator RunSim(float simSeconds, float speed)
        {
            _c.simulationSpeed = speed;
            float target = _c.Patient.secondsSinceStart + simSeconds, t0 = Time.realtimeSinceStartup;
            while (_c.Patient.secondsSinceStart < target && _c.Patient.outcome == Outcome.Ongoing && Time.realtimeSinceStartup - t0 < 40f)
                yield return null;
            _c.simulationSpeed = 1f;
        }

        bool Logged(string code) => _c.Protocol.log.Any(e => e.code == code);
        string LogText => string.Join(" | ", _c.Protocol.log.Select(e => e.code));

        // ------------------------------------------------------------------ required bot scenarios
        [UnityTest]
        public IEnumerator Tourniquet_HighAndTight_StopsArterialBleed_CasualtySurvives()
        {
            yield return Scene(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var site = _c.WoundSites[0];
            var cat = _c.Interaction.Ifak.Find<TourniquetCAT>(); // his own tourniquet (TCCC)
            Assert.That(cat, Is.Not.Null, "Erlan's IFAK has his CAT");
            var marker = _pouch.Find<MarkerItem>();
            yield return _bot.ApplyTourniquet(cat, _c, Limb.RightLeg, 0.12f, 4, marker);
            Assert.That(cat.State.Locked, Is.True, _bot.lastStep + " stage=" + cat.State.stage);
            Assert.That(cat.State.Secured, Is.True, "TIME strap secured");
            Assert.That(cat.State.TimeMarked, Is.True, "time written with the marker");
            Assert.That(cat.State.slackCm, Is.LessThanOrEqualTo(TourniquetState.TightSlackCm));
            Assert.That(cat.State.HalfTurns, Is.InRange(4, 5));
            yield return null;
            Assert.That(Logged("tq_ok"), Is.True, LogText);
            Assert.That(site.FlowMlPerSec, Is.EqualTo(0f), "arterial spurting stopped");
            Assert.That(_c.Patient.PulseStrength(PulseSite.PedalR), Is.EqualTo(0f), "no distal pulse");
            Assert.That(_c.Protocol.tourniquetTimeMarked, Is.True);
            Assert.That(_c.Pain.TourniquetHalfTurns("legR"), Is.EqualTo(cat.State.HalfTurns), "every half-turn hurt");

            // the bot feels the foot: nothing
            var lh = _bot.left.GetComponent<HandTreatment>();
            var input = (ScriptedHandInput)_bot.left.Input;
            int before = input.HapticCount;
            yield return _bot.CheckPulse(_c, PulseSite.PedalR, 1.5f);
            Assert.That(lh.PulseBeats, Is.EqualTo(0), "no beats felt below the tourniquet");
            yield return _bot.CheckPulse(_c, PulseSite.Carotid, 2.5f);
            Assert.That(lh.PulseBeats, Is.GreaterThanOrEqualTo(2), "carotid pulse felt as haptic beats");

            yield return RunSim(1200f, 120f);
            Assert.That(_c.Patient.outcome, Is.EqualTo(Outcome.Ongoing), _c.Patient.causeOfDeath);
            Assert.That(_c.Patient.LossFraction, Is.LessThan(0.3f));
            Debug.Log($"[FMVR] TQ bot: loss {_c.Patient.LossFraction:P0}, score {_c.Protocol.Score()}, fallback grabs {_bot.fallbackGrabs}, log {LogText}");
        }

        [UnityTest]
        public IEnumerator Tourniquet_OnKnee_DoesNotStopShinBleed()
        {
            yield return Scene(WoundType.ArterialLimb, BodyRegion.ShinR, WoundVisual.RaggedLaceration);
            var cat = _pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, _c, Limb.RightLeg, 1.0f, 4);
            Assert.That(cat.State.Locked, Is.True, _bot.lastStep);
            Assert.That(cat.State.OnJoint, Is.True);
            Assert.That(Logged("tq_on_joint"), Is.True, LogText);
            Assert.That(_c.Patient.wounds[0].IsControlled, Is.False, "a band on the knee does not compress the artery");
            Assert.That(_c.Patient.PulseStrength(PulseSite.PedalR), Is.GreaterThan(0f));
            yield return RunSim(900f, 90f);
            Assert.That(_c.Patient.outcome, Is.EqualTo(Outcome.Died), "bled out despite the tourniquet");
        }

        [UnityTest]
        public IEnumerator Drag_ByHandle_MovesCasualtyThreeMetresIntoCover()
        {
            yield return Scene(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var hips = _c.rig.Bone(HumanBodyBones.Hips);
            Vector3 start = hips.position;
            Vector3 dest = start + new Vector3(-3.6f, 0f, 0f);
            var cover = new GameObject("Cover");
            _spawned.Add(cover);
            cover.transform.position = dest + Vector3.up * 0.5f;
            cover.AddComponent<CoverZone>().size = new Vector3(1.4f, 2f, 1.6f);
            yield return _bot.DragTo(_c, dest);
            Vector3 moved = hips.position - start; moved.y = 0f;
            Assert.That(moved.magnitude, Is.GreaterThanOrEqualTo(3f), "dragged >= 3 m");
            Assert.That(_c.Interaction.Drag.DraggedDistance, Is.GreaterThan(2.5f));
            Assert.That(_c.Protocol.phase, Is.EqualTo(Phase.TacticalFieldCare), "in cover: MARCH phase");
            Assert.That(Mathf.Abs(_c.transform.position.y), Is.LessThan(0.05f), "stays on the floor");
            Assert.That(Logged("drag"), Is.True);
        }

        [UnityTest]
        public IEnumerator GroinBleed_CutPackHoldBandage_Controlled()
        {
            yield return Scene(WoundType.Junctional, BodyRegion.GroinR, WoundVisual.RaggedLaceration, HitKind.Explosion, Phase.TacticalFieldCare);
            var w = _c.Patient.wounds[0];
            var care = _c.Interaction.Care(w);
            Assert.That(care, Is.Not.Null);
            // gauze cannot go through the trousers
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.RightLeg, 0.85f, 0.1f);
            Assert.That(w.exposed, Is.True, "trouser leg cut open, groin exposed");
            yield return _bot.Pack(_pouch.Find<GauzeItem>(), care);
            Assert.That(care.Packing.Complete, Is.True, $"packed {care.Packing.Fill:P0} strokes {care.Packing.strokes} ({_bot.lastStep})");
            Assert.That(w.control, Is.EqualTo(BleedControl.Packed));
            yield return _bot.HoldPressure(care, 3.6f);
            Assert.That(w.control, Is.EqualTo(BleedControl.PackedAndHeld), $"held {w.packingHoldSeconds:F0} s");
            yield return _bot.Bandage(_pouch.Find<BandageItem>(), care);
            Assert.That(care.Bandage.Complete, Is.True, $"bandage {care.Bandage.step} wraps {care.Bandage.Wraps:F1} ({_bot.lastStep})");
            Assert.That(w.control, Is.EqualTo(BleedControl.PressureDressing));
            Assert.That(w.IsControlled, Is.True, "groin bleed controlled");
            yield return RunSim(900f, 90f);
            Assert.That(_c.Patient.outcome, Is.EqualTo(Outcome.Ongoing), _c.Patient.causeOfDeath);
        }

        [UnityTest]
        public IEnumerator ChestWound_SealThenNeedle_PreventsTensionDeath()
        {
            yield return Scene(WoundType.PenetratingChest, BodyRegion.ChestR, WoundVisual.ChestEntry, HitKind.Shot, Phase.TacticalFieldCare);
            var w = _c.Patient.wounds[0];
            var care = _c.Interaction.Care(w);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(), care);
            var seal = care.Seal;
            Assert.That(seal, Is.Not.Null, _bot.lastStep);
            Assert.That(seal.Proc.Complete, Is.True, $"seal step {seal.Proc.step} presses {seal.Proc.edgePresses}");
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Sealed));
            // the injured lung keeps leaking: tension builds despite the (vented) seal
            _c.simulationSpeed = 60f;
            float t0 = Time.realtimeSinceStartup;
            while (!(_c.Patient.pneumo == PneumoState.Tension && _c.Patient.tensionSeverity > 0.25f) && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            _c.simulationSpeed = 1f;
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Tension));
            // 5th ICS anterior axillary line: the 2nd ICS is under the seal on this side
            var needle = _pouch.Find<NeedleItem>();
            yield return _bot.Needle(needle, _c, NeedleSite.FifthIcsAal, true);
            Assert.That(Logged("decompressed"), Is.True, $"{LogText} site {needle.LastSite} angle {needle.LastAngle:F0} depth {needle.DepthCm:F1}");
            Assert.That(_c.Patient.pneumo, Is.EqualTo(PneumoState.Decompressed));
            yield return RunSim(900f, 90f);
            Assert.That(_c.Patient.outcome, Is.EqualTo(Outcome.Ongoing), _c.Patient.causeOfDeath);
            Assert.That(needle.Attached, Is.True, "catheter left in the chest");
        }

        // ------------------------------------------------------------------ hands-on details
        [UnityTest]
        public IEnumerator LiftLeg_FollowsHand_Hurts_FallsBackOnRelease()
        {
            yield return Scene(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var foot = _c.rig.Bone(HumanBodyBones.RightFoot);
            float y0 = foot.position.y;
            float pain0 = _c.Pain.spike;
            yield return _bot.LiftLeg(_c, Limb.RightLeg, 0.35f, 0.5f);
            Assert.That(foot.position.y - y0, Is.GreaterThan(0.2f), "the leg is lifted by the hand (IK)");
            var handle = _c.Interaction.Handle(Limb.RightLeg, LimbHandle.Grip.Ankle);
            Assert.That(handle.IsLifted, Is.True);
            Assert.That(_c.Pain.spike, Is.GreaterThan(pain0), "moving the wounded leg hurts");
            yield return _bot.Release(_bot.left);
            yield return _bot.Wait(0.8f);
            Assert.That(foot.position.y - y0, Is.LessThan(0.1f), "falls back to the ground");
        }

        [UnityTest]
        public IEnumerator BloodSweep_FindsHiddenForearmWound()
        {
            yield return Scene(WoundType.Venous, BodyRegion.ForearmL, WoundVisual.FragmentHole, HitKind.Explosion, Phase.TacticalFieldCare);
            var w = _c.Patient.wounds[0];
            Assert.That(w.discovered, Is.False);
            yield return _bot.BloodSweep(_c);
            Assert.That(w.discovered, Is.True, "hidden wound found by the sweep");
            Assert.That(Logged("wound_found"), Is.True);
            Assert.That(_bot.right.GetComponent<HandTreatment>().Swept.Count, Is.GreaterThan(8));
        }

        [UnityTest]
        public IEnumerator LogRoll_TwoHands_OnSide_ThenPropped()
        {
            yield return Scene(WoundType.Venous, BodyRegion.ForearmL, WoundVisual.FragmentHole, HitKind.Explosion, Phase.TacticalFieldCare);
            yield return _bot.Roll(_c, true);
            yield return _bot.Wait(0.5f);
            Assert.That(Mathf.Abs(_c.Interaction.Roll.Angle), Is.GreaterThan(60f), "rolled onto his side");
            Assert.That(_c.rolledOnSide, Is.True, "stays propped on the side");
            Assert.That(Logged("log_roll"), Is.True);
        }
    }
}
