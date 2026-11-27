using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Json;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim.Saves
{
    internal enum SaveReadKind
    {
        Loaded,

        /// <summary>The file cannot be trusted (unreadable, cut short, checksum or consistency failure): the backup may be tried.</summary>
        Damaged,

        /// <summary>The file is intact but this game cannot use it (newer or unknown format, or tuning that lacks something it uses): no backup is tried.</summary>
        Refused,
    }

    internal sealed class SaveReadOutcome
    {
        public SaveReadKind Kind { get; }
        public Simulation Simulation { get; }
        public long SavedUtcMs { get; }
        public string Problem { get; }

        private SaveReadOutcome(SaveReadKind kind, Simulation sim, long savedUtcMs, string problem)
        {
            Kind = kind;
            Simulation = sim;
            SavedUtcMs = savedUtcMs;
            Problem = problem;
        }

        public static SaveReadOutcome Loaded(Simulation sim, long savedUtcMs) => new SaveReadOutcome(SaveReadKind.Loaded, sim, savedUtcMs, null);
        public static SaveReadOutcome Damaged(string problem) => new SaveReadOutcome(SaveReadKind.Damaged, null, 0, problem);
        public static SaveReadOutcome Refused(string problem) => new SaveReadOutcome(SaveReadKind.Refused, null, 0, problem);
    }

    /// <summary>
    /// Rebuilds a game from save bytes (spec 11.1, 11.2). The game is built separately and handed back only when every
    /// step succeeds, so nothing is ever half loaded. Tuning comes from the current tuning file, not the save: prices,
    /// rates, and limits follow it, while voyages in flight keep their locked terms and arrival ticks.
    /// </summary>
    internal static class SaveReader
    {
        /// <summary>The last millisecond of the year 9999, the latest time a save can carry.</summary>
        private const long MaxUtcMs = 253_402_300_799_999;

        public static SaveReadOutcome Read(byte[] bytes, TuningData tuning, DebugCapability debug)
        {
            string text;
            try
            {
                text = SaveFormat.Utf8.GetString(bytes);
            }
            catch (ArgumentException)
            {
                return SaveReadOutcome.Damaged("the file contains bytes that are not valid text");
            }

            JsonValue root;
            try
            {
                root = JsonParser.Parse(text);
            }
            catch (JsonSyntaxException e)
            {
                return SaveReadOutcome.Damaged("it is not complete, readable save data: " + e.Message);
            }

            try
            {
                var top = SaveNode.Root(root);
                if (!top.Has("format") || top.Text("format") != SaveFormat.Marker) return SaveReadOutcome.Damaged("it is not a spacetrade2 save file");
                long version = top.Long("version");
                if (version > SaveFormat.CurrentVersion)
                    return SaveReadOutcome.Refused($"it was made by a newer version of spacetrade2 (save format {version}; this game reads formats {SaveFormat.OldestVersion} to {SaveFormat.CurrentVersion})");
                if (version < SaveFormat.OldestVersion)
                    return SaveReadOutcome.Refused($"its save format {version} is not one spacetrade2 has ever written (this game reads formats {SaveFormat.OldestVersion} to {SaveFormat.CurrentVersion})");

                int start = SaveFormat.ChecksumValueStart(text);
                if (start < 0) return SaveReadOutcome.Damaged("it has no checksum");
                string stored = top.Text("checksum");
                if (stored != text.Substring(start, SaveFormat.ChecksumLength) || stored != SaveFormat.Checksum(text, start))
                    return SaveReadOutcome.Damaged("its checksum does not match its contents, so it was changed or damaged after it was written");

                if (version < SaveFormat.CurrentVersion)
                {
                    top = SaveNode.Root(SaveMigrations.Migrate(root, (int)version));
                    top.Text("format");
                    top.Long("version");
                    top.Text("checksum");
                }
                long savedUtcMs = top.Long("savedUtcMs", 0, MaxUtcMs);
                var game = top.Child("game");
                top.Done();
                var sim = ReadGame(game, tuning, debug, out string refusal);
                if (refusal != null) return SaveReadOutcome.Refused(refusal);
                return SaveReadOutcome.Loaded(sim, savedUtcMs);
            }
            catch (Exception e) when (e is SaveDataException || e is ArgumentException || e is OverflowException || e is InvalidOperationException)
            {
                return SaveReadOutcome.Damaged("its contents do not fit together: " + e.Message);
            }
        }

        private static Simulation ReadGame(SaveNode g, TuningData tuning, DebugCapability debug, out string refusal)
        {
            refusal = null;
            var clock = g.Child("clock");
            long tick = clock.Long("tick", 0);
            var speed = (ClockSpeed)clock.Int("speed");
            if (!ClockSpeeds.IsValid(speed)) throw new SaveDataException($"game.clock.speed must be 1, 3, or 5, got {(int)speed}.");
            bool paused = clock.Bool("paused");
            clock.Done();

            long credits = g.Long("credits", 0);
            long suppliesBought = g.Long("suppliesBought", 0);
            long inSystemIncome = g.Long("inSystemIncome", 0);
            int nextShipId = g.Int("nextShipId", 1);
            var galaxy = ReadGalaxy(g.Child("galaxy"));
            var market = ReadMarket(g.Child("market"), galaxy, tuning);
            var ships = g.List("ships").Select(s => ReadShip(s, galaxy.Systems.Count)).ToList();
            var totals = ReadTotals(g.Child("deliveryTotals"));
            var receipts = g.List("receipts").Select(ReadReceipt).ToList();
            var notifications = g.List("notifications").Select(n =>
            {
                var x = new Notification(n.Long("tick", 0), n.Enum<NotificationKind>("kind"), n.Int("ship", 1), n.Text("text"));
                n.Done();
                return x;
            }).ToList();
            var ev = g.Child("events");
            long nextSequence = ev.Long("nextSequence", 0);
            var events = ev.List("pending").Select(e =>
            {
                long at = e.Long("tick", 0);
                string type = e.Text("type");
                int id = e.Int("id");
                long sequence = e.Long("sequence", 0);
                e.Done();
                var created = SaveEvents.Create(type, id) ?? throw new SaveDataException($"{e.Path}.type \"{type}\" is not an event the game knows.");
                return new EventQueue.ScheduledEvent(at, sequence, created);
            }).ToList();
            ev.Done();
            g.Done();

            var missing = market.MissingFromTuning(tuning);
            foreach (var ship in ships)
            {
                if (tuning.FindShip(ship.ClassId) == null) missing.Add($"Ship {ship.Id} ({ship.Name}) is a \"{ship.ClassId}\", a ship class the tuning file does not define.");
                if (tuning.FindDrive(ship.DriveId) == null) missing.Add($"Ship {ship.Id} ({ship.Name}) has drive \"{ship.DriveId}\", which the tuning file does not define.");
            }
            if (missing.Count > 0)
            {
                refusal = "the tuning file no longer defines something this game uses. " + string.Join(" ", missing) + " Put it back in tuning.json to load this save";
                return null;
            }

            var sim = new Simulation(tick, speed, paused, debug);
            sim.RestoreState(market, credits, suppliesBought, inSystemIncome, nextShipId, ships, totals, receipts, notifications, events, nextSequence);
            return sim;
        }

        private static Galaxy ReadGalaxy(SaveNode g)
        {
            long seed = g.Long("seed");
            int start = g.Int("startSystem", 0);
            var systems = g.List("systems").Select(s =>
            {
                var sys = new StarSystem(s.Int("id", 0), s.Text("name"), s.Long("xMilliLy"), s.Long("yMilliLy"), s.Int("populationMillions", 0),
                    s.TextList("industries"), s.Bool("shipyard"));
                s.Done();
                return sys;
            }).ToList();
            g.Done();
            if (systems.Count == 0) throw new SaveDataException("game.galaxy.systems is empty.");
            return new Galaxy(seed, start, systems);
        }

        private static SeededRandom ReadRandom(SaveNode n, string name)
        {
            var words = n.TextList(name);
            if (words.Count != 4) throw new SaveDataException($"{n.Path}.{name} must hold 4 numbers, got {words.Count}.");
            var parsed = new ulong[4];
            for (int i = 0; i < 4; i++)
                if (words[i].Length != 16 || !ulong.TryParse(words[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed[i]))
                    throw new SaveDataException($"{n.Path}.{name}[{i}] must be 16 hexadecimal digits, got \"{words[i]}\".");
            return new SeededRandom(parsed[0], parsed[1], parsed[2], parsed[3]);
        }

        private static Market ReadMarket(SaveNode m, Galaxy galaxy, TuningData tuning)
        {
            long created = m.Long("createdTick", 0);
            long jobLimit = m.Long("maxJobDistanceMilliLy", 1);
            var random = ReadRandom(m, "random");
            var yardRandom = ReadRandom(m, "yardRandom");
            bool holds = m.Bool("holdsOpeningRates");
            List<CommodityStock> Stocks(string name) => m.List(name).Select(s =>
            {
                var x = new CommodityStock(s.Int("system"), s.Text("commodity"), s.Int("tonnes", 0));
                s.Done();
                return x;
            }).ToList();
            var supplies = Stocks("supplies");
            var demands = Stocks("demands");
            var offers = m.List("offers").Select(o =>
            {
                var x = (o.Int("origin"), o.Text("commodity"), o.Int("destination"), o.Int("rollPercent", 0));
                o.Done();
                return x;
            }).ToList();
            var pools = m.List("pools").Select(p =>
            {
                var x = (p.Int("origin"), p.Int("destination"), p.Int("waiting", 0), p.Int("rollPercent", 0));
                p.Done();
                return x;
            }).ToList();
            var rolls = m.List("provisionRolls").Select(r =>
            {
                var x = new ProvisionRoll(r.Int("system", 0), r.Int("fuelPercent", 0), r.Int("foodPercent", 0), r.Int("waterPercent", 0));
                r.Done();
                return x;
            }).ToList();
            var yards = m.List("shipyards").Select(y =>
            {
                var yard = new Shipyard(y.Int("system"), y.Int("priceRollPercent", 0));
                foreach (var s in y.List("ships")) { yard.Add(new YardStock(YardItemKind.Ship, s.Text("id"), s.Int("count", 0), s.Int("limitRollPercent", 0, 100))); s.Done(); }
                foreach (var d in y.List("drives")) { yard.Add(new YardStock(YardItemKind.Drive, d.Text("id"), d.Int("count", 0), d.Int("limitRollPercent", 0, 100))); d.Done(); }
                y.Done();
                return yard;
            }).ToList();
            m.Done();
            return Market.Restore(galaxy, tuning, created, random, yardRandom, holds, supplies, demands, offers, pools, rolls, yards, jobLimit);
        }

        private static void ReadSupply(SaveNode ship, string name, SupplyStock stock)
        {
            var s = ship.Child(name);
            long kg = s.Long("kg", 0);
            long cost = s.Long("cost", 0);
            s.Done();
            if (kg == 0 && cost != 0) throw new SaveDataException($"{s.Path} has 0 kg but a cost of {cost}; an empty supply has no cost.");
            stock.Add(kg, cost);
        }

        private static Ship ReadShip(SaveNode s, int systemCount)
        {
            int id = s.Int("id", 1);
            string name = s.Text("name");
            string classId = s.Text("class");
            string driveId = s.Text("drive");
            int? docked = s.OptInt("dockedSystem", 0, systemCount - 1);
            var ship = new Ship(id, name, classId, driveId, docked ?? 0) { DockedSystemId = docked };
            ReadSupply(s, "fuel", ship.Fuel);
            ReadSupply(s, "food", ship.Food);
            ReadSupply(s, "water", ship.Water);
            ship.InSystemSinceTick = s.OptLong("inSystemSinceTick", 0);
            var v = s.OptChild("voyage");
            if (v != null) ship.Voyage = ReadVoyage(v, id, systemCount);
            var r = s.OptChild("route");
            if (r != null) ship.Route = ReadRoute(r, systemCount);
            s.Done();

            if (docked.HasValue == (ship.Voyage != null))
                throw new SaveDataException($"{s.Path} must have either a docked system or a voyage in flight, not {(docked.HasValue ? "both" : "neither")}.");
            if (ship.InSystemSinceTick.HasValue && ship.Voyage != null)
                throw new SaveDataException($"{s.Path} is on in-system transport while in flight.");
            return ship;
        }

        private static List<LockedFreight> ReadFreight(SaveNode n) => n.List("freight").Select(f =>
        {
            var x = new LockedFreight(f.Text("commodity"), f.Long("tonnes", 1), f.Long("ratePerTonne", 0));
            f.Done();
            return x;
        }).ToList();

        private static Voyage ReadVoyage(SaveNode v, int shipId, int systemCount)
        {
            var voyage = new Voyage(shipId, v.Int("origin", 0, systemCount - 1), v.Int("destination", 0, systemCount - 1), v.Long("distanceMilliLy", 1), v.Text("drive"),
                v.Long("departureTick", 0), v.Long("arrivalTick", 0), ReadFreight(v), v.Int("passengers", 0), v.Long("farePerPassenger", 0),
                v.Long("fuelUseKg", 0), v.Long("foodUseKg", 0), v.Long("waterUseKg", 0));
            v.Done();
            return voyage;
        }

        private static ShipRoute ReadRoute(SaveNode r, int systemCount)
        {
            bool paused = r.Bool("paused");
            int current = r.Int("currentStop", 0);
            var status = r.Enum<RouteStatus>("status");
            string text = r.OptText("statusText");
            bool topUp = r.Bool("blockedForTopUp");
            long? last = r.OptLong("lastCheckTick", 0);
            long? pending = r.OptLong("pendingCheckTick", 0);
            var stops = r.List("stops").Select(s =>
            {
                var stop = new RouteStop(s.Int("system", 0, systemCount - 1), s.TextList("freight"), s.Bool("carryPassengers"), s.Bool("autoProvision"),
                    s.Int("minFreightTonnes", 0), s.Int("minPassengers", 0), s.Bool("emptyLeg"));
                s.Done();
                return stop;
            }).ToList();
            r.Done();
            if (stops.Count < 2) throw new SaveDataException($"{r.Path}.stops must list at least 2 stops, got {stops.Count}.");
            if (current >= stops.Count) throw new SaveDataException($"{r.Path}.currentStop is {current}, but the route has {stops.Count} stops.");
            return new ShipRoute(stops, current)
            {
                IsPaused = paused,
                Status = status,
                StatusText = text,
                BlockedForTopUp = topUp,
                LastCheckTick = last,
                PendingCheckTick = pending,
            };
        }

        private static DeliveryTotals ReadTotals(SaveNode t)
        {
            var totals = new DeliveryTotals(t.Long("deliveries", 0), t.Long("freightTonnes", 0), t.Long("passengers", 0), t.Long("freightRevenue", 0), t.Long("fareRevenue", 0),
                t.Long("fuelKg", 0), t.Long("fuelCost", 0), t.Long("foodKg", 0), t.Long("foodCost", 0), t.Long("waterKg", 0), t.Long("waterCost", 0));
            t.Done();
            return totals;
        }

        internal static VoyageReceipt ReadReceipt(SaveNode r)
        {
            var receipt = new VoyageReceipt(r.Int("ship", 1), r.Text("shipName"), r.Int("origin", 0), r.Int("destination", 0), r.Long("departureTick", 0), r.Long("arrivalTick", 0),
                ReadFreight(r), r.Int("passengers", 0), r.Long("farePerPassenger", 0), r.Long("freightRevenue", 0), r.Long("fareRevenue", 0),
                r.List("supplies").Select(s =>
                {
                    var x = new ConsumedSupply(s.Enum<SupplyKind>("kind"), s.Long("kg", 0), s.Long("cost", 0));
                    s.Done();
                    return x;
                }).ToList());
            r.Done();
            return receipt;
        }
    }

    /// <summary>
    /// Converts an older save format to the current one, one version at a time, before it is read (spec 11.2). Each step
    /// takes the whole save for version v and returns it as version v + 1.
    /// </summary>
    internal static class SaveMigrations
    {
        private static readonly Dictionary<int, Func<JsonValue, JsonValue>> Steps = new Dictionary<int, Func<JsonValue, JsonValue>>
        {
            [1] = AddDeliveryTotals,
            [2] = AddJobDistanceLimit,
        };

        public static JsonValue Migrate(JsonValue save, int fromVersion)
        {
            for (int v = fromVersion; v < SaveFormat.CurrentVersion; v++)
            {
                if (!Steps.TryGetValue(v, out var step)) throw new SaveDataException($"There is no way to convert save format {v} to format {v + 1}.");
                save = step(save);
            }
            return save;
        }

        /// <summary>
        /// Format 1 to 2: format 1 kept every receipt, so the delivery totals are exactly the totals of the receipts in the file.
        /// Receipts beyond receipts.keepNewest are dropped later, when the game is restored.
        /// </summary>
        private static JsonValue AddDeliveryTotals(JsonValue save)
        {
            var game = save.Get("game") ?? throw new SaveDataException("save.game is missing.");
            if (game.Kind != JsonKind.Object) throw new SaveDataException($"save.game must be a group of fields, got {game.Describe()}.");
            if (game.Get("deliveryTotals") != null) throw new SaveDataException("save.game.deliveryTotals is not a field save format 1 has.");
            var receipts = SaveNode.At(game, "save.game").List("receipts").Select(SaveReader.ReadReceipt);
            var totals = JsonParser.Parse(SaveWriter.Totals(DeliveryTotals.Of(receipts)).ToString());

            var gameMembers = new List<KeyValuePair<string, JsonValue>>();
            foreach (var m in game.Members)
            {
                if (m.Key == "receipts") gameMembers.Add(new KeyValuePair<string, JsonValue>("deliveryTotals", totals));
                gameMembers.Add(m);
            }
            return WithGame(save, 2, gameMembers);
        }

        /// <summary>
        /// Format 2 to 3: format 2 had freight offers and passenger pools at any distance, so it gets no limit: the largest
        /// galaxy.maxJobDistanceMilliLy the tuning file accepts, or the widest distance between two of its systems if that is
        /// larger. Every job it has stays.
        /// </summary>
        private static JsonValue AddJobDistanceLimit(JsonValue save)
        {
            var game = save.Get("game") ?? throw new SaveDataException("save.game is missing.");
            if (game.Kind != JsonKind.Object) throw new SaveDataException($"save.game must be a group of fields, got {game.Describe()}.");
            var market = game.Get("market") ?? throw new SaveDataException("save.game.market is missing.");
            if (market.Kind != JsonKind.Object) throw new SaveDataException($"save.game.market must be a group of fields, got {market.Describe()}.");
            if (market.Get("maxJobDistanceMilliLy") != null) throw new SaveDataException("save.game.market.maxJobDistanceMilliLy is not a field save format 2 has.");
            var systems = SaveNode.At(game, "save.game").Child("galaxy").List("systems").Select(s => (x: s.Long("xMilliLy"), y: s.Long("yMilliLy"))).ToList();
            long widest = TuningReader.MaxJobDistanceLimitMilliLy;
            foreach (var a in systems)
            foreach (var b in systems)
                widest = Math.Max(widest, Galaxy.Distance(a.x, a.y, b.x, b.y));

            var marketMembers = new List<KeyValuePair<string, JsonValue>>();
            foreach (var m in market.Members)
            {
                marketMembers.Add(m);
                if (m.Key == "createdTick")
                    marketMembers.Add(new KeyValuePair<string, JsonValue>("maxJobDistanceMilliLy", JsonValue.Number(m.Value.Line, widest.ToString(CultureInfo.InvariantCulture))));
            }
            var gameMembers = game.Members
                .Select(m => m.Key == "market" ? new KeyValuePair<string, JsonValue>("market", JsonValue.Object(m.Value.Line, marketMembers)) : m)
                .ToList();
            return WithGame(save, 3, gameMembers);
        }

        /// <summary>The save with its version set to <paramref name="version"/> and its game replaced by <paramref name="gameMembers"/>.</summary>
        private static JsonValue WithGame(JsonValue save, int version, List<KeyValuePair<string, JsonValue>> gameMembers)
        {
            var members = new List<KeyValuePair<string, JsonValue>>();
            foreach (var m in save.Members)
            {
                if (m.Key == "version") members.Add(new KeyValuePair<string, JsonValue>("version", JsonValue.Number(m.Value.Line, version.ToString(CultureInfo.InvariantCulture))));
                else if (m.Key == "game") members.Add(new KeyValuePair<string, JsonValue>("game", JsonValue.Object(m.Value.Line, gameMembers)));
                else members.Add(m);
            }
            return JsonValue.Object(save.Line, members);
        }
    }
}
