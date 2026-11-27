using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SpaceTrade.Sim.Json;
using UnityEngine;

namespace SpaceTrade.Game.Theming
{
    public sealed class ThemeLoadResult
    {
        /// <summary>The theme to use: the file's values, or the built-in defaults if the file had any error.</summary>
        public GameTheme Theme { get; }

        /// <summary>Every problem found, each naming the field and the line. Empty when the file was used.</summary>
        public IReadOnlyList<string> Errors { get; }

        public bool UsedDefaults { get; }

        internal ThemeLoadResult(GameTheme theme, IReadOnlyList<string> errors, bool usedDefaults)
        {
            Theme = theme;
            Errors = errors;
            UsedDefaults = usedDefaults;
        }
    }

    /// <summary>
    /// Reads and checks theme.json the way tuning.json is read (spec 9.7): every field must be present, spelled correctly,
    /// of the right kind, and inside its limits. Any error sets the whole file aside and the built-in defaults are used, so
    /// a half-valid file never mixes with defaults. Every message names the field and the line.
    /// The file's layout follows <see cref="GameTheme"/> exactly: one group per section, one entry per field.
    /// </summary>
    public static class ThemeReader
    {
        private const float DefaultMin = 0f;
        private const float DefaultMax = 100000f;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>The folder under Assets/Resources that holds fonts a theme may name.</summary>
        public const string FontFolder = "Fonts";

        public static ThemeLoadResult LoadText(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json ?? "");
            }
            catch (JsonSyntaxException e)
            {
                errors.Add("The theme file is not valid JSON: " + e.Message);
                return Fallback(errors);
            }

            var theme = new GameTheme();
            if (root.Kind != JsonKind.Object) errors.Add($"theme: must be a group of fields in {{ }}, but is {root.Describe()} (line {root.Line})");
            else ReadObject(root, theme, "", errors, isSection: false);
            return errors.Count == 0 ? new ThemeLoadResult(theme, errors, false) : Fallback(errors);
        }

        public static ThemeLoadResult LoadFile(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return Fallback(new List<string> { $"Could not read the theme file at {path}: {e.Message}" });
            }
            return LoadText(text);
        }

        private static ThemeLoadResult Fallback(List<string> errors) => new ThemeLoadResult(new GameTheme(), errors, true);

        /// <summary>The name a field has in the file: the C# name with a small first letter.</summary>
        public static string FileName(FieldInfo f) => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1);

        public static FieldInfo[] FieldsOf(Type t) => t.GetFields(BindingFlags.Public | BindingFlags.Instance);

        private static void ReadObject(JsonValue json, object target, string path, List<string> errors, bool isSection)
        {
            var fields = FieldsOf(target.GetType());
            var seen = new HashSet<string>();
            foreach (var member in json.Members)
            {
                string where = path + member.Key;
                FieldInfo field = null;
                foreach (var f in fields) if (FileName(f) == member.Key) field = f;
                if (field == null)
                {
                    errors.Add($"{where}: is not a recognised field (check the spelling) (line {member.Value.Line})");
                    continue;
                }
                if (!seen.Add(member.Key))
                {
                    errors.Add($"{where}: is written twice (line {member.Value.Line})");
                    continue;
                }
                if (isSection) ReadValue(member.Value, target, field, where, errors);
                else if (member.Value.Kind != JsonKind.Object) errors.Add($"{where}: must be a group of fields in {{ }}, but is {member.Value.Describe()} (line {member.Value.Line})");
                else ReadObject(member.Value, field.GetValue(target), where + ".", errors, isSection: true);
            }
            foreach (var f in fields)
                if (!seen.Contains(FileName(f))) errors.Add($"{path}{FileName(f)}: is missing (line {json.Line})");
        }

        private static void ReadValue(JsonValue v, object target, FieldInfo field, string where, List<string> errors)
        {
            var type = field.FieldType;
            string line = $" (line {v.Line})";
            if (type == typeof(Color))
            {
                if (v.Kind == JsonKind.String && TryParseColour(v.StringValue, out var c)) field.SetValue(target, c);
                else errors.Add($"{where}: must be a colour written as \"#RRGGBBAA\" (red, green, blue, and opacity, each 00 to FF), but is {v.Describe()}{line}");
            }
            else if (type == typeof(float) || type == typeof(int))
            {
                var range = field.GetCustomAttribute<ThemeRangeAttribute>();
                float min = range?.Min ?? DefaultMin, max = range?.Max ?? DefaultMax;
                bool whole = type == typeof(int);
                float number = 0f;
                bool ok = v.Kind == JsonKind.Number && float.TryParse(v.NumberText, NumberStyles.Float, Invariant, out number) && !float.IsInfinity(number);
                long asLong = 0;
                if (ok && whole) ok = v.TryGetLong(out asLong);
                if (!ok) errors.Add($"{where}: must be a {(whole ? "whole number" : "number")}, but is {v.Describe()}{line}");
                else if (number < min || number > max) errors.Add($"{where}: must be between {Num(min)} and {Num(max)}, but is {v.Describe()}{line}");
                else if (whole) field.SetValue(target, (int)asLong);
                else field.SetValue(target, number);
            }
            else if (type == typeof(bool))
            {
                if (v.Kind == JsonKind.Bool) field.SetValue(target, v.BoolValue);
                else errors.Add($"{where}: must be true or false, but is {v.Describe()}{line}");
            }
            else if (type == typeof(string))
            {
                if (v.Kind != JsonKind.String) errors.Add($"{where}: must be text in quotes, but is {v.Describe()}{line}");
                else if (field.Name == nameof(TextTheme.Font) && v.StringValue.Length > 0 && Resources.Load<Font>(FontFolder + "/" + v.StringValue) == null)
                    errors.Add($"{where}: there is no font file named \"{v.StringValue}\" in Assets/Resources/{FontFolder} (leave it as \"\" for the built-in font){line}");
                else field.SetValue(target, v.StringValue);
            }
            else if (type.IsEnum)
            {
                object found = null;
                if (v.Kind == JsonKind.String)
                    foreach (var name in Enum.GetNames(type))
                        if (EnumFileName(name) == v.StringValue) found = Enum.Parse(type, name);
                if (found != null) field.SetValue(target, found);
                else errors.Add($"{where}: must be one of {EnumChoices(type)}, but is {v.Describe()}{line}");
            }
            else throw new NotSupportedException($"Theme field {where} has a type the theme file cannot hold: {type.Name}");
        }

        private static string EnumFileName(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static string EnumChoices(Type type)
        {
            var names = Enum.GetNames(type);
            var parts = new string[names.Length];
            for (int i = 0; i < names.Length; i++) parts[i] = "\"" + EnumFileName(names[i]) + "\"";
            return string.Join(", ", parts);
        }

        private static string Num(float f) => f.ToString("R", Invariant);

        /// <summary>Reads "#RRGGBBAA".</summary>
        public static bool TryParseColour(string text, out Color colour)
        {
            colour = default;
            if (text == null || text.Length != 9 || text[0] != '#') return false;
            var parts = new byte[4];
            for (int i = 0; i < 4; i++)
                if (!byte.TryParse(text.Substring(1 + 2 * i, 2), NumberStyles.AllowHexSpecifier, Invariant, out parts[i])) return false;
            colour = new Color(parts[0] / 255f, parts[1] / 255f, parts[2] / 255f, parts[3] / 255f);
            return true;
        }

        public static string ColourText(Color c) =>
            "#" + Byte(c.r).ToString("X2", Invariant) + Byte(c.g).ToString("X2", Invariant) + Byte(c.b).ToString("X2", Invariant) + Byte(c.a).ToString("X2", Invariant);

        private static int Byte(float f) => Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255);

        /// <summary>Writes a theme as the file text, every section and field in the order of <see cref="GameTheme"/>.</summary>
        public static string Write(GameTheme theme)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            var sections = FieldsOf(typeof(GameTheme));
            for (int s = 0; s < sections.Length; s++)
            {
                sb.Append("  \"").Append(FileName(sections[s])).Append("\": {\n");
                object section = sections[s].GetValue(theme);
                var fields = FieldsOf(section.GetType());
                for (int i = 0; i < fields.Length; i++)
                {
                    sb.Append("    \"").Append(FileName(fields[i])).Append("\": ").Append(ValueText(fields[i].GetValue(section)));
                    sb.Append(i < fields.Length - 1 ? ",\n" : "\n");
                }
                sb.Append(s < sections.Length - 1 ? "  },\n" : "  }\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string ValueText(object value)
        {
            switch (value)
            {
                case Color c: return "\"" + ColourText(c) + "\"";
                case float f: return Num(f);
                case int i: return i.ToString(Invariant);
                case bool b: return b ? "true" : "false";
                case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case Enum e: return "\"" + EnumFileName(e.ToString()) + "\"";
                default: throw new NotSupportedException("Theme value of type " + value?.GetType().Name);
            }
        }
    }
}
