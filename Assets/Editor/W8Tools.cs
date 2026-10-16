using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Casualty;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FieldMedic.EditorTools
{
    /// <summary>w8 helpers: Erlan head probe (front profile of the balaclava/goggles), face preview renders.</summary>
    public static class W8Tools
    {
        const string ErlanPrefab = "Assets/Prefabs/Erlan.prefab";
        static string Tmp => Path.Combine(Path.GetTempPath(), "w8");

        [MenuItem("FieldMedic/W8/Probe Erlan Head")]
        public static void ProbeHead()
        {
            Directory.CreateDirectory(Tmp);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ErlanPrefab));
            go.transform.position = Vector3.zero;
            var anim = go.GetComponentInChildren<Animator>();
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            var rig = go.GetComponent<CasualtyRig>();
            rig.Init();
            Quaternion rel = head.rotation * Quaternion.Inverse(chest.rotation);
            Vector3 fwd = (rel * rig.BodyForward).normalized, up = (rel * rig.BodyUp).normalized, right = Vector3.Cross(up, fwd).normalized;
            Vector3 c = head.position + up * 0.06f;
            var sb = new StringBuilder();
            sb.AppendLine($"head {head.position:F3} fwd {fwd:F3} up {up:F3} right {right:F3} lossy {head.lossyScale:F3}");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh();
                smr.BakeMesh(m, true);
                var v = m.vertices;
                var bw = smr.sharedMesh.boneWeights;
                int hi = System.Array.IndexOf(smr.bones, head);
                sb.AppendLine($"smr {smr.name} verts {v.Length} subMeshes {smr.sharedMesh.subMeshCount} headIndex {hi} mats {string.Join(",", smr.sharedMaterials.Select(x => x != null ? x.name : "null"))}");
                // depth grid: right -0.12..0.12, up -0.16..0.10 around c, 1 cm cells, max forward
                int nx = 25, ny = 27;
                var grid = new float[nx, ny];
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) grid[i, j] = float.NaN;
                var L = smr.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 w = L.MultiplyPoint3x4(v[i]) - c;
                    float x = Vector3.Dot(w, right), y = Vector3.Dot(w, up), z = Vector3.Dot(w, fwd);
                    if (z < 0f) continue;
                    int gx = Mathf.RoundToInt((x + 0.12f) * 100f), gy = Mathf.RoundToInt((y + 0.16f) * 100f);
                    if (gx < 0 || gx >= nx || gy < 0 || gy >= ny) continue;
                    if (float.IsNaN(grid[gx, gy]) || z > grid[gx, gy]) grid[gx, gy] = z;
                }
                sb.AppendLine("front depth (cm) rows up=+0.10 .. -0.16, cols right=-0.12..+0.12");
                for (int j = ny - 1; j >= 0; j--)
                {
                    sb.Append($"{(j / 100f - 0.16f):+0.00;-0.00} ");
                    for (int i = 0; i < nx; i++) sb.Append(float.IsNaN(grid[i, j]) ? "  ." : $"{grid[i, j] * 100f,3:0}");
                    sb.AppendLine();
                }
                Object.DestroyImmediate(m);
            }
            File.WriteAllText(Path.Combine(Tmp, "head_probe.txt"), sb.ToString());
            RenderHead(go, head, fwd, up, right, "head");
            Debug.Log("[FMVR] w8 head probe -> " + Tmp);
        }

        static void RenderHead(GameObject go, Transform head, Vector3 fwd, Vector3 up, Vector3 right, string prefix)
        {
            var lightGo = new GameObject("~Key");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.6f;
            lightGo.transform.rotation = Quaternion.LookRotation(-fwd - up * 0.6f + right * 0.3f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
            Vector3 c = head.position + up * 0.06f;
            var pose = new GameObject("~Pose").transform;
            foreach (var (name, dir) in new[] { ("front", fwd), ("side", (fwd + right * 1.4f).normalized), ("profile", right) })
            {
                pose.SetPositionAndRotation(c + dir * 0.55f, Quaternion.LookRotation(-dir, up));
                ScreenshotTool.Render(pose, 32f, null, 900, 900, Path.Combine(Tmp, prefix + "_" + name + ".jpg"));
            }
            Object.DestroyImmediate(pose.gameObject);
            Object.DestroyImmediate(lightGo);
        }


        [MenuItem("FieldMedic/W8/Probe Goggles")]
        public static void ProbeGoggles()
        {
            Directory.CreateDirectory(Tmp);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ErlanPrefab));
            var rig = go.GetComponent<CasualtyRig>();
            rig.Init();
            var head = rig.Bone(HumanBodyBones.Head);
            Quaternion rel = head.rotation * Quaternion.Inverse(rig.ChestBone.rotation);
            Vector3 fwd = (rel * rig.BodyForward).normalized, up = (rel * rig.BodyUp).normalized, right = Vector3.Cross(up, fwd).normalized;
            Vector3 c = head.position + up * 0.06f;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var src = smr.sharedMesh;
            var sb = new StringBuilder();
            sb.AppendLine($"readable {src.isReadable}");
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var v = baked.vertices; var L = smr.transform.localToWorldMatrix;
            var loc = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { Vector3 w = L.MultiplyPoint3x4(v[i]) - c; loc[i] = new Vector3(Vector3.Dot(w, right), Vector3.Dot(w, up), Vector3.Dot(w, fwd)); }
            var uv = src.uv;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var t = src.GetTriangles(sm);
                var hist = new int[25];
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = loc[t[i]], b = loc[t[i + 1]], d = loc[t[i + 2]];
                    Vector3 m = (a + b + d) / 3f;
                    if (Mathf.Abs(m.x) > 0.1f || m.y < -0.02f || m.y > 0.1f || m.z < 0.0f) continue;
                    hist[Mathf.Clamp((int)(m.z * 100f), 0, 24)]++;
                }
                sb.AppendLine($"submesh {sm} tris {t.Length / 3} eye-band centroid depth histogram (cm): " + string.Join(" ", hist.Select((n, k) => k + ":" + n)));
            }
            File.WriteAllText(Path.Combine(Tmp, "goggles_probe.txt"), sb.ToString());
            // variants: strip tris of one submesh inside the eye band and in front of `minZ`
            foreach (var (sm, minZ) in new[] { (0, 0.11f), (1, 0.11f), (1, 0.13f), (0, 0.13f) })
            {
                var copy = Object.Instantiate(src);
                var t = copy.GetTriangles(sm).ToList();
                var keep = new System.Collections.Generic.List<int>();
                for (int i = 0; i < t.Count; i += 3)
                {
                    bool inBox = true;
                    for (int k = 0; k < 3; k++) { var p = loc[t[i + k]]; if (Mathf.Abs(p.x) > 0.105f || p.y < -0.005f || p.y > 0.095f || p.z < minZ) inBox = false; }
                    if (!inBox) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
                copy.SetTriangles(keep, sm);
                smr.sharedMesh = copy;
                var pose = new GameObject("~Pose").transform;
                var lightGo = new GameObject("~Key"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.6f;
                lightGo.transform.rotation = Quaternion.LookRotation(-fwd - up * 0.6f + right * 0.3f);
                Vector3 dir = (fwd + right * 0.5f).normalized;
                pose.SetPositionAndRotation(c + dir * 0.55f, Quaternion.LookRotation(-dir, up));
                ScreenshotTool.Render(pose, 32f, null, 700, 700, Path.Combine(Tmp, $"strip_{sm}_{(int)(minZ * 100)}.jpg"));
                Object.DestroyImmediate(pose.gameObject); Object.DestroyImmediate(lightGo);
                smr.sharedMesh = src;
            }
            Debug.Log("[FMVR] w8 goggles probe -> " + Tmp);
        }

        [MenuItem("FieldMedic/W8/Render Face Preview")]
        public static void FacePreview()
        {
            Directory.CreateDirectory(Tmp);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ErlanPrefab));
            var rig = go.GetComponent<CasualtyRig>();
            rig.Init();
            var c = go.GetComponent<CasualtyController>();
            var face = go.AddComponent<CasualtyFace>();
            face.casualty = c;
            face.Build();
            var head = rig.Bone(HumanBodyBones.Head);
            var chest = rig.ChestBone;
            Quaternion rel = head.rotation * Quaternion.Inverse(chest.rotation);
            Vector3 fwd = (rel * rig.BodyForward).normalized, up = (rel * rig.BodyUp).normalized, right = Vector3.Cross(up, fwd).normalized;
            float[] shock = { 0f, 0.6f, 1f };
            for (int k = 0; k < shock.Length; k++)
            {
                face.PreviewShock(shock[k], shock[k] > 0.5f ? 0.7f : 0f, k == 0 ? 1f : 0f);
                RenderHead(go, head, fwd, up, right, "face" + k);
            }
            // hands / nail beds
            var hand = rig.Bone(HumanBodyBones.LeftHand);
            var tipB = rig.Bone(HumanBodyBones.LeftMiddleDistal);
            Debug.Log($"[FMVR] w8 hand {hand.position:F3} middle distal {(tipB != null ? tipB.position.ToString("F3") : "none")} nails {face.Nails.Count}");
            Vector3 hc = tipB != null ? Vector3.Lerp(hand.position, tipB.position, 0.6f) : hand.position;
            var pose = new GameObject("~Pose").transform;
            var keyL = new GameObject("~KeyH").AddComponent<Light>(); keyL.type = LightType.Directional; keyL.intensity = 1.5f; keyL.transform.rotation = Quaternion.LookRotation(Vector3.down + Vector3.forward * 0.3f);
            pose.SetPositionAndRotation(hc + Vector3.up * 0.42f + fwd * 0.12f, Quaternion.LookRotation(hc - (hc + Vector3.up * 0.42f + fwd * 0.12f), fwd));
            ScreenshotTool.Render(pose, 24f, null, 900, 900, Path.Combine(Tmp, "hand.jpg"));
            pose.SetPositionAndRotation(hc + fwd * 0.42f + Vector3.up * 0.1f, Quaternion.LookRotation(hc - (hc + fwd * 0.42f + Vector3.up * 0.1f), Vector3.up));
            ScreenshotTool.Render(pose, 24f, null, 900, 900, Path.Combine(Tmp, "hand_front.jpg"));
            Object.DestroyImmediate(keyL.gameObject);
            Object.DestroyImmediate(pose.gameObject);
            // diagnostics: mesh edit only / face only
            foreach (var (tag, skinOn, editOn) in new[] { ("diag_meshonly", false, true), ("diag_faceonly", true, false) })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var g2 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ErlanPrefab));
                var r2 = g2.GetComponent<CasualtyRig>(); r2.Init();
                var f2 = g2.AddComponent<CasualtyFace>();
                f2.casualty = g2.GetComponent<CasualtyController>(); f2.buildSkin = skinOn; f2.flipNvgUp = editOn;
                f2.Build();
                var h2 = r2.Bone(HumanBodyBones.Head);
                Quaternion rl = h2.rotation * Quaternion.Inverse(r2.ChestBone.rotation);
                Vector3 fw = (rl * r2.BodyForward).normalized, upv = (rl * r2.BodyUp).normalized, rt = Vector3.Cross(upv, fw).normalized;
                RenderHead(g2, h2, fw, upv, rt, tag);
            }
            Debug.Log("[FMVR] w8 face preview -> " + Tmp);
        }
    }
}
