using System.Collections.Generic;
using System.IO;
using FieldMedic.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Battlefield effects built once by the scene builder: horizon smoke columns (few big billboard puffs each,
    /// wind-bent, fire glow at the base of the near ones), smouldering wrecks, the Kazakhstan flag (vertex-waved
    /// cloth on a steel pole) and the helicopter LZ (VS-17 panel + green signal smoke).
    /// </summary>
    public static class BattlefieldFx
    {
        public static readonly Vector3 Wind = new Vector3(1.6f, 0f, -0.5f); // m/s drift at altitude (from the west)

        // ------------------------------------------------------------------ smoke puff texture
        public static Texture2D PuffTexture(string dir)
        {
            string path = dir + "/FX_SmokePuff.png";
            if (!File.Exists(path))
            {
                const int N = 128;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float f = 0f, amp = 0.5f, fr = 3f;
                    for (int o = 0; o < 5; o++) { f += amp * Mathf.PerlinNoise(u * fr + o * 7.1f, v * fr + o * 3.3f); amp *= 0.5f; fr *= 2.1f; }
                    float edge = Mathf.Clamp01((1f - r) * 1.6f);
                    float a = Mathf.Clamp01(edge * edge * (0.25f + 1.2f * f) - 0.12f);
                    // fake top light: billows brighter on top, darker underneath
                    float shade = Mathf.Lerp(0.62f, 1f, Mathf.Clamp01(v * 0.8f + f * 0.5f));
                    t.SetPixel(x, y, new Color(shade, shade, shade, a));
                }
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material PuffMat(string dir, string name, Color tint, bool additive)
        {
            var m = AtmosphereBuilder.FxMat(name, tint, additive);
            if (!additive) m.SetTexture("_MainTex", PuffTexture(dir));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Soft smoke sheet (2×2 frames, 512²): each frame is a cauliflower billow of overlapping gaussian lobes with
        /// fbm detail, very soft edges (no alpha clip → no "beads"), top-lit. Used by the horizon smoke columns (w4).
        /// </summary>
        public static Texture2D SmokeSheetTexture(string dir)
        {
            string path = dir + "/FX_SmokeSheet.png";
            if (!File.Exists(path))
            {
                const int N = 512, F = 256;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color[N * N];
                var rng = new System.Random(5);
                for (int fy = 0; fy < 2; fy++)
                for (int fx = 0; fx < 2; fx++)
                {
                    var lobes = new List<Vector4>();
                    for (int k = 0; k < 9; k++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * 0.32f;
                        lobes.Add(new Vector4(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r * 0.85f, 0.13f + (float)rng.NextDouble() * 0.12f, 0f));
                    }
                    float ox = (float)rng.NextDouble() * 50f, oy = (float)rng.NextDouble() * 50f;
                    for (int y = 0; y < F; y++)
                    for (int x = 0; x < F; x++)
                    {
                        float u = (x + 0.5f) / F, v = (y + 0.5f) / F;
                        float d = 0f;
                        foreach (var l in lobes) { float dx = u - l.x, dy = v - l.y; d += Mathf.Exp(-(dx * dx + dy * dy) / (2f * l.z * l.z)); }
                        float f = 0f, amp = 0.5f, fr = 4f;
                        for (int o = 0; o < 5; o++) { f += amp * Mathf.PerlinNoise(u * fr + ox + o * 7.1f, v * fr + oy + o * 3.3f); amp *= 0.5f; fr *= 2.03f; }
                        float rr = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                        float window = Mathf.Clamp01(1f - rr * rr);                      // fade out well inside the frame
                        float dens = Mathf.Clamp01(d * 0.55f) * (0.55f + 0.9f * f) * window;
                        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dens * 1.15f));
                        float shade = Mathf.Lerp(0.55f, 1.05f, Mathf.Clamp01(v * 0.75f + f * 0.45f));  // lit from above
                        px[(fy * F + y) * N + fx * F + x] = new Color(shade, shade, shade, alpha);
                    }
                }
                t.SetPixels(px);
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material SmokeSheetMat(string dir)
        {
            var m = AtmosphereBuilder.FxMat("FX_SmokeColumn", Color.white, false);
            m.SetTexture("_MainTex", SmokeSheetTexture(dir));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ smoke columns
        public static ParticleSystem SmokeColumn(Transform parent, string name, Vector3 pos, float scale, bool fire, Material smoke, Material glow, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(30f, 40f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f * scale, 3.0f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(9f * scale, 15f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.16f, 0.15f, 0.14f, 0.9f), new Color(0.26f, 0.24f, 0.22f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;
            main.randomizeRotationDirection = 0.5f;
            // continuous column (w4, supervisor 07:00 #3): puffs overlap ~3× instead of a chain of separate beads
            var em = ps.emission; em.rateOverTime = 3.4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 7f; sh.radius = 2.2f * scale;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var fo = ps.forceOverLifetime; fo.enabled = true; fo.space = ParticleSystemSimulationSpace.World;
            fo.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.12f); fo.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.12f);
            fo.y = new ParticleSystem.MinMaxCurve(-0.07f); // buoyancy fades, column flattens out and leans downwind
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.5f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.5f);
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1.5f), new Keyframe(1f, 3.8f)));
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.5f, 0.46f), 0f), new GradientColorKey(new Color(0.95f, 0.95f, 0.95f), 0.25f), new GradientColorKey(new Color(1.6f, 1.6f, 1.6f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.05f), new GradientAlphaKey(0.42f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (smoke != null && smoke.mainTexture != null && smoke.mainTexture.name == "FX_SmokeSheet")
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = 2; tsa.numTilesY = 2;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smoke;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 4f;
            var rnd = ps.main; rnd.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)seed;

            if (fire)
            {
                var fgo = new GameObject("FireGlow");
                fgo.transform.SetParent(go.transform, false);
                var fps = fgo.AddComponent<ParticleSystem>();
                fps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var fm = fps.main;
                fm.loop = true; fm.prewarm = true;
                fm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                fm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
                fm.startSize = new ParticleSystem.MinMaxCurve(2.5f * scale, 4.5f * scale);
                fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.12f, 0.8f), new Color(1f, 0.62f, 0.25f, 0.6f));
                fm.maxParticles = 14;
                fm.simulationSpace = ParticleSystemSimulationSpace.World;
                var fem = fps.emission; fem.rateOverTime = 8f;
                var fsh = fps.shape; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = 1.4f * scale;
                var fcol = fps.colorOverLifetime; fcol.enabled = true;
                var fg = new Gradient();
                fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
                fcol.color = fg;
                var fr = fgo.GetComponent<ParticleSystemRenderer>();
                fr.sharedMaterial = glow;
                fr.shadowCastingMode = ShadowCastingMode.Off;
            }
            return ps;
        }

        /// <summary>Thin smoke curling out of a burnt-out wreck.</summary>
        public static ParticleSystem Smoulder(Transform parent, Vector3 pos, Material smoke, float scale = 1f)
        {
            var go = new GameObject("Smoulder");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * scale, 1.4f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.19f, 0.18f, 0.8f), new Color(0.3f, 0.29f, 0.27f, 0.7f));
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 2.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f * scale;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.35f); vel.y = new ParticleSystem.MinMaxCurve(0.3f); vel.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.35f);
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 5f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1.5f, 1.5f, 1.5f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.1f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smoke;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.sortMode = ParticleSystemSortMode.Distance;
            return ps;
        }

        // ------------------------------------------------------------------ flag
        public static GameObject Flag(Transform parent, Vector3 basePos, string matDir, string meshDir, Texture flagTex)
        {
            var root = new GameObject("KZ_Flag");
            root.transform.SetParent(parent, false);
            root.transform.position = basePos;
            const float poleH = 5.6f, flagL = 1.8f, flagH = 0.9f;

            // steel pole: tapered tube with a ball finial, bolted into a rubble-filled tyre/rubble
            var pole = new RuinBuilder.MeshBuild();
            const int sides = 10;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                float r0 = 0.045f, r1 = 0.03f;
                Vector3 P(float a, float r, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                var nrm = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                var c = Color.white;
                pole.Quad(new[] { P(a0, r0, -0.4f), P(a1, r0, -0.4f), P(a1, r1, poleH), P(a0, r1, poleH) }, nrm,
                    new[] { new Vector2(s / (float)sides, 0f), new Vector2((s + 1f) / sides, 0f), new Vector2((s + 1f) / sides, poleH), new Vector2(s / (float)sides, poleH) },
                    new[] { c, c, c, c });
            }
            var pm = pole.ToMesh("FlagPole");
            pm.RecalculateNormals();
            Save(pm, meshDir);
            var pgo = new GameObject("Pole");
            pgo.transform.SetParent(root.transform, false);
            pgo.AddComponent<MeshFilter>().sharedMesh = pm;
            var steel = Lit(matDir, "Bf_PoleSteel", null, null, new Color(0.32f, 0.33f, 0.32f), 0.45f, 0.7f, Vector2.one);
            pgo.AddComponent<MeshRenderer>().sharedMaterial = steel;
            var cap = pgo.AddComponent<CapsuleCollider>();
            cap.radius = 0.05f; cap.height = poleH; cap.center = new Vector3(0f, poleH * 0.5f, 0f);

            // cloth: 28 × 14 grid, x = 0 at the pole. w7: the texture is the corrected flag (W7Demo.FlagTexturePath,
            // the BlenderKit atlas crop rotated 180°): u = 0 at the pole (hoist ornament), v = 1 at the top (sun above
            // the eagle). The old mapping read the rotated atlas with only u flipped → the flag hung upside down.
            var fm = new Mesh { name = "FlagCloth" };
            const int nx = 28, ny = 14;
            var v = new Vector3[(nx + 1) * (ny + 1)];
            var uv = new Vector2[v.Length];
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = i / (float)nx * flagL, y = j / (float)ny * flagH;
                v[j * (nx + 1) + i] = new Vector3(x, y, 0f);
                uv[j * (nx + 1) + i] = FlagUV(i / (float)nx, j / (float)ny);
            }
            var t = new int[nx * ny * 6];
            int k = 0;
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i;
                t[k++] = a; t[k++] = a + nx + 1; t[k++] = a + 1;
                t[k++] = a + 1; t[k++] = a + nx + 1; t[k++] = a + nx + 2;
            }
            fm.vertices = v; fm.uv = uv; fm.triangles = t;
            fm.RecalculateNormals();
            fm.bounds = new Bounds(new Vector3(flagL * 0.5f, flagH * 0.5f, 0f), new Vector3(flagL + 0.4f, flagH + 0.4f, 0.8f));
            Save(fm, meshDir);
            var fgo = new GameObject("Cloth");
            fgo.transform.SetParent(root.transform, false);
            fgo.transform.localPosition = new Vector3(0.035f, poleH - flagH - 0.08f, 0f);
            // fly downwind
            Vector3 w = Wind; w.y = 0f;
            fgo.transform.rotation = Quaternion.LookRotation(Vector3.Cross(w.normalized, Vector3.up), Vector3.up);
            fgo.AddComponent<MeshFilter>().sharedMesh = fm;
            string mp = matDir + "/Bf_FlagKZ.mat";
            var sh = Shader.Find("FieldMedic/FlagWave");
            var fmat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (fmat == null) { fmat = new Material(sh); AssetDatabase.CreateAsset(fmat, mp); }
            fmat.shader = sh;
            if (flagTex != null) fmat.SetTexture("_BaseMap", flagTex);
            else fmat.SetColor("_BaseColor", new Color(0f, 0.68f, 0.79f)); // KZ sky blue if the licensed texture is missing
            fmat.SetFloat("_Length", flagL);
            fmat.enableInstancing = true;
            EditorUtility.SetDirty(fmat);
            var fr = fgo.AddComponent<MeshRenderer>();
            fr.sharedMaterial = fmat;
            fr.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        /// <summary>Cloth UV for grid point (x 0 = pole → 1 = fly end, y 0 = bottom → 1 = top) on the corrected flag texture.</summary>
        public static Vector2 FlagUV(float x, float y) => new Vector2(Mathf.Lerp(0.003f, 0.997f, x), Mathf.Lerp(0.004f, 0.996f, y));

        // ------------------------------------------------------------------ LZ
        public static LzMarker LandingZone(Transform parent, Vector3 pos, string matDir, string meshDir, Material smokeMat)
        {
            var root = new GameObject("LZ_Marker");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            var lz = root.AddComponent<LzMarker>();

            // VS-17 panel 1.8 × 0.6 m (fluorescent orange-red side up), pegged at the corners, wind ripples
            var panel = new RuinBuilder.MeshBuild();
            const int nx = 12, nz = 4;
            float L = 1.8f, W = 0.6f;
            float H(float x, float z) => 0.015f + Mathf.Abs(Mathf.Sin(x * 6.1f + z * 3f)) * 0.012f + Mathf.PerlinNoise(x * 4f, z * 4f) * 0.02f;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x0 = -L / 2 + L * i / nx, x1 = -L / 2 + L * (i + 1) / nx, z0 = -W / 2 + W * j / nz, z1 = -W / 2 + W * (j + 1) / nz;
                var c = Color.white;
                panel.Quad(new[] { new Vector3(x0, H(x0, z0), z0), new Vector3(x0, H(x0, z1), z1), new Vector3(x1, H(x1, z1), z1), new Vector3(x1, H(x1, z0), z0) },
                    Vector3.up, new[] { new Vector2(x0 * 2f, z0 * 2f), new Vector2(x0 * 2f, z1 * 2f), new Vector2(x1 * 2f, z1 * 2f), new Vector2(x1 * 2f, z0 * 2f) },
                    new[] { c, c, c, c });
            }
            var pm = panel.ToMesh("LZ_Panel");
            pm.RecalculateNormals(); pm.RecalculateTangents();
            Save(pm, meshDir);
            var pgo = new GameObject("VS17_Panel");
            pgo.transform.SetParent(root.transform, false);
            pgo.transform.localPosition = new Vector3(0f, BattlefieldLayout.GroundHeight(pos.x, pos.z) - pos.y, 3.5f);
            pgo.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
            pgo.AddComponent<MeshFilter>().sharedMesh = pm;
            var cloth = Lit(matDir, "Bf_VS17", "Assets/Art/Environment/Textures/Env_Hessian_Albedo.jpg", "Assets/Art/Environment/Textures/Env_Hessian_Normal.jpg",
                new Color(1f, 0.42f, 0.2f), 0.15f, 0f, new Vector2(2f, 2f), desaturate: true);
            pgo.AddComponent<MeshRenderer>().sharedMaterial = cloth;

            // signal smoke grenade beside the panel
            var sgo = new GameObject("SignalSmoke");
            sgo.transform.SetParent(root.transform, false);
            sgo.transform.localPosition = new Vector3(1.6f, 0.05f, 3.2f);
            var ps = sgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.66f, 0.3f, 0.9f), new Color(0.5f, 0.72f, 0.36f, 0.9f));
            main.maxParticles = 110;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 11f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.05f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.8f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.8f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 2.4f; lim.dampen = 0.08f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 7f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1.25f, 1.25f, 1.2f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smokeMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.sortMode = ParticleSystemSortMode.Distance;
            lz.smoke = ps;
            return lz;
        }

        // ------------------------------------------------------------------ concertina wire
        /// <summary>Coiled razor wire between two ground points: a jittered helix (Ø 0.9 m, loops ~0.25 m apart)
        /// swept as a thin 4-sided tube; sags between loops, never cover (no collider), NavMeshObstacle box.</summary>
        public static GameObject Concertina(Transform parent, string name, Vector3 a, Vector3 b, Material m, string meshDir, int seed)
        {
            var rng = new System.Random(seed);
            Vector3 dir = b - a; dir.y = 0f;
            float L = dir.magnitude; dir /= L;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            const float R = 0.45f, wire = 0.0055f;
            int loops = Mathf.RoundToInt(L / 0.24f), per = 14;
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= loops * per; i++)
            {
                float t = i / (float)(loops * per);
                float ang = i * Mathf.PI * 2f / per;
                float loop = i / (float)per;
                float jr = R * (0.9f + 0.18f * Mathf.PerlinNoise(seed + loop * 0.7f, 1.3f));
                float sag = Mathf.Sin(t * Mathf.PI * Mathf.Max(1, Mathf.RoundToInt(L / 4f))) * 0.06f;
                Vector3 c = a + dir * (t * L) + side * ((Mathf.PerlinNoise(seed * 0.3f, loop * 0.15f) - 0.5f) * 0.3f);
                float gy = BattlefieldLayout.GroundHeight(c.x, c.z);
                Vector3 p = new Vector3(c.x, gy + jr - 0.04f - Mathf.Abs(sag), c.z) + side * Mathf.Cos(ang) * jr + Vector3.up * Mathf.Sin(ang) * jr
                          + dir * (Mathf.Sin(ang) * 0.06f + (float)(rng.NextDouble() - 0.5) * 0.02f);
                pts.Add(p);
            }
            var mb = new RuinBuilder.MeshBuild();
            var col = Color.white;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[i], p1 = pts[i + 1];
                Vector3 f = (p1 - p0).normalized;
                Vector3 u = Vector3.Cross(f, Vector3.up); if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(f, Vector3.right);
                u.Normalize();
                Vector3 w = Vector3.Cross(f, u);
                for (int k = 0; k < 4; k++)
                {
                    float a0 = k * Mathf.PI * 0.5f, a1 = (k + 1) * Mathf.PI * 0.5f;
                    Vector3 o0 = (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * wire, o1 = (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * wire;
                    Vector3 n = (o0 + o1).normalized;
                    mb.Quad(new[] { p0 + o0, p1 + o0, p1 + o1, p0 + o1 }, n,
                        new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }, new[] { col, col, col, col });
                }
            }
            var mesh = mb.ToMesh(name);
            mesh.RecalculateBounds();
            Save(mesh, meshDir);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic | UnityEditor.StaticEditorFlags.OccludeeStatic);
            RuinBuilder.AddObstacle(go);
            return go;
        }

        // ------------------------------------------------------------------ helpers
        public static void Save(Mesh m, string meshDir)
        {
            string path = $"{meshDir}/{m.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        public static Material Lit(string dir, string name, string albedo, string normal, Color tint, float smooth, float metal, Vector2 tiling, bool desaturate = false)
        {
            string path = $"{dir}/{name}.mat";
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            var a = albedo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedo) : null;
            var n = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
            m.SetTexture("_BaseMap", a);
            m.SetTextureScale("_BaseMap", tiling);
            if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            // hessian is brown: a saturated dye over it needs a brighter tint to stay fluorescent
            m.SetColor("_BaseColor", desaturate && a != null ? tint * 1.9f : tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
