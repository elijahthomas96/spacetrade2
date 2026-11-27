using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Hud;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Timing;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Session 14: the job list (sortable, filterable, within the job distance limit), the system view (spec 9.2), and the
    /// 50 ms frame rule for opening and sorting the list at 50 systems.
    /// </summary>
    public class JobListTests : PlannerTestBase
    {
        private const double OrdinaryFrameLimitMs = 50.0;
        private GameObject _probeObject;

        [TearDown]
        public void RemoveProbe()
        {
            if (_probeObject != null) Object.Destroy(_probeObject);
            _probeObject = null;
        }

        private static bool Ordered<T>(IEnumerable<T> items, System.Func<T, T, bool> inOrder)
        {
            var list = items.ToList();
            for (int i = 1; i < list.Count; i++) if (!inOrder(list[i - 1], list[i])) return false;
            return true;
        }

        [UnityTest]
        public IEnumerator ThePlannersJobList_SortsByNearestMostProfitableAndQuickest_AndHidesJobsTheShipCannotFly()
        {
            // 25 systems with no distance limit: about half the start port's jobs are too far for the starter courier.
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(25));
            var ship = Sim.Ships[0];
            yield return OpenPlannerFromTheShipPanel();
            var board = Sim.JobBoardFor(ship.Id);
            int all = board.Entries.Count, flyable = board.Entries.Count(e => e.CanFly);
            Assert.Greater(flyable, 0);
            Assert.Greater(all, flyable, "this port has jobs the starter cannot fly");
            var list = Planner.Jobs;
            Assert.AreEqual(all, list.Shown.Count, "every job at the port is listed");

            // Every row carries the board's own figures.
            foreach (var row in list.Shown)
            {
                var e = row.Entry;
                Assert.AreEqual(e.DistanceMilliLy, row.DistanceMilliLy);
                Assert.AreEqual(e.NetProfit, row.NetProfit);
                Assert.AreEqual(e.TransitTicks, row.TransitTicks);
                StringAssert.StartsWith(Format.LightYears(e.DistanceMilliLy) + " · ", row.Detail);
                if (e.CanFly)
                {
                    Assert.AreEqual("Net " + Format.Credits(e.NetProfit), row.Figure);
                    Assert.AreEqual(Sim.Quote(e.BestQuote.Plan).NetProfit, e.NetProfit, "the profit shown is the real quote's");
                }
                else
                {
                    Assert.AreEqual("Cannot fly", row.Figure);
                    StringAssert.EndsWith("Cannot fly: " + e.Reason, row.Detail);
                }
            }

            Assert.AreEqual(JobSort.Nearest, list.Sort, "nearest first to begin with");
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.DistanceMilliLy <= b.DistanceMilliLy));

            Press(PlannerButton("planner-jobs-sort-MostProfitable"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Ordered(list.Shown.Where(r => r.CanFly), (a, b) => a.NetProfit >= b.NetProfit), "most profitable first");
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.CanFly || !b.CanFly), "jobs the ship cannot fly come last");
            Assert.AreEqual(board.Entries.Where(e => e.CanFly).Max(e => e.NetProfit), list.Shown[0].NetProfit);

            Press(PlannerButton("planner-jobs-sort-Quickest"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.TransitTicks <= b.TransitTicks), "quickest first");

            Press(PlannerButton("planner-jobs-sort-Nearest"));
            Press(PlannerButton("planner-jobs-hide-unflyable"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(list.HideUnflyable);
            Assert.AreEqual($"Hide jobs {ship.Name} cannot fly", PlannerButton("planner-jobs-hide-unflyable").text);
            Assert.AreEqual(flyable, list.Shown.Count, "only the jobs the ship can fly are left");
            Assert.IsTrue(list.Shown.All(r => r.CanFly));
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.DistanceMilliLy <= b.DistanceMilliLy), "and still in order");

            Press(PlannerButton("planner-jobs-hide-unflyable"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(all, list.Shown.Count, "the filter off again: everything is back");

            // A job the ship cannot fly can still be chosen, to read why: the quote says it in the simulation's words.
            Planner.Jobs.SetSort(JobSort.MostProfitable);
            var cannot = list.Shown.Last();
            Assert.IsFalse(cannot.CanFly);
            Planner.ChooseJob(cannot);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(cannot.Entry.Reason, string.Join(" ", Sim.Quote(Planner.Plan()).Problems), "the row's reason is the quote's");
            Assert.AreEqual(string.Join("\n", Sim.Quote(Planner.Plan()).Problems), Planner.LineText("quote-problems"));
            Assert.IsFalse(Planner.CanLaunch);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheJobLists_RespectTheJobDistanceLimit_AndAnEmptyVoyageCanStillGoBeyondIt()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(25, 1, t => t.Galaxy.MaxJobDistanceMilliLy = 9_000));
            long limit = Sim.Market.MaxJobDistanceMilliLy;
            Assert.AreEqual(9_000, limit);
            var galaxy = Sim.Market.Galaxy;
            var ship = Sim.Ships[0];
            int origin = ship.DockedSystemId.Value;
            var beyond = galaxy.Systems.Where(s => s.Id != origin && galaxy.DistanceMilliLy(origin, s.Id) > limit).Select(s => s.Id).ToList();
            Assert.Greater(beyond.Count, 0, "this galaxy has systems beyond the limit");

            // The planner's list.
            yield return OpenPlannerFromTheShipPanel();
            Assert.Greater(Planner.Jobs.Shown.Count, 0);
            Assert.AreEqual(Sim.Market.OffersFrom(origin).Count + Sim.Market.PoolsFrom(origin).Count, Planner.Jobs.Shown.Count);
            foreach (var row in Planner.Jobs.Shown)
            {
                Assert.LessOrEqual(galaxy.DistanceMilliLy(origin, row.DestinationId), limit, "a job beyond the limit: " + row.Title);
                CollectionAssert.DoesNotContain(beyond, row.DestinationId);
            }

            // An empty voyage may go to any system: the destination list has them all, nearest first.
            Press(PlannerButton("planner-mode"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(OnScreen(Planner.Destinations.Root));
            Assert.IsFalse(OnScreen(Planner.Jobs.Root));
            Assert.AreEqual(galaxy.Systems.Count - 1, Planner.Destinations.Shown.Count);
            Assert.IsTrue(Ordered(Planner.Destinations.Shown, (a, b) => a.DistanceMilliLy <= b.DistanceMilliLy));
            var far = Planner.Destinations.Shown.First(r => beyond.Contains(r.DestinationId));
            Assert.AreEqual($"{Format.LightYears(galaxy.DistanceMilliLy(origin, far.DestinationId))} · empty voyage, earns nothing", far.Detail);
            Planner.ChooseDestination(far.DestinationId);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(far.DestinationId, Planner.DestinationId);
            Assert.AreEqual(0, Planner.Plan().Freight.Count);
            Assert.AreEqual(0, Planner.Plan().Passengers);
            var quote = Sim.Quote(Planner.Plan());
            Assert.AreEqual(JobText.Terms(quote), JobText.Terms(Planner.Quote));
            Assert.AreEqual(0, Planner.Quote.GrossRevenue, "an empty voyage earns nothing");
            Assert.AreEqual("Revenue: freight 0 + fares 0 = 0", Planner.LineText("quote-revenue"));
            if (quote.IsFeasible)
            {
                Assert.Less(quote.NetProfit, 0, "and the quote shows the loss");
                Assert.IsTrue(Planner.CanLaunch);
            }
            else Assert.AreEqual(string.Join("\n", quote.Problems), Planner.LineText("quote-problems"));

            // The system view's list too, for every system.
            foreach (var s in galaxy.Systems)
            {
                Map.Select(s.Id);
                yield return null;
                foreach (var row in Map.Hud.SystemJobs.Shown)
                    Assert.LessOrEqual(galaxy.DistanceMilliLy(s.Id, row.DestinationId), limit, $"{s.Name}: a job beyond the limit: {row.Title}");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheSystemView_ShowsIndustriesJobsWithRatesSupplyPricesDockedShipsAndYardStock()
        {
            yield return _rig.Open(MapTestRig.FleetGame(12, 0, 2));
            var market = Sim.Market;
            var yard = market.Shipyards[0];
            int id = yard.SystemId;
            var system = market.Galaxy.Systems[id];
            Map.Select(id);
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Hud.InfoPanelShowing);
            Assert.AreEqual(system.Name, Map.Hud.InfoTitle);
            var lines = Map.Hud.InfoLines;

            // Industries.
            foreach (var industry in system.IndustryIds)
                CollectionAssert.Contains(lines, market.Tuning.Industries.First(i => i.Id == industry).Name);

            // Supply prices: the simulation's.
            var prices = market.ProvisionPricesAt(id);
            CollectionAssert.Contains(lines, "Supplies for sale, per tonne");
            CollectionAssert.Contains(lines, "Fuel " + Format.Credits(prices.FuelPerTonne));
            CollectionAssert.Contains(lines, "Food " + Format.Credits(prices.FoodPerTonne));
            CollectionAssert.Contains(lines, "Water " + Format.Credits(prices.WaterPerTonne));

            // Jobs with their rates, nearest first, then by highest rate.
            var offers = market.OffersFrom(id);
            var pools = market.PoolsFrom(id);
            var list = Map.Hud.SystemJobs;
            CollectionAssert.Contains(lines, $"Jobs from {system.Name} ({offers.Count + pools.Count})");
            Assert.AreEqual(offers.Count + pools.Count, list.Shown.Count);
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.DistanceMilliLy <= b.DistanceMilliLy), "nearest first");
            foreach (var offer in offers)
            {
                var row = list.All.Single(r => r.CommodityId == offer.CommodityId && r.DestinationId == offer.DestinationId && !r.IsPassengers);
                Assert.AreEqual($"{JobText.Cargo(Sim, offer.CommodityId)} to {market.Galaxy.Systems[offer.DestinationId].Name}", row.Title);
                Assert.AreEqual($"{Format.LightYears(offer.DistanceMilliLy)} · {Format.Credits(market.TonnesAvailable(offer))} t waiting", row.Detail);
                Assert.AreEqual($"{Format.Credits(market.RatePerTonne(offer))} per tonne", row.Figure);
            }
            foreach (var pool in pools)
            {
                var row = list.All.Single(r => r.IsPassengers && r.DestinationId == pool.DestinationId);
                Assert.AreEqual($"{Format.LightYears(pool.DistanceMilliLy)} · {Format.Credits(pool.Waiting)} waiting", row.Detail);
                Assert.AreEqual($"{Format.Credits(market.FarePerPassenger(pool))} each", row.Figure);
            }
            Press(Map.Hud.Root.Q<Button>("system-jobs-sort-HighestRate"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Ordered(list.Shown, (a, b) => a.Rate >= b.Rate), "highest rate first");

            // Docked ships and shipyard stock.
            int dockedHere = Sim.Ships.Count(s => s.DockedSystemId == id);
            Assert.GreaterOrEqual(dockedHere, 2);
            CollectionAssert.Contains(lines, $"Docked here ({dockedHere})");
            foreach (var ship in Sim.Ships.Where(s => s.DockedSystemId == id))
                CollectionAssert.Contains(lines, $"{ship.Name} · {SpaceTrade.Game.Map.ShipText.ClassName(Sim, ship)}");
            CollectionAssert.Contains(lines, "Shipyard stock");
            foreach (var stock in yard.Ships.Concat(yard.Drives))
            {
                if (!market.Stocks(stock)) continue;
                string name = stock.Kind == SpaceTrade.Sim.Yards.YardItemKind.Ship ? market.Tuning.FindShip(stock.ItemId).Name : market.Tuning.FindDrive(stock.ItemId).Name;
                string count = stock.Count == 0 ? "out of stock" : $"{stock.Count} in stock";
                CollectionAssert.Contains(Map.Hud.InfoLines, $"{name}: {count} · {Format.Credits(market.Price(yard, stock.Kind, stock.ItemId))} credits");
            }

            // A new game day: the list is read again from the simulation (stock is loaded and replenished as time passes).
            Bar.ChooseSpeed(ClockSpeed.X5);
            Bar.TogglePause();
            yield return _rig.RealMs(200_000); // 100 game days at 5x
            Assert.AreEqual(JobSort.HighestRate, Map.Hud.SystemJobs.Sort, "the chosen order is kept");
            foreach (var offer in offers)
            {
                var row = Map.Hud.SystemJobs.All.Single(r => r.CommodityId == offer.CommodityId && r.DestinationId == offer.DestinationId && !r.IsPassengers);
                Assert.AreEqual($"{Format.Credits(market.RatePerTonne(offer))} per tonne", row.Figure, "today's rate");
                Assert.AreEqual($"{Format.LightYears(offer.DistanceMilliLy)} · {Format.Credits(market.TonnesAvailable(offer))} t waiting", row.Detail, "today's stock");
            }

            // A system with no yard shows no stock.
            var plain = market.Galaxy.Systems.First(s => !s.HasShipyard);
            Map.Select(plain.Id);
            yield return MapTestRig.Frames(3);
            CollectionAssert.DoesNotContain(Map.Hud.InfoLines, "Shipyard stock");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The owner's check: at 50 systems the job list opens and sorts with no frame over 50 ms. The fixture has no job
        /// distance limit, so these are the longest lists the game can make. The clock runs at 5x with 50 ships in flight
        /// throughout, so the frames measured also hold the daily re-read of the job board.
        /// </summary>
        [UnityTest]
        public IEnumerator At50Systems_TheJobListOpensAndSorts_AndOurWorkInEveryFrame_StaysUnder50ms()
        {
            _probeObject = new GameObject("Probe");
            var probe = _probeObject.AddComponent<FrameTimeProbe>();
            yield return MapTestRig.Frames();
            var sim = MapTestRig.FleetGame(50, 50, 1);
            yield return MapTestRig.Frames(3);
            _rig.Build(sim, fakeTime: false);
            yield return TestGameView.WaitUntilApplied();
            yield return MapTestRig.Frames(3);
            yield return PlannerFrameCheck.Run(Map, _rig.Runner.Driver, probe, 1.5f, "50 systems x 50 ships");
            var systems = sim.Market.Galaxy.Systems;
            int plannerJobs = PlannerFrameCheck.PlannerJobs, opens = PlannerFrameCheck.Opens, rowElements = PlannerFrameCheck.RowElements;
            TestContext.WriteLine(probe.Report());
            TestContext.WriteLine(PlannerFrameCheck.LastSummary);
            Assert.AreEqual(50, systems.Count);
            Assert.Greater(plannerJobs, 50, "a long list");
            Assert.Greater(opens, 5);
            Assert.Greater(rowElements, 0, "rows were on screen");
            Assert.Less(rowElements, 30, "only the rows on screen exist");
            Assert.AreEqual(2, probe.Segments.Count);
            foreach (var segment in probe.Segments)
            {
                Assert.Greater(segment.Frames, 10, segment.Label);
                Assert.Less(segment.SlowestWorkMs, OrdinaryFrameLimitMs, segment.Label + ": our work in the slowest frame");
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
