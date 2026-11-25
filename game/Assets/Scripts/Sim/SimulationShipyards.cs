using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim
{
    /// <summary>Shipyard commands (spec 7): buying ships, fitting drives, and the drive upgrade preview.</summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Buys a ship of this class at the yard in this system, from anywhere on the map. It is delivered
        /// docked at that yard with the slowest drive, empty supplies, and in-system transport off, named from
        /// the tuning's ship names. Refused, changing nothing, unless the yard has one in stock and the
        /// credits cover its price.
        /// </summary>
        public CommandResult BuyShip(int yardSystemId, string classId, out int newShipId)
        {
            newShipId = 0;
            if (Market == null) return CommandResult.Fail("There is no market yet.");
            var t = Market.Tuning;
            var systems = Market.Galaxy.Systems;
            if (yardSystemId < 0 || yardSystemId >= systems.Count) return CommandResult.Fail($"There is no system with id {yardSystemId}.");
            var shipClass = classId == null ? null : t.FindShip(classId);
            if (shipClass == null) return CommandResult.Fail($"\"{classId}\" is not a ship class in the tuning.");

            string where = systems[yardSystemId].Name;
            string item = "the " + shipClass.Name.ToLowerInvariant();
            var yard = Market.FindShipyard(yardSystemId);
            if (yard == null) return CommandResult.Fail($"{where} has no shipyard. {Sellers(YardItemKind.Ship, classId, item)}");
            var stock = yard.Find(YardItemKind.Ship, classId);
            if (!Market.Stocks(stock)) return CommandResult.Fail($"The shipyard at {where} does not sell {item}. {Sellers(YardItemKind.Ship, classId, item)}");

            long price = Market.Price(yard, YardItemKind.Ship, classId);
            var problems = new List<string>();
            if (stock.Count == 0) problems.Add(OutOfStock(where, item, stock));
            if (price > Credits) problems.Add(CannotAfford(where, item, price));
            if (problems.Count > 0) return CommandResult.Fail(string.Join(" ", problems));

            // Nothing below can fail.
            string name = YardMath.PickPurchaseName(Market.YardRandom, t.ShipNames, new HashSet<string>(_ships.Select(s => s.Name)));
            Credits -= price;
            Market.TakeOne(stock);
            newShipId = AddShip(name, classId, YardMath.SlowestDrive(t).Id, yardSystemId).Id;
            return CommandResult.Ok();
        }

        /// <summary>
        /// Fits a drive to a ship docked at a yard that stocks it, at that yard's full price. The old drive is
        /// gone, with no refund. Any stocked drive other than the one installed may be fitted, slower ones
        /// included. In-system transport, if on, carries on. Refused, changing nothing, for a ship in transit,
        /// a ship not docked at a yard that stocks the drive, the drive already installed, none in stock, or
        /// too few credits.
        /// </summary>
        public CommandResult BuyDrive(int shipId, string driveId)
        {
            if (Market == null) return CommandResult.Fail("There is no market yet.");
            var t = Market.Tuning;
            var systems = Market.Galaxy.Systems;
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            var drive = driveId == null ? null : t.FindDrive(driveId);
            if (drive == null) return CommandResult.Fail($"\"{driveId}\" is not a drive in the tuning.");
            if (ship.IsInTransit)
                return CommandResult.Fail($"{ship.Name} is in transit to {systems[ship.Voyage.DestinationId].Name}, arriving {GameDate.FromTick(ship.Voyage.ArrivalTick)}. A drive can only be fitted while the ship is docked at a shipyard that stocks it.");

            string item = "the " + drive.Name;
            int at = ship.DockedSystemId.Value;
            string where = systems[at].Name;
            var problems = new List<string>();
            if (ship.DriveId == driveId) problems.Add($"{ship.Name} already has {item} installed. Choose a different drive.");

            var yard = Market.FindShipyard(at);
            var stock = yard?.Find(YardItemKind.Drive, driveId);
            long price = 0;
            if (yard == null)
                problems.Add($"{ship.Name} is docked at {where}, which has no shipyard. {NearestStockist(at, driveId, item)}");
            else if (!Market.Stocks(stock))
                problems.Add($"The shipyard at {where}, where {ship.Name} is docked, does not stock {item}. {NearestStockist(at, driveId, item)}");
            else
            {
                price = Market.Price(yard, YardItemKind.Drive, driveId);
                if (stock.Count == 0) problems.Add(OutOfStock(where, item, stock));
                if (price > Credits) problems.Add(CannotAfford(where, item, price));
            }
            if (problems.Count > 0) return CommandResult.Fail(string.Join(" ", problems));

            // Nothing below can fail.
            Credits -= price;
            Market.TakeOne(stock);
            ship.DriveId = driveId;
            return CommandResult.Ok();
        }

        /// <summary>
        /// The drive upgrade preview (spec 7): the drive's price at a yard, and for a route (an ordered loop of
        /// two or more stops, the last leg returning to the first) each leg's transit time and crew-only fuel
        /// with the ship's current drive and with the new one, and the total time saved. Works for any ship,
        /// docked or in transit, from anywhere. Changes nothing.
        /// </summary>
        public DriveUpgradePreview PreviewDriveUpgrade(int shipId, int yardSystemId, string driveId, IReadOnlyList<int> stops)
        {
            var p = new DriveUpgradePreview(shipId, yardSystemId, driveId);
            if (Market == null) { p.AddProblem("There is no market yet."); return p; }
            var t = Market.Tuning;
            var systems = Market.Galaxy.Systems;
            var ship = FindShip(shipId);
            if (ship == null) { p.AddProblem($"There is no ship with id {shipId}."); return p; }
            p.CurrentDriveId = ship.DriveId;
            var shipClass = t.FindShip(ship.ClassId);
            var current = t.FindDrive(ship.DriveId);
            var drive = driveId == null ? null : t.FindDrive(driveId);
            if (shipClass == null) p.AddProblem($"{ship.Name}'s class \"{ship.ClassId}\" is not in the tuning file.");
            if (current == null) p.AddProblem($"{ship.Name}'s drive \"{ship.DriveId}\" is not in the tuning file.");
            if (drive == null) p.AddProblem($"\"{driveId}\" is not a drive in the tuning.");
            if (yardSystemId < 0 || yardSystemId >= systems.Count) p.AddProblem($"There is no system with id {yardSystemId}.");
            if (stops == null || stops.Count < 2)
                p.AddProblem($"A route needs at least 2 stops; this one has {stops?.Count ?? 0}.");
            else
            {
                bool idsOk = true;
                for (int i = 0; i < stops.Count; i++)
                    if (stops[i] < 0 || stops[i] >= systems.Count) { p.AddProblem($"Stop {i + 1}: there is no system with id {stops[i]}."); idsOk = false; }
                if (idsOk)
                    for (int i = 0; i < stops.Count; i++)
                    {
                        int next = (i + 1) % stops.Count;
                        if (stops[i] == stops[next]) p.AddProblem($"Stops {i + 1} and {next + 1} are both {systems[stops[i]].Name}; each leg must go to a different system.");
                    }
            }
            if (!p.IsValid) return p;

            string item = "the " + drive.Name;
            string where = systems[yardSystemId].Name;
            var yard = Market.FindShipyard(yardSystemId);
            var stock = yard?.Find(YardItemKind.Drive, driveId);
            if (yard == null) p.AddNote($"{where} has no shipyard, so {item} cannot be bought there.");
            else if (!Market.Stocks(stock)) p.AddNote($"The shipyard at {where} does not stock {item}.");
            else
            {
                p.Price = Market.Price(yard, YardItemKind.Drive, driveId);
                p.InStock = stock.Count;
                if (stock.Count == 0) p.AddNote(OutOfStock(where, item, stock));
            }
            if (ship.DriveId == driveId) p.AddNote($"{ship.Name} already has {item} installed.");

            long currentTotal = 0, newTotal = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                int from = stops[i], to = stops[(i + 1) % stops.Count];
                long distance = Market.Galaxy.DistanceMilliLy(from, to);
                var withCurrent = ProvisioningMath.Compute(t, shipClass, current, distance, 0, 0, 0, 0);
                var withNew = ProvisioningMath.Compute(t, shipClass, drive, distance, 0, 0, 0, 0);
                p.AddLeg(new PreviewLeg(from, to, distance, withCurrent.TransitTicks, withNew.TransitTicks, withCurrent.FuelRequiredKg, withNew.FuelRequiredKg));
                currentTotal = checked(currentTotal + withCurrent.TransitTicks);
                newTotal = checked(newTotal + withNew.TransitTicks);
            }
            p.CurrentTotalTicks = currentTotal;
            p.NewTotalTicks = newTotal;
            return p;
        }

        private string OutOfStock(string where, string item, YardStock stock)
        {
            var y = Market.Tuning.Shipyards;
            return $"The shipyard at {where} has 0 of {item} in stock (stock limit {Market.StockLimit(stock)}). It restocks {y.RestockAmount} every {y.RestockIntervalDays} game days; the next restock is {GameDate.FromTick(Market.NextRestockTick(CurrentTick))}.";
        }

        private string CannotAfford(string where, string item, long price)
        {
            long more = price - Credits;
            return $"{Capitalise(item)} costs {Format.Credits(price)} credits at {where}; you have {Format.Credits(Credits)}. You need {Format.Credits(more)} more {(more == 1 ? "credit" : "credits")}.";
        }

        private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private string StockSummary(Shipyard yard, YardItemKind kind, string itemId) =>
            $"{yard.Find(kind, itemId).Count} in stock, {Format.Credits(Market.Price(yard, kind, itemId))} credits";

        /// <summary>Every yard that sells this item, by system id, with stock and price.</summary>
        private string Sellers(YardItemKind kind, string itemId, string item)
        {
            var sellers = Market.Shipyards.Where(y => Market.Stocks(y.Find(kind, itemId)))
                .Select(y => $"{Market.Galaxy.Systems[y.SystemId].Name} ({StockSummary(y, kind, itemId)})").ToList();
            return sellers.Count == 0 ? $"No shipyard sells {item}." : $"Shipyards that sell {item}: {string.Join(", ", sellers)}.";
        }

        /// <summary>The nearest yard that stocks this drive (ties by system id), with its distance, stock, and price.</summary>
        private string NearestStockist(int fromSystemId, string driveId, string item)
        {
            var nearest = Market.Shipyards.Where(y => y.SystemId != fromSystemId && Market.Stocks(y.Find(YardItemKind.Drive, driveId)))
                .OrderBy(y => Market.Galaxy.DistanceMilliLy(fromSystemId, y.SystemId)).ThenBy(y => y.SystemId).FirstOrDefault();
            if (nearest == null) return $"No other shipyard stocks {item}.";
            return $"The nearest shipyard that stocks {item} is {Market.Galaxy.Systems[nearest.SystemId].Name}, {Format.LightYears(Market.Galaxy.DistanceMilliLy(fromSystemId, nearest.SystemId))} away ({StockSummary(nearest, YardItemKind.Drive, driveId)}).";
        }
    }
}
