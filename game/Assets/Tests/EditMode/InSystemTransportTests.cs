using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class InSystemTransportTests
    {
        private const long Day = GameTime.TicksPerDay;

        private static long Daily(Simulation sim, string classId = "courier") => sim.Market.Tuning.FindShip(classId).InSystemDailyIncome;

        private static void On(Simulation sim, int shipId = CourierId) => Assert.IsTrue(sim.SetInSystemTransport(shipId, true).Succeeded);
        private static void Off(Simulation sim, int shipId = CourierId) => Assert.IsTrue(sim.SetInSystemTransport(shipId, false).Succeeded);

        [Test]
        public void FixtureCourier_Earns17PerDay()
        {
            Assert.AreEqual(17, Daily(NewSim(0)));
        }

        [Test]
        public void TogglingOnAndOffWithinADay_EarnsNothing()
        {
            var sim = NewSim(1_000);
            var rng = new SeededRandom(5);
            for (int round = 0; round < 200; round++)
            {
                On(sim);
                sim.Advance(sim.CurrentTick + rng.Range(0L, Day - 1));
                Off(sim);
                sim.Advance(sim.CurrentTick + rng.Range(0L, 3 * Day));
            }
            Assert.AreEqual(1_000, sim.Credits);
            StringAssert.DoesNotContain("InSystemIncomeEvent", State(sim));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(7)]
        [TestCase(360)]
        [TestCase(3_600)]
        public void NFullDays_EarnExactlyNTimesTheDailyRate(int n)
        {
            var sim = NewSim(0);
            On(sim);
            sim.Advance(n * Day - 1);
            Assert.AreEqual((n - 1) * Daily(sim), sim.Credits, "One tick short of the last day");
            sim.Advance(n * Day);
            Assert.AreEqual(n * Daily(sim), sim.Credits);
        }

        [Test]
        public void SameTotal_InOneStep_ManySteps_OrThroughTheRealTimeDriver()
        {
            const long days = 500;
            long end = days * Day + 4_321;

            Simulation Fresh()
            {
                var s = NewSim(0);
                s.SetSpeed(ClockSpeed.X5);
                s.Advance(1_234);
                On(s);
                return s;
            }

            var one = Fresh();
            one.Advance(end);

            var many = Fresh();
            var rng = new SeededRandom(9);
            while (many.CurrentTick < end) many.Advance(System.Math.Min(end, many.CurrentTick + rng.Range(1L, 3 * Day)));

            var driven = Fresh();
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(driven, clock);
            long realMicroseconds = (end - driven.CurrentTick) * 1_000 / 5;
            while (realMicroseconds > 0)
            {
                long chunk = System.Math.Min(realMicroseconds, rng.Range(1L, 50_000L));
                clock.AdvanceMicroseconds(chunk);
                driver.Pump();
                realMicroseconds -= chunk;
            }

            Assert.AreEqual(end, driven.CurrentTick);
            Assert.AreEqual(days * Daily(one), one.Credits);
            Assert.AreEqual(State(one), State(many));
            Assert.AreEqual(State(one), State(driven));
        }

        [Test]
        public void SwitchingOffThenOn_RestartsTheCount()
        {
            var sim = NewSim(0);
            On(sim);
            sim.Advance(Day + 5_000);
            Assert.AreEqual(Daily(sim), sim.Credits);
            Off(sim);
            On(sim);
            sim.Advance(2 * Day + 4_999);
            Assert.AreEqual(Daily(sim), sim.Credits, "The half day before switching off pays nothing");
            sim.Advance(2 * Day + 5_000);
            Assert.AreEqual(2 * Daily(sim), sim.Credits);
        }

        [Test]
        public void SwitchingOffAtTheMomentADayCompletes_KeepsThatDay()
        {
            var sim = NewSim(0);
            On(sim);
            sim.Advance(3 * Day);
            Off(sim);
            sim.Advance(10 * Day);
            Assert.AreEqual(3 * Daily(sim), sim.Credits);
        }

        [Test]
        public void AskingForTheCurrentState_SucceedsAndChangesNothing()
        {
            var sim = NewSim(0);
            var before = StateBytes(sim);
            Assert.IsTrue(sim.SetInSystemTransport(CourierId, false).Succeeded);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            On(sim);
            sim.Advance(Day / 2);
            before = StateBytes(sim);
            Assert.IsTrue(sim.SetInSystemTransport(CourierId, true).Succeeded);
            CollectionAssert.AreEqual(before, StateBytes(sim), "Switching on again does not restart the count");
            sim.Advance(Day);
            Assert.AreEqual(Daily(sim), sim.Credits);
        }

        [Test]
        public void UnknownShip_IsRefused()
        {
            var sim = NewSim(0);
            var before = StateBytes(sim);
            var r = sim.SetInSystemTransport(99, true);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("no ship with id 99", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        [Test]
        public void EachShipEarnsItsOwnClassRate()
        {
            var sim = NewSim(0);
            var liner = sim.AddShip("Test Liner", "passenger_liner", sim.Market.Tuning.Start.StarterDrive, Alpha);
            On(sim);
            On(sim, liner.Id);
            sim.Advance(30 * Day);
            Assert.AreEqual(30 * (Daily(sim) + Daily(sim, "passenger_liner")), sim.Credits);
        }

        [Test]
        public void ToggledShip_CannotBeDispatched_AndNothingChanges()
        {
            var sim = NewSim(1_000_000);
            On(sim);
            sim.Advance(Day / 3);
            var before = StateBytes(sim);
            foreach (bool topUp in new[] { true, false })
            {
                var r = sim.Dispatch(Plan(10, 1), topUp);
                Assert.IsFalse(r.Succeeded);
                StringAssert.Contains("Turn off in-system transport to dispatch it.", r.Reason);
                StringAssert.Contains("Test Courier is on in-system transport at Alpha", r.Reason);
                CollectionAssert.AreEqual(before, StateBytes(sim), $"buyTopUp={topUp}");
            }
        }

        [Test]
        public void ToggledShip_CanStillBeQuotedAndProvisioned()
        {
            var sim = NewSim(1_000_000);
            On(sim);
            Assert.IsTrue(sim.Quote(Plan(10, 1)).IsFeasible);
            Assert.IsTrue(sim.Provision(Plan(10, 1)).Succeeded);
            Assert.IsTrue(Courier(sim).IsOnInSystemTransport);
            Off(sim);
            Assert.IsTrue(sim.Dispatch(Plan(10, 1), buyTopUp: false).Succeeded);
        }

        [Test]
        public void ShipInFlight_CannotBeSwitchedOn_AndNothingChanges()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Dispatch(Plan(10, 0), buyTopUp: true).Succeeded);
            sim.Advance(Day * 100);
            var before = StateBytes(sim);
            var r = sim.SetInSystemTransport(CourierId, true);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("Test Courier is in transit to Beta, arriving", r.Reason);
            StringAssert.Contains("can only be switched on for a docked ship", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            Assert.IsTrue(sim.SetInSystemTransport(CourierId, false).Succeeded, "Off is already the state: succeeds, changes nothing");
            CollectionAssert.AreEqual(before, StateBytes(sim));

            sim.Advance(Courier(sim).Voyage.ArrivalTick);
            On(sim);
            Assert.IsTrue(Courier(sim).IsOnInSystemTransport, "Once docked it can be switched on");
        }

        [Test]
        public void EarnsNothingWhilePaused()
        {
            var sim = NewSim(0);
            On(sim);
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);
            Assert.IsTrue(driver.Pause().Succeeded);
            clock.AdvanceSeconds(3_600);
            driver.Pump();
            Assert.AreEqual(0, sim.CurrentTick);
            Assert.AreEqual(0, sim.Credits, "A real hour paused earns nothing");

            Assert.IsTrue(driver.Play().Succeeded);
            clock.AdvanceSeconds(10);
            driver.Pump();
            Assert.AreEqual(Day, sim.CurrentTick);
            Assert.AreEqual(Daily(sim), sim.Credits, "10 real seconds at 1x is one game day");
        }

        [Test]
        public void RepeatedToggling_CannotCreateExtraMoney()
        {
            var sim = NewSim(0);
            var rng = new SeededRandom(77);
            const long end = 400 * Day;
            long expectedDays = 0;
            long? since = null;
            while (sim.CurrentTick < end)
            {
                long next = System.Math.Min(end, sim.CurrentTick + rng.Range(1L, 3 * Day));
                sim.Advance(next);
                if (rng.Range(0, 1) == 0) continue;
                if (since == null) { On(sim); since = sim.CurrentTick; }
                else { Off(sim); expectedDays += (sim.CurrentTick - since.Value) / Day; since = null; }
            }
            if (since != null) expectedDays += (end - since.Value) / Day;

            Assert.AreEqual(expectedDays * Daily(sim), sim.Credits, "Pay is exactly the full days of each on-period");
            Assert.LessOrEqual(sim.Credits, (end / Day) * Daily(sim), "Never more than leaving it on the whole time");
        }

        /// <summary>Spec 14.2: a docked ship with no credits earns enough to provision the cheapest feasible job.</summary>
        [Test]
        public void ShipWithZeroCredits_EarnsEnoughToProvisionTheCheapestJob()
        {
            var sim = NewGame.Create(1, TestTuning.Load()).Simulation;
            sim.SetCredits(0);
            const int ship = 1;
            int start = sim.Market.Galaxy.StartSystemId;

            var candidates = sim.Market.OffersFrom(start).Select(o => new VoyagePlan(ship, o.DestinationId, new[] { new FreightLine(o.CommodityId, 1) }))
                .Concat(sim.Market.PoolsFrom(start).Select(p => new VoyagePlan(ship, p.DestinationId, null, 1)));
            VoyageQuote cheapest = null;
            foreach (var plan in candidates)
            {
                var q = sim.Quote(plan);
                bool onlyMoneyShort = q.IsPlanValid && q.Problems.Count == 1 && !q.CanAffordTopUp;
                if (onlyMoneyShort && (cheapest == null || q.TopUpCost < cheapest.TopUpCost)) cheapest = q;
            }
            Assert.IsNotNull(cheapest, "Test setup: no job is blocked only by money");
            long cost = cheapest.TopUpCost;
            long daily = Daily(sim);
            long days = (cost + daily - 1) / daily;
            TestContext.WriteLine($"Cheapest job top-up {cost} credits at {daily} a day: {days} game days ({days / 360.0:F1} game years)");

            sim.Play();
            On(sim, ship);
            sim.Advance((days - 1) * Day);
            Assert.Less(sim.Credits, cost);
            Assert.IsFalse(sim.Provision(cheapest.Plan).Succeeded, "One day short");
            sim.Advance(days * Day);
            Assert.AreEqual(days * daily, sim.Credits);

            Assert.IsTrue(sim.Provision(cheapest.Plan).Succeeded);
            var refused = sim.Dispatch(cheapest.Plan, buyTopUp: false);
            Assert.IsFalse(refused.Succeeded, "Cannot dispatch while toggled");
            StringAssert.Contains("Turn off in-system transport", refused.Reason);
            Off(sim, ship);
            Assert.IsTrue(sim.Dispatch(cheapest.Plan, buyTopUp: false).Succeeded);
        }
    }
}
