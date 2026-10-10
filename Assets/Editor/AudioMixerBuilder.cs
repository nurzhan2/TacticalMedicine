using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace FieldMedic.EditorTools
{
    /// <summary>
    /// Creates Assets/Audio/CombatMixer.mixer (Master → Ambience, SFX, Voice, Tinnitus; snapshots Normal and
    /// Muffled) through Unity's internal AudioMixerController API (there is no public one). Best effort: if the
    /// internal API changes, returns null and the game falls back to per-source low-pass filters.
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string MixerPath = "Assets/Audio/CombatMixer.mixer";

        public sealed class Result
        {
            public AudioMixer mixer;
            public AudioMixerGroup ambience, sfx, voice, tinnitus;
            public AudioMixerSnapshot normal, muffled;
        }

        public static Result Ensure()
        {
            try
            {
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
                if (mixer == null) mixer = Create();
                if (mixer == null) return null;
                var r = new Result { mixer = mixer };
                r.ambience = mixer.FindMatchingGroups("Ambience").FirstOrDefault();
                r.sfx = mixer.FindMatchingGroups("SFX").FirstOrDefault();
                r.voice = mixer.FindMatchingGroups("Voice").FirstOrDefault();
                r.tinnitus = mixer.FindMatchingGroups("Tinnitus").FirstOrDefault();
                r.normal = mixer.FindSnapshot("Normal") ?? mixer.FindSnapshot("Snapshot");
                r.muffled = mixer.FindSnapshot("Muffled");
                Debug.Log($"[FMVR] mixer: groups amb={r.ambience != null} sfx={r.sfx != null} voice={r.voice != null} tin={r.tinnitus != null} snapshots normal={r.normal != null} muffled={r.muffled != null}");
                return r;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FMVR] AudioMixer creation failed, using filter fallback: " + (e.InnerException ?? e));
                if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null) AssetDatabase.DeleteAsset(MixerPath);
                return null;
            }
        }

        static AudioMixer Create()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var asm = typeof(Editor).Assembly;
            var ctrlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
            if (ctrlType == null) return null;
            var create = ctrlType.GetMethod("CreateMixerControllerAtPath", All);
            var ctrl = create.Invoke(null, new object[] { MixerPath });
            Debug.Log("[FMVR] mixer controller created");
            var master = ctrlType.GetProperty("masterGroup", All).GetValue(ctrl);
            var newGroup = ctrlType.GetMethod("CreateNewGroup", All);
            var addChild = ctrlType.GetMethod("AddChildToParent", All);
            var addView = ctrlType.GetMethod("AddGroupToCurrentView", All);
            foreach (var name in new[] { "Ambience", "SFX", "Voice", "Tinnitus" })
            {
                var g = newGroup.Invoke(ctrl, new object[] { name, false });
                Debug.Log("[FMVR] mixer group " + name);
                addChild.Invoke(ctrl, new[] { g, master });
                try { addView?.Invoke(ctrl, new[] { g }); } catch (Exception) { /* editor view list only */ }
            }
            // snapshots: rename the default to Normal, clone → Muffled with ambience/SFX lowered
            var target = ctrlType.GetProperty("TargetSnapshot", All)?.GetValue(ctrl) as UnityEngine.Object;
            if (target != null) target.name = "Normal";
            var clone = ctrlType.GetMethod("CloneNewSnapshotFromTarget", All);
            if (clone != null) try
            {
                clone.Invoke(ctrl, new object[] { false });
                var snap = ctrlType.GetProperty("TargetSnapshot", All)?.GetValue(ctrl) as UnityEngine.Object;
                if (snap != null && snap != target)
                {
                    snap.name = "Muffled";
                    var setVol = asm.GetType("UnityEditor.Audio.AudioMixerGroupController")?.GetMethod("SetValueForVolume", All);
                    var mixer0 = (AudioMixer)ctrl;
                    if (setVol != null)
                        foreach (var gname in new[] { "Ambience", "SFX", "Voice" })
                        {
                            var g = mixer0.FindMatchingGroups(gname).FirstOrDefault();
                            if (g != null) setVol.Invoke(g, new object[] { ctrl, snap, gname == "Voice" ? -9f : -14f });
                        }
                    if (target != null) ctrlType.GetProperty("TargetSnapshot", All)?.SetValue(ctrl, target);
                }
            }
            catch (Exception e) { Debug.LogWarning("[FMVR] mixer snapshot setup skipped: " + (e.InnerException ?? e).Message); }
            EditorUtility.SetDirty((UnityEngine.Object)ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        }
    }
}
