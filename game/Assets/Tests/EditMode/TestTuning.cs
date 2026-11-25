using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using UnityEngine;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// The fixed test copy of the tuning data. Tests never read the live tuning.json, so editing it
    /// cannot break the suite.
    /// </summary>
    internal static class TestTuning
    {
        public static string FixturePath => Path.Combine(Application.dataPath, "Tests", "EditMode", "Fixtures", "test-tuning.json");

        public static string Text() => File.ReadAllText(FixturePath);

        public static TuningData Load()
        {
            var result = TuningLoader.LoadText(Text());
            Assert.IsFalse(result.UsedDefaults, "Test fixture failed to load: " + string.Join("; ", result.Errors));
            return result.Tuning;
        }

        /// <summary>Returns the fixture text with one exact piece replaced, failing if that piece is not found.</summary>
        public static string With(string text, string find, string replace)
        {
            StringAssert.Contains(find, text, "Test setup: text to replace not found in the fixture.");
            return text.Replace(find, replace);
        }
    }
}
