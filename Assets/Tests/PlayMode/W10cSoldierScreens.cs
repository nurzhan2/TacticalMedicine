using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// w10c: Erlan is the owner's soldier (Soldier_KZ). Checks that the new body keeps the casualty working (humanoid
    /// bones, capsule colliders, evac handle on the back of the vest, NPA at the nostril, wounds/TQ/seal/blanket by
    /// the real interaction code) and takes docs/screens/w10c_*.jpg.
    /// </summary>
    public class W10cSoldierScreens
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
            GameSettings.Language = GameLanguage.RU;
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
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
            foreach (var m in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            _guard?.Restore();
        }

        static void Shot(string name, Vector3 from, Vector3 look, float fov = 50f, float near = 0.01f, Vector3? up = null)
        {
            const int w = 1600, h = 900;
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = 200f;
            cam.enabled = false;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from, up ?? Vector3.up));
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

        static void HideHands()
        {
            foreach (var r in Object.FindObjectsByType<HandRig>(FindObjectsSortMode.None))
                foreach (var mr in r.GetComponentsInChildren<Renderer>(true)) mr.enabled = false;
        }

        IEnumerator HandsAway()
        {
            yield return _bot.Release(_bot.left);
            yield return _bot.Release(_bot.right);
            yield return _bot.Move(_bot.left, _c.transform.position + new Vector3(1.2f, 1.2f, -1f), false, 3f);
            yield return _bot.Move(_bot.right, _c.transform.position + new Vector3(1.3f, 1.2f, -1f), false, 3f);
            HideHands();
        }

        Vector3 Hips => _c.rig.Bone(HumanBodyBones.Hips).position;

        // ------------------------------------------------------------------ the model itself
        [UnityTest]
        public IEnumerator Soldier_IsErlan_LyingFaceFlagNpa()
        {
#if UNITY_EDITOR
            if (!File.Exists("Assets/Art/Characters/SoldierKZ/Soldier_KZ.fbx")) Assert.Ignore("no soldier model");
#endif
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            var rig = _c.rig;
            Assert.IsTrue(rig.animator.isHuman, "humanoid avatar");
            Assert.IsTrue(rig.animator.avatar != null && rig.animator.avatar.isValid, "avatar valid");
            Assert.IsTrue(_c.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("SoldierKZ"))), "Erlan wears the soldier materials");
            // capsules per bone, no mesh colliders
            Assert.GreaterOrEqual(rig.Parts.Count, 20);
            Assert.AreEqual(0, _c.GetComponentsInChildren<MeshCollider>(true).Length);
            // the evac handle sits on the back of the vest, near the upper back (not inside the body / not floating)
            var drag = _c.Interaction.Drag;
            Assert.IsNotNull(drag, "drag handle");
            float hd = Vector3.Distance(drag.transform.position, rig.ChestBone.position);
            Assert.Less(hd, 0.4f, "handle near the upper back");
            // NPA frame: nostril in front of the head bone, ears to the sides
            NpaItem.Face(rig, out var nostril, out var earL, out var earR, out var post, out var hu);
            float earGap = Vector3.Distance(earL, earR);
            Assert.That(earGap, Is.InRange(0.11f, 0.22f), "ear lobes");
            Assert.That(Vector3.Distance(nostril, rig.Bone(HumanBodyBones.Head).position), Is.InRange(0.05f, 0.2f), "nostril");
            Debug.Log($"[FMVR] w10c soldier: tris {_c.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => (int)Enumerable.Range(0, r.sharedMesh.subMeshCount).Sum(i => (long)r.sharedMesh.GetIndexCount(i)) / 3)} handle-chest {hd:F3} ears {earGap:F3}");

            HideHands();
            var hips = Hips;
            Shot("w10c_01_lying", hips + new Vector3(1.6f, 1.25f, -1.4f), hips + new Vector3(0f, 0.05f, 0.1f), 50f);
            var head = rig.Bone(HumanBodyBones.Head).position;
            Vector3 faceDir = -post;
            Shot("w10c_02_face", head + faceDir * 0.55f + hu * 0.15f, head + faceDir * 0.05f + hu * 0.06f, 40f, 0.01f, hu);
            // the sleeve patches (both arms; the flag must read the right way up)
            foreach (var (bone, tag) in new[] { (HumanBodyBones.LeftUpperArm, "L"), (HumanBodyBones.RightUpperArm, "R") })
            {
                var a = rig.Bone(bone).position;
                var b = rig.Bone(bone == HumanBodyBones.LeftUpperArm ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
                Vector3 mid = Vector3.Lerp(a, b, 0.35f);
                Vector3 outward = (mid - rig.ChestBone.position); outward = Vector3.ProjectOnPlane(outward, (b - a).normalized).normalized;
                Shot("w10c_03_sleeve_" + tag, mid + outward * 0.4f + Vector3.up * 0.15f, mid, 40f);
            }
            // NPA fully inserted: flange at the nostril, the tube going straight back
            var npa = _pouch.Find<NpaItem>();
            Assert.IsNotNull(npa);
            yield return _bot.Grab(_bot.right, npa); // out of the kit, the bare tube in the hand
            npa.ForceRelease();
            npa.GetComponent<Rigidbody>().isKinematic = true;
            // as NpaItem does when the tube is fully in: attached to the head (out of the kit slot)
            typeof(MedItem).GetMethod("AttachTo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(npa, new object[] { rig.Bone(HumanBodyBones.Head) });
            npa.SetHiddenInKit(false);
            typeof(MedItem).GetMethod("OnPouchState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(npa, new object[] { false });
            npa.transform.SetPositionAndRotation(nostril + post * (NpaItem.Length * 0.5f - 0.004f), Quaternion.LookRotation(post, hu));
            yield return null;
            Shot("w10c_04_npa", nostril + faceDir * 0.3f - hu * 0.12f + rig.BodyRight * 0.08f, nostril, 35f, 0.005f, hu);
        }

        /// <summary>Upright (crouch idle, no casualty logic): the KZ flag on the sleeve must read the right way up.</summary>
        [UnityTest]
        public IEnumerator Soldier_FlagPatch_Standing()
        {
#if UNITY_EDITOR
            if (!File.Exists("Assets/Art/Characters/SoldierKZ/Soldier_KZ.fbx")) Assert.Ignore("no soldier model");
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
#else
            GameObject prefab = null;
#endif
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _spawned.Add(go);
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
            yield return new WaitForSeconds(0.6f); // default state: crouching idle, arms down
            var anim = go.GetComponentInChildren<Animator>();
            foreach (var (bone, tag) in new[] { (HumanBodyBones.LeftUpperArm, "L"), (HumanBodyBones.RightUpperArm, "R") })
            {
                var a = anim.GetBoneTransform(bone).position;
                Vector3 side = (a - anim.GetBoneTransform(HumanBodyBones.Chest).position); side.y = 0f; side.Normalize();
                Vector3 at = a + side * 0.12f + Vector3.down * 0.02f;
                Shot("w10c_10_flag_standing_" + tag, at + side * 0.45f + go.transform.forward * 0.1f, at, 40f);
            }
            Shot("w10c_11_standing", new Vector3(0.6f, 1.3f, 2.4f), new Vector3(0f, 0.95f, 0f), 50f);
        }

        /// <summary>The friendly line / self-aid rescuers are the KZ soldier (LOD1) with a rifle in the hands.</summary>
        [UnityTest]
        public IEnumerator Rescuers_AreKzSoldiers_WithRifles()
        {
#if UNITY_EDITOR
            if (!File.Exists("Assets/Art/Characters/SoldierKZ/Soldier_KZ_LOD1.fbx")) Assert.Ignore("no soldier LOD");
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/_Battle/FriendlySoldier.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            var poses = new[] { FieldMedic.Battle.SoldierPose.Aim, FieldMedic.Battle.SoldierPose.Run, FieldMedic.Battle.SoldierPose.KneelAim };
            for (int i = 0; i < poses.Length; i++)
            {
                var go = Object.Instantiate(prefab, new Vector3(-1.2f + i * 1.2f, 0f, 0f), Quaternion.Euler(0f, 200f, 0f));
                _spawned.Add(go);
                var agent = go.GetComponent<FieldMedic.Battle.SoldierAgent>();
                if (agent != null) agent.enabled = false;
                var poser = go.GetComponent<FieldMedic.Battle.SoldierPoser>();
                poser.pose = poses[i];
                Assert.IsTrue(go.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("SoldierKZ"))), "KZ soldier body");
                Assert.IsNotNull(go.transform.Find("Rifle") ?? go.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name == "Rifle")?.transform, "rifle");
            }
            yield return new WaitForSeconds(1f);
            Shot("w10c_12_rescuers", new Vector3(0.4f, 1.5f, -3.6f), new Vector3(0f, 0.8f, 0f), 50f);
        }

        // ------------------------------------------------------------------ treatment on the new body
        [UnityTest]
        public IEnumerator Soldier_Tourniquet_Cut_ChestSeal()
        {
#if UNITY_EDITOR
            if (!File.Exists("Assets/Art/Characters/SoldierKZ/Soldier_KZ.fbx")) Assert.Ignore("no soldier model");
#endif
            yield return Casualty(WoundType.PenetratingChest, BodyRegion.ChestR, WoundVisual.ChestEntry, HitKind.Shot);
            _c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration);
            yield return new WaitForSeconds(1f);
            var cat = _pouch.Find<TourniquetCAT>();
            yield return _bot.ApplyTourniquet(cat, _c, Limb.RightLeg, 0.12f, 4, _pouch.Find<MarkerItem>());
            yield return _bot.CutClothing(_pouch.Find<ShearsItem>(), _c, Limb.RightLeg, 0.9f, 0.2f);
            var chest = _c.Patient.wounds.First(w => w.region == BodyRegion.ChestR);
            var care = _c.Interaction.Care(chest);
            yield return _bot.ChestSeal(_pouch.Find<ChestSealItem>(), care);
            yield return HandsAway();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(cat.State.Locked, $"cat {cat.State.stage} ({_bot.lastStep})");
            Assert.IsNotNull(care.Seal, "seal " + _bot.lastStep);
            var p = cat.transform.position;
            Shot("w10c_05_tq_thigh", p + new Vector3(0.3f, 0.45f, -0.3f), p, 45f);
            var site = _c.WoundSites.First(w => w != null && w.Wound != null && w.Wound.region == BodyRegion.ThighR);
            Assert.IsTrue(site.Wound.exposed, "the cut exposed the thigh wound");
            var thigh = site.transform.position;
            Shot("w10c_06_cut_exposed", thigh + Vector3.up * 0.42f + _c.rig.BodyRight * 0.12f - _c.rig.BodyUp * 0.1f, thigh, 45f);
            var s = care.Seal.transform;
            Shot("w10c_07_chest_seal", s.position + s.up * 0.45f + Vector3.up * 0.25f + Vector3.right * 0.1f, s.position, 45f);
        }

        [UnityTest]
        public IEnumerator Soldier_Drag_And_Blanket()
        {
#if UNITY_EDITOR
            if (!File.Exists("Assets/Art/Characters/SoldierKZ/Soldier_KZ.fbx")) Assert.Ignore("no soldier model");
#endif
            yield return Casualty(WoundType.ArterialLimb, BodyRegion.ThighL, WoundVisual.RaggedLaceration);
            Vector3 start = Hips;
            Vector3 dest = start + new Vector3(-2.2f, 0f, 0.4f);
            _bot.StartCoroutine(_bot.DragTo(_c, dest, 20f));
            yield return new WaitForSeconds(2.2f);
            var drag = _c.Interaction.Drag;
            Assert.IsTrue(drag.IsGrabbed, "dragging by the evac handle");
            var h = drag.transform.position;
            Shot("w10c_08_dragged", h + new Vector3(1.4f, 1.1f, -1.6f), Hips + new Vector3(0f, 0.15f, 0f), 50f);
            float t0 = Time.time;
            while (drag.IsGrabbed && Time.time - t0 < 20f) yield return null;
            float moved = Vector3.Distance(new Vector3(Hips.x, 0f, Hips.z), new Vector3(start.x, 0f, start.z));
            Assert.Greater(moved, 1.5f, "dragged");
            var blanket = _pouch.Find<BlanketItem>();
            Assert.IsNotNull(blanket);
            yield return _bot.Blanket(blanket, _c);
            yield return HandsAway();
            yield return new WaitForSeconds(0.5f);
            var side = Vector3.Cross(Vector3.up, _c.rig.BodyUp).normalized;
            Shot("w10c_09_blanket", Hips + side * 1.6f + Vector3.up * 1.3f + _c.rig.BodyUp * 0.3f, Hips + _c.rig.BodyUp * 0.25f, 50f);
        }
    }
}
