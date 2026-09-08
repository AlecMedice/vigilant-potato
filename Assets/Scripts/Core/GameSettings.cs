// -----------------------------------------------------------------------------
// GameSettings — the handful of preferences that persist between sessions.
//
// PlayerPrefs rather than a settings file: it is the only storage that works
// identically on Windows, macOS and Linux with no path handling, and these four
// values are not worth more machinery than that.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace LochNess.Core
{
    public static class GameSettings
    {
        private const string KeyName = "lochness.name";
        private const string KeySensitivity = "lochness.sensitivity";
        private const string KeyVolume = "lochness.volume";
        private const string KeyInvertY = "lochness.inverty";

        /// <summary>Longest name that survives the FixedString32Bytes on the wire. See CrewController.</summary>
        public const int MaxNameLength = 24;

        public static string DisplayName
        {
            get
            {
                string stored = PlayerPrefs.GetString(KeyName, "");
                return string.IsNullOrWhiteSpace(stored) ? "Skipper" : stored;
            }
            set
            {
                string clean = string.IsNullOrWhiteSpace(value) ? "Skipper" : value.Trim();
                if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength);
                PlayerPrefs.SetString(KeyName, clean);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Degrees of view rotation per unit of mouse movement.</summary>
        public static float MouseSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, 2.2f), 0.3f, 8f);
            set { PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, 0.3f, 8f)); PlayerPrefs.Save(); }
        }

        public static float MasterVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 0.8f));
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(KeyVolume, v);
                AudioListener.volume = v;
                PlayerPrefs.Save();
            }
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            set { PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Push stored values into the engine. Called once at boot.</summary>
        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
        }
    }
}
