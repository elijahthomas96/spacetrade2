using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SpaceTrade.Sim.Json;

namespace SpaceTrade.Game.Settings
{
    /// <summary>
    /// The player's own settings: three volumes from 0 to 100 and mute. They belong to this computer, not to a game, so
    /// they are kept in their own file and are never part of a save (spec 11.1).
    /// </summary>
    public sealed class GameSettings
    {
        public const int MaxVolume = 100;

        public int MasterVolume = 80;
        public int EffectsVolume = 80;
        public int MusicVolume = 60;
        public bool Muted;

        public GameSettings Copy() => new GameSettings { MasterVolume = MasterVolume, EffectsVolume = EffectsVolume, MusicVolume = MusicVolume, Muted = Muted };

        public string Describe() => $"master={MasterVolume} effects={EffectsVolume} music={MusicVolume} muted={(Muted ? "true" : "false")}";
    }

    /// <summary>
    /// Reads and writes the settings file at one fixed path. A missing file means defaults; a damaged file means defaults
    /// too, with a message, and is replaced the next time a setting changes. Writing goes through a temporary file so a
    /// crash never leaves half a file.
    /// </summary>
    public sealed class SettingsStore
    {
        public const string FileName = "settings.json";

        public string Path { get; }

        /// <summary>What was wrong with the file at the last <see cref="Load"/>, or null.</summary>
        public string LastProblem { get; private set; }

        public SettingsStore(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A settings path is needed.", nameof(path));
            Path = path;
        }

        public GameSettings Load()
        {
            LastProblem = null;
            string text;
            try
            {
                if (!File.Exists(Path)) return new GameSettings();
                text = File.ReadAllText(Path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LastProblem = $"The settings file at {Path} could not be read ({e.Message}), so the default settings are used.";
                return new GameSettings();
            }

            var problems = new List<string>();
            var settings = new GameSettings();
            try
            {
                var root = JsonParser.Parse(text);
                if (root.Kind != JsonKind.Object) problems.Add("it is not a group of fields");
                else
                {
                    settings.MasterVolume = Volume(root, "masterVolume", problems);
                    settings.EffectsVolume = Volume(root, "effectsVolume", problems);
                    settings.MusicVolume = Volume(root, "musicVolume", problems);
                    var muted = root.Get("muted");
                    if (muted == null || muted.Kind != JsonKind.Bool) problems.Add("muted must be true or false");
                    else settings.Muted = muted.BoolValue;
                }
            }
            catch (JsonSyntaxException e)
            {
                problems.Add(e.Message);
            }
            if (problems.Count == 0) return settings;
            LastProblem = $"The settings file at {Path} is damaged ({string.Join("; ", problems)}), so the default settings are used.";
            return new GameSettings();
        }

        private static int Volume(JsonValue root, string name, List<string> problems)
        {
            var v = root.Get(name);
            if (v == null || !v.TryGetLong(out long n) || n < 0 || n > GameSettings.MaxVolume)
            {
                problems.Add($"{name} must be a whole number from 0 to {GameSettings.MaxVolume}");
                return 0;
            }
            return (int)n;
        }

        public void Save(GameSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"masterVolume\": ").Append(s.MasterVolume.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"effectsVolume\": ").Append(s.EffectsVolume.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"musicVolume\": ").Append(s.MusicVolume.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"muted\": ").Append(s.Muted ? "true" : "false").Append("\n");
            sb.Append("}\n");
            string folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(temp, Path, null);
            else File.Move(temp, Path);
        }
    }
}
