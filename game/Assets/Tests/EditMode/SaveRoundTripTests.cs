using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Json;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Ships;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 11.1 and 14.3: a save holds everything, and a save and load round trip is exact.</summary>
    public class SaveRoundTripTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        private void AssertRoundTrip(Simulation sim, Tuning.TuningData tuning)
        {
            byte[] first = SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a", tuning);
            Assert.AreEqual(State(sim), State(loaded), "The loaded game's state dump must equal the saved game's.");
            CollectionAssert.AreEqual(StateBytes(sim), StateBytes(loaded));
            byte[] second = SaveAt(_folder, "b", loaded);
            CollectionAssert.AreEqual(first, second, "Saving the loaded game must give a byte-identical file.");
        }

        [Test]
        public void RouteFleet_RoundTrip_IsByteIdentical()
        {
            var sim = RouteFleet();
            Assert.IsTrue(sim.Ships.Any(s => s.IsInTransit), "Setup: a ship should be mid-voyage.");
            Assert.IsTrue(sim.Ships.Any(s => s.Route?.Status == Routes.RouteStatus.Blocked), "Setup: a ship should be blocked.");
            Assert.IsTrue(sim.Ships.Any(s => s.IsOnInSystemTransport), "Setup: a ship should be on in-system transport.");
            Assert.Greater(sim.Receipts.Count, 3);
            Assert.Greater(sim.Notifications.Count, 3);
            AssertRoundTrip(sim, FlatTuning());
        }

        [Test]
        public void YardGame_RoundTrip_IsByteIdentical()
        {
            var sim = YardGame();
            Assert.IsTrue(sim.FindShip(1).IsInTransit, "Setup: the starter should be mid-voyage.");
            AssertRoundTrip(sim, TestTuning.Load());
        }

        [Test]
        public void FreshNewGame_RoundTrip_KeepsPauseAndOpeningRateHold()
        {
            var sim = NewGame.Create(7, TestTuning.Load()).Simulation;
            Assert.IsTrue(sim.Clock.IsPaused);
            Assert.IsTrue(sim.Market.HoldsOpeningRates);
            AssertRoundTrip(sim, TestTuning.Load());
            var loaded = Load(_folder, "a", TestTuning.Load());
            Assert.IsTrue(loaded.Clock.IsPaused);
            Assert.IsTrue(loaded.Market.HoldsOpeningRates);
            Assert.AreEqual(sim.Market.Galaxy.Seed, loaded.Market.Galaxy.Seed);
        }

        [Test]
        public void LoadedGame_RunsOnExactlyLikeTheOriginal()
        {
            var sim = RouteFleet();
            SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a");
            long to = sim.CurrentTick + 10 * Year;
            sim.Advance(to);
            loaded.Advance(to);
            Assert.AreEqual(State(sim), State(loaded));
        }

        [Test]
        public void MidTransitSave_LoadsInFlight_AndPaysExactlyOnceOnArrival()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), buyTopUp: true).Succeeded);
            sim.Advance(Year + 5);
            var voyage = Courier(sim).Voyage;
            SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a");
            var ship = Courier(loaded);
            Assert.IsTrue(ship.IsInTransit);
            Assert.IsNull(ship.DockedSystemId);
            Assert.AreEqual(voyage.DescribeState(), ship.Voyage.DescribeState());
            foreach (SupplyKind k in new[] { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water })
                Assert.AreEqual(sim.RemainingSupplyKg(CourierId, k), loaded.RemainingSupplyKg(CourierId, k));

            long before = loaded.Credits;
            loaded.Advance(voyage.ArrivalTick);
            Assert.AreEqual(before + voyage.Payout, loaded.Credits);
            Assert.AreEqual(1, loaded.Receipts.Count);
            loaded.Advance(voyage.ArrivalTick + 10 * Year);
            Assert.AreEqual(before + voyage.Payout, loaded.Credits, "No second payment.");
            Assert.AreEqual(1, loaded.Receipts.Count);
        }

        [Test]
        public void SaveFile_HoldsEveryShipsIdNameClassDriveAndDockedSystem_EmptyInTransit()
        {
            var sim = RouteFleet();
            SaveAt(_folder, "a", sim);
            var root = JsonParser.Parse(System.IO.File.ReadAllText(_folder.File("a.json")));
            var ships = root.Get("game").Get("ships").Items;
            Assert.AreEqual(sim.Ships.Count, ships.Count);
            for (int i = 0; i < ships.Count; i++)
            {
                var s = sim.Ships[i];
                var j = ships[i];
                Assert.IsTrue(j.Get("id").TryGetLong(out long id) && id == s.Id);
                Assert.AreEqual(s.Name, j.Get("name").StringValue);
                Assert.AreEqual(s.ClassId, j.Get("class").StringValue);
                Assert.AreEqual(s.DriveId, j.Get("drive").StringValue);
                if (s.IsInTransit)
                {
                    Assert.IsNull(j.Get("dockedSystem"), $"Ship {s.Id} is in transit, so it has no docked system.");
                    Assert.IsNotNull(j.Get("voyage"));
                }
                else Assert.IsTrue(j.Get("dockedSystem").TryGetLong(out long d) && d == s.DockedSystemId.Value);
            }
            Assert.IsTrue(sim.Ships.Any(s => s.IsInTransit) && sim.Ships.Any(s => !s.IsInTransit), "Setup: both kinds should be present.");
        }

        [Test]
        public void SaveFile_StoresEventOrderingNumbersExactly()
        {
            var sim = RouteFleet();
            SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a");
            var original = State(sim).Split('\n').Where(l => l.StartsWith("event")).ToArray();
            var restored = State(loaded).Split('\n').Where(l => l.StartsWith("event")).ToArray();
            Assert.Greater(original.Length, 5);
            CollectionAssert.AreEqual(original, restored, "Every pending event, its ordering number, and the counter must come back exactly.");
        }

        [Test]
        public void UnknownEventType_IsRefusedWhenSaving_AndNothingIsWritten()
        {
            var sim = NewSim(1_000_000);
            sim.Schedule(5, new CountingEvent());
            var result = _folder.Store.Write("a", sim, T0);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("CountingEvent", result.Problem);
            CollectionAssert.IsEmpty(_folder.FileNames());
        }

        private sealed class CountingEvent : Events.SimEvent
        {
            public override Events.EventPhase Phase => Events.EventPhase.Arrival;
            public override int EntityId => 0;
            public override void Apply(Simulation sim) { }
        }
    }
}
