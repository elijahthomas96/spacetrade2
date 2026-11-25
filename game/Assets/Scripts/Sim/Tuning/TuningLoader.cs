using System;
using System.Collections.Generic;
using System.IO;
using SpaceTrade.Sim.Json;

namespace SpaceTrade.Sim.Tuning
{
    public sealed class TuningLoadResult
    {
        /// <summary>The tuning to use: the file's values, or the built-in defaults if the file had any error.</summary>
        public TuningData Tuning { get; }

        /// <summary>Every problem found, each naming the field. Empty when the file was used.</summary>
        public IReadOnlyList<string> Errors { get; }

        public bool UsedDefaults { get; }

        internal TuningLoadResult(TuningData tuning, IReadOnlyList<string> errors, bool usedDefaults)
        {
            Tuning = tuning;
            Errors = errors;
            UsedDefaults = usedDefaults;
        }
    }

    /// <summary>
    /// Reads and validates tuning (spec 12). Any error means the whole file is set aside and the
    /// built-in defaults are used, so a half-valid file never mixes with defaults.
    /// </summary>
    public static class TuningLoader
    {
        public static TuningLoadResult LoadText(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json ?? "");
            }
            catch (JsonSyntaxException e)
            {
                errors.Add("The tuning file is not valid JSON: " + e.Message);
                return Fallback(errors);
            }

            var tuning = TuningReader.Read(root, errors);
            if (errors.Count == 0) TuningValidator.Validate(tuning, errors);
            return errors.Count == 0 ? new TuningLoadResult(tuning, errors, usedDefaults: false) : Fallback(errors);
        }

        public static TuningLoadResult LoadFile(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return Fallback(new List<string> { $"Could not read the tuning file at {path}: {e.Message}" });
            }
            return LoadText(text);
        }

        private static TuningLoadResult Fallback(List<string> errors) => new TuningLoadResult(TuningDefaults.Create(), errors, usedDefaults: true);
    }
}
