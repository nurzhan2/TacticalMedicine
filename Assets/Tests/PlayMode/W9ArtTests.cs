using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios.Bots;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9d: the Astra models in the game — the real two-echelon IFAK (items on its Slot_* empties, rip-away by the pull
    /// tab), the elastic Alpha / Esmarch tourniquets stopping a real bleed in the physiology model, and the moving parts
    /// of the imported items (shear blades, marker cap, packs that open when taken out).
    /// </summary>
    public class W9ArtTests
    {
        readonly List<Object> _spawned = new List<Object>();
        GlobalStateGuard _guard;
        CasualtyController _c;
        MedicBot _bot;
        IfakPouch _pouch;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure();
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "~Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            _spawned.Add(floor);
            var ear = new GameObject("~Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var h in Object.FindObjectsByType<ItemHint>(FindObjectsSortMode.None)) Object.Destroy(h.gameObject);
            _guard?.Restore();
        }

        HandGrabber MakeHand(Handedness h, Vector3 at)
        {
            var go = new GameObject("~Hand_" + h);
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

        void MakeBot(Vector3 near)
        {
            var l = MakeHand(Handedness.Left, near + new Vector3(-0.2f, 0.3f, 0f));
            var r = MakeHand(Handedness.Right, near + new Vector3(0.2f, 0.3f, 0f));
            var bgo = new GameObject("~Bot");
            _spawned.Add(bgo);
            _bot = bgo.AddComponent<MedicBot>();
            _bot.Init(l, r);
        }

        IEnumerator Kit(Vector3 at)
        {
            var pgo = new GameObject("~PlayerIFAK");
            _spawned.Add(pgo);
            pgo.transform.position = at;
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.buildOnStart = false;
            _pouch.Build();
            yield return null;
        }

        /// <summary>Erlan down with one wound, the kit beside him, the bot kneeling at his right.</summary>
        IEnumerator Casualty(WoundType type, BodyRegion region)
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
            _c.AddWound(type, region, WoundVisual.RaggedLaceration);
            _c.Hit(HitKind.Explosion);
            float t0 = Time.time;
            while (_c.IsFalling && Time.time - t0 < 9f) yield return null;
            Vector3 last = _c.rig.Bone(HumanBodyBones.Hips).position;
            for (float s = 0f; s < 6f; s += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                Vector3 now = _c.rig.Bone(HumanBodyBones.Hips).position;
                if (s > 1f && Vector3.Distance(now, last) < 0.004f) break;
                last = now;
            }
            var hips = _c.rig.Bone(HumanBodyBones.Hips).position;
            MakeBot(hips + new Vector3(0.4f, 0.2f, 0f));
            yield return Kit(hips + new Vector3(0.75f, 0.05f, 0.25f));
        }

        static void RequireArt()
        {
            if (ArtModels.Load(ArtModels.Pouch.id) == null) Assert.Ignore("imported art not present");
        }

        // ------------------------------------------------------------------ the kit
        [UnityTest]
        public IEnumerator RealKit_ItemsOnDeliveredSlots_PullTabRipsItOpen()
        {
            RequireArt();
            MakeBot(new Vector3(0f, 1f, -0.4f));
            yield return Kit(new Vector3(0f, 1f, 0f));
            Assert.IsNotNull(_pouch.Art, "FMVR_IFAK used for the player's kit");
            Assert.AreEqual(20, _pouch.Items.Count, "quick pouch 2 + main pouch 18");
            foreach (var it in _pouch.Items)
            {
                Assert.IsTrue(it.InPouch, it.name);
                Assert.That(it.homeSlot.parent.name, Does.StartWith("Slot_"), it.name + " sits on a delivered slot empty");
                Assert.IsNotNull(it.ArtRoot, it.name + " uses its imported model");
            }
            Assert.AreEqual("Slot_TQ", _pouch.Find<TourniquetCAT>().homeSlot.parent.name);
            Assert.AreEqual("Slot_Shears", _pouch.Find<ShearsItem>().homeSlot.parent.name);
            Assert.AreEqual("Slot_AlphaTQ", _pouch.Find(MedItemKind.AlphaTourniquet).homeSlot.parent.name);
            Assert.AreEqual("Slot_Esmarch", _pouch.Find(MedItemKind.EsmarchTourniquet).homeSlot.parent.name);
            Assert.AreEqual(0f, _pouch.Art.Open01, "starts folded on its back panel");
            Assert.LessOrEqual(_pouch.Art.DrawCalls, 3, "closed kit drawn as one combined mesh per material");
            Assert.IsFalse(_pouch.Find<GauzeItem>().CanBeGrabbedBy(_bot.right), "main pouch locked while closed");
            Assert.IsTrue(_pouch.Find<TourniquetCAT>().CanBeGrabbedBy(_bot.right), "quick pouch always open");
            var back = _pouch.Art.BackPanel.position;
            yield return _bot.OpenPouch(_pouch, _bot.left);
            Assert.IsTrue(_pouch.IsOpen, "ripped open by the pull tab");
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(1f, _pouch.Art.Open01, 1e-3f, "panels unfolded");
            Assert.LessOrEqual(_pouch.Art.DrawCalls, 3, "open kit settled: combined again");
            Assert.Less(Vector3.Distance(back, _pouch.Art.BackPanel.position), 1e-3f, "back panel stays on the vest");
            foreach (var it in _pouch.Items)
                Assert.Less(Vector3.Distance(it.transform.position, it.homeSlot.position), 0.002f, it.name + " moved with its panel");
            Assert.IsTrue(_pouch.Find<GauzeItem>().CanBeGrabbedBy(_bot.right));
        }

        [UnityTest]
        public IEnumerator ImportedItems_PacksOpen_ShearBladesMove_MarkerCapComesOff_SplintHint()
        {
            RequireArt();
            MakeBot(new Vector3(0f, 1f, -0.4f));
            yield return Kit(new Vector3(0f, 1f, 0f));
            _pouch.Flap.Open();
            yield return new WaitForSeconds(0.8f);
            var h = _bot.right;

            var shears = _pouch.Find<ShearsItem>();
            Assert.AreEqual(0f, shears.BladeAngle, 0.5f, "closed in the holster");
            yield return _bot.Grab(h, shears);
            yield return new WaitForSeconds(0.3f);
            Assert.That(shears.BladeAngle, Is.InRange(15f, 30f), "ready-open in the hand");
            yield return _bot.SetTrigger(h, 1f);
            float min = 99f, max = 0f;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime) { min = Mathf.Min(min, shears.BladeAngle); max = Mathf.Max(max, shears.BladeAngle); yield return null; }
            yield return _bot.SetTrigger(h, 0f);
            Assert.Greater(max - min, 15f, "blades snip open/close around the screw while squeezed");
            yield return _bot.Release(h);

            var marker = _pouch.Find<MarkerItem>();
            Assert.AreEqual(0f, marker.CapOff, "cap on in the kit");
            yield return _bot.Grab(h, marker);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1f, marker.CapOff, 1e-3f, "cap posted on the back end");
            yield return _bot.Release(h);

            var needle = _pouch.Find<NeedleItem>();
            Assert.IsTrue(ArtModels.FindPart(needle.ArtRoot, "Case").gameObject.activeSelf, "needle in its case in the kit");
            yield return _bot.Grab(h, needle);
            Assert.IsFalse(ArtModels.FindPart(needle.ArtRoot, "Case").gameObject.activeSelf, "taken out of the case");
            Assert.IsTrue(ArtModels.FindPart(needle.ArtRoot, "Needle").gameObject.activeSelf);
            yield return _bot.Release(h);

            var gauze = _pouch.Find<GauzeItem>();
            Assert.IsTrue(ArtModels.FindPart(gauze.ArtRoot, "Pack_Closed").gameObject.activeSelf);
            yield return _bot.Grab(h, gauze);
            Assert.IsFalse(ArtModels.FindPart(gauze.ArtRoot, "Pack_Closed").gameObject.activeSelf, "pack torn open in the hand");
            Assert.IsTrue(ArtModels.FindPart(gauze.ArtRoot, "Pack_OpenBottom").gameObject.activeSelf);
            yield return _bot.Move(h, gauze.homeSlot.position + _pouch.transform.up * 0.1f, false, 2f);
            yield return _bot.Release(h);
            Assert.IsTrue(gauze.InPouch, "back in its slot");
            Assert.IsTrue(ArtModels.FindPart(gauze.ArtRoot, "Pack_Closed").gameObject.activeSelf, "packed look again");

            var splint = (PropItem)_pouch.Find(MedItemKind.Splint);
            yield return _bot.Grab(h, splint);
            Assert.IsTrue(splint.HintShown);
            Assert.IsNotNull(ItemHint.Last);
            StringAssert.Contains("перелом", ItemHint.Last.Text.Replace("\n", " "));
            yield return _bot.Release(h);
            foreach (var k in new[] { MedItemKind.TriangularBandage, MedItemKind.EyeShield, MedItemKind.AntisepticWipes, MedItemKind.DuctTape })
            {
                var it = _pouch.Find(k);
                Assert.IsNotNull(it, k + " in the kit");
                Assert.IsInstanceOf<PropItem>(it);
                Assert.IsNull(((PropItem)it).HintText, k + ": a plain prop, no fake function");
            }
        }

        // ------------------------------------------------------------------ elastic tourniquets on Erlan
        [UnityTest]
        public IEnumerator Alpha_FourStretchedTurnsHighOnThigh_StopsArterialBleed()
        {
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ThighR);
            var site = _c.WoundSites[0];
            Assert.Greater(site.FlowMlPerSec, 0f, "bleeding before");
            var alpha = (ElasticTourniquetItem)_pouch.Find(MedItemKind.AlphaTourniquet);
            yield return _bot.ApplyElasticTourniquet(alpha, _c, Limb.RightLeg, 0.12f, 4);
            Assert.IsTrue(alpha.Fixed, _bot.lastStep + " stage " + alpha.State.stage + " turns " + alpha.Turns);
            Assert.GreaterOrEqual(alpha.Turns, 4);
            Assert.IsTrue(alpha.State.FullOcclusion, $"occlusion {alpha.State.Occlusion:F2} wraps {alpha.State.elasticWraps:F2}");
            yield return null;
            Assert.AreEqual(0f, site.FlowMlPerSec, "arterial bleeding stopped");
            Assert.AreEqual(0f, _c.Patient.PulseStrength(PulseSite.PedalR), "no distal pulse");
            Assert.IsTrue(_c.Protocol.log.Any(e => e.code == "tq_ok_elastic"), string.Join("|", _c.Protocol.log.Select(e => e.code)));
            Assert.IsTrue(_c.Protocol.penalties.ContainsKey("tq_elastic_thigh"), "debrief: windlass preferred on a thigh");
            Assert.AreEqual(0, CareProtocol.Penalty["tq_elastic_thigh"].points);
            // time on the band with the marker
            var marker = _pouch.Find<MarkerItem>();
            yield return _bot.Write(marker, alpha.TimeLabelPoint, _bot.left);
            Assert.IsTrue(alpha.State.TimeMarked, "time written on the band");
        }

        [UnityTest]
        public IEnumerator Esmarch_ThreeTurnsOnUpperArm_StopsForearmBleed()
        {
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ForearmR);
            var site = _c.WoundSites[0];
            var esm = (ElasticTourniquetItem)_pouch.Find(MedItemKind.EsmarchTourniquet);
            yield return _bot.ApplyElasticTourniquet(esm, _c, Limb.RightArm, 0.4f, 3);
            Assert.IsTrue(esm.Fixed, _bot.lastStep);
            Assert.IsTrue(esm.State.FullOcclusion, $"occlusion {esm.State.Occlusion:F2}");
            yield return null;
            Assert.AreEqual(0f, site.FlowMlPerSec);
            Assert.IsTrue(_c.Protocol.log.Any(e => e.code == "tq_ok"), string.Join("|", _c.Protocol.log.Select(e => e.code)));
        }

        [UnityTest]
        public IEnumerator Elastic_WrappedWithoutPulling_DoesNotStopTheBleed_LetGoSpringsOff()
        {
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ThighR);
            var site = _c.WoundSites[0];
            float before = site.FlowMlPerSec;
            var alpha = (ElasticTourniquetItem)_pouch.Find(MedItemKind.AlphaTourniquet);
            yield return _bot.ApplyElasticTourniquet(alpha, _c, Limb.RightLeg, 0.12f, 3, 0.015f, null, false);
            Assert.GreaterOrEqual(alpha.Turns, 2, _bot.lastStep);
            Assert.IsFalse(alpha.State.FullOcclusion, "slack turns do not compress the artery");
            yield return null;
            Assert.Greater(site.FlowMlPerSec, 0f, "still bleeding");
            // let go of the roll away from the band: it springs off
            yield return _bot.Move(_bot.right, _bot.right.PalmPosition + Vector3.up * 0.3f, false, 1.5f);
            yield return _bot.Release(_bot.right);
            Assert.IsFalse(alpha.State.OnLimb, "elastic springs back off the limb");
            Assert.IsFalse(_c.Patient.tourniquets.Contains(alpha.State));
            Debug.Log($"[FMVR] slack wrap: flow before {before:F2} after {site.FlowMlPerSec:F2} ml/s");
        }
    }
}
