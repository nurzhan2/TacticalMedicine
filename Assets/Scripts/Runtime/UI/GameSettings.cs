using UnityEngine;

namespace FieldMedic.UI
{
    public enum GameLanguage { RU = 0, KZ = 1 }

    /// <summary>Player settings persisted in PlayerPrefs. Russian is the default UI language.</summary>
    public static class GameSettings
    {
        const string LangKey = "fm_lang";
        static GameLanguage? _lang;

        public static GameLanguage Language
        {
            get
            {
                if (_lang == null) _lang = (GameLanguage)PlayerPrefs.GetInt(LangKey, (int)GameLanguage.RU);
                return _lang.Value;
            }
            set
            {
                _lang = value;
                PlayerPrefs.SetInt(LangKey, (int)value);
                LanguageChanged?.Invoke(value);
            }
        }

        public static event System.Action<GameLanguage> LanguageChanged;

        /// <summary>Training option: the 3-minute pressure hold after packing runs this many times faster.</summary>
        public static float HoldTimeScale = 1f;
        /// <summary>Show training hints (hold timer near the wound, feedback lines in the desktop HUD).</summary>
        public static bool Hints = true;

        /// <summary>Probability that the casualty speaks Kazakh for a given line (he mixes both, as in reality).</summary>
        public static float CasualtyKazakhShare => Language == GameLanguage.KZ ? 0.8f : 0.35f;
    }
}
