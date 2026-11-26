using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Spec 6.7, 11.1, 12: only the newest receipts.keepNewest receipts are kept in full, while running totals count every
    /// delivery, so the return summary still covers a whole absence exactly.
    /// </summary>
    public class ReceiptLimitTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        private static TuningData Keeping(int n)
        {
            var t = FlatTuning();
            t.Receipts.KeepNewest = n;
            return t;
        }

        private static string[] Lines(System.Collections.Generic.IEnumerable<VoyageReceipt> receipts) => receipts.Select(r => r.DescribeState()).ToArray();

        [Test]
        public void WhilePlaying_OnlyTheNewestReceiptsAreKept_AndTheTotalsCountEveryDelivery()
        {
            var full = RouteFleet(Keeping(500));
            var trimmed = RouteFleet(Keeping(3));
            int all = full.Receipts.Count;
            Assert.Greater(all, 3, "Setup: more deliveries than the limit.");
            Assert.Less(all, 500, "Setup: the full run keeps every receipt.");

            Assert.AreEqual(3, trimmed.Receipts.Count);
            CollectionAssert.AreEqual(Lines(full.Receipts.Skip(all - 3)), Lines(trimmed.Receipts), "The newest three, in arrival order.");
            Assert.AreEqual(DeliveryTotals.Of(full.Receipts).DescribeState(), trimmed.DeliveryTotals.DescribeState(), "The totals still count every delivery.");
            Assert.AreEqual(all, trimmed.DeliveryTotals.Deliveries);
            Assert.AreEqual(DeliveryTotals.Of(full.Receipts.Take(all - 3)).DescribeState(), trimmed.OlderDeliveryTotals.DescribeState(), "Older deliveries are the totals less the receipts kept.");
            Assert.AreEqual(0, full.OlderDeliveryTotals.Deliveries);
            Assert.AreEqual(full.Credits, trimmed.Credits, "Trimming receipts changes no money.");
            Assert.AreEqual(full.Notifications.Count, trimmed.Notifications.Count);

            var t = trimmed.DeliveryTotals;
            Assert.AreEqual(full.Receipts.Sum(r => r.GrossRevenue), t.GrossRevenue);
            Assert.AreEqual(full.Receipts.Sum(r => r.SuppliesCost), t.SuppliesCost);
            Assert.AreEqual(full.Receipts.Sum(r => r.NetProfit), t.NetProfit);
            Assert.AreEqual(full.Receipts.Sum(r => r.Passengers), t.Passengers);
            Assert.AreEqual(full.Receipts.Sum(r => r.Freight.Sum(f => f.Tonnes)), t.FreightTonnes);
        }

        [Test]
        public void ReturnSummary_CoversTheWholeAbsenceExactly_WhenThereWereMoreDeliveriesThanReceiptsKept()
        {
            var full = RouteFleet(Keeping(500), runTicks: Year);
            var trimmed = RouteFleet(Keeping(3), runTicks: Year);
            Ok(full.SetSpeed(ClockSpeed.X5));
            Ok(trimmed.SetSpeed(ClockSpeed.X5));
            int before = full.Receipts.Count;
            long credits = trimmed.Credits;
            SaveAt(_folder, "full", full, T0);
            SaveAt(_folder, "trimmed", trimmed, T0);

            var f = Open(_folder, "full", T0 + 4 * Hour, Keeping(500));
            var r = Open(_folder, "trimmed", T0 + 4 * Hour, Keeping(3));
            var fs = f.Summary;
            var s = r.Summary;
            var newReceipts = f.Session.Simulation.Receipts.Skip(before).ToList();
            Assert.AreEqual(newReceipts.Count, fs.DeliveryCount);
            Assert.Greater(s.DeliveryCount, 3, "Setup: more deliveries while away than receipts kept.");

            Assert.AreEqual(fs.DeliveryCount, s.DeliveryCount, "Every delivery while away is counted.");
            Assert.AreEqual(DeliveryTotals.Of(newReceipts).DescribeState(), s.Delivered.DescribeState());
            Assert.AreEqual(newReceipts.Sum(x => x.GrossRevenue), s.VoyagePayouts);
            Assert.AreEqual(3, s.Deliveries.Count, "Only the receipts still kept are listed.");
            Assert.AreEqual(s.DeliveryCount - 3, s.DeliveriesNotListed);
            CollectionAssert.AreEqual(Lines(newReceipts.Skip(newReceipts.Count - 3)), Lines(s.Deliveries));
            Assert.AreEqual(0, fs.DeliveriesNotListed);

            Assert.AreEqual(r.Session.Simulation.Credits - credits, s.NetCreditChange);
            Assert.AreEqual(s.NetCreditChange, s.VoyagePayouts + s.InSystemIncome - s.SuppliesBought, "Payouts + in-system income - supplies bought = net change.");
            Assert.AreEqual(fs.NetCreditChange, s.NetCreditChange);
            TestContext.WriteLine($"Away 4 real hours at 5x: {s.DeliveryCount} deliveries, {s.Deliveries.Count} listed, payouts {s.VoyagePayouts}, in-system {s.InSystemIncome}, supplies {s.SuppliesBought}, net {s.NetCreditChange}.");
        }

        [Test]
        public void LoweringTheLimit_TrimsOnLoad_AndTheTotalsAreUnchanged()
        {
            var sim = RouteFleet(Keeping(500));
            int all = sim.Receipts.Count;
            Assert.Greater(all, 4);
            SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a", Keeping(4));
            Assert.AreEqual(4, loaded.Receipts.Count);
            CollectionAssert.AreEqual(Lines(sim.Receipts.Skip(all - 4)), Lines(loaded.Receipts));
            Assert.AreEqual(sim.DeliveryTotals.DescribeState(), loaded.DeliveryTotals.DescribeState());

            var raised = Load(_folder, "a", Keeping(500));
            Assert.AreEqual(State(sim), State(raised), "With the limit unchanged the round trip is byte-identical.");
        }

        [Test]
        public void SavedTotalsSmallerThanTheReceiptsKept_IsDamaged()
        {
            var sim = RouteFleet(Keeping(500));
            SaveAt(_folder, "a", sim);
            string text = System.IO.File.ReadAllText(_folder.File("a.json"));
            string count = $"\"deliveries\": {sim.DeliveryTotals.Deliveries},";
            StringAssert.Contains(count, text);
            System.IO.File.WriteAllText(_folder.File("a.json"), SaveFormat.Seal(text.Replace(count, $"\"deliveries\": {sim.Receipts.Count - 1},")));
            var r = _folder.Store.Load("a", Keeping(500), T0);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("must cover every delivery", r.Messages[0]);
        }

        // ---- Format 1 saves ----

        /// <summary>The save as format 1 wrote it: no delivery totals, version 1, sealed again.</summary>
        private string AsFormatOne(string text)
        {
            var block = new Regex("\"deliveryTotals\": \\{[^}]*\\},\\s*");
            Assert.AreEqual(1, block.Matches(text).Count, "Setup: one totals block.");
            string one = block.Replace(text, "");
            StringAssert.Contains("\"version\": 2,", one);
            return SaveFormat.Seal(one.Replace("\"version\": 2,", "\"version\": 1,"));
        }

        [Test]
        public void FormatOneSave_IsConverted_TheTotalsAreItsReceipts_AndTheGameIsTheSame()
        {
            var sim = RouteFleet(Keeping(500));
            SaveAt(_folder, "a", sim);
            System.IO.File.WriteAllText(_folder.File("one.json"), AsFormatOne(System.IO.File.ReadAllText(_folder.File("a.json"))));

            var loaded = Load(_folder, "one", Keeping(500));
            Assert.AreEqual(State(sim), State(loaded), "Format 1 kept every receipt, so the converted game is byte-identical.");

            var trimmed = Load(_folder, "one", Keeping(2));
            Assert.AreEqual(2, trimmed.Receipts.Count);
            Assert.AreEqual(DeliveryTotals.Of(sim.Receipts).DescribeState(), trimmed.DeliveryTotals.DescribeState(), "Receipts dropped on load stay in the totals.");

            Ok(_folder.Store.Write("one", loaded, T0));
            StringAssert.Contains("\"version\": 2,", System.IO.File.ReadAllText(_folder.File("one.json")), "Saving again writes the current format.");
        }

        [Test]
        public void FormatOneSave_ThatAlreadyHasTotals_IsDamaged()
        {
            var sim = RouteFleet(Keeping(500));
            SaveAt(_folder, "a", sim);
            string text = System.IO.File.ReadAllText(_folder.File("a.json"));
            System.IO.File.WriteAllText(_folder.File("a.json"), SaveFormat.Seal(text.Replace("\"version\": 2,", "\"version\": 1,")));
            var r = _folder.Store.Load("a", Keeping(500), T0);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("save.game.deliveryTotals is not a field save format 1 has", r.Messages[0]);
        }

        // ---- Tuning ----

        [Test]
        public void KeepNewestOfZero_NamesTheField_FallsBackToDefaults()
        {
            var result = TuningLoader.LoadText(TestTuning.With(TestTuning.Text(), "\"keepNewest\": 500", "\"keepNewest\": 0"));
            Assert.IsTrue(result.UsedDefaults);
            var error = result.Errors.FirstOrDefault(e => e.StartsWith("receipts.keepNewest:"));
            Assert.IsNotNull(error, string.Join("\n", result.Errors));
            StringAssert.Contains("must be at least 1, got 0", error);
        }

        [Test]
        public void MissingReceiptsBlock_NamesTheField()
        {
            var result = TuningLoader.LoadText(TestTuning.With(TestTuning.Text(), ",\n  \"receipts\": {\n    \"keepNewest\": 500\n  }", ""));
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("receipts:") && e.Contains("is missing")), string.Join("\n", result.Errors));
        }

        [Test]
        public void KeepNewestOfOne_IsAccepted_AndDefaultIs500()
        {
            var result = TuningLoader.LoadText(TestTuning.With(TestTuning.Text(), "\"keepNewest\": 500", "\"keepNewest\": 1"));
            Assert.IsFalse(result.UsedDefaults, string.Join("\n", result.Errors));
            Assert.AreEqual(1, result.Tuning.Receipts.KeepNewest);
            Assert.AreEqual(500, TuningDefaults.Create().Receipts.KeepNewest);
            Assert.AreEqual(500, TestTuning.Load().Receipts.KeepNewest);
        }
    }
}
