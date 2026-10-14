using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FieldMedic.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// GDD_V2 §7 budget check of the Battlefield scene → docs/PERF_V2.md. From several player spots × 8 headings
    /// (100° FOV, 420 m far clip) it counts, per view: triangles of the LOD level each LODGroup would actually
    /// show at that distance (+ non-LOD renderers), frustum-culled by bounds, and a draw-call estimate (static
    /// batching merges static renderers sharing a material; dynamic renderers × sub-meshes; particles/lines 1
    /// each). The measured profiler capture (render counters in play mode) is appended by the PlayMode test
    /// BattlefieldScreenshots.Profile_Render_Stats_From_Player_Spots. Batch (with graphics): -executeMethod FieldMedic.EditorTools.PerfReportV2.Run
    /// </summary>
    public static class PerfReportV2
    {
        const int TriBudget = 500000, DrawBudget = 150, ShadowCasterBudget = 4;

        [MenuItem("FieldMedic/Perf Report (Battlefield V2)")]
        public static void Run()
        {
            int code = 0;
            try { Report(); }
            catch (System.Exception e) { Debug.LogError("[FMVR] PerfReportV2 failed: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void Report()
        {
            EditorSceneManager.OpenScene(BattlefieldLayout.ScenePath, OpenSceneMode.Single);
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var lodOf = new Dictionary<Renderer, (LODGroup g, int level)>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) lodOf[r] = (g, i);
            }

            var spots = new (string name, Vector3 eye)[]
            {
                ("player start (house A)", BattlefieldLayout.PlayerStart + Vector3.up * 1.6f),
                ("treatment spot (kneeling)", BattlefieldLayout.TreatmentSpot + Vector3.up * 1.0f),
                ("behind the sandbags", new Vector3(-3.2f, 1.1f, 5.8f)),
                ("Erlan in the open", BattlefieldLayout.ErlanFallPoint + Vector3.up * 1.6f),
                ("LZ", BattlefieldLayout.LandingZone + Vector3.up * 1.6f),
            };
            var cam = new GameObject("~perfcam").AddComponent<Camera>();
            cam.fieldOfView = 100f; cam.aspect = 1f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 420f;
            var rows = new List<string>();
            long worstTris = 0; int worstDraws = 0; string worstName = "";
            Vector3 worstPos = Vector3.zero; float worstYaw = 0f;
            foreach (var (name, eye) in spots)
            {
                long spotTris = 0; int spotDraws = 0; float spotYaw = 0f;
                for (int i = 0; i < 8; i++)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, i * 45f, 0f));
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    var vis = new List<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                        if (lodOf.TryGetValue(r, out var l) && ActiveLod(l.g, cam) != l.level) continue;
                        vis.Add(r);
                    }
                    long tris = vis.Sum(r => (long)Tris(r));
                    int draws = EstimateDraws(vis);
                    if (tris > spotTris) { spotTris = tris; spotYaw = i * 45f; }
                    spotDraws = Mathf.Max(spotDraws, draws);
                }
                rows.Add($"| {name} | {spotTris:N0} (heading {spotYaw:F0}°) | {spotDraws} |");
                if (spotTris > worstTris) { worstTris = spotTris; worstName = name; worstPos = eye; worstYaw = spotYaw; }
                worstDraws = Mathf.Max(worstDraws, spotDraws);
            }

            Object.DestroyImmediate(cam.gameObject);

            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int shadowLights = lights.Count(l => l.shadows != LightShadows.None && l.enabled);
            int casters = renderers.Count(r => r.shadowCastingMode != ShadowCastingMode.Off && !(r is ParticleSystemRenderer));
            long total = renderers.Sum(r => (long)Tris(r));
            int lodGroups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length;
            int staticR = renderers.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));
            int particles = renderers.Count(r => r is ParticleSystemRenderer);
            var maxTex = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t => t != null).Distinct()
                .Select(t => { var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter; return ti != null ? Mathf.Min(ti.maxTextureSize, Mathf.Max(t.width, t.height)) : Mathf.Max(t.width, t.height); })
                .DefaultIfEmpty(0).Max();
            bool occl = StaticOcclusionCulling.isRunning || HasOcclusionData();

            var sb = new StringBuilder();
            sb.AppendLine("# Performance report — Battlefield (V2), Quest 3S budget (GDD_V2 §7)");
            sb.AppendLine();
            sb.AppendLine("Generated by `PerfReportV2.Run` (editor analysis: LOD-aware, frustum-culled by bounds, no occlusion; verify on device with OVR Metrics Tool).");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value | Budget | Status |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| Triangles in view, worst spot/heading ({worstName}) | {worstTris:N0} | {TriBudget:N0} | {(worstTris <= TriBudget ? "OK" : "OVER")} |");
            sb.AppendLine($"| Draw calls estimate, worst view (static batching per material) | {worstDraws} | {DrawBudget} | {(worstDraws <= DrawBudget ? "OK" : "OVER")} |");
            sb.AppendLine($"| Shadow-casting lights | {shadowLights} | 1 main | {(shadowLights <= 1 ? "OK" : "OVER")} |");
            sb.AppendLine($"| Max texture size | {maxTex} | 2048 | {(maxTex <= 2048 ? "OK" : "OVER")} |");
            sb.AppendLine($"| Triangles in scene (all LODs) | {total:N0} | — | info |");
            sb.AppendLine($"| Renderers / static-batched / LODGroups / particle systems | {renderers.Length} / {staticR} / {lodGroups} / {particles} | — | info |");
            sb.AppendLine($"| Renderers casting shadows (into the one directional map) | {casters} | — | info |");
            sb.AppendLine($"| Occlusion culling data baked | {(occl ? "yes" : "no")} | — | info |");
            sb.AppendLine();
            sb.AppendLine("## Per spot (worst of 8 headings)");
            sb.AppendLine("| Spot | Triangles in view | Draw calls est. |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in rows) sb.AppendLine(r);
            sb.AppendLine();
            sb.AppendLine("Notes: one realtime directional light (overcast sun, soft shadows, 32 m shadow distance) is the only shadow caster light; " +
                          "ambient + reflections come from the HDRI skybox. Smoke columns are 7 pooled particle systems (≤ 48 big puffs each), grass is " +
                          "combined into 12 static meshes, small debris switches LODs early and culls when tiny.");
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/PERF_V2.md", sb.ToString());
            Debug.Log($"[FMVR] perfV2: worst view {worstTris} tris ({worstName}), draws {worstDraws}, shadows {shadowLights}, total {total}, occlusion data {occl}");
        }

        /// <summary>Baked occlusion = the scene references an OcclusionCullingData asset next to it.</summary>
        static bool HasOcclusionData() =>
            File.Exists(Path.Combine(Path.GetDirectoryName(BattlefieldLayout.ScenePath), BattlefieldLayout.SceneName, "OcclusionCullingData.asset"));

        /// <summary>LOD level a group shows from this camera (Unity's screen-relative height rule); lods.Length = culled.</summary>
        public static int ActiveLod(LODGroup g, Camera cam)
        {
            var lods = g.GetLODs();
            Vector3 c = g.transform.TransformPoint(g.localReferencePoint);
            float size = g.size * Mathf.Max(g.transform.lossyScale.x, Mathf.Max(g.transform.lossyScale.y, g.transform.lossyScale.z));
            float dist = Vector3.Distance(cam.transform.position, c);
            float rel = size / (2f * Mathf.Max(0.01f, dist) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) return i;
            return lods.Length;
        }

        static int Tris(Renderer r)
        {
            Mesh m = null;
            if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
            else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
            else if (r is LineRenderer lr) return Mathf.Max(0, lr.positionCount - 1) * 2;
            else if (r is ParticleSystemRenderer pr) return pr.GetComponent<ParticleSystem>().main.maxParticles * 2;
            if (m == null) return 0;
            long t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
            return (int)t;
        }

        static int EstimateDraws(IEnumerable<Renderer> rs)
        {
            var staticMats = new HashSet<Material>();
            int dynamic = 0;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) { dynamic++; continue; }
                bool isStatic = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (isStatic) staticMats.Add(m); else dynamic++;
                }
            }
            return staticMats.Count + dynamic;
        }
    }
}
