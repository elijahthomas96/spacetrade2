using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Session 10c: the job distance limit (spec 4, 5, 11, 12). On the three-system line, Alpha (mining colony) to Gamma
    /// (agricultural system) is 7.5 ly, beyond a 7 ly limit, while Alpha to Beta (4.5 ly) and Beta to Gamma (3 ly) are within it.
    /// </summary>
    public class JobDistanceLimitTests
    {
        private const long Limit = 7_000;
        private const int FarShipId = 2;

        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        private static TuningData Limited(long limit = Limit)
        {
            var t = FlatTuning();
            t.Galaxy.MaxJobDistanceMilliLy = limit;
            return t;
        }

        /// <summary>The three-system line with the courier (ship 1) at Alpha and a second courier (ship 2) at Gamma.</summary>
        private static Simulation Line(long limit = Limit)
        {
            var t = Limited(limit);
            var sim = NewThreeSystemSim(5_000_000, t);
            sim.AddShip("Far", t.Start.StarterShipClass, t.Start.StarterDrive, Gamma);
            return sim;
        }

        private static TuningData Sized(TuningData t, int systems)
        {
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            var names = new List<string>(t.Galaxy.Names);
            for (int i = names.Count; i < systems; i++) names.Add($"System {i + 1:000}");
            t.Galaxy.Names = names;
            return t;
        }

        // ---- What a market offers ----

        [Test]
        public void Fixture_IsUnlimited_EveryPairHasItsJobs_On200Seeds()
        {
            var t = TestTuning.Load();
            Assert.AreEqual(1_000_000, t.Galaxy.MaxJobDistanceMilliLy, "The fixture's limit is the largest the tuning accepts.");
            long widest = 0;
            for (long seed = 1; seed <= 200; seed++)
            {
                var sim = NewGame.TryCreate(seed, t, out var problems);
                Assert.IsNotNull(sim, string.Join(" ", problems));
                var g = sim.Market.Galaxy;
                int n = g.Systems.Count;
                Assert.AreEqual(n * (n - 1), sim.Market.Pools.Count, $"seed {seed}: a pool for every pair of systems");
                for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    widest = System.Math.Max(widest, g.DistanceMilliLy(a, b));
                JobDistanceChecks.AssertGuaranteesWithin(sim, t, t.Galaxy.MaxJobDistanceMilliLy, $"fixture seed {seed}");
            }
            TestContext.WriteLine($"Widest distance in 200 fixture galaxies: {widest} milli-ly");
            Assert.Less(widest, t.Galaxy.MaxJobDistanceMilliLy, "Nothing is out of range with the fixture.");
        }

        [Test]
        public void Fixture_At50Systems_IsStillUnlimited()
        {
            var t = Sized(TestTuning.Load(), 50);
            for (long seed = 1; seed <= 10; seed++)
            {
                var sim = NewGame.TryCreate(seed, t, out var problems);
                Assert.IsNotNull(sim, string.Join(" ", problems));
                Assert.AreEqual(50 * 49, sim.Market.Pools.Count, $"seed {seed}");
                JobDistanceChecks.AssertGuaranteesWithin(sim, t, t.Galaxy.MaxJobDistanceMilliLy, $"fixture at 50, seed {seed}");
            }
        }

        [Test]
        public void HandBuiltLine_HasNoJobBeyondTheLimit()
        {
            var limited = Line();
            Assert.IsNull(limited.Market.FindPool(Alpha, Gamma));
            Assert.IsNull(limited.Market.FindPool(Gamma, Alpha));
            Assert.IsNull(limited.Market.FindOffer(Gamma, "food", Alpha), "Gamma's food reaches Alpha only beyond the limit.");
            Assert.IsNotNull(limited.Market.FindOffer(Alpha, "ore", Beta));
            Assert.IsNotNull(limited.Market.FindPool(Beta, Gamma));
            Assert.AreEqual(4, limited.Market.Pools.Count, "Alpha-Beta and Beta-Gamma, both ways.");
            StringAssert.Contains("\njoblimit 7000\n", limited.DescribeState());

            var open = Line(1_000_000);
            Assert.IsNotNull(open.Market.FindOffer(Gamma, "food", Alpha));
            Assert.AreEqual(6, open.Market.Pools.Count);
        }

        [TestCase(7_000)]
        [TestCase(14_000)]
        public void EveryGuaranteeHoldsWithinTheLimit_On200Seeds(long limit)
        {
            var t = TestTuning.Load();
            t.Galaxy.MaxJobDistanceMilliLy = limit;
            Assert.IsEmpty(TuningValidator.Validate(t));
            int opening = 0;
            var refused = new List<string>();
            for (long seed = 1; seed <= 200; seed++)
            {
                var galaxy = GalaxyGenerator.Generate(seed, t);
                var sim = new Simulation();
                sim.AttachMarket(Market.Create(galaxy, t, 0));
                JobDistanceChecks.AssertGuaranteesWithin(sim, t, limit, $"limit {limit}, seed {seed}");

                var game = NewGame.TryCreate(seed, t, out var problems);
                if (game == null)
                {
                    refused.Add($"seed {seed}");
                    Assert.IsTrue(problems.All(p => p.Contains("galaxy.maxJobDistanceMilliLy")), $"seed {seed}: the refusal must name the field: " + string.Join(" ", problems));
                    continue;
                }
                opening++;
                NewGameChecks.AssertGuarantee(game, t, $"limit {limit}, seed {seed}");
            }
            TestContext.WriteLine($"Limit {limit}: galaxy guarantees on 200 of 200 seeds; opening offers on {opening}" + (refused.Count > 0 ? $" ({string.Join(", ", refused)} refused, naming the field)" : ""));
            if (limit == 14_000) Assert.AreEqual(200, opening, "The live value meets the opening offers on every seed.");
        }

        // ---- Too small ----

        [TestCase(6_999, "galaxy.maxJobDistanceMilliLy: must be at least galaxy.maxHopMilliLy (7000), so every hop that links the map to the start system has jobs, got 6999")]
        [TestCase(0, "galaxy.maxJobDistanceMilliLy:")]
        [TestCase(1_000_001, "galaxy.maxJobDistanceMilliLy:")]
        public void LimitOutOfRange_NamesTheField_AndFallsBackToDefaults(long value, string error)
        {
            var text = TestTuning.With(TestTuning.Text(), "\"maxJobDistanceMilliLy\": 1000000", $"\"maxJobDistanceMilliLy\": {value}");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith(error)), string.Join("\n", result.Errors));
            Assert.AreEqual(TuningWriter.Write(TuningDefaults.Create()), TuningWriter.Write(result.Tuning));
        }

        [Test]
        public void MissingLimit_NamesTheField()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"maxJobDistanceMilliLy\": 1000000,", "");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("galaxy.maxJobDistanceMilliLy")), string.Join("\n", result.Errors));
        }

        [Test]
        public void GalaxyRules_NameTheLimit_WhenATradeExistsOnlyBeyondIt()
        {
            var galaxy = Line().Market.Galaxy;
            var limited = GalaxyRules.FindProblems(galaxy, Limited());
            TestContext.WriteLine(string.Join("\n", limited));
            Assert.IsTrue(limited.Contains("Alpha: uses nothing that another system within galaxy.maxJobDistanceMilliLy (7000 milli-ly) produces (no import)."), string.Join("\n", limited));
            Assert.IsTrue(limited.Contains("Gamma: produces nothing that another system within galaxy.maxJobDistanceMilliLy (7000 milli-ly) uses (no export)."), string.Join("\n", limited));
            Assert.IsTrue(limited.Any(p => p.StartsWith("No closed three-stop freight circuit exists with every leg within galaxy.maxJobDistanceMilliLy")));

            var open = GalaxyRules.FindProblems(galaxy, Limited(1_000_000));
            Assert.IsFalse(open.Any(p => p.StartsWith("Alpha: uses nothing") || p.StartsWith("Gamma: produces nothing")), string.Join("\n", open));
        }

        // ---- Voyages past the limit ----

        [Test]
        public void LoadedFreightPastTheLimit_IsRefused_WithBothDistances_AndChangesNothing()
        {
            var sim = Line();
            var before = StateBytes(sim);
            var plan = new VoyagePlan(FarShipId, Alpha, new[] { new FreightLine("food", 5) });
            var q = sim.Quote(plan);
            Assert.IsFalse(q.IsFeasible);
            CollectionAssert.Contains(q.Problems, "Gamma offers no food jobs to Alpha: it is 7.5 ly away, beyond the 7 ly job distance limit. An empty voyage can still go there.");
            var r = sim.Dispatch(plan, buyTopUp: true);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("beyond the 7 ly job distance limit", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            Assert.IsTrue(Line(1_000_000).Quote(plan).IsFeasible, "With no limit the same job is offered.");
        }

        [Test]
        public void PassengersPastTheLimit_AreRefused_AndChangeNothing()
        {
            var sim = Line();
            var before = StateBytes(sim);
            var plan = new VoyagePlan(CourierId, Gamma, null, 2);
            var q = sim.Quote(plan);
            Assert.IsFalse(q.IsFeasible);
            CollectionAssert.Contains(q.Problems, "Alpha has no passenger bookings to Gamma: it is 7.5 ly away, beyond the 7 ly job distance limit. An empty voyage can still go there.");
            Assert.AreEqual(0, q.FarePerPassenger);
            Assert.IsFalse(sim.Dispatch(plan, buyTopUp: true).Succeeded);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        [Test]
        public void EmptyVoyagePastTheLimit_IsAllowed_AndPaysNothing()
        {
            var sim = Line();
            var plan = new VoyagePlan(CourierId, Gamma);
            var q = sim.Quote(plan);
            Assert.IsTrue(q.IsFeasible, string.Join(" ", q.Problems));
            Assert.AreEqual(7_500, q.DistanceMilliLy);
            Ok(sim.Dispatch(plan, buyTopUp: true));
            sim.Advance(q.ArrivalTick);
            Assert.AreEqual(Gamma, Courier(sim).DockedSystemId);
            var receipt = sim.Receipts.Last();
            Assert.AreEqual(0, receipt.FreightRevenue + receipt.FareRevenue);
        }

        [Test]
        public void JobsWithinTheLimit_AreUnaffected()
        {
            var q = Line().Quote(new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 10) }, 2));
            Assert.IsTrue(q.IsFeasible, string.Join(" ", q.Problems));
            var same = Line(1_000_000).Quote(new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 10) }, 2));
            Assert.AreEqual(same.FreightRevenue, q.FreightRevenue);
            Assert.AreEqual(same.FareRevenue, q.FareRevenue);
        }

        // ---- Routes ----

        private static List<RouteStop> FarLoaded() => new List<RouteStop>
        {
            new RouteStop(Gamma, new[] { "food" }),
            new RouteStop(Alpha, new[] { "ore" }),
            new RouteStop(Beta, carryPassengers: true),
        };

        private static List<RouteStop> FarEmpty() => new List<RouteStop>
        {
            new RouteStop(Gamma, emptyLeg: true),
            new RouteStop(Alpha, new[] { "ore" }),
            new RouteStop(Beta, carryPassengers: true),
        };

        private const string FarLegProblem = "Stop 1 (Gamma): the leg to Alpha is 7.5 ly, beyond the 7 ly job distance limit, so Gamma offers no freight or passenger jobs to Alpha. " +
                                             "Mark this leg as an empty repositioning leg, or choose a nearer next stop.";

        [Test]
        public void RouteWithALoadedLegPastTheLimit_IsRefusedWhenSet_ListingTheLegAndBothDistances()
        {
            var sim = Line();
            var before = StateBytes(sim);
            var r = sim.SetRoute(FarShipId, FarLoaded());
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(FarLegProblem, r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            var passengersOnly = FarLoaded();
            passengersOnly[0] = new RouteStop(Gamma, carryPassengers: true);
            Assert.AreEqual(FarLegProblem, sim.SetRoute(FarShipId, passengersOnly).Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            var estimate = sim.EstimateCircuit(FarShipId, FarLoaded());
            Assert.IsFalse(estimate.IsValid);
            CollectionAssert.Contains(estimate.Problems, FarLegProblem);

            Ok(Line(1_000_000).SetRoute(FarShipId, FarLoaded()));
        }

        [Test]
        public void RouteWithTheFarLegMarkedEmpty_IsAccepted_AndRuns()
        {
            var sim = Line();
            Ok(sim.SetRoute(FarShipId, FarEmpty()));
            sim.Advance(25 * Year);
            var receipts = sim.Receipts.Where(x => x.ShipId == FarShipId).ToList();
            Assert.Greater(receipts.Count, 3, "The route keeps running.");
            Assert.IsTrue(receipts.Where(x => x.OriginId == Gamma).All(x => x.FreightRevenue + x.FareRevenue == 0), "The far leg flies empty.");
            Assert.IsTrue(receipts.Any(x => x.OriginId == Alpha && x.FreightRevenue > 0), "The legs within the limit carry freight.");
        }

        // ---- Saves ----

        [Test]
        public void SaveRoundTrip_KeepsTheLimit_ByteIdentical()
        {
            var sim = Line();
            Ok(sim.SetRoute(FarShipId, FarEmpty()));
            sim.Advance(2 * Year + 777);
            SaveAt(_folder, "a", sim);
            StringAssert.Contains("\"maxJobDistanceMilliLy\": 7000,", System.IO.File.ReadAllText(_folder.File("a.json")));
            Assert.AreEqual(State(sim), State(Load(_folder, "a", Limited())));
        }

        [TestCase(1_000_000)]
        [TestCase(14_000)]
        [TestCase(7_500)]
        public void LoadingWithAnEditedLimit_ChangesNothingInThatGame(long editedLimit)
        {
            var sim = Line();
            Ok(sim.SetRoute(FarShipId, FarEmpty()));
            sim.Advance(Year + 4_321);
            SaveAt(_folder, "a", sim);

            var loaded = Load(_folder, "a", Limited(editedLimit));
            Assert.AreEqual(Limit, loaded.Market.MaxJobDistanceMilliLy);
            Assert.AreEqual(State(sim), State(loaded), "Loading with an edited limit changes nothing.");
            Assert.IsNull(loaded.Market.FindPool(Alpha, Gamma), "No job appears beyond the game's own limit.");

            sim.Advance(sim.CurrentTick + 3 * Year);
            loaded.Advance(loaded.CurrentTick + 3 * Year);
            Assert.AreEqual(State(sim), State(loaded), "And it plays on exactly as before.");

            Assert.AreEqual(editedLimit, Line(editedLimit).Market.MaxJobDistanceMilliLy, "A new game uses the edited value.");
        }

        [Test]
        public void LoadingAnUnlimitedGameWithATighterLimit_ChangesNothing()
        {
            var sim = Line(1_000_000);
            sim.Advance(Year);
            SaveAt(_folder, "a", sim);
            var loaded = Load(_folder, "a", Limited());
            Assert.AreEqual(State(sim), State(loaded));
            Assert.IsNotNull(loaded.Market.FindOffer(Gamma, "food", Alpha), "Jobs the game already had stay.");
        }

        /// <summary>The save as format 2 wrote it: no job distance limit, version 2, sealed again.</summary>
        private static string AsFormatTwo(string text)
        {
            var limit = new Regex("\"maxJobDistanceMilliLy\": \\d+,\\s*");
            Assert.AreEqual(1, limit.Matches(text).Count, "Setup: one job distance limit.");
            string two = limit.Replace(text, "");
            StringAssert.Contains("\"version\": 3,", two);
            return SaveFormat.Seal(two.Replace("\"version\": 3,", "\"version\": 2,"));
        }

        [Test]
        public void FormatTwoSave_IsConverted_WithNoLimit_AndTheGameIsTheSame()
        {
            var sim = Line(1_000_000);
            Ok(sim.SetRoute(FarShipId, FarLoaded()));
            sim.Advance(Year);
            SaveAt(_folder, "a", sim);
            System.IO.File.WriteAllText(_folder.File("two.json"), AsFormatTwo(System.IO.File.ReadAllText(_folder.File("a.json"))));

            var loaded = Load(_folder, "two", FlatTuning());
            Assert.AreEqual(1_000_000, loaded.Market.MaxJobDistanceMilliLy);
            Assert.AreEqual(State(sim), State(loaded), "Format 2 had every job at any distance, so the converted game is byte-identical.");

            var tighter = Load(_folder, "two", Limited(14_000));
            Assert.AreEqual(State(sim), State(tighter), "The tuning's limit does not reach a converted save either.");

            Ok(_folder.Store.Write("two", loaded, T0));
            StringAssert.Contains("\"version\": 3,", System.IO.File.ReadAllText(_folder.File("two.json")));
        }

        [Test]
        public void FormatTwoSave_ThatAlreadyHasALimit_IsDamaged()
        {
            SaveAt(_folder, "a", Line());
            string text = System.IO.File.ReadAllText(_folder.File("a.json"));
            System.IO.File.WriteAllText(_folder.File("a.json"), SaveFormat.Seal(text.Replace("\"version\": 3,", "\"version\": 2,")));
            var r = _folder.Store.Load("a", Limited(), T0);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("save.game.market.maxJobDistanceMilliLy is not a field save format 2 has", r.Messages[0]);
        }

        [TestCase(8_000, "is missing, though it is within the game's job distance limit of 8000 milli-ly")]
        [TestCase(4_000, "is beyond the game's job distance limit of 4000 milli-ly")]
        public void SaveWhoseJobsDoNotMatchItsLimit_IsDamaged(long editedLimit, string reason)
        {
            SaveAt(_folder, "a", Line());
            string text = System.IO.File.ReadAllText(_folder.File("a.json"));
            System.IO.File.WriteAllText(_folder.File("a.json"), SaveFormat.Seal(text.Replace("\"maxJobDistanceMilliLy\": 7000,", $"\"maxJobDistanceMilliLy\": {editedLimit},")));
            var r = _folder.Store.Load("a", Limited(), T0);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains(reason, r.Messages[0]);
        }
    }
}
