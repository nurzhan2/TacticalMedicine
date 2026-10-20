using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// w10d debug hook: every AudioClip played through the game's own audio helpers (CombatAmbience.PlayAt / beds,
    /// BattleAudio.Play / beds / breathing, DroneAudio, CasualtyVoice) is recorded here. Logs
    /// "[FMVR] clip &lt;name&gt;" once per unique clip (adb logcat | grep "[FMVR] clip" tells which file is audible).
    /// </summary>
    public static class ClipLog
    {
        static readonly HashSet<string> _played = new HashSet<string>();
        static readonly HashSet<string> _background = new HashSet<string>();

        /// <summary>Every unique clip name played so far.</summary>
        public static IReadOnlyCollection<string> Played => _played;
        /// <summary>Unique clip names played by background layers (ambience/battle/drones), not by Erlan.</summary>
        public static IReadOnlyCollection<string> Background => _background;

        public static bool LogToConsole = true;

        public static void Record(AudioClip clip, bool background = true)
        {
            if (clip == null) return;
            Record(clip.name, background);
        }

        public static void Record(string name, bool background = true)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (background) _background.Add(name);
            if (_played.Add(name) && LogToConsole) Debug.Log("[FMVR] clip " + name + (background ? " (bg)" : ""));
        }

        public static void Clear()
        {
            _played.Clear();
            _background.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Clear();
    }

    /// <summary>
    /// w10d, owner (twice): «надо крики убрать, пусть только пули и взрывы на фоне». Background layers never play a
    /// clip whose name suggests a human voice (screams, shouts, crowd, wounded, "Medic!", moans...) or a field
    /// recording known to contain voices (freesound 385895 "BattleField Ambience (Loop)" - shouting soldiers;
    /// 486027 "Artillery Guns Firing" - gun crew commands). Gunfire, explosions, drones, wind stay.
    /// </summary>
    public static class BackgroundAudioPolicy
    {
        static readonly string[] Banned =
        {
            "scream", "shout", "yell", "crowd", "medic", "wounded", "moan", "groan", "pain", "cry", "voice",
            "speech", "talk", "hurt", "death", "battlefield_ambience", "385895", "artillery_guns_firing", "486027",
        };

        public static bool IsVoiceLike(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            var n = clipName.ToLowerInvariant();
            foreach (var b in Banned) if (n.Contains(b)) return true;
            return false;
        }

        public static bool Allowed(AudioClip clip) => clip != null && !IsVoiceLike(clip.name);

        /// <summary>The pool without voice-like clips (never null).</summary>
        public static AudioClip[] Filter(AudioClip[] pool)
        {
            if (pool == null || pool.Length == 0) return System.Array.Empty<AudioClip>();
            var keep = new List<AudioClip>(pool.Length);
            foreach (var c in pool) if (Allowed(c)) keep.Add(c);
            return keep.Count == pool.Length ? pool : keep.ToArray();
        }
    }
}
