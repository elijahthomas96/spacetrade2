using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.MarketTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class ReservationTests
    {
        private static ReservationRequest Ore(int from, int to, int tonnes) =>
            new ReservationRequest(from, to, new[] { new FreightLine("ore", tonnes) });

        private static TuningData OreTuning(int supplyCap, int demandCap)
        {
            var t = TestTuning.Load();
            t.FindCommodity("ore").SupplyCapTonnes = supplyCap;
            t.FindCommodity("ore").DemandCapTonnes = demandCap;
            return t;
        }

        private static int[] OreProducers(Market m) => m.Supplies.Where(s => s.CommodityId == "ore").Select(s => s.SystemId).ToArray();
        private static int[] OreConsumers(Market m) => m.Demands.Where(d => d.CommodityId == "ore").Select(d => d.SystemId).ToArray();

        private static void AssertReserved(CommandResult r) => Assert.IsTrue(r.Succeeded, r.Reason);

        // ---- The 80-tonne case (spec 5.3) ----

        [Test]
        public void EightyTonnes_TwoShipsOnTheSameOffer_NeverTakeMoreThanEighty()
        {
            var sim = FindSim(OreTuning(80, 200), x => x.Offers.Any(o => o.CommodityId == "ore"), "an ore offer");
            var m = sim.Market;
            var offer = m.Offers.First(o => o.CommodityId == "ore");
            int a = offer.OriginId, b = offer.DestinationId;
            Assert.AreEqual(80, m.TonnesAvailable(offer));

            AssertReserved(m.TryReserve(Ore(a, b, 50), out var first));

            string before = m.DescribeState();
            var second = m.TryReserve(Ore(a, b, 50), out var none);
            Assert.IsFalse(second.Succeeded);
            Assert.IsNull(none);
            StringAssert.Contains("Only 30 t of ore available", second.Reason);
            StringAssert.Contains("requested 50 t", second.Reason);
            Assert.AreEqual(before, m.DescribeState(), "A failed reservation must change nothing.");
            TestContext.WriteLine("Second ship's failure: " + second.Reason);

            AssertReserved(m.TryReserve(Ore(a, b, 30), out var remainder));
            Assert.AreEqual(80, first.Freight.Sum(f => f.Tonnes) + remainder.Freight.Sum(f => f.Tonnes));
            Assert.AreEqual(0, m.FindSupply(a, "ore").Tonnes);
            Assert.AreEqual(120, m.FindDemand(b, "ore").Tonnes);

            var third = m.TryReserve(Ore(a, b, 1), out _);
            Assert.IsFalse(third.Succeeded);
            StringAssert.Contains("Only 0 t of ore available", third.Reason);
        }

        [Test]
        public void EightyTonnes_TwoDestinationsShareOneSupply()
        {
            var sim = FindSim(OreTuning(80, 200), x => OreProducers(x).Any(p => OreConsumers(x).Count(c => c != p) >= 2), "one ore producer and two ore consumers");
            var m = sim.Market;
            int a = OreProducers(m).First(p => OreConsumers(m).Count(c => c != p) >= 2);
            var dests = OreConsumers(m).Where(c => c != a).Take(2).ToArray();

            AssertReserved(m.TryReserve(Ore(a, dests[0], 50), out _));
            var second = m.TryReserve(Ore(a, dests[1], 50), out _);
            Assert.IsFalse(second.Succeeded);
            StringAssert.Contains("Only 30 t of ore available", second.Reason);
            StringAssert.Contains("supply 30 t", second.Reason);
            AssertReserved(m.TryReserve(Ore(a, dests[1], 30), out _));

            Assert.AreEqual(0, m.FindSupply(a, "ore").Tonnes);
            Assert.AreEqual(150, m.FindDemand(dests[0], "ore").Tonnes);
            Assert.AreEqual(170, m.FindDemand(dests[1], "ore").Tonnes);
        }

        [Test]
        public void EightyTonnes_TwoOriginsShareOneDemand()
        {
            var sim = FindSim(OreTuning(200, 80), x => OreConsumers(x).Any(c => OreProducers(x).Count(p => p != c) >= 2), "two ore producers and one ore consumer");
            var m = sim.Market;
            int b = OreConsumers(m).First(c => OreProducers(m).Count(p => p != c) >= 2);
            var origins = OreProducers(m).Where(p => p != b).Take(2).ToArray();

            AssertReserved(m.TryReserve(Ore(origins[0], b, 50), out _));
            var second = m.TryReserve(Ore(origins[1], b, 50), out _);
            Assert.IsFalse(second.Succeeded);
            StringAssert.Contains("Only 30 t of ore available", second.Reason);
            StringAssert.Contains("demand 30 t", second.Reason);
            AssertReserved(m.TryReserve(Ore(origins[1], b, 30), out _));

            Assert.AreEqual(0, m.FindDemand(b, "ore").Tonnes);
            Assert.AreEqual(150, m.FindSupply(origins[0], "ore").Tonnes);
            Assert.AreEqual(170, m.FindSupply(origins[1], "ore").Tonnes);
        }

        [Test]
        public void ManyRandomClaims_NeverReserveMoreThanExists()
        {
            var tuning = TestTuning.Load();
            int attempts = 0, granted = 0;
            for (long seed = 1; seed <= 50; seed++)
            {
                var m = NewSim(seed, tuning).Market;
                var startSupply = m.Supplies.ToDictionary(s => (s.SystemId, s.CommodityId), s => s.Tonnes);
                var startDemand = m.Demands.ToDictionary(d => (d.SystemId, d.CommodityId), d => d.Tonnes);
                var startWaiting = m.Pools.ToDictionary(p => (p.OriginId, p.DestinationId), p => p.Waiting);
                var takenSupply = new Dictionary<(int, string), int>();
                var takenDemand = new Dictionary<(int, string), int>();
                var takenSeats = new Dictionary<(int, int), int>();
                var rng = new SeededRandom(seed * 31);
                for (int i = 0; i < 300; i++)
                {
                    var o = m.Offers[rng.Range(0, m.Offers.Count - 1)];
                    int tonnes = rng.Range(1, 40);
                    int seats = rng.Range(0, 3);
                    attempts++;
                    var r = m.TryReserve(new ReservationRequest(o.OriginId, o.DestinationId, new[] { new FreightLine(o.CommodityId, tonnes) }, seats), out _);
                    if (!r.Succeeded) continue;
                    granted++;
                    takenSupply[(o.OriginId, o.CommodityId)] = takenSupply.TryGetValue((o.OriginId, o.CommodityId), out int ts) ? ts + tonnes : tonnes;
                    takenDemand[(o.DestinationId, o.CommodityId)] = takenDemand.TryGetValue((o.DestinationId, o.CommodityId), out int td) ? td + tonnes : tonnes;
                    takenSeats[(o.OriginId, o.DestinationId)] = takenSeats.TryGetValue((o.OriginId, o.DestinationId), out int tp) ? tp + seats : seats;
                }
                foreach (var s in m.Supplies)
                {
                    takenSupply.TryGetValue((s.SystemId, s.CommodityId), out int taken);
                    Assert.LessOrEqual(taken, startSupply[(s.SystemId, s.CommodityId)], $"seed {seed}");
                    Assert.AreEqual(startSupply[(s.SystemId, s.CommodityId)] - taken, s.Tonnes, $"seed {seed}");
                }
                foreach (var d in m.Demands)
                {
                    takenDemand.TryGetValue((d.SystemId, d.CommodityId), out int taken);
                    Assert.LessOrEqual(taken, startDemand[(d.SystemId, d.CommodityId)], $"seed {seed}");
                    Assert.AreEqual(startDemand[(d.SystemId, d.CommodityId)] - taken, d.Tonnes, $"seed {seed}");
                }
                foreach (var p in m.Pools)
                {
                    takenSeats.TryGetValue((p.OriginId, p.DestinationId), out int taken);
                    Assert.AreEqual(startWaiting[(p.OriginId, p.DestinationId)] - taken, p.Waiting, $"seed {seed}");
                    Assert.GreaterOrEqual(p.Waiting, 0);
                }
            }
            Assert.Greater(granted, 0);
            Assert.Less(granted, attempts, "Some claims should have been refused once stock ran out.");
            TestContext.WriteLine($"{attempts} random claims, {granted} granted, {attempts - granted} refused; no stock went below zero.");
        }

        // ---- All or nothing ----

        private static Simulation OreSim(out int from, out int to)
        {
            var sim = FindSim(TestTuning.Load(), x => x.Offers.Any(o => o.CommodityId == "ore"), "an ore offer");
            var offer = sim.Market.Offers.First(o => o.CommodityId == "ore");
            from = offer.OriginId;
            to = offer.DestinationId;
            return sim;
        }

        [Test]
        public void FailedRequest_TooManyPassengers_ReservesNoFreightEither()
        {
            var m = OreSim(out int a, out int b).Market;
            int waiting = m.FindPool(a, b).Waiting;
            string before = m.DescribeState();
            var r = m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 20) }, waiting + 1), out var res);
            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(res);
            StringAssert.Contains($"Only {waiting} passengers waiting", r.Reason);
            Assert.AreEqual(before, m.DescribeState());
        }

        [Test]
        public void FailedRequest_OneBadLine_ReservesNoOtherLine()
        {
            var m = OreSim(out int a, out int b).Market;
            string notBought = m.Tuning.Commodities.Select(c => c.Id).First(c => m.FindDemand(b, c) == null);
            string before = m.DescribeState();
            var r = m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 20), new FreightLine(notBought, 5) }), out _);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("does not buy", r.Reason);
            Assert.AreEqual(before, m.DescribeState());
        }

        [Test]
        public void DuplicateLines_AreAddedTogetherBeforeChecking()
        {
            var m = OreSim(out int a, out int b).Market;
            var r = m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 40), new FreightLine("ore", 30) }), out _);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("requested 70 t", r.Reason);
            AssertReserved(m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 40), new FreightLine("ore", 20) }), out var ok));
            Assert.AreEqual(1, ok.Freight.Count);
            Assert.AreEqual(60, ok.Freight[0].Tonnes);
        }

        [Test]
        public void InvalidRequests_FailWithReason_AndChangeNothing()
        {
            var m = OreSim(out int a, out int b).Market;
            string notProduced = m.Tuning.Commodities.First(c => m.FindSupply(a, c.Id) == null).Id;
            string before = m.DescribeState();
            var cases = new[]
            {
                (new ReservationRequest(a, a, null, 1), "both"),
                (new ReservationRequest(a, 999, null, 1), "no system with id 999"),
                (Ore(a, b, 0), "at least 1 t"),
                (new ReservationRequest(a, b, new[] { new FreightLine("gold", 5) }), "\"gold\" is not a commodity"),
                (new ReservationRequest(a, b, null, -1), "cannot be negative"),
                (new ReservationRequest(a, b, new[] { new FreightLine(notProduced, 5) }), "does not produce"),
            };
            foreach (var (request, expected) in cases)
            {
                var r = m.TryReserve(request, out var res);
                Assert.IsFalse(r.Succeeded, expected);
                Assert.IsNull(res);
                StringAssert.Contains(expected, r.Reason);
                Assert.AreEqual(before, m.DescribeState(), expected);
            }
        }

        // ---- Release and locked terms ----

        [Test]
        public void Release_PutsBackExactlyWhatWasTaken_Once()
        {
            var m = OreSim(out int a, out int b).Market;
            string before = m.DescribeState();
            AssertReserved(m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 30) }, 2), out var res));
            Assert.AreNotEqual(before, m.DescribeState());

            Assert.IsTrue(m.Release(res).Succeeded);
            Assert.AreEqual(before, m.DescribeState());
            Assert.IsTrue(res.IsReleased);

            var again = m.Release(res);
            Assert.IsFalse(again.Succeeded);
            StringAssert.Contains("already been released", again.Reason);
            Assert.AreEqual(before, m.DescribeState());
        }

        [Test]
        public void Release_FromAnotherMarket_IsRefused()
        {
            var m1 = OreSim(out int a, out int b).Market;
            var m2 = Market.Create(m1.Galaxy, m1.Tuning, 0);
            AssertReserved(m1.TryReserve(Ore(a, b, 10), out var res));
            string before = m2.DescribeState();
            Assert.IsFalse(m2.Release(res).Succeeded);
            Assert.AreEqual(before, m2.DescribeState());
        }

        [Test]
        public void Reservation_LocksPublishedRateAndFare_ThroughLaterReRolls()
        {
            var sim = OreSim(out int a, out int b);
            var m = sim.Market;
            var offer = m.FindOffer(a, "ore", b);
            var pool = m.FindPool(a, b);
            long rate = m.RatePerTonne(offer);
            long fare = m.FarePerPassenger(pool);
            AssertReserved(m.TryReserve(new ReservationRequest(a, b, new[] { new FreightLine("ore", 12) }, 3), out var res));
            Assert.AreEqual(rate, res.Freight[0].RatePerTonne);
            Assert.AreEqual(fare, res.FarePerPassenger);
            Assert.AreEqual(12 * rate, res.FreightRevenue);
            Assert.AreEqual(3 * fare, res.FareRevenue);

            int k = 0;
            while (m.RatePerTonne(offer) == rate && k < 20) sim.Advance(Days(m.Tuning.Rates.RerollIntervalDays * ++k));
            Assert.AreNotEqual(rate, m.RatePerTonne(offer), "Test setup: the published rate never changed.");
            Assert.AreEqual(rate, res.Freight[0].RatePerTonne);
            Assert.AreEqual(fare, res.FarePerPassenger);
        }
    }
}
