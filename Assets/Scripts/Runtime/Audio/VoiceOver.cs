using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// w10c: the radio operator / instructor voice-over is real Russian (ElevenLabs, Resources/Radio/&lt;id&gt;_ru).
    /// The owner chose Russian-only speech: the same recording plays with the KZ UI (texts/subtitles stay Kazakh).
    /// The old robotic Windows-TTS lines (KZ radio placeholders, casualty TTS words) are never played.
    /// w10d: Erlan's own words are real too (ElevenLabs "Prince Nur", eleven_v3, Resources/Erlan/&lt;id&gt;_ru,
    /// tools/erlan_post.py) - played in both UI languages; lines without a recording fall back to pain sounds.
    /// </summary>
    public static class VoiceOver
    {
        public const string Folder = "Radio/";
        public const string ErlanFolder = "Erlan/";

        /// <summary>Russian recording for a line id, or null.</summary>
        public static AudioClip Clip(string id) => string.IsNullOrEmpty(id) ? null : Resources.Load<AudioClip>(Folder + id + "_ru");

        /// <summary>The casualty's spoken words are Windows-TTS placeholders (Assets/Audio/Voice). Off: Erlan uses real
        /// recorded pain sounds (groans/screams/gasps, freesound CC0) instead, until real word recordings exist.</summary>
        public static bool CasualtyTtsWords = false;

        /// <summary>w10d: play Erlan's real ElevenLabs recordings when a line has one.</summary>
        public static bool ErlanRecordings = true;

        static readonly Dictionary<string, AudioClip> _erlan = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> _missing = new HashSet<string>();

        /// <summary>Erlan's (or the player's) real recorded line, or null (cached, also the misses).</summary>
        public static AudioClip ErlanClip(string id)
        {
            if (!ErlanRecordings || string.IsNullOrEmpty(id)) return null;
            if (_erlan.TryGetValue(id, out var c) && c != null) return c;
            if (_missing.Contains(id)) return null;
            c = Resources.Load<AudioClip>(ErlanFolder + id + "_ru");
            if (c != null) _erlan[id] = c; else _missing.Add(id);
            return c;
        }
    }
}
