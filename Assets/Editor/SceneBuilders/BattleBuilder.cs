using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.Battle;
using FieldMedic.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// w3: adds the continuous battle to the Battlefield scene (called by <see cref="BattlefieldBuilder.Build"/>
    /// after the atmosphere): BattleDirector + settings asset, BattleAudio (beds + pooled voices), BattleFx, near
    /// infantry (enemy + friendly agents), enemy fire at the player + suppression vignette, vehicles on splines,
    /// the instanced mid crowd, horizon impostor lines, the sky layer (jet + helicopter), and two smoke grenades on
    /// the player's vest. Everything lives under a separate "Battle" root (not touched by the static merging,
    /// LOD tuning or occlusion of the battlefield builder).
    /// </summary>
    public static class BattleBuilder
    {
        public const string SettingsPath = "Assets/Data/BattleSettings.asset";

        // ---- layout (shared with tests through BattleLayout below)
        public static BattleSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<BattleSettings>(SettingsPath);
            if (s == null)
            {
                Directory.CreateDirectory("Assets/Data");
                s = ScriptableObject.CreateInstance<BattleSettings>();
                AssetDatabase.CreateAsset(s, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return s;
        }

        /// <summary>Batch: re-import the ground textures (import rules changed in w3), rebuild Battlefield + Boot.</summary>
        public static void BuildBatchW3()
        {
            bool ok = false;
            try
            {
                foreach (var p in GroundTextureFix.Ground) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                BattlefieldBuilder.Build();
                BootSceneBuilder.Build();
                ok = true;
            }
            catch (System.Exception e) { Debug.LogError("[FMVR] w3 build failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static Vector3 G(float x, float z, float up = 0f) => new Vector3(x, BattlefieldLayout.GroundHeight(x, z) + up, z);

        public static void Build(GameObject rig, Transform eye)
        {
            BattleAssets.Prepare();
            var settings = Settings();
            var root = new GameObject("Battle").transform;

            // ---- audio
            var audio = new GameObject("BattleAudio").AddComponent<BattleAudio>();
            audio.transform.SetParent(root, false);
            audio.listener = eye;
            audio.rifleBurst = AtmosphereBuilder.Clips("gunfire_ak");
            audio.mgBurst = AtmosphereBuilder.Clips("mg_burst");
            audio.distantFire = AtmosphereBuilder.Clips("gunfire_distant");
            audio.bulletCrack = AtmosphereBuilder.Clips("bullet_crack");
            audio.bulletWhiz = AtmosphereBuilder.Clips("bullet_whiz");
            audio.impactDirt = AtmosphereBuilder.Clips("bullet_impact_dirt");
            audio.impactHard = AtmosphereBuilder.Clips("impact_concrete");
            audio.tankCannon = AtmosphereBuilder.Clips("tank_cannon");
            audio.tankEngine = AtmosphereBuilder.Clips("tank_engine");
            audio.rocketLaunch = AtmosphereBuilder.Clips("rocket_launch");
            audio.bombLarge = AtmosphereBuilder.Clips("bomb_large");
            audio.jetFlyby = AtmosphereBuilder.Clips("jet_flyby");
            audio.heliLoop = AtmosphereBuilder.Clips("heli_flyby");
            audio.smokeHiss = AtmosphereBuilder.Clips("smoke_grenade");
            audio.pinPull = AtmosphereBuilder.Clips("pin_pull");
            audio.artilleryBed = AtmosphereBuilder.Clips("artillery_rumble");
            audio.battleBed = AtmosphereBuilder.Clips("battle_bed");
            audio.breathing = AtmosphereBuilder.Clips("breath_heavy");
            var mix = AudioMixerBuilder.Ensure();
            if (mix != null) { audio.sfxGroup = mix.sfx; audio.ambienceGroup = mix.ambience; }

            // ---- fx
            var fx = new GameObject("BattleFx").AddComponent<BattleFx>();
            fx.transform.SetParent(root, false);
            fx.head = eye;
            fx.glowMaterial = AtmosphereBuilder.FxMat("FX_Glow", Color.white, true, 2f);
            fx.smokeMaterial = BattlefieldFx.PuffMat(BattlefieldBuilder.ArtDir, "FX_SmokePuff", Color.white, false);
            fx.dirtMaterial = AtmosphereBuilder.FxMat("FX_Dirt", Color.white, false);
            var scorch = BattleAssets.Mat("Battle_Scorch", "FieldMedic/Particle");
            scorch.SetTexture("_MainTex", BattleAssets.ScorchTexture());
            scorch.SetColor("_TintColor", new Color(1f, 1f, 1f, 0.9f));
            scorch.SetFloat("_Boost", 1f);
            scorch.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            scorch.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            scorch.renderQueue = 2460; // on the ground before other transparents
            fx.scorchMaterial = scorch;
            fx.scorchMesh = BattleAssets.GroundQuad();

            // ---- director
            var director = new GameObject("BattleDirector").AddComponent<BattleDirector>();
            director.transform.SetParent(root, false);
            director.settings = settings;
            director.head = eye;
            director.ourTargets = OurTargets();
            director.mgNests = MgNests();

            BuildInfantry(root, settings, eye);
            BuildEnemyFire(root, settings, eye);
            BuildVehicles(root, audio);
            BuildCrowd(root, eye, settings);
            BuildHorizon(root, settings);
            BuildAir(root, eye);
            BuildSmokeGrenades(rig, eye);
            Debug.Log("[FMVR] battle layers built");
        }

        // ================================================================== layout data
        public static Vector3[] EnemySpots() => new[]
        {
            G(-8f, 36.6f), G(10f, 43f), G(-34f, 48f), G(36f, 56.5f), G(4f, 65.5f), G(-29.5f, 50.5f),
            G(-18f, 44f), G(20f, 41f), G(-2f, 51f), G(16f, 55f), G(-24f, 60f), G(28f, 48f), G(-12f, 57f), G(8f, 39.5f),
        };

        public static Vector3[] FriendlySpots() => new[]
        {
            G(-5.2f, 5.75f), G(-3.5f, 5.8f), G(-0.9f, 5.85f), G(-10.4f, 1.5f), G(-12.4f, 5.4f),
        };

        public static Vector3[] OurLine() => new[]
        {
            G(-4.6f, 6.9f, 1.4f), G(-1.6f, 7.0f, 1.4f), G(5.3f, 4.6f, 1.3f), G(-3f, 4.2f, 2f), G(0.5f, 4.2f, 2.2f),
            G(6.9f, 7.4f, 1.6f), G(-17f, 11f, 2f), G(-14.5f, 17.2f, 1f), G(10.6f, -0.4f, 1.6f), G(-10.4f, 2.9f, 0.8f),
        };

        public static Vector3[] OurTargets() => new[]
        {
            G(-17f, 7.4f), G(-10f, 10f), G(8f, 6f), G(14f, 2f), G(-6f, 12f), G(3f, 10f), G(20f, 10f), G(-24f, 4f),
            G(12f, -10f), G(-12f, -8f), G(0f, -18f), G(-3f, -30f), G(-28f, -6f), G(24f, -4f),
        };

        public static Vector3[] MgNests() => new[] { G(-22f, 95f, 0.9f), G(18f, 105f, 0.9f), G(55f, 88f, 0.9f) };

        // ================================================================== infantry
        static void BuildInfantry(Transform root, BattleSettings s, Transform eye)
        {
            var ctrl = BattleAssets.SoldierController();
            var enemy = BattleAssets.SoldierPrefab(Team.Enemy, ctrl);
            var friend = BattleAssets.SoldierPrefab(Team.Friendly, ctrl);
            var inf = new GameObject("NearInfantry").AddComponent<NearInfantry>();
            inf.transform.SetParent(root, false);
            inf.settings = s;
            inf.head = eye;
            inf.enemySpots = EnemySpots();
            inf.friendlySpots = FriendlySpots();
            inf.ourLine = OurLine();
            if (enemy == null) { Debug.LogWarning("[FMVR] battle: no soldier prefab"); return; }
            for (int i = 0; i < s.nearEnemies; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(enemy, inf.transform);
                go.name = "Enemy_" + i;
                go.transform.position = G(-40f + i * 10f, 100f);
                inf.enemies.Add(go.GetComponent<SoldierAgent>());
            }
            for (int i = 0; i < s.nearFriendlies && friend != null; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(friend, inf.transform);
                go.name = "Friendly_" + i;
                go.transform.position = inf.friendlySpots[i % inf.friendlySpots.Length];
                inf.friendlies.Add(go.GetComponent<SoldierAgent>());
            }
        }

        static void BuildEnemyFire(Transform root, BattleSettings s, Transform eye)
        {
            var ef = new GameObject("EnemyFire").AddComponent<EnemyFire>();
            ef.transform.SetParent(root, false);
            ef.settings = s;
            ef.head = eye;
            ef.mgNests = MgNests();
            ef.armedWithoutScenario = true;
            // suppression vignette: quad right in front of the eye
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SuppressionVignette";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(eye, false);
            q.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            q.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = BattleAssets.Mat("Battle_Vignette", "FieldMedic/Vignette");
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            var sup = q.AddComponent<SuppressionFx>();
            sup.quad = r;
            ef.suppressionFx = sup;
        }

        // ================================================================== vehicles
        static ParticleSystem TrackDust(Transform parent)
        {
            var go = new GameObject("TrackDust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, -2.5f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startColor = new Color(0.52f, 0.46f, 0.37f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.gravityModifier = -0.01f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, 0.2f, 1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = BattlefieldFx.PuffMat(BattlefieldBuilder.ArtDir, "FX_SmokePuff", Color.white, false);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static AudioSource Loop3D(Transform parent, string name, AudioClip clip, float minD, float maxD, float vol, float doppler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.playOnAwake = clip != null;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = minD;
            a.maxDistance = maxD;
            a.volume = vol;
            a.dopplerLevel = doppler;
            var mix = AudioMixerBuilder.Ensure();
            if (mix != null) a.outputAudioMixerGroup = mix.sfx;
            return a;
        }

        static void BuildVehicles(Transform root, BattleAudio audio)
        {
            var layer = new GameObject("Vehicles").AddComponent<VehicleLayer>();
            layer.transform.SetParent(root, false);
            var tank = BattleAssets.SplitVehicle("vehicles", "m1a2_tank", "EnemyTank", 1, 0.5f, new Color(0.58f, 0.6f, 0.52f), "barrel");
            var ifv = BattleAssets.SplitVehicle("vehicles", "arx_apc", "EnemyIFV", 1, 0.62f, new Color(0.6f, 0.6f, 0.54f), "barrel");
            var engine = BattleAudio.Pick(audio.tankEngine);
            void Add(BattleAssets.SplitResult res, string name, Vector3[] path, float speed, bool autocannon)
            {
                if (res == null || res.prefab == null) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(res.prefab, layer.transform);
                go.name = name;
                var agent = go.AddComponent<VehicleAgent>();
                agent.turret = go.transform.Find("Model/Part");
                agent.muzzleLocal = res.muzzleLocal;
                agent.path = path.Select(p => G(p.x, p.z)).ToArray();
                agent.speed = speed;
                agent.autocannon = autocannon;
                agent.hullLength = Mathf.Max(5f, res.length * 0.8f);
                agent.engine = Loop3D(go.transform, "Engine", engine, 8f, 220f, 0.8f, 0.6f);
                agent.dust = TrackDust(go.transform);
                go.transform.position = agent.path[0];
                layer.vehicles.Add(agent);
            }
            Add(tank, "Tank_A", new[] { new Vector3(-70f, 0f, 62f), new Vector3(-35f, 0f, 58f), new Vector3(0f, 0f, 64f), new Vector3(35f, 0f, 57f), new Vector3(70f, 0f, 66f), new Vector3(40f, 0f, 80f), new Vector3(0f, 0f, 74f), new Vector3(-40f, 0f, 82f) }, 3.5f, false);
            Add(tank, "Tank_B", new[] { new Vector3(-90f, 0f, 95f), new Vector3(-40f, 0f, 88f), new Vector3(10f, 0f, 96f), new Vector3(60f, 0f, 90f), new Vector3(95f, 0f, 104f), new Vector3(50f, 0f, 112f), new Vector3(0f, 0f, 106f), new Vector3(-50f, 0f, 114f) }, 4f, false);
            Add(ifv, "IFV_A", new[] { new Vector3(20f, 0f, 50f), new Vector3(45f, 0f, 46f), new Vector3(70f, 0f, 55f), new Vector3(60f, 0f, 70f), new Vector3(35f, 0f, 66f), new Vector3(15f, 0f, 60f) }, 5f, true);
            Add(ifv, "IFV_B", new[] { new Vector3(-30f, 0f, 120f), new Vector3(-70f, 0f, 110f), new Vector3(-110f, 0f, 125f), new Vector3(-70f, 0f, 140f) }, 4.5f, true);
        }

        // ================================================================== crowd + horizon
        static void BuildCrowd(Transform root, Transform eye, BattleSettings s)
        {
            var meshes = BattleAssets.CrowdMeshes();
            var crowd = new GameObject("CrowdLayer").AddComponent<CrowdLayer>();
            crowd.transform.SetParent(root, false);
            crowd.head = eye;
            crowd.count = s.crowdSoldiers;
            if (meshes.Length >= 10)
            {
                crowd.runFrames = meshes.Take(8).ToArray();
                crowd.kneelMesh = meshes[8];
                crowd.deadMesh = meshes[9];
            }
            var m = BattleAssets.Mat("Battle_Crowd", "FieldMedic/CrowdLit");
            m.SetColor("_Tint", Color.white);
            crowd.material = m;
        }

        static void BuildHorizon(Transform root, BattleSettings s)
        {
            var sheet = BattleAssets.ImpostorSheet();
            var hz = new GameObject("HorizonLayer").AddComponent<HorizonLayer>();
            hz.transform.SetParent(root, false);
            int n = s.horizonFigures;
            var rng = new System.Random(4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var data = new List<Vector4>(); var col = new List<Color>(); var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                // ragged lines: groups of 4–8 figures advancing together
                float lineX = R(-175f, 175f), z = R(150f, 185f);
                float x = lineX;
                Vector3 c = new Vector3(x, 0f, z);
                c.y = BattlefieldLayout.GroundHeight(x, z - 13f) - 0.05f;
                float phase = R(0f, 1f), speed = R(1.4f, 3f);
                float shade = R(0.75f, 1.05f);
                int b = pos.Count;
                var corners = new[] { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(-0.5f, 1f) };
                var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                for (int k = 0; k < 4; k++)
                {
                    pos.Add(c); uv.Add(uvs[k]);
                    data.Add(new Vector4(corners[k].x, corners[k].y, phase, speed));
                    col.Add(new Color(shade, shade, shade, 1f));
                }
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            var mesh = new Mesh { name = "HorizonFigures" };
            mesh.SetVertices(pos); mesh.SetUVs(0, uv); mesh.SetUVs(1, data); mesh.SetColors(col); mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 2f, 150f), new Vector3(420f, 30f, 120f));
            string mp = BattleAssets.Dir + "/Meshes/HorizonFigures.asset";
            AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mesh, mp);
            var fig = new GameObject("Figures");
            fig.transform.SetParent(hz.transform, false);
            fig.AddComponent<MeshFilter>().sharedMesh = mesh;
            var fr = fig.AddComponent<MeshRenderer>();
            var fm = BattleAssets.Mat("Battle_Impostor", "FieldMedic/Impostor", licensed: true);
            fm.SetTexture("_MainTex", sheet);
            fm.SetColor("_Tint", new Color(0.62f, 0.62f, 0.6f, 1f));
            fr.sharedMaterial = fm;
            fr.shadowCastingMode = ShadowCastingMode.Off;
            fr.receiveShadows = false;
            hz.figures = fr;

            var tw = new GameObject("Twinkles");
            tw.transform.SetParent(hz.transform, false);
            tw.AddComponent<MeshFilter>().sharedMesh = mesh; // same vertex layout; the shader lifts it to the muzzle
            var tr = tw.AddComponent<MeshRenderer>();
            var tm = BattleAssets.Mat("Battle_Twinkle", "FieldMedic/ImpostorTwinkle");
            tm.SetTexture("_MainTex", AtmosphereBuilder.FxMat("FX_Glow", Color.white, true, 2f).GetTexture("_MainTex"));
            tr.sharedMaterial = tm;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            hz.twinkles = tr;
        }

        // ================================================================== sky
        static void BuildAir(Transform root, Transform eye)
        {
            var air = new GameObject("AirLayer").AddComponent<AirLayer>();
            air.transform.SetParent(root, false);
            air.head = eye;
            var jet = BattleAssets.Aircraft("f16_jet", "AttackJet", new Color(0.62f, 0.66f, 0.62f));
            if (jet != null)
            {
                var j = (GameObject)PrefabUtility.InstantiatePrefab(jet, air.transform);
                air.jet = j.transform;
                air.jetAudio = Loop3D(j.transform, "JetAudio", null, 40f, 2500f, 1f, 1f);
                air.jetAudio.loop = false;
            }
            var heli = BattleAssets.SplitVehicle("aircraft", "utility_helicopter", "AttackHeli", 1, 0.74f, new Color(0.5f, 0.55f, 0.45f), "mast");
            if (heli != null && heli.prefab != null)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(heli.prefab, air.transform);
                air.heli = h.transform;
                air.heliRotor = h.transform.Find("Model/Part");
                air.heliAudio = Loop3D(h.transform, "RotorAudio", null, 25f, 1500f, 1f, 0.8f);
            }
        }

        // ================================================================== smoke grenades on the vest
        static void BuildSmokeGrenades(GameObject rig, Transform eye)
        {
            var vest = new GameObject("VestMount");
            vest.transform.SetParent(rig.transform, false);
            var mount = vest.AddComponent<VestMount>();
            mount.head = eye;
            var model = BattleAssets.ModelPrefab("props", "smoke_grenade");
            var smokeMat = BattlefieldFx.PuffMat(BattlefieldBuilder.ArtDir, "FX_SmokeScreen", new Color(0.92f, 0.92f, 0.9f, 0.9f), false);
            for (int i = 0; i < 2; i++)
            {
                var slot = new GameObject("SmokeSlot" + i).transform;
                slot.SetParent(vest.transform, false);
                slot.localPosition = new Vector3(-0.045f + i * 0.075f, 0f, 0f);
                slot.localRotation = Quaternion.identity;

                var g = new GameObject("SmokeGrenade" + i);
                g.transform.SetPositionAndRotation(slot.position, slot.rotation);
                var rb = g.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var cap = g.AddComponent<CapsuleCollider>();
                cap.radius = 0.03f; cap.height = 0.135f; cap.center = new Vector3(0f, 0.0675f, 0f);
                if (model != null)
                {
                    var vis = (GameObject)PrefabUtility.InstantiatePrefab(model, g.transform);
                    vis.name = "Visual";
                    foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    foreach (var lg in vis.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lg);
                    foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        if (r.name.Contains("_LOD") && !r.name.EndsWith("_LOD0")) r.enabled = false;
                        r.sharedMaterials = r.sharedMaterials.Select(m => m == null ? null : SmokeTint(m)).ToArray();
                    }
                    GameObjectUtility.SetStaticEditorFlags(vis, 0);
                    vis.transform.localPosition = Vector3.zero;
                }
                // pull ring + pin (separate grabbable): at the top, on the side
                var pinGo = new GameObject("PullRing");
                pinGo.transform.SetParent(g.transform, false);
                pinGo.transform.localPosition = new Vector3(0.028f, 0.118f, 0f);
                var prb = pinGo.AddComponent<Rigidbody>();
                prb.isKinematic = true;
                var sc = pinGo.AddComponent<SphereCollider>();
                sc.radius = 0.022f;
                var ring = Torus(0.014f, 0.0022f);
                var rv = new GameObject("RingVisual");
                rv.transform.SetParent(pinGo.transform, false);
                rv.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rv.AddComponent<MeshFilter>().sharedMesh = ring;
                var rr = rv.AddComponent<MeshRenderer>();
                rr.sharedMaterial = BattleAssets.Mat("Battle_RingSteel", "Universal Render Pipeline/Lit");
                rr.sharedMaterial.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.52f));
                rr.sharedMaterial.SetFloat("_Metallic", 0.9f);
                rr.sharedMaterial.SetFloat("_Smoothness", 0.55f);
                rr.shadowCastingMode = ShadowCastingMode.Off;
                var pin = pinGo.AddComponent<SmokePin>();

                // smoke emitter (detaches into world space when it starts)
                var ps = SmokeEmitter(g.transform, smokeMat);
                var hiss = g.AddComponent<AudioSource>();
                hiss.playOnAwake = false;
                hiss.loop = true;
                hiss.spatialBlend = 1f;
                hiss.minDistance = 1.5f; hiss.maxDistance = 40f;
                var clips = AtmosphereBuilder.Clips("smoke_grenade");
                hiss.clip = clips.Length > 0 ? clips[0] : null;
                var mix = AudioMixerBuilder.Ensure();
                if (mix != null) hiss.outputAudioMixerGroup = mix.sfx;

                var sg = g.AddComponent<SmokeGrenade>();
                sg.slot = slot;
                sg.pin = pin;
                sg.smoke = ps;
                sg.hiss = hiss;
                sg.wind = new Vector3(BattlefieldFx.Wind.x, 0f, BattlefieldFx.Wind.z) * 0.35f;
                pin.grenade = sg;
            }
        }

        static Material SmokeTint(Material src)
        {
            string path = $"{BattleAssets.LDir}/Materials/{src.name}_smoke.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            // olive drab canister (the flashbang model's yellow-green body → OD green)
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.62f, 0.66f, 0.5f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static ParticleSystem SmokeEmitter(Transform parent, Material mat)
        {
            var go = new GameObject("SmokeScreen");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.86f, 0.84f, 0.85f), new Color(0.95f, 0.95f, 0.93f, 0.95f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.012f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 380;
            var em = ps.emission; em.rateOverTime = 22f; em.enabled = false;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.15f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(BattlefieldFx.Wind.x * 0.35f); vel.y = new ParticleSystem.MinMaxCurve(0.05f); vel.z = new ParticleSystem.MinMaxCurve(BattlefieldFx.Wind.z * 0.35f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.88f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 2f;
            return ps;
        }

        static Mesh Torus(float R, float r, int seg = 20, int side = 6)
        {
            string path = BattleAssets.Dir + "/Meshes/PullRing.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0f);
                Vector3 radial = c.normalized;
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f;
                    Vector3 dir = radial * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    v.Add(c + dir * r); n.Add(dir);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + side + 1;
                    t.AddRange(new[] { a0, a1, a0 + 1, a0 + 1, a1, a1 + 1 });
                }
            m = new Mesh { name = "PullRing" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ================================================================== bookmarks
        public static void Bookmarks()
        {
            var root = GameObject.Find(ScreenshotTool.BookmarkRoot)?.transform;
            if (root == null) return;
            float k = BattlefieldLayout.KneelEyeHeight;
            MainSceneBuilder.Bookmark(root, "v2_w3_assault", new Vector3(-0.6f, 1.95f, 3.6f), new Vector3(2f, 1.5f, 55f), 70f);
            MainSceneBuilder.Bookmark(root, "v2_w3_cover", new Vector3(-3.0f, k + 0.25f, 5.55f), new Vector3(-2.5f, 1.3f, 40f), 78f);
            MainSceneBuilder.Bookmark(root, "v2_w3_open_field", BattlefieldLayout.ErlanFallPoint + new Vector3(-1f, 1.5f, -2f), new Vector3(8f, 1.5f, 60f), 72f);
            MainSceneBuilder.Bookmark(root, "v2_w3_overview", new Vector3(-30f, 22f, -28f), new Vector3(5f, 0f, 45f), 62f);
            MainSceneBuilder.Bookmark(root, "v2_w3_sky", new Vector3(-2f, 1.7f, -2f), new Vector3(10f, 30f, 60f), 80f);
            MainSceneBuilder.Bookmark(root, "v2_w3_smoke", BattlefieldLayout.DoorwayCentre + new Vector3(-0.5f, 1.4f, -0.6f), BattlefieldLayout.ErlanFallPoint + new Vector3(0f, 1f, 0f), 75f);
            MainSceneBuilder.Bookmark(root, "v2_w3_tank", new Vector3(12f, 1.6f, 30f), new Vector3(10f, 2f, 60f), 55f);
        }
    }
}
