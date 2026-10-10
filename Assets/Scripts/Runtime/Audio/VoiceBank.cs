using System;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Audio
{
    /// <summary>
    /// Voice lines by id (RU/KZ variants, any number of takes) + wordless vocalisation pools.
    /// Built by FieldMedic → Casualty → Rebuild Voice Bank from Assets/Audio/Voice/&lt;id&gt;_&lt;ru|kz&gt;[_n].wav and
    /// Assets/Audio/SFX/&lt;pool&gt;/*. Replace the wav files with real recordings using the same names (docs/VOICE_SCRIPT.md).
    /// </summary>
    [CreateAssetMenu(menuName = "FieldMedic/Voice Bank")]
    public sealed class VoiceBank : ScriptableObject
    {
        [Serializable]
        public sealed class Line
        {
            public string id;
            public AudioClip[] ru = Array.Empty<AudioClip>();
            public AudioClip[] kz = Array.Empty<AudioClip>();
        }

        public Line[] lines = Array.Empty<Line>();
        public AudioClip[] groan = Array.Empty<AudioClip>();
        public AudioClip[] scream = Array.Empty<AudioClip>();
        public AudioClip[] gasp = Array.Empty<AudioClip>();
        public AudioClip[] cough = Array.Empty<AudioClip>();
        public AudioClip[] breathHeavy = Array.Empty<AudioClip>();
        public AudioClip[] snore = Array.Empty<AudioClip>();
        public AudioClip[] heartbeat = Array.Empty<AudioClip>();

        public Line Find(string id)
        {
            foreach (var l in lines) if (l != null && l.id == id) return l;
            return null;
        }

        public bool Has(string id) => Find(id) != null;

        /// <summary>Picks a clip for the line. kzShare = probability of Kazakh (falls back to the other language).</summary>
        public AudioClip Get(string id, float kzShare, System.Random rng = null)
        {
            var l = Find(id);
            if (l == null) return null;
            rng ??= _rng;
            bool kz = rng.NextDouble() < kzShare;
            var arr = kz ? l.kz : l.ru;
            if (arr == null || arr.Length == 0) arr = kz ? l.ru : l.kz;
            return Pick(arr, rng);
        }

        public AudioClip Get(string id, GameLanguage lang) => Get(id, lang == GameLanguage.KZ ? 1f : 0f);

        public static AudioClip Pick(AudioClip[] pool, System.Random rng = null)
        {
            if (pool == null || pool.Length == 0) return null;
            rng ??= _rng;
            return pool[rng.Next(pool.Length)];
        }

        static readonly System.Random _rng = new System.Random();
    }
}
