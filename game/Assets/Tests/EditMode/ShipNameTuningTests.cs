using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>The shipNames list and the in-system income floor (session 7 decisions 3, 7a, 8a).</summary>
    public class ShipNameTuningTests
    {
        private static readonly Regex ShipNamesBlock = new Regex("\"shipNames\": \\[[^\\]]*\\],");

        private static void AssertFellBackWithError(TuningLoadResult result, string fieldPath, string contains)
        {
            Assert.IsTrue(result.UsedDefaults, "Expected the loader to fall back to defaults.");
            var error = result.Errors.FirstOrDefault(e => e.StartsWith(fieldPath + ":"));
            Assert.IsNotNull(error, $"Expected an error naming '{fieldPath}'. Errors were:\n" + string.Join("\n", result.Errors));
            StringAssert.Contains(contains, error);
            Assert.AreEqual(TuningWriter.Write(TuningDefaults.Create()), TuningWriter.Write(result.Tuning), "Fallback tuning should equal the built-in defaults.");
        }

        [Test]
        public void Fixture_HasShipNames()
        {
            var t = TestTuning.Load();
            Assert.AreEqual(24, t.ShipNames.Count);
            Assert.AreEqual("Brass Tack", t.ShipNames[0]);
        }

        [Test]
        public void MissingShipNames_NamesField_FallsBackToDefaults()
        {
            string text = TestTuning.Text();
            Assert.IsTrue(ShipNamesBlock.IsMatch(text), "Test setup: shipNames block not found.");
            AssertFellBackWithError(TuningLoader.LoadText(ShipNamesBlock.Replace(text, "")), "shipNames", "is missing");
        }

        [Test]
        public void EmptyShipNames_NamesField_FallsBackToDefaults()
        {
            string text = ShipNamesBlock.Replace(TestTuning.Text(), "\"shipNames\": [],");
            AssertFellBackWithError(TuningLoader.LoadText(text), "shipNames", "must list at least one name");
        }

        [Test]
        public void BlankShipName_NamesField_FallsBackToDefaults()
        {
            string text = TestTuning.With(TestTuning.Text(), "\"Fair Wind\"", "\"   \"");
            AssertFellBackWithError(TuningLoader.LoadText(text), "shipNames[#2]", "must be non-empty text");
        }

        [Test]
        public void DuplicateShipName_NamesField_FallsBackToDefaults()
        {
            string text = TestTuning.With(TestTuning.Text(), "\"Fair Wind\"", "\"Brass Tack\"");
            AssertFellBackWithError(TuningLoader.LoadText(text), "shipNames", "\"Brass Tack\" appears more than once");
        }

        [Test]
        public void ZeroInSystemDailyIncome_NamesField_FallsBackToDefaults()
        {
            string text = TestTuning.With(TestTuning.Text(), "\"inSystemDailyIncome\": 17", "\"inSystemDailyIncome\": 0");
            AssertFellBackWithError(TuningLoader.LoadText(text), "ships[courier].inSystemDailyIncome", "must be at least 1, got 0");
        }

        [Test]
        public void InSystemDailyIncomeOfOne_IsAccepted()
        {
            string text = TestTuning.With(TestTuning.Text(), "\"inSystemDailyIncome\": 17", "\"inSystemDailyIncome\": 1");
            var result = TuningLoader.LoadText(text);
            Assert.IsFalse(result.UsedDefaults, string.Join("\n", result.Errors));
        }
    }
}
