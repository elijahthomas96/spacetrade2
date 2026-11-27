using System.Collections.Generic;
using UnityEngine;

namespace SpaceTrade.Game.Theming
{
    /// <summary>
    /// The theme every screen reads from (AGENTS.md): colours, fonts, text sizes, spacing, glow sizes, label rules,
    /// display limits, and control values. Until a file is loaded it is the built-in defaults. Only the game's startup
    /// loads the live theme.json; tests load the fixed test theme.
    /// </summary>
    public static class Theme
    {
        public const string FileName = "theme.json";

        private static GameTheme _current;
        private static IReadOnlyList<string> _errors;
        private static Font _font;
        private static bool _fontLooked;

        /// <summary>The project enters Play mode without reloading code, so the last session's theme must not carry over.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _current = null;
            _errors = null;
            _font = null;
            _fontLooked = false;
        }

        public static GameTheme Current => _current ?? (_current = new GameTheme());

        /// <summary>The problems found in the last file loaded. Empty when it was used, or when no file was loaded.</summary>
        public static IReadOnlyList<string> Errors => _errors ?? new string[0];

        /// <summary>Screens built after this call read the given theme. Null goes back to the built-in defaults.</summary>
        public static void Use(GameTheme theme)
        {
            _current = theme;
            _errors = null;
            _font = null;
            _fontLooked = false;
        }

        /// <summary>Loads a theme file and uses it, or the built-in defaults when it has any problem.</summary>
        public static ThemeLoadResult LoadFile(string path) => Apply(ThemeReader.LoadFile(path));

        public static ThemeLoadResult LoadText(string json) => Apply(ThemeReader.LoadText(json));

        private static ThemeLoadResult Apply(ThemeLoadResult result)
        {
            Use(result.Theme);
            _errors = result.Errors;
            return result;
        }

        /// <summary>The theme's font, or null for Unity's built-in font.</summary>
        public static Font Font
        {
            get
            {
                if (_fontLooked) return _font;
                _fontLooked = true;
                string name = Current.Text.Font;
                _font = string.IsNullOrEmpty(name) ? null : Resources.Load<Font>(ThemeReader.FontFolder + "/" + name);
                return _font;
            }
        }

        public static ColourTheme Colours => Current.Colours;
        public static TextTheme Text => Current.Text;
        public static PanelTheme Panels => Current.Panels;
        public static TopBarTheme TopBar => Current.TopBar;
        public static NotificationTheme Notifications => Current.Notifications;
        public static SettingsTheme Settings => Current.Settings;
        public static LabelTheme Labels => Current.Labels;
        public static MapTheme Map => Current.Map;
        public static ShipTheme Ships => Current.Ships;
        public static RouteTheme Routes => Current.Routes;
        public static BackgroundTheme Background => Current.Background;
        public static CloseUpTheme CloseUp => Current.CloseUp;
        public static ControlTheme Controls => Current.Controls;
    }
}
