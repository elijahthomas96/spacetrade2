using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Tests
{
    public class TuningLoaderTests
    {
        private static string DefaultsJson => TuningWriter.Write(TuningDefaults.Create());

        private static void AssertFellBackWithError(TuningLoadResult result, string fieldPath)
        {
            Assert.IsTrue(result.UsedDefaults, "Expected the loader to fall back to defaults.");
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith(fieldPath + ":")),
                $"Expected an error naming '{fieldPath}'. Errors were:\n" + string.Join("\n", result.Errors));
            Assert.AreEqual(DefaultsJson, TuningWriter.Write(result.Tuning), "Fallback tuning should equal the built-in defaults.");
        }

        [Test]
        public void Fixture_LoadsWithNoErrors()
        {
            var result = TuningLoader.LoadText(TestTuning.Text());
            Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
            Assert.IsFalse(result.UsedDefaults);
        }

        [Test]
        public void Fixture_RoundTripsThroughWriter()
        {
            var first = TestTuning.Load();
            string written = TuningWriter.Write(first);
            var second = TuningLoader.LoadText(written);
            Assert.IsEmpty(second.Errors, string.Join("\n", second.Errors));
            Assert.AreEqual(written, TuningWriter.Write(second.Tuning));
        }

        [Test]
        public void BuiltInDefaults_PassValidation()
        {
            var result = TuningLoader.LoadText(DefaultsJson);
            Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
            Assert.IsFalse(result.UsedDefaults);
        }

        [Test]
        public void TextInNumberField_NamesField_FallsBackToDefaults()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"systemCountMin\": 8", "\"systemCountMin\": \"abc\"");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "galaxy.systemCountMin");
            StringAssert.Contains("must be a whole number", result.Errors[0]);
        }

        [Test]
        public void DecimalNumber_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"nearestNeighbourMinMilliLy\": 2500", "\"nearestNeighbourMinMilliLy\": 2.5");
            AssertFellBackWithError(TuningLoader.LoadText(text), "galaxy.nearestNeighbourMinMilliLy");
        }

        [Test]
        public void NegativeValue_NamesFieldInsideList()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"baseRatePerTonne\": 1500", "\"baseRatePerTonne\": -5");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "commodities[ore].baseRatePerTonne");
        }

        [Test]
        public void MissingField_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"startingCredits\": 200000,", "");
            AssertFellBackWithError(TuningLoader.LoadText(text), "start.startingCredits");
        }

        [Test]
        public void MisspelledField_NamesBothFields()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"rerollIntervalDays\"", "\"rerollIntervalDayz\"");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "rates.rerollIntervalDayz");
            AssertFellBackWithError(result, "rates.rerollIntervalDays");
        }

        [Test]
        public void MinAboveMax_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"systemCountMax\": 12", "\"systemCountMax\": 6");
            AssertFellBackWithError(TuningLoader.LoadText(text), "galaxy.systemCountMax");
        }

        [Test]
        public void MaxHopBelowNearestNeighbourMax_NamesField_FallsBackToDefaults()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"maxHopMilliLy\": 7000", "\"maxHopMilliLy\": 6999");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "galaxy.maxHopMilliLy");
            StringAssert.Contains("nearestNeighbourMaxMilliLy", result.Errors.First(e => e.StartsWith("galaxy.maxHopMilliLy")));
        }

        [Test]
        public void MaxHopEqualToNearestNeighbourMax_IsAccepted()
        {
            var result = TuningLoader.LoadText(TestTuning.Text());
            Assert.AreEqual(result.Tuning.Galaxy.NearestNeighbourMaxMilliLy, result.Tuning.Galaxy.MaxHopMilliLy);
            Assert.IsFalse(result.UsedDefaults);
        }

        [Test]
        public void MarketStartFill_Above100_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"supplyPercent\": 100", "\"supplyPercent\": 101");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "start.marketStartFill.supplyPercent");
            StringAssert.Contains("must be at most 100", result.Errors[0]);
        }

        [Test]
        public void MarketStartFill_Negative_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"passengerPercent\": 100", "\"passengerPercent\": -1");
            AssertFellBackWithError(TuningLoader.LoadText(text), "start.marketStartFill.passengerPercent");
        }

        [Test]
        public void MarketStartFill_ZeroAnd100_AreAccepted()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"demandPercent\": 100", "\"demandPercent\": 0");
            var result = TuningLoader.LoadText(text);
            Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
            Assert.AreEqual(0, result.Tuning.Start.MarketStartFill.DemandPercent);
            Assert.AreEqual(100, result.Tuning.Start.MarketStartFill.SupplyPercent);
        }

        [Test]
        public void CombinedModifierFloorAboveCeiling_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"combinedModifierMinPercent\": 50", "\"combinedModifierMinPercent\": 250");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "provisions.combinedModifierMaxPercent");
            StringAssert.Contains("combinedModifierMinPercent", result.Errors[0]);
        }

        [Test]
        public void CombinedModifierFloorZero_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"combinedModifierMinPercent\": 50", "\"combinedModifierMinPercent\": 0");
            AssertFellBackWithError(TuningLoader.LoadText(text), "provisions.combinedModifierMinPercent");
        }

        [Test]
        public void UnknownReference_NamesField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"starterShipClass\": \"courier\"", "\"starterShipClass\": \"yacht\"");
            AssertFellBackWithError(TuningLoader.LoadText(text), "start.starterShipClass");
        }

        [Test]
        public void IndustriesWithNoPossibleCircuit_NamesSection()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"consumes\": [\"food\", \"machinery\"]", "\"consumes\": [\"metals\"]");
            text = TestTuning.With(text, "\"consumes\": [\"machinery\", \"chemicals\"]", "\"consumes\": [\"ore\"]");
            AssertFellBackWithError(TuningLoader.LoadText(text), "industries");
        }

        [Test]
        public void SeveralBadFields_AllReported()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"systemCountMin\": 8", "\"systemCountMin\": true");
            text = TestTuning.With(text, "\"personMassKg\": 100", "\"personMassKg\": 0");
            var result = TuningLoader.LoadText(text);
            AssertFellBackWithError(result, "galaxy.systemCountMin");
            AssertFellBackWithError(result, "provisions.personMassKg");
        }

        [Test]
        public void ErrorsIncludeLineNumber()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"systemCountMin\": 8", "\"systemCountMin\": \"abc\"");
            int line = text.Substring(0, text.IndexOf("\"systemCountMin\"")).Count(c => c == '\n') + 1;
            var result = TuningLoader.LoadText(text);
            StringAssert.Contains($"(line {line})", result.Errors[0]);
        }

        [Test]
        public void BrokenJsonSyntax_ReportsLineAndFallsBack()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"systemCountMin\": 8,", "\"systemCountMin\": 8");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("not valid JSON", result.Errors[0]);
            StringAssert.Contains("line ", result.Errors[0]);
            Assert.AreEqual(DefaultsJson, TuningWriter.Write(result.Tuning));
        }

        [Test]
        public void EmptyText_FallsBack()
        {
            var result = TuningLoader.LoadText("");
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsNotEmpty(result.Errors);
        }

        [Test]
        public void MissingFile_FallsBackWithMessage()
        {
            string path = Path.Combine(Path.GetTempPath(), "spacetrade2-no-such-tuning-" + System.Guid.NewGuid().ToString("N") + ".json");
            var result = TuningLoader.LoadFile(path);
            Assert.IsTrue(result.UsedDefaults);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("Could not read the tuning file", result.Errors[0]);
            Assert.AreEqual(DefaultsJson, TuningWriter.Write(result.Tuning));
        }

        [Test]
        public void LoadFile_ReadsFixture()
        {
            var result = TuningLoader.LoadFile(TestTuning.FixturePath);
            Assert.IsFalse(result.UsedDefaults, string.Join("\n", result.Errors));
        }
    }
}
