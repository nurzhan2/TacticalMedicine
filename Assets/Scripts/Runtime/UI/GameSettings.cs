using UnityEngine;

namespace FieldMedic.UI
{
    public enum GameLanguage { RU = 0, KZ = 1 }
    public enum GoreLevel { Low = 0, Medium = 1, Max = 2 }

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

        public static bool KZ => Language == GameLanguage.KZ;

        public static event System.Action<GameLanguage> LanguageChanged;

        /// <summary>Training option: the 3-minute pressure hold after packing runs this many times faster.</summary>
        public static float HoldTimeScale = 1f;

        static bool? _hints;
        /// <summary>Show training hints (hold timer near the wound, feedback lines in the desktop HUD).</summary>
        public static bool Hints
        {
            get => _hintsRun ?? (_hints ??= PlayerPrefs.GetInt("fm_hints", 1) == 1);
            set { _hints = value; PlayerPrefs.SetInt("fm_hints", value ? 1 : 0); }
        }

        static bool? _hintsRun;
        /// <summary>w10 audit: the exam turns hints off for its run only (it used to save Hints=false for good, so later
        /// Training runs lost the ghost glove and the tray glow). null = back to the player's setting.</summary>
        public static void OverrideHintsForRun(bool? on) => _hintsRun = on;

        static int? _gore;
        /// <summary>Owner default: maximum naturalistic.</summary>
        public static GoreLevel Gore
        {
            get => (GoreLevel)(_gore ??= PlayerPrefs.GetInt("fm_gore", (int)GoreLevel.Max));
            set { _gore = (int)value; PlayerPrefs.SetInt("fm_gore", (int)value); }
        }

        /// <summary>Blood particle/pool multiplier for the gore level.</summary>
        public static float GoreScale => Gore switch { GoreLevel.Low => 0.35f, GoreLevel.Medium => 0.7f, _ => 1f };

        static bool? _seated;
        public static bool Seated
        {
            get => _seated ??= PlayerPrefs.GetInt("fm_seated", 0) == 1;
            set { _seated = value; PlayerPrefs.SetInt("fm_seated", value ? 1 : 0); }
        }

        static int? _hand;
        /// <summary>0 = right-handed, 1 = left-handed (which hand the bot/ghost guides demonstrate with).</summary>
        public static bool LeftHanded
        {
            get => (_hand ??= PlayerPrefs.GetInt("fm_hand", 0)) == 1;
            set { _hand = value ? 1 : 0; PlayerPrefs.SetInt("fm_hand", value ? 1 : 0); }
        }

        static int? _snap;
        public static float SnapTurnDegrees
        {
            get => _snap ??= PlayerPrefs.GetInt("fm_snap", 45);
            set { _snap = Mathf.RoundToInt(value); PlayerPrefs.SetInt("fm_snap", Mathf.RoundToInt(value)); }
        }

        /// <summary>w10: the controls tutorial ran (or was skipped) once — it no longer starts by itself.</summary>
        public static bool TutorialSeen
        {
            get => PlayerPrefs.GetInt("fm_tutorial_seen", 0) == 1;
            set { PlayerPrefs.SetInt("fm_tutorial_seen", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static int BestScore(string scenarioId) => PlayerPrefs.GetInt("fm_best_" + scenarioId, -1);
        public static void SetBestScore(string scenarioId, int score) { PlayerPrefs.SetInt("fm_best_" + scenarioId, score); PlayerPrefs.Save(); }

        /// <summary>Probability that the casualty speaks Kazakh for a given line (he mixes both, as in reality).</summary>
        public static float CasualtyKazakhShare => Language == GameLanguage.KZ ? 0.8f : 0.35f;

        /// <summary>Pick the UI string for the current language.</summary>
        public static string T(string ru, string kz) => KZ && !string.IsNullOrEmpty(kz) ? kz : ru;
    }
}
