using System.IO;
using FieldMedic.EditorTools;
using FieldMedic.Environment;
using FieldMedic.Treatment;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9: the art drop pipeline (SourceAssets/Incoming → Resources/Art, docs/ART_IMPORT.md) validates the sub-part names
    /// and never breaks an item when they are missing; a valid model gets box colliders only and is swapped into the
    /// item's Visual child. Also the sky-text audit rule (QuestSceneAudit.SkyTextRules).
    /// </summary>
    public class ArtImportTests
    {
        const string TestRoot = "Assets/_ArtTest";
        string _src;

        [SetUp]
        public void SetUp()
        {
            _src = Path.Combine(Path.GetTempPath(), "fmvr_art_src_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_src);
            ArtModels.Loader = null;
        }

        [TearDown]
        public void TearDown()
        {
            ArtModels.Loader = null;
            ArtModels.ClearCache();
            if (AssetDatabase.IsValidFolder(TestRoot)) AssetDatabase.DeleteAsset(TestRoot);
            if (Directory.Exists(_src)) Directory.Delete(_src, true);
        }

        static GameObject Model(params string[] parts)
        {
            var root = new GameObject("model");
            float z = -0.08f;
            foreach (var p in parts)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = p;
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = new Vector3(0f, 0f, z);
                c.transform.localScale = new Vector3(0.02f, 0.005f, 0.03f);
                z += 0.16f / Mathf.Max(1, parts.Length - 1);
            }
            var mc = root.AddComponent<MeshCollider>(); // must never survive
            return root;
        }

        [Test]
        public void FindPart_IgnoresCase_AcceptsSuffixes_AndAlternatives()
        {
            var m = Model("strap.001", "Windlass_LOD0", "WindlassClip", "Flap");
            try
            {
                Assert.AreEqual("strap.001", ArtModels.FindPart(m.transform, "Strap").name);
                Assert.AreEqual("Windlass_LOD0", ArtModels.FindPart(m.transform, "Windlass").name);
                Assert.AreEqual("WindlassClip", ArtModels.FindPart(m.transform, "WindlassClip").name);
                Assert.AreEqual("Flap", ArtModels.FindPart(m.transform, "Lid|Flap").name);
                Assert.IsNull(ArtModels.FindPart(m.transform, "Buckle"));
                CollectionAssert.AreEqual(new[] { "TimeTab", "Buckle" }, ArtModels.MissingParts(m.transform, ArtModels.ByKind[MedItemKind.Tourniquet]));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void MissingSubParts_ReportOnly_NoPrefab()
        {
            var m = Model("BladeA"); // BladeB missing
            try
            {
                var res = new ArtImporter.Result { id = "shears" };
                var p = ArtImporter.BuildPrefab(m, ArtModels.ByKind[MedItemKind.TraumaShears], TestRoot + "/Resources/Art", res);
                Assert.IsNull(p);
                Assert.IsFalse(res.ok);
                CollectionAssert.AreEqual(new[] { "BladeB" }, res.missing);
                Assert.IsFalse(File.Exists(TestRoot + "/Resources/Art/shears.prefab"));
                StringAssert.Contains("BladeB", res.ToString());
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void ValidModel_BoxCollidersOnly_CentimetreModelRescaled()
        {
            var m = Model("BladeA", "BladeB");
            m.transform.localScale = Vector3.one * 100f; // exported in centimetres
            try
            {
                var res = new ArtImporter.Result { id = "shears" };
                var p = ArtImporter.BuildPrefab(m, ArtModels.ByKind[MedItemKind.TraumaShears], TestRoot + "/Resources/Art", res);
                Assert.IsNotNull(p, res.ToString());
                Assert.IsTrue(res.ok);
                Assert.IsNull(p.GetComponentInChildren<MeshCollider>(true), "no mesh colliders");
                Assert.IsNotNull(p.GetComponent<BoxCollider>(), "one simple box");
                var b = MeshyImporter.LocalBounds(p.transform);
                Assert.Less(Mathf.Max(b.size.x, b.size.y, b.size.z), 0.5f, "cm model scaled to metres: " + res);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void Import_FolderWithWrongNames_IsReported_AndUnknownIdsToo()
        {
            var fixture = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "fixtures", "meshy_dummy.fbx");
            Directory.CreateDirectory(Path.Combine(_src, "shears"));
            File.Copy(fixture, Path.Combine(_src, "shears", "my_shears.fbx"));
            Directory.CreateDirectory(Path.Combine(_src, "banana"));
            var r = ArtImporter.ImportFrom(_src, TestRoot + "/Models", TestRoot + "/Resources/Art");
            Assert.AreEqual(2, r.Count);
            var banana = r.Find(x => x.id == "banana");
            Assert.IsFalse(banana.ok);
            StringAssert.Contains("unknown item id", banana.ToString());
            var shears = r.Find(x => x.id == "shears");
            Assert.IsFalse(shears.ok, shears.ToString());
            CollectionAssert.AreEquivalent(new[] { "BladeA", "BladeB" }, shears.missing);
            Assert.IsFalse(File.Exists(TestRoot + "/Resources/Art/shears.prefab"), "nothing swapped in");
            Assert.IsTrue(File.Exists(TestRoot + "/Models/shears/shears.fbx"), "model copied for inspection");
        }

        [Test]
        public void Swap_HidesProceduralVisual_KeepsLogicParts()
        {
            var item = new GameObject("FakeShears");
            var vis = new GameObject(MedItem.VisualName).transform;
            vis.SetParent(item.transform, false);
            var merged = GameObject.CreatePrimitive(PrimitiveType.Cube);
            merged.name = "Merged_Steel";
            merged.transform.SetParent(vis, false);
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "UpperBlade";
            blade.transform.SetParent(item.transform, false);
            var art = Model("BladeA", "BladeB");
            try
            {
                var res = new ArtImporter.Result { id = "shears" };
                var p = ArtImporter.BuildPrefab(art, ArtModels.ByKind[MedItemKind.TraumaShears], TestRoot + "/Resources/Art", res);
                Assert.IsNotNull(p);
                // ArtModels.Apply(MedItem) needs a live item; the swap itself is shared → exercise it through the pouch path
                var pouch = new GameObject("FakePouch");
                var pv = new GameObject("PouchVisual").transform;
                pv.SetParent(pouch.transform, false);
                var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tray.transform.SetParent(pv, false);
                ArtModels.Loader = id => id == ArtModels.Pouch.id ? p : null;
                var swapped = ArtModels.ApplyPouch(pouch.transform);
                Assert.IsNotNull(swapped);
                Assert.AreEqual(pv, swapped.parent, "model under the Visual");
                Assert.IsFalse(tray.GetComponent<Renderer>().enabled, "procedural look hidden");
                Assert.IsNull(swapped.GetComponentInChildren<Collider>(), "item keeps its own logic collider");
                Object.DestroyImmediate(pouch);
            }
            finally { Object.DestroyImmediate(item); Object.DestroyImmediate(art); }
        }

        // ------------------------------------------------------------------ sky text rule
        static Transform Sky(Vector3 eye, float yaw, float elev, float dist, bool mirrored = false)
        {
            var pose = SkyText.PlaceFrom(eye, yaw, elev, dist);
            var root = new GameObject(SkyText.ObjectName).transform;
            root.SetPositionAndRotation(pose.position, pose.rotation);
            var t = new GameObject("Text").AddComponent<TextMeshPro>();
            t.transform.SetParent(root, false);
            t.transform.localRotation = mirrored ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            t.text = SkyText.Phrase;
            return root;
        }

        [Test]
        public void SkyText_Rule_AcceptsTheBuiltPose_RejectsMirroredLowOrMissing()
        {
            var eye = new Vector3(0.6f, 1.65f, 0.4f);
            var ok = Sky(eye, 30f, 30f, 420f);
            var mirrored = Sky(eye, 30f, 30f, 420f, true);
            var low = Sky(eye, 30f, 10f, 420f);
            var behind = Sky(eye, 210f, 30f, 420f);
            try
            {
                CollectionAssert.IsEmpty(QuestSceneAudit.SkyTextRules("t", ok, eye, 30f, 1500f));
                StringAssert.Contains("mirrored", string.Join("|", QuestSceneAudit.SkyTextRules("t", mirrored, eye, 30f, 1500f)));
                StringAssert.Contains("elevation", string.Join("|", QuestSceneAudit.SkyTextRules("t", low, eye, 30f, 1500f)));
                StringAssert.Contains("starts facing", string.Join("|", QuestSceneAudit.SkyTextRules("t", behind, eye, 30f, 1500f)));
                StringAssert.Contains("missing", string.Join("|", QuestSceneAudit.SkyTextRules("t", null, eye, 30f, 1500f)));
                StringAssert.Contains("far plane", string.Join("|", QuestSceneAudit.SkyTextRules("t", ok, eye, 30f, 300f)));
                Assert.AreEqual(30f, SkyText.ElevationFrom(eye, ok.position), 0.01f);
            }
            finally
            {
                foreach (var t in new[] { ok, mirrored, low, behind }) Object.DestroyImmediate(t.gameObject);
            }
        }
    }
}
