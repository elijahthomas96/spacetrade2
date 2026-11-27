using System;
using System.IO;
using NUnit.Framework;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Theming;
using UnityEngine;

namespace SpaceTrade.Game.Tests
{
    /// <summary>The fixed test theme fixture, never the live theme.json (AGENTS.md).</summary>
    internal static class GameTestTheme
    {
        public static string FixturePath => Path.Combine(Application.dataPath, "Tests", "PlayMode", "Fixtures", "test-theme.json");
        public static string LivePath => Path.Combine(Application.streamingAssetsPath, Theme.FileName);

        public static string Text() => File.ReadAllText(FixturePath);

        public static GameTheme Load()
        {
            var result = ThemeReader.LoadText(Text());
            Assert.IsFalse(result.UsedDefaults, "Test theme fixture failed to load: " + string.Join("; ", result.Errors));
            return result.Theme;
        }

        /// <summary>Makes the fixture the theme every screen built from now on reads.</summary>
        public static GameTheme Use()
        {
            var theme = Load();
            Theme.Use(theme);
            return theme;
        }
    }

    /// <summary>Settings files for tests live in a temporary folder, never where the player's real settings are.</summary>
    internal static class GameTestSettings
    {
        public static string Folder => Path.Combine(Path.GetTempPath(), "spacetrade2-tests");

        public static string NewPath()
        {
            string path = Path.Combine(Folder, Guid.NewGuid().ToString("N"), "settings.json");
            Assert.AreNotEqual(Path.GetFullPath(GameBootstrap.SettingsPath), Path.GetFullPath(path), "tests must never touch the player's real settings file");
            StringAssert.DoesNotStartWith(Path.GetFullPath(Application.persistentDataPath), Path.GetFullPath(path));
            return path;
        }

        /// <summary>Removes a test settings file and its own temporary folder.</summary>
        public static void Remove(string path)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!folder.StartsWith(Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Not a test settings folder: " + folder);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
