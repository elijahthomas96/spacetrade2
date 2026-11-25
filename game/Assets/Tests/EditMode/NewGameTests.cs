using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    public class NewGameTests
    {
        private const long RerollTicks = 90 * GameTime.TicksPerDay;

        private static Simulation NewSim(long seed, TuningData t = null) => NewGame.Create(seed, t ?? TestTuning.Load()).Simulation;

        [Test]
        public void NewGame_OpensPausedAt1x_OnY1M01D01_WithTheStarterShip()
        {
            var t = TestTuning.Load();
            var result = NewGame.Create(42, t);
            var sim = result.Simulation;

            Assert.IsTrue(sim.Clock.IsPaused);
            Assert.AreEqual(ClockSpeed.X1, sim.Clock.Speed);
            Assert.AreEqual(0, sim.CurrentTick);
            Assert.AreEqual("Y1 M01 D01", GameDate.FromTick(sim.CurrentTick).ToString());

            Assert.AreEqual(t.Start.StartingCredits, sim.Credits);
            Assert.AreEqual(1, sim.Ships.Count);
            var ship = sim.Ships[0];
            Assert.AreEqual(t.Start.StarterShipClass, ship.ClassId);
            Assert.AreEqual(t.Start.StarterDrive, ship.DriveId);
            Assert.AreEqual(sim.Market.Galaxy.StartSystemId, ship.DockedSystemId);
            Assert.IsFalse(ship.IsInTransit);
            Assert.IsFalse(ship.IsOnInSystemTransport);
            Assert.AreEqual(0, ship.Fuel.Kg + ship.Food.Kg + ship.Water.Kg, "Supplies start empty");
            Assert.AreEqual(0, ship.Fuel.CostCredits + ship.Food.CostCredits + ship.Water.CostCredits);
            Assert.IsEmpty(sim.Receipts);
            Assert.IsTrue(sim.Market.HoldsOpeningRates);
            Assert.AreEqual(42, result.SeedUsed);
            Assert.IsFalse(result.UsedDefaults);
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void SameSeed_GivesSameStarterName_AndIdenticalState()
        {
            for (long seed = 1; seed <= 20; seed++)
            {
                var a = NewSim(seed);
                var b = NewSim(seed);
                Assert.AreEqual(a.Ships[0].Name, b.Ships[0].Name, $"seed {seed}");
                Assert.AreEqual(a.DescribeState(), b.DescribeState(), $"seed {seed}");
            }
        }

        [Test]
        public void StarterName_ComesFromTheList_AndVariesBySeed()
        {
            var t = TestTuning.Load();
            var seen = new HashSet<string>();
            for (long seed = 1; seed <= 200; seed++)
            {
                string name = NewSim(seed, t).Ships[0].Name;
                CollectionAssert.Contains(t.ShipNames, name, $"seed {seed}");
                seen.Add(name);
            }
            TestContext.WriteLine($"{seen.Count} different starter names over 200 seeds");
            Assert.Greater(seen.Count, 12);
        }

        [Test]
        public void StarterName_IsKeptWhenTheListChangesLater()
        {
            var sim = NewSim(7);
            string name = sim.Ships[0].Name;
            var edited = TestTuning.Load();
            edited.ShipNames = new List<string> { "Renamed" };
            sim.Market.ReplaceTuning(edited);
            sim.Play();
            sim.Advance(400 * GameTime.TicksPerDay);
            Assert.AreEqual(name, sim.Ships[0].Name);
            StringAssert.Contains($"\"{name}\"", sim.DescribeState());

            Assert.AreEqual("Renamed", NewSim(7, edited).Ships[0].Name, "A new game uses the edited list");
        }

        [Test]
        public void TwoHundredSeeds_MeetTheOpeningOfferGuarantee()
        {
            var t = TestTuning.Load();
            int minJobs = int.MaxValue, maxJobs = 0, minProfitable = int.MaxValue;
            for (long seed = 1; seed <= 200; seed++)
            {
                var result = NewGame.Create(seed, t);
                Assert.AreEqual(seed, result.SeedUsed, "The fixture must meet the guarantee on the seed asked for");
                Assert.IsFalse(result.UsedDefaults);
                int jobs = NewGameChecks.AssertGuarantee(result.Simulation, t, $"seed {seed}");
                int profitable = OpeningOffers.Check(result.Simulation, 1).ProfitableJobCount;
                minJobs = System.Math.Min(minJobs, jobs);
                maxJobs = System.Math.Max(maxJobs, jobs);
                minProfitable = System.Math.Min(minProfitable, profitable);
            }
            TestContext.WriteLine($"Feasible jobs per start port: {minJobs} to {maxJobs}; fewest profitable: {minProfitable}");
        }

        [Test]
        public void TwoHundredSeeds_StarterCanReachNearestShipyard_PayingFromStartingCredits()
        {
            var t = TestTuning.Load();
            int flown = 0, atYard = 0;
            for (long seed = 1; seed <= 200; seed++)
            {
                if (NewGameChecks.AssertStarterReachesNearestShipyard(NewSim(seed, t), t, $"seed {seed}")) flown++;
                else atYard++;
            }
            TestContext.WriteLine($"Flown to nearest yard: {flown}; start system has a yard: {atYard}");
            Assert.Greater(flown, 0);
        }

        [Test]
        public void Guarantee_ChangesOnlyStartPortRolls_NeverDistancesOrStock()
        {
            var t = TestTuning.Load();
            int adjusted = 0;
            for (long seed = 1; seed <= 200; seed++)
            {
                var sim = NewSim(seed, t);
                var plain = Market.Create(GalaxyGenerator.Generate(seed, t), t, 0);
                var m = sim.Market;
                int start = m.Galaxy.StartSystemId;
                Assert.AreEqual(plain.Offers.Count, m.Offers.Count);
                bool changed = false;
                for (int i = 0; i < m.Offers.Count; i++)
                {
                    var (a, b) = (plain.Offers[i], m.Offers[i]);
                    Assert.AreEqual(a.DistanceMilliLy, b.DistanceMilliLy);
                    if (a.OriginId != start) { Assert.AreEqual(a.RateRollPercent, b.RateRollPercent, $"seed {seed}"); continue; }
                    Assert.GreaterOrEqual(b.RateRollPercent, a.RateRollPercent);
                    Assert.LessOrEqual(b.RateRollPercent, t.FindCommodity(b.CommodityId).RateRollMaxPercent);
                    changed |= a.RateRollPercent != b.RateRollPercent;
                }
                for (int i = 0; i < m.Pools.Count; i++)
                {
                    var (a, b) = (plain.Pools[i], m.Pools[i]);
                    Assert.AreEqual(a.DistanceMilliLy, b.DistanceMilliLy);
                    Assert.AreEqual(a.Waiting, b.Waiting);
                    if (a.OriginId != start) { Assert.AreEqual(a.FareRollPercent, b.FareRollPercent, $"seed {seed}"); continue; }
                    Assert.GreaterOrEqual(b.FareRollPercent, a.FareRollPercent);
                    Assert.LessOrEqual(b.FareRollPercent, t.Passengers.FareRollMaxPercent);
                    changed |= a.FareRollPercent != b.FareRollPercent;
                }
                for (int i = 0; i < m.Supplies.Count; i++) Assert.AreEqual(plain.Supplies[i].Tonnes, m.Supplies[i].Tonnes);
                for (int i = 0; i < m.Demands.Count; i++) Assert.AreEqual(plain.Demands[i].Tonnes, m.Demands[i].Tonnes);
                if (changed) adjusted++;
            }
            TestContext.WriteLine($"Seeds whose opening rolls were raised: {adjusted} of 200");
        }

        [Test]
        public void OpeningRates_HeldThroughSeveralRerolls_UntilFirstDispatch_ThenRejoinTheSchedule()
        {
            var sim = NewSim(1);
            var m = sim.Market;
            int start = m.Galaxy.StartSystemId;
            string StartRolls() => string.Join(",", m.OffersFrom(start).Select(o => $"{o.RateRollPercent}:{m.RatePerTonne(o)}"))
                + "|" + string.Join(",", m.PoolsFrom(start).Select(p => $"{p.FareRollPercent}:{m.FarePerPassenger(p)}"));
            string OtherRolls() => string.Join(",", m.Offers.Where(o => o.OriginId != start).Select(o => o.RateRollPercent))
                + "|" + string.Join(",", m.Pools.Where(p => p.OriginId != start).Select(p => p.FareRollPercent));

            string startAtZero = StartRolls();
            var jobsAtZero = OpeningOffers.FindJobs(sim, 1).Select(j => $"{j.DestinationId}:{j.CommodityId}:{j.NetProfit}").ToList();
            sim.Play();
            string others = OtherRolls();
            for (int k = 1; k <= 4; k++)
            {
                sim.Advance(k * RerollTicks);
                Assert.AreEqual(startAtZero, StartRolls(), $"Start port rolls changed at re-roll {k}");
                string now = OtherRolls();
                Assert.AreNotEqual(others, now, $"Other systems should re-roll at re-roll {k}");
                others = now;
            }
            Assert.IsTrue(m.HoldsOpeningRates);
            Assert.IsTrue(OpeningOffers.Check(sim, 1).Holds, "The guarantee still holds after waiting four re-rolls");
            CollectionAssert.AreEqual(jobsAtZero, OpeningOffers.FindJobs(sim, 1).Select(j => $"{j.DestinationId}:{j.CommodityId}:{j.NetProfit}").ToList());

            var job = OpeningOffers.FindJobs(sim, 1).First(j => j.NetProfit > 0);
            Assert.IsTrue(sim.Dispatch(job.Quote.Plan, buyTopUp: true).Succeeded);
            Assert.IsFalse(m.HoldsOpeningRates, "The hold ends at the first dispatch");
            Assert.AreEqual(startAtZero, StartRolls(), "Nothing re-rolls until the next scheduled re-roll");

            sim.Advance(5 * RerollTicks - 1);
            Assert.AreEqual(startAtZero, StartRolls());
            sim.Advance(5 * RerollTicks);
            Assert.AreNotEqual(startAtZero, StartRolls(), "The start port re-rolls on the normal schedule after the first dispatch");
            Assert.AreNotEqual(others, OtherRolls());
        }

        [Test]
        public void MarketsNotMadeByNewGame_DoNotHoldRates()
        {
            Assert.IsFalse(Market.Create(GalaxyGenerator.Generate(1, TestTuning.Load()), TestTuning.Load(), 0).HoldsOpeningRates);
        }

        /// <summary>
        /// The path the built-in defaults take when a seed cannot meet the guarantee: the next seed is tried, and
        /// the seed actually used is the game's seed. Exercised with low starting credits, where some seeds fail.
        /// </summary>
        [Test]
        public void SeedThatFailsTheGuarantee_MovesToTheNextSeed_AndStoresTheSeedUsed()
        {
            var t = TestTuning.Load();
            t.Start.StartingCredits = 30_000;
            long failing = -1;
            for (long s = 1; s <= 100 && failing < 0; s++)
                if (NewGame.TryCreate(s, t, out _) == null) failing = s;
            Assert.Greater(failing, 0, "Test setup: no failing seed found");
            long expected = failing + 1;
            while (NewGame.TryCreate(expected, t, out _) == null) expected++;

            var sim = NewGame.CreateTryingSeeds(failing, t, out long used);
            TestContext.WriteLine($"Seed {failing} fails; next seed that works is {used}");
            Assert.AreEqual(expected, used);
            Assert.AreEqual(used, sim.Market.Galaxy.Seed);
            StringAssert.StartsWith($"seed={used} ", sim.DescribeState().Split('\n').First(l => l.StartsWith("seed=")));
            Assert.AreEqual(NewGame.TryCreate(used, t, out _).DescribeState(), sim.DescribeState(), "Identical to a game made directly from the seed used");
            NewGameChecks.AssertGuarantee(sim, t, $"seed {used}");
        }

        [Test]
        public void BuiltInDefaults_NeverFail_OnTwoHundredSeeds()
        {
            for (long seed = 1; seed <= 200; seed++)
            {
                var result = NewGame.Create(seed, TuningDefaults.Create());
                Assert.IsFalse(result.UsedDefaults);
                Assert.IsEmpty(result.Errors);
                Assert.AreEqual(result.SeedUsed, result.Simulation.Market.Galaxy.Seed);
                Assert.IsTrue(OpeningOffers.Check(result.Simulation, 1).Holds, $"seed {seed}");
            }
        }

        [Test]
        public void EditedTuningWithTooLittleMoney_NamesTheFields_AndFallsBackToDefaults()
        {
            var t = TestTuning.Load();
            t.Start.StartingCredits = 5_000;
            var result = NewGame.Create(3, t);
            TestContext.WriteLine(string.Join("\n", result.Errors));
            Assert.IsTrue(result.UsedDefaults);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("start.openingOffers.minFeasibleJobs", result.Errors[0]);
            StringAssert.Contains("start.startingCredits", result.Errors[0]);
            StringAssert.Contains("seed 3", result.Errors[0]);
            Assert.AreEqual(TuningDefaults.Create().Start.StartingCredits, result.Simulation.Credits, "The game was built from the defaults");
            Assert.IsTrue(OpeningOffers.Check(result.Simulation, 1).Holds);
        }

        [Test]
        public void EditedTuningWhereNothingPays_NamesTheFields_AndFallsBackToDefaults()
        {
            var t = TestTuning.Load();
            foreach (var c in t.Commodities) c.BaseRatePerTonne = 1;
            t.Passengers.BaseFare = 1;
            var result = NewGame.Create(3, t);
            TestContext.WriteLine(string.Join("\n", result.Errors));
            Assert.IsTrue(result.UsedDefaults);
            var error = result.Errors.FirstOrDefault(e => e.Contains("start.openingOffers.minProfitableJobs"));
            Assert.IsNotNull(error, string.Join("\n", result.Errors));
            StringAssert.Contains("baseRatePerTonne and rateRollMaxPercent", error);
            StringAssert.Contains("passengers.baseFare", error);
            Assert.IsTrue(OpeningOffers.Check(result.Simulation, 1).Holds);
        }
    }
}
