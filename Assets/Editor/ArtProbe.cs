using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>w9d: dumps the Unity-space layout of the delivered models (Assets/Art/Items/*/*.fbx) to logs/art_probe.txt:
    /// per object local position / rotation / axes and mesh bounds in model space. Used to map model axes onto item frames.</summary>
    public static class ArtProbe
    {
        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories("Assets/Art/Items").OrderBy(d => d))
            {
                var fbx = Directory.GetFiles(dir, "*.fbx").FirstOrDefault();
                if (fbx == null) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx.Replace(Path.DirectorySeparatorChar, '/'));
                if (model == null) { sb.AppendLine("== " + fbx + " NOT IMPORTED"); continue; }
                var go = Object.Instantiate(model);
                sb.AppendLine("== " + Path.GetFileName(dir) + "  root rot " + go.transform.rotation.eulerAngles + " scale " + go.transform.localScale);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == go.transform) continue;
                    string line = $"  {t.name,-28} parent={t.parent.name,-24} pos={F(go.transform.InverseTransformPoint(t.position))} fwd={F(go.transform.InverseTransformDirection(t.forward))} up={F(go.transform.InverseTransformDirection(t.up))}";
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var r = t.GetComponent<Renderer>();
                        var b = r.bounds;
                        line += $" bmin={F(b.min)} bmax={F(b.max)} size={F(b.size)} mats={string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}";
                    }
                    sb.AppendLine(line);
                }
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/art_probe.txt", sb.ToString());
            Debug.Log("[FMVR] art probe written");
        }

        /// <summary>Batch: import SourceAssets/Incoming, dump the layout, render the contact sheet docs/screens/w9_art_sheet.jpg.</summary>
        public static void ImportAndSheet()
        {
            int code = 0;
            try
            {
                var res = ArtImporter.ImportFrom(ArtImporter.IncomingDir, ArtImporter.ModelRoot, ArtImporter.PrefabRoot);
                if (!res.All(r => r.ok)) code = 1;
                Dump();
                Sheet();
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] art sheet: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void SheetBatch()
        {
            int code = 0;
            try { Sheet(); } catch (System.Exception e) { Debug.LogError("[FMVR] art sheet: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>All Resources/Art prefabs on a grey table, each in its item frame (Z = long side, Y = up), from above-front.</summary>
        public static void Sheet()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var lightGo = new GameObject("Sun");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.3f; l.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
            var table = GameObject.CreatePrimitive(PrimitiveType.Quad);
            table.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            table.transform.localScale = new Vector3(3f, 3f, 1f);
            var tm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            tm.SetColor("_BaseColor", new Color(0.42f, 0.42f, 0.44f));
            table.GetComponent<Renderer>().sharedMaterial = tm;
            var prefabs = Resources.LoadAll<GameObject>("Art").Where(p => p.name != "ifak").OrderBy(p => p.name).ToArray();
            ScreenshotTool.JpgQuality = 88;
            Directory.CreateDirectory("docs/screens");
            var cam = new GameObject("pose").transform;
            // two pages of 10, each item twice: as imported (left) and flipped upside down (right) → which face is "up"
            for (int page = 0; page * 10 < prefabs.Length; page++)
            {
                var spawned = new System.Collections.Generic.List<GameObject>();
                for (int k = 0; k < 10 && page * 10 + k < prefabs.Length; k++)
                {
                    var pf = prefabs[page * 10 + k];
                    for (int flip = 0; flip < 2; flip++)
                    {
                        var go = (GameObject)Object.Instantiate(pf);
                        if (flip == 1) go.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
                        var b = ArtImporter.BoundsOf(null, new[] { go.transform });
                        Vector3 slot = new Vector3((k % 5 - 2f) * 0.2f + (flip == 0 ? -0.045f : 0.045f), 0f, -(k / 5) * 0.24f + 0.12f);
                        go.transform.position = slot + new Vector3(0f, -b.min.y + 0.001f, 0f) - new Vector3(b.center.x, 0f, b.center.z);
                        spawned.Add(go);
                    }
                    var label = new GameObject("L").AddComponent<TextMesh>();
                    label.text = pf.name; label.characterSize = 0.0045f; label.fontSize = 64; label.anchor = TextAnchor.MiddleCenter;
                    label.color = Color.black;
                    label.transform.position = new Vector3((k % 5 - 2f) * 0.2f, 0.002f, -(k / 5) * 0.24f + 0.12f - 0.1f);
                    label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    spawned.Add(label.gameObject);
                }
                cam.position = new Vector3(0f, 0.62f, -0.32f);
                cam.LookAt(new Vector3(0f, 0f, 0.0f));
                ScreenshotTool.Render(cam, 55f, null, 1600, 1000, "docs/screens/w9_art_sheet" + (page + 1) + ".jpg");
                foreach (var g in spawned) Object.DestroyImmediate(g);
            }
            // the kit: open pose as delivered, front and three-quarter
            var kit = Resources.Load<GameObject>("Art/ifak");
            if (kit != null)
            {
                foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) if (g.GetComponent<Renderer>() != null && g != table) Object.DestroyImmediate(g);
                var k = (GameObject)Object.Instantiate(kit);
                k.transform.position = new Vector3(0f, 0.4f, 0f);
                foreach (var t in k.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Slot_"))
                    {
                        var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.DestroyImmediate(m.GetComponent<Collider>());
                        m.transform.SetParent(t, false); m.transform.localScale = new Vector3(0.004f, 0.03f, 0.012f); // long along slot Y, thick along Z
                    }
                cam.position = new Vector3(0.35f, 0.75f, 0.75f);
                cam.LookAt(new Vector3(0f, 0.32f, 0f));
                ScreenshotTool.Render(cam, 50f, null, 1400, 1000, "docs/screens/w9_art_kit_raw.jpg");
                cam.position = new Vector3(0f, 0.3f, -0.8f); cam.LookAt(new Vector3(0f, 0.32f, 0f));
                ScreenshotTool.Render(cam, 50f, null, 1400, 1000, "docs/screens/w9_art_kit_raw_back.jpg");
            }
            Debug.Log("[FMVR] art sheet rendered");
        }

        static string F(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
    }
}
