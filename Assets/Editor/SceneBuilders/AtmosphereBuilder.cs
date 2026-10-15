using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Night atmosphere for the Main scene: procedural sky, FX materials, lighting director with a pool of
    /// para-flares, combat VFX, the audio director (clips from Assets/Audio/SFX, AudioMixer), the threat system,
    /// the player's red headlamp, and the comfort layer (snap turn / seated / height, proximity passthrough, shake).
    /// </summary>
    public static class AtmosphereBuilder
    {
        const string FxDir = "Assets/Art/Environment/FX";
        const string SfxDir = "Assets/Audio/SFX";

        public static Material SkyMaterial()
        {
            Directory.CreateDirectory(FxDir);
            string path = FxDir + "/NightSky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("FieldMedic/NightSky");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            // the moon in the sky matches the moon light (light points along +forward of Euler(22,-35,0))
            Vector3 moonDir = -(Quaternion.Euler(22f, -35f, 0f) * Vector3.forward);
            m.SetVector("_MoonDir", moonDir);
            m.SetFloat("_GlowStrength", 0.55f);
            m.SetFloat("_StarDensity", 0.3f);
            m.SetFloat("_StarBrightness", 0.75f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SoftTexture()
        {
            string path = FxDir + "/FX_Soft.png";
            if (!File.Exists(path))
            {
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
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

        public static Material FxMat(string name, Color tint, bool additive, float boost = 1f)
        {
            string path = $"{FxDir}/{name}.mat";
            var sh = Shader.Find("FieldMedic/Particle");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_MainTex", SoftTexture());
            m.SetColor("_TintColor", tint);
            m.SetFloat("_Boost", boost);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.renderQueue = 3000;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void Build(GameObject rig, Light moon) => Build(rig, moon, true, TrenchBuilder.G + 0.1f);

        /// <summary>
        /// <paramref name="night"/> = V1 trench (para-flares steering the moon light, red headlamp, crickets/rain beds).
        /// Day (V2 battlefield): no flares/headlamp, wind bed; <paramref name="parapetHeight"/> = eye height above which
        /// the V1 threat model counts the head as exposed (the battlefield passes a huge value: w3 replaces it with
        /// ray-cast line of fire against the Cover layer).
        /// </summary>
        public static void Build(GameObject rig, Light moon, bool night, float parapetHeight)
        {
            Directory.CreateDirectory(FxDir);
            var eye = Find(rig.transform, "CenterEyeAnchor");
            var ts = Find(rig.transform, "TrackingSpace");
            var root = new GameObject("Atmosphere").transform;

            // ---- VFX
            var vfx = root.gameObject.AddComponent<CombatVfx>();
            vfx.dirtMaterial = FxMat("FX_Dirt", Color.white, false);
            vfx.smokeMaterial = FxMat("FX_Smoke", Color.white, false);
            vfx.glowMaterial = FxMat("FX_Glow", Color.white, true, 2f);
            vfx.tracerMaterial = FxMat("FX_Tracer", new Color(1f, 0.45f, 0.18f), true, 3f);
            vfx.rainMaterial = FxMat("FX_Rain", new Color(0.7f, 0.75f, 0.85f, 0.5f), true, 0.6f);
            vfx.head = eye;

            // ---- lighting director + flares
            if (night)
            {
            var dir = new GameObject("NightLighting").AddComponent<NightLightingDirector>();
            dir.transform.SetParent(root, false);
            dir.mainLight = moon;
            dir.skyMaterial = RenderSettings.skybox;
            dir.moonEuler = moon.transform.eulerAngles;
            dir.focus = new Vector3(0f, 0f, 0.5f);
            dir.flareIntensity = 0.95f;
            var flareMat = FxMat("FX_FlareGlow", new Color(1f, 0.93f, 0.8f), true, 3f);
            var smokeMat = FxMat("FX_FlareSmoke", new Color(0.55f, 0.55f, 0.58f, 0.18f), false);
            for (int i = 0; i < 3; i++) dir.flares.Add(BuildFlare(root, i, flareMat, smokeMat));
            }

            // ---- audio
            var amb = new GameObject("CombatAmbience").AddComponent<CombatAmbience>();
            amb.transform.SetParent(root, false);
            amb.listener = eye;
            amb.nightBed = night ? Clips("crickets_night") : Clips("wind_night");
            amb.rainBed = night ? Clips("rain_trench") : new AudioClip[0];
            amb.artilleryOut = Clips("artillery_outgoing");
            amb.explosionFar = Clips("explosion_far");
            amb.explosionNear = Clips("explosion_near", "explosion_near2");
            amb.gunfireDistant = Clips("gunfire_distant");
            amb.incomingWhistle = Clips("incoming_whistle");
            amb.bulletWhiz = Clips("bullet_whiz");
            amb.bulletImpact = Clips("bullet_impact_dirt");
            amb.debris = Clips("debris_dirt");
            amb.droneFpv = Clips("drone_fpv2", "drone_fpv");
            amb.droneRecon = Clips("drone_mavic");
            amb.droneShahed = Clips("drone_shahed");
            amb.tinnitus = Clips("tinnitus");
            amb.flarePop = Clips("flare");
            var mix = AudioMixerBuilder.Ensure();
            if (mix != null)
            {
                amb.mixer = mix.mixer;
                amb.ambienceGroup = mix.ambience; amb.sfxGroup = mix.sfx; amb.voiceGroup = mix.voice; amb.tinnitusGroup = mix.tinnitus;
                amb.normalSnapshot = mix.normal; amb.muffledSnapshot = mix.muffled;
                var erlan = GameObject.Find("Erlan");
                if (erlan != null && mix.voice != null)
                    foreach (var src in erlan.GetComponentsInChildren<AudioSource>(true)) src.outputAudioMixerGroup = mix.voice;
            }

            // ---- threats
            var threat = new GameObject("ThreatSystem").AddComponent<ThreatSystem>();
            threat.transform.SetParent(root, false);
            threat.head = eye;
            threat.parapetHeight = parapetHeight;
            threat.underFire = true;
            root.gameObject.AddComponent<RunEndTemplate>().fadeMaterial = FxMat("FX_Fade", Color.black, false);

            // ---- player: red headlamp (spot, no shadows) on the helmet
            if (night)
            {
            var lampGo = new GameObject("Headlamp");
            lampGo.transform.SetParent(eye, false);
            lampGo.transform.localPosition = new Vector3(0f, 0.09f, 0.06f);
            lampGo.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 62f;
            lamp.innerSpotAngle = 28f;
            lamp.shadows = LightShadows.None;
            lampGo.AddComponent<Headlamp>();
            }

            // ---- comfort
            rig.AddComponent<CameraShake>();
            var comfort = rig.AddComponent<ComfortRig>();
            comfort.trackingSpace = ts;
            comfort.head = eye;
            // w0b: no passthrough at all. ProximityPassthrough blacked out the whole image whenever the boundary
            // query failed / the player stood outside the fallback play area, because passthrough itself never ran
            // (project config: passthrough off) → black screen on the Quest. Horizon OS's own guardian is the fallback.
            var mgr = rig.GetComponent<OVRManager>();
            if (mgr != null)
            {
                mgr.isInsightPassthroughEnabled = false;
                EditorUtility.SetDirty(mgr);
            }
        }

        static IlluminationFlare BuildFlare(Transform root, int i, Material glow, Material smoke)
        {
            var go = new GameObject("IllumFlare" + i);
            go.transform.SetParent(root, false);
            var f = go.AddComponent<IlluminationFlare>();
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "Glow";
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(go.transform, false);
            g.GetComponent<Renderer>().sharedMaterial = glow;
            g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            g.AddComponent<Billboard>();
            f.glow = g.transform;

            var sgo = new GameObject("SmokeTrail");
            sgo.transform.SetParent(go.transform, false);
            var ps = sgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 3.5f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smoke;
            r.shadowCastingMode = ShadowCastingMode.Off;
            f.smoke = ps;
            go.SetActive(false);
            return f;
        }

        public static AudioClip[] Clips(params string[] folders)
        {
            var list = new List<AudioClip>();
            foreach (var f in folders)
            {
                string dir = $"{SfxDir}/{f}";
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                    if (clip == null) continue;
                    // long beds/loops: stream / keep compressed (Quest memory)
                    var ai = AssetImporter.GetAtPath(p) as AudioImporter;
                    if (ai != null && clip.length > 8f)
                    {
                        var s = ai.defaultSampleSettings;
                        var want = clip.length > 25f ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                        if (s.loadType != want) { s.loadType = want; ai.defaultSampleSettings = s; ai.SaveAndReimport(); clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p); }
                    }
                    list.Add(clip);
                }
            }
            if (list.Count == 0) Debug.LogWarning("[FMVR] no clips in " + string.Join(",", folders));
            return list.ToArray();
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
