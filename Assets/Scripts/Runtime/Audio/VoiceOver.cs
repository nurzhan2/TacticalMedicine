using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// w10c: the radio operator / instructor voice-over is real Russian (ElevenLabs, Resources/Radio/&lt;id&gt;_ru).
    /// The owner chose Russian-only speech: the same recording plays with the KZ UI (texts/subtitles stay Kazakh).
    /// The old robotic Windows-TTS lines (KZ radio placeholders, casualty TTS words) are never played.
    /// </summary>
    public static class VoiceOver
    {
        public const string Folder = "Radio/";

        /// <summary>Russian recording for a line id, or null.</summary>
        public static AudioClip Clip(string id) => string.IsNullOrEmpty(id) ? null : Resources.Load<AudioClip>(Folder + id + "_ru");

        /// <summary>The casualty's spoken words are Windows-TTS placeholders (Assets/Audio/Voice). Off: Erlan uses real
        /// recorded pain sounds (groans/screams/gasps, freesound CC0) instead, until real word recordings exist.</summary>
        public static bool CasualtyTtsWords = false;
    }
}
