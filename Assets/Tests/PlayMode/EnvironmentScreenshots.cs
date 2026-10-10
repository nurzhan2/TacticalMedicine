using System.Collections;
using System.IO;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Environment;
using FieldMedic.Medical;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Renders docs/screens/s4_*.png from the real Main scene in play mode (flares, explosion, headlamp).
    /// Run explicitly (GPU needed, no -nographics): -testFilter FieldMedic.Tests.EnvironmentScreenshots
    /// </summary>
    [Category("Screenshots")]
    public class EnvironmentScreenshots
    {
        const string Dir = "docs/screens";

        static IEnumerator LoadMain()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Main.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            AudioListener.volume = 0f;
            var dir = NightLightingDirector.Instance;
            if (dir != null) { dir.autoFlares = false; dir.autoTracers = false; }
            var amb = CombatAmbience.Instance;
            if (amb != null) amb.autoAmbience = false;
            var threat = ThreatSystem.Instance;
            if (threat != null) threat.enableHits = false;
        }

        static void Shot(string name, Vector3 from, Vector3 at, float fov = 62f)
        {
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.stereoTargetEye = StereoTargetEyeMask.None; }
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 600f;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            foreach (var b in Object.FindObjectsByType<Billboard>(FindObjectsSortMode.None)) b.Face(from);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
            Debug.Log("[FMVR] screenshot " + name);
        }

        static IlluminationFlare Flare(Vector3 at, float age)
        {
            var dir = NightLightingDirector.Instance;
            var f = dir.LaunchFlare(at);
            f.Pose(age);
            dir.Apply(0f);
            return f;
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Lamp(bool on, Vector3 from, Vector3 at)
        {
            var lamp = Headlamp.Instance;
            if (lamp == null) return;
            lamp.SetMode(on ? LightMode.Red : LightMode.Off);
            lamp.transform.SetPositionAndRotation(from + Vector3.up * 0.08f, Quaternion.LookRotation(at - from));
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Capture_S4()
        {
            yield return LoadMain();
            var c = Object.FindAnyObjectByType<CasualtyController>();
            if (c != null && c.voice != null) c.voice.muted = true;
            var dir = NightLightingDirector.Instance;
            Assert.IsNotNull(dir, "Main scene has the night lighting director");

            // 1) moon only: the trench in the dark, red lamp off
            Lamp(false, Vector3.zero, Vector3.forward);
            dir.Apply(0f);
            yield return Frames(2);
            Shot("s4_00_moon_only", new Vector3(-3.9f, 1.55f, -0.1f), new Vector3(4f, 0.8f, 0.3f), 70f);

            // 2) para-flare over no-man's land: wide trench view along the bay
            var f = Flare(new Vector3(-8f, 40f, 26f), 9f);
            yield return Frames(3);
            dir.Apply(0f);
            Shot("s4_01_trench_flare", new Vector3(-3.9f, 1.55f, -0.1f), new Vector3(4f, 0.8f, 0.3f), 70f);
            Shot("s4_02_overview", new Vector3(-13f, 7.5f, -9f), new Vector3(0.5f, 0.6f, 1.5f), 58f);
            Shot("s4_03_east_bay_net", new Vector3(3.6f, 1.6f, -0.1f), new Vector3(9.5f, 1.1f, 1.6f), 66f);

            // 3) close at the dugout (flare dimmer, candle inside)
            f.Pose(31f); dir.Apply(0f);
            yield return Frames(2);
            Shot("s4_04_dugout", new Vector3(-5.1f, 1.45f, -0.35f), new Vector3(-7.6f, 0.85f, -1.55f), 64f);

            // 4) near explosion seen from the firing step
            f.Pose(14f); dir.Apply(0f);
            var amb = CombatAmbience.Instance;
            amb.Detonate(new Vector3(4f, TrenchBuilderConst.G, 13f), 1.3f);
            yield return Frames(4);
            Shot("s4_05_explosion", new Vector3(-3.4f, 2.05f, 0.25f), new Vector3(2f, 2.2f, 14f), 66f);
            yield return new WaitForSeconds(0.6f);
            Shot("s4_06_explosion_after", new Vector3(-3.4f, 2.05f, 0.25f), new Vector3(2f, 2.2f, 14f), 66f);

            // 5) far front: tracers + horizon flash
            f.Pose(60f); f.gameObject.SetActive(false); dir.Apply(0f);
            var vfx = CombatVfx.Instance;
            for (int i = 0; i < 4; i++)
                vfx.TracerBurst(new Vector3(-90f + i * 50f, 2f, 160f + i * 20f), new Vector3(1f - i * 0.5f, 0.04f, -0.2f), 5);
            amb.Detonate(new Vector3(300f, TrenchBuilderConst.G, 1500f), 1.5f);
            yield return new WaitForSeconds(0.35f);
            Shot("s4_07_front_tracers", new Vector3(-3.4f, 2.05f, 0.25f), new Vector3(5f, 3f, 40f), 70f);

            // 6) red headlamp on the casualty (he is hit, down in the mud)
            if (c != null)
            {
                c.AddWound(WoundType.ArterialLimb, BodyRegion.ThighR, WoundVisual.RaggedLaceration, "thighR_artery");
                c.Hit(HitKind.Explosion);
                float t0 = Time.time;
                while (c.IsFalling && Time.time - t0 < 9f) yield return null;
                yield return new WaitForSeconds(3f);
                var hips = c.rig.Bone(HumanBodyBones.Hips).position;
                var from = new Vector3(hips.x - 0.95f, 1.0f, Mathf.Clamp(hips.z, -0.35f, 0.35f));
                var at = hips + Vector3.down * 0.05f;
                Lamp(true, from, at);
                dir.Apply(0f);
                yield return Frames(2);
                Shot("s4_08_headlamp_casualty", from, at, 60f);
            }
        }
    }

    /// <summary>Mirror of the editor TrenchBuilder ground level (tests can't reference editor assemblies).</summary>
    static class TrenchBuilderConst { public const float G = 1.75f; }
}
