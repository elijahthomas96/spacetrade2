using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 12: only the newest notifications.keepNewest notifications are kept; receipts are never trimmed.</summary>
    public class NotificationLimitTests
    {
        private static TuningData Keeping(int n)
        {
            var t = FlatTuning();
            t.Notifications.KeepNewest = n;
            return t;
        }

        private static string[] Texts(Simulation sim) => sim.Notifications.Select(n => $"{n.Tick} {n.Kind} {n.ShipId} {n.Text}").ToArray();

        [Test]
        public void WhilePlaying_OnlyTheNewestAreKept_AndReceiptsAreNeverTrimmed()
        {
            var full = RouteFleet(Keeping(200));
            var trimmed = RouteFleet(Keeping(5));
            Assert.Greater(full.Notifications.Count, 5, "Setup: more notifications than the limit.");
            Assert.LessOrEqual(full.Notifications.Count, 200);
            Assert.AreEqual(5, trimmed.Notifications.Count);
            CollectionAssert.AreEqual(Texts(full).Skip(full.Notifications.Count - 5).ToArray(), Texts(trimmed), "The newest five, in order.");
            Assert.AreEqual(full.Receipts.Count, trimmed.Receipts.Count);
            Assert.AreEqual(full.Credits, trimmed.Credits, "Trimming notifications changes nothing else.");
        }

        [Test]
        public void LoweringTheLimit_TrimsOnLoad_AndReceiptsAreKept()
        {
            using (var folder = new TempSaveFolder())
            {
                var sim = RouteFleet(Keeping(200));
                Assert.Greater(sim.Notifications.Count, 3);
                SaveAt(folder, "a", sim);
                var loaded = Load(folder, "a", Keeping(3));
                Assert.AreEqual(3, loaded.Notifications.Count);
                CollectionAssert.AreEqual(Texts(sim).Skip(sim.Notifications.Count - 3).ToArray(), Texts(loaded));
                Assert.AreEqual(sim.Receipts.Count, loaded.Receipts.Count);
            }
        }

        [Test]
        public void KeepNewestOfZero_NamesTheField_FallsBackToDefaults()
        {
            string text = TestTuning.With(TestTuning.Text(), "\"keepNewest\": 200", "\"keepNewest\": 0");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            var error = result.Errors.FirstOrDefault(e => e.StartsWith("notifications.keepNewest:"));
            Assert.IsNotNull(error, string.Join("\n", result.Errors));
            StringAssert.Contains("must be at least 1, got 0", error);
        }

        [Test]
        public void MissingNotificationsBlock_NamesTheField()
        {
            string text = TestTuning.With(TestTuning.Text(), ",\n  \"notifications\": {\n    \"keepNewest\": 200\n  }", "");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("notifications:") && e.Contains("is missing")), string.Join("\n", result.Errors));
        }

        [Test]
        public void KeepNewestOfOne_IsAccepted_AndDefaultIs200()
        {
            var result = TuningLoader.LoadText(TestTuning.With(TestTuning.Text(), "\"keepNewest\": 200", "\"keepNewest\": 1"));
            Assert.IsFalse(result.UsedDefaults, string.Join("\n", result.Errors));
            Assert.AreEqual(1, result.Tuning.Notifications.KeepNewest);
            Assert.AreEqual(200, TuningDefaults.Create().Notifications.KeepNewest);
        }
    }
}
