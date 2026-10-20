using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// w9d screenshots of the Astra models in the game (docs/screens/w9_art_*.jpg): the real IFAK open with every item in
    /// its slot (player's view + close), closed on the back panel, each tourniquet type applied on Erlan (CAT, Alpha,
    /// Esmarch), the shears cutting the trouser leg, the chest seal applied. Everything is done by the medic bot through
    /// the real interaction code on a ground patch with sun + sky.
    /// </summary>
    public class W9ArtScreens
    {
        readonly List<Object> _spawned = new List<Object>();
        GlobalStateGuard _guard;
        CasualtyController _c;
        MedicBot _bot;
        IfakPouch _pouch;
        int _quality;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return FreshScene.Ensure();
            _guard = new GlobalStateGuard();
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            GameSettings.Language = GameLanguage.RU;
            _quality = QualitySettings.GetQualityLevel();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "~Ground";
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
#if UNITY_EDITOR
            var gm = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Battlefield/Materials/Bf_Ground.mat");
            if (gm != null) floor.GetComponent<Renderer>().sharedMaterial = gm;
#endif
            _spawned.Add(floor);
            var sun = new GameObject("~Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            _spawned.Add(sun.gameObject);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.47f, 0.5f);
            var ear = new GameObject("~Listener");
            ear.AddComponent<AudioListener>();
            _spawned.Add(ear);
        }

        [TearDown]
        public void TearDown()
        {
            IfakPouch.UseTray = true;
            QualitySettings.SetQualityLevel(_quality, true);
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            _guard?.Restore();
        }

        static void Shot(string name, Vector3 from, Vector3 look, float fov = 50f, float near = 0.01f)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = 200f;
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
            Directory.CreateDirectory("docs/screens");
            File.WriteAllBytes(Path.Combine("docs/screens", name + ".jpg"), tex.EncodeToJPG(85));
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
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

        IEnumerator Casualty(WoundType type, BodyRegion region, WoundVisual visual, HitKind hit = HitKind.Explosion)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
#else
            GameObject prefab = null;
#endif
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _spawned.Add(go);
            _c = go.GetComponent<CasualtyController>();
            _c.voice.muted = true;
            yield return null;
            _c.AddWound(type, region, visual);
            _c.Hit(hit);
            float t0 = Time.time;
            while (_c.IsFalling && Time.time - t0 < 9f) yield return null;
            yield return new WaitForSeconds(2.5f);
            _c.Protocol.phase = Phase.TacticalFieldCare;
            var hips = _c.rig.Bone(HumanBodyBones.Hips).position;
            MakeBot(hips + new Vector3(0.4f, 0.2f, 0f));
            var pgo = new GameObject("~PlayerIFAK");
            _spawned.Add(pgo);
            pgo.transform.SetPositionAndRotation(hips + new Vector3(0.8f, 0.02f, 0.35f), Quaternion.Euler(0f, -90f, 0f));
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.buildOnStart = false;
            _pouch.Build();
            yield return null;
        }

        /// <summary>The long gun lying on his leg hides the bands in the close-ups (screenshots only).</summary>
        void HideLongGear()
        {
            if (_c == null) return;
            foreach (var r in _c.GetComponentsInChildren<MeshRenderer>(true))
            {
                var e = r.bounds.size;
                if (Mathf.Max(e.x, Mathf.Max(e.y, e.z)) > 0.55f) { Debug.Log("[FMVR] screens: hiding " + r.name); r.enabled = false; }
            }
        }

        static void HideHands()
        {
            foreach (var r in Object.FindObjectsByType<HandRig>(FindObjectsSortMode.None))
                foreach (var mr in r.GetComponentsInChildren<Renderer>(true)) mr.enabled = false;
        }

        // ------------------------------------------------------------------ the kit
        [UnityTest]
        public IEnumerator Kit_OpenWithAllItems_AndClosed()
        {
            if (ArtModels.Load(ArtModels.Pouch.id) == null) Assert.Ignore("no imported art");
            var head = new GameObject("~Head").transform;
            head.position = new Vector3(0f, 1.62f, 0f);
            _spawned.Add(head.gameObject);
            MakeBot(head.position + new Vector3(0f, -0.4f, 0.3f));
            var pgo = new GameObject("~PlayerIFAK");
            _spawned.Add(pgo);
            IfakPouch.UseTray = false; // the worn art kit (folding panels) — w10 players get the tray (W10Screens)
            _pouch = pgo.AddComponent<IfakPouch>();
            _pouch.bodyLocked = true;
            _pouch.head = head;
            yield return null;
            yield return null;
            HideHands();
            Vector3 k = _pouch.transform.position;
            Shot("w9_art_kit_closed", k + new Vector3(0.32f, 0.3f, -0.28f), k + new Vector3(0f, 0.02f, 0.02f), 45f);
            yield return _bot.OpenPouch(_pouch, _bot.left);
            yield return new WaitForSeconds(1f);
            HideHands();
            k = _pouch.transform.position;
            Vector3 center = _pouch.transform.TransformPoint(new Vector3(0.03f, 0.04f, -0.04f));
            Shot("w9_art_kit_open_view", head.position + new Vector3(0f, 0.02f, -0.02f), center, 62f);
            Shot("w9_art_kit_open", center + _pouch.transform.up * 0.62f + _pouch.transform.right * 0.12f - _pouch.transform.forward * 0.12f, center, 48f);
            Shot("w9_art_kit_open_side", center + _pouch.transform.right * 0.55f + _pouch.transform.up * 0.35f - _pouch.transform.forward * 0.2f, center, 48f);
        }

        // ------------------------------------------------------------------ tourniquets
        [UnityTest]
        public IEnumerator Tourniquets_Cat_Alpha_Esmarch_Applied()
        {
            if (ArtModels.Load(ArtModels.Pouch.id) == null) Assert.Ignore("no imported art");
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var cat = _pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, _c, Limb.RightLeg, 0.12f, 4, _pouch.Find<MarkerItem>());
            var alpha = (ElasticTourniquetItem)_pouch.Find(MedItemKind.AlphaTourniquet);
            yield return _bot.ApplyElasticTourniquet(alpha, _c, Limb.LeftLeg, 0.2f, 4);
            var esm = (ElasticTourniquetItem)_pouch.Find(MedItemKind.EsmarchTourniquet);
            yield return _bot.ApplyElasticTourniquet(esm, _c, Limb.RightArm, 0.4f, 3);
            yield return _bot.Write(_pouch.Find<MarkerItem>(), alpha.TimeLabelPoint, _bot.left);
            yield return _bot.Release(_bot.left);
            yield return _bot.Release(_bot.right);
            yield return _bot.Move(_bot.left, _c.transform.position + new Vector3(1.2f, 1.2f, -1f), false, 3f);
            yield return _bot.Move(_bot.right, _c.transform.position + new Vector3(1.3f, 1.2f, -1f), false, 3f);
            yield return new WaitForSeconds(0.3f);
            HideHands();
            Assert.IsTrue(cat.State.Locked && alpha.Fixed && esm.Fixed, $"cat {cat.State.stage} alpha {alpha.State.stage} esm {esm.State.stage} ({_bot.lastStep})");
            Shoot("w9_art_tq_cat", cat.transform.position, 0.36f);
            Shoot("w9_art_tq_alpha", alpha.transform.position, 0.32f);
            Shoot("w9_art_tq_esmarch", esm.transform.position, 0.3f);
            var hips = _c.rig.Bone(HumanBodyBones.Hips).position;
            Shot("w9_art_tq_all", hips + new Vector3(0.9f, 0.85f, -0.6f), hips + new Vector3(0f, 0f, -0.1f), 50f);
        }

        void Shoot(string name, Vector3 target, float dist, Vector3? along = null)
        {
            // from above and a little to the side of the medic (never through the body / kit), or along a given normal
            Vector3 dir = (along ?? new Vector3(0.3f, 1f, -0.25f)).normalized;
            Shot(name, target + dir * (dist + 0.1f), target, 45f);
        }

        // ------------------------------------------------------------------ shears and chest seal
        [UnityTest]
        public IEnumerator Shears_Cutting_And_ChestSeal_Applied()
        {
            if (ArtModels.Load(ArtModels.Pouch.id) == null) Assert.Ignore("no imported art");
            yield return Casualty(WoundType.PenetratingChest, BodyRegion.ChestR, WoundVisual.ChestEntry, HitKind.Shot);
            var shears = _pouch.Find<ShearsItem>();
            // cut the trouser leg and keep squeezing mid-cut for the shot
            yield return _bot.Grab(_bot.right, shears);
            var rig = _c.rig;
            Vector3 a = LimbMath.PointAt(rig, Limb.RightLeg, 0.85f), b = LimbMath.PointAt(rig, Limb.RightLeg, 0.55f);
            Vector3 up = LimbMath.UpAround(rig, Limb.RightLeg, 0.7f);
            float r = LimbMath.RadiusAt(Limb.RightLeg, 0.7f) + 0.024f; // on the trouser cloth
            yield return _bot.MoveHeld(_bot.right, shears.transform, shears.TipPosition, a + up * (r + 0.004f));
            yield return _bot.AimHeld(_bot.right, shears.transform, Quaternion.LookRotation((b - a).normalized, up));
            yield return _bot.MoveHeld(_bot.right, shears.transform, shears.TipPosition, a + up * (r + 0.004f));
            yield return _bot.SetTrigger(_bot.right, 1f);
            yield return _bot.MoveHeld(_bot.right, shears.transform, shears.TipPosition, b + up * (r + 0.004f), 0.25f);
            HideHands();
            Shoot("w9_art_shears", shears.transform.position, 0.26f, shears.transform.up + Vector3.up * 0.8f + (shears.transform.position - _c.transform.position).normalized * 0.5f);
            yield return _bot.SetTrigger(_bot.right, 0f);
            yield return _bot.Release(_bot.right);

            // a second seal torn open in the hand: the delivered round Seal with its Liner
            var spare = _pouch.Find<ChestSealItem>(1);
            yield return _bot.Grab(_bot.left, spare);
            Vector3 hold = _c.transform.position + new Vector3(0.7f, 0.65f, -0.5f);
            yield return _bot.Move(_bot.left, hold, false, 2f);
            yield return _bot.Grab(_bot.right, spare.TearNotch, spare.TearNotch.transform.position);
            yield return _bot.Move(_bot.right, _bot.right.PalmPosition + (spare.TearNotch.transform.position - spare.transform.position).normalized * 0.12f, false, 0.4f);
            yield return _bot.Release(_bot.right);
            yield return _bot.Move(_bot.right, hold + new Vector3(0.5f, 0.3f, 0f), false, 2f);
            yield return _bot.AimHeld(_bot.left, spare.transform, Quaternion.LookRotation(Vector3.forward, Vector3.up));
            HideHands();
            Shot("w9_art_chestseal_torn", spare.transform.position + new Vector3(0.12f, 0.24f, -0.14f), spare.transform.position, 45f);
            yield return _bot.Move(_bot.left, hold + new Vector3(-1.2f, 0.2f, 0f), false, 2f);
            yield return _bot.Release(_bot.left);

            var w = _c.Patient.wounds[0];
            var care = _c.Interaction.Care(w);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(), care);
            yield return _bot.Release(_bot.left);
            yield return _bot.Release(_bot.right);
            yield return _bot.Move(_bot.left, _c.transform.position + new Vector3(1.2f, 1.2f, -1f), false, 3f);
            yield return _bot.Move(_bot.right, _c.transform.position + new Vector3(1.3f, 1.2f, -1f), false, 3f);
            HideHands();
            var seal = care.Seal;
            Assert.IsNotNull(seal, _bot.lastStep);
            // the seal lies on the skin under the plate carrier (one skinned mesh with the body): the camera sits
            // between the carrier and the chest, looking straight down at the seal
            Shot("w9_art_chestseal", seal.transform.position + seal.transform.up * 0.065f + seal.transform.right * 0.025f, seal.transform.position, 80f, 0.003f);
            Shot("w9_art_chestseal_wide", seal.transform.position + seal.transform.up * 0.55f + Vector3.up * 0.4f + Vector3.right * 0.15f, seal.transform.position, 50f);
        }
    }
}
