using System.IO;
using FieldMedic.EditorTools;
using FieldMedic.Interaction;
using FieldMedic.Treatment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// MeshyImporter with a dummy model (tools/fixtures/meshy_dummy.fbx|glb: a 1 x 0.3 x 0.1 box lying along X)
    /// dropped as "trauma_shears": real size, axis normalisation, collider + grab point, runtime swap + fallback.
    /// </summary>
    public class MeshyImporterTests
    {
        const string TestRoot = "Assets/_MeshyTest";
        static string Fixture(string ext) => Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "fixtures", "meshy_dummy" + ext);
        string _src;

        [SetUp]
        public void SetUp()
        {
            _src = Path.Combine(Path.GetTempPath(), "fmvr_meshy_src_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_src);
            MeshyModels.Loader = null;
        }

        [TearDown]
        public void TearDown()
        {
            MeshyModels.Loader = null;
            MeshyModels.ClearCache();
            if (AssetDatabase.IsValidFolder(TestRoot)) AssetDatabase.DeleteAsset(TestRoot);
            if (Directory.Exists(_src)) Directory.Delete(_src, true);
        }

        GameObject ImportDummy(string ext)
        {
            File.Copy(Fixture(ext), Path.Combine(_src, "trauma_shears" + ext));
            var built = MeshyImporter.ImportFrom(_src, TestRoot + "/Models", TestRoot + "/Resources/Meshy", true);
            Assert.AreEqual(1, built.Count, "one prefab expected");
            return built[0];
        }

        static void AssertNormalised(GameObject prefab)
        {
            var b = MeshyImporter.LocalBounds(prefab.transform);
            Assert.AreEqual(0.19f, b.size.z, 0.004f, "longest side = real trauma shears length along Z");
            Assert.Less(b.size.y, b.size.x, "thinnest side along Y");
            Assert.AreEqual(0.19f * 0.1f, b.size.y, 0.003f);
            Assert.Less(b.center.magnitude, 0.002f, "centred");
            var box = prefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "collider");
            Assert.AreEqual(b.size.z, box.size.z, 0.002f);
            Assert.IsNotNull(prefab.GetComponentInChildren<GrabPoint>(), "grab point");
        }

        [Test]
        public void Fbx_IsImportedAtRealScale_WithColliderAndGrabPoint()
        {
            AssertNormalised(ImportDummy(".fbx"));
            Assert.IsFalse(MeshyImporter.HasChanges(_src, TestRoot + "/Models"), "stamp written -> no re-import");
        }

        [Test]
        public void Glb_IsConvertedByBlender()
        {
            if (!File.Exists(MeshyImporter.Blender)) Assert.Ignore("Blender not installed");
            AssertNormalised(ImportDummy(".glb"));
        }

        [Test]
        public void UnknownFileNames_AreIgnored()
        {
            File.Copy(Fixture(".fbx"), Path.Combine(_src, "random_model.fbx"));
            Assert.AreEqual(0, MeshyImporter.ImportFrom(_src, TestRoot + "/Models", TestRoot + "/Resources/Meshy", true).Count);
        }

        static GameObject FakeItem(out Renderer procedural)
        {
            var item = new GameObject("FakeItem");
            var merged = GameObject.CreatePrimitive(PrimitiveType.Cube);
            merged.name = "Merged_Steel";
            Object.DestroyImmediate(merged.GetComponent<Collider>());
            merged.transform.SetParent(item.transform, false);
            merged.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            merged.transform.localScale = new Vector3(0.03f, 0.01f, 0.18f);
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "UpperBlade";
            blade.transform.SetParent(item.transform, false);
            procedural = merged.GetComponent<Renderer>();
            return item;
        }

        [Test]
        public void Swap_HidesProceduralLook_AndCentresModel()
        {
            var prefab = ImportDummy(".fbx");
            var item = FakeItem(out var proc);
            try
            {
                var vis = MeshyModels.Apply(item.transform, prefab, null, null, false, new[] { "UpperBlade" });
                Assert.IsNotNull(vis);
                Assert.IsFalse(proc.enabled, "procedural renderer hidden");
                Assert.IsFalse(item.transform.Find("UpperBlade").GetComponent<Renderer>().enabled, "moving part hidden");
                Assert.AreEqual(0.02f, vis.localPosition.z, 1e-4f, "model centred on the procedural look");
                Assert.IsNull(vis.GetComponentInChildren<Collider>(), "item keeps its own collider");
            }
            finally { Object.DestroyImmediate(item); }
        }

        [Test]
        public void MissingFile_KeepsProceduralFallback()
        {
            MeshyModels.Loader = _ => null;
            var go = new GameObject("Shears");
            var item = FakeItem(out var proc);
            try
            {
                Assert.IsNull(MeshyModels.Apply(item.transform, MeshyModels.Load("trauma_shears"), null));
                Assert.IsTrue(proc.enabled);
            }
            finally { Object.DestroyImmediate(item); Object.DestroyImmediate(go); }
        }

        [Test]
        public void EveryItemKindWithAMeshyFile_HasARealSize()
        {
            foreach (var kv in MeshyModels.ByKind)
            {
                Assert.IsTrue(MeshyModels.RealLength.ContainsKey(kv.Value.file), kv.Value.file);
                Assert.That(kv.Value.length, Is.InRange(0.05f, 0.4f));
            }
        }
    }
}
