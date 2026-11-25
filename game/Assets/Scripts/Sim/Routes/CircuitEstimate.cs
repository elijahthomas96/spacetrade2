using System.Collections.Generic;

namespace SpaceTrade.Sim.Routes
{
    /// <summary>One commodity an estimated leg would carry, at today's published rate.</summary>
    public sealed class CircuitFreight
    {
        public string CommodityId { get; }
        public long Tonnes { get; }
        public long RatePerTonne { get; }
        public long Revenue => Tonnes * RatePerTonne;

        internal CircuitFreight(string commodityId, long tonnes, long ratePerTonne)
        {
            CommodityId = commodityId;
            Tonnes = tonnes;
            RatePerTonne = ratePerTonne;
        }
    }

    /// <summary>
    /// One leg of the whole-circuit estimate (spec 8.6). Distance and transit time are exact. Every figure whose
    /// name starts with "Estimated" is an estimate and must be labelled so. The supply kilograms are exact for an
    /// empty leg and an estimate for a loaded one (<see cref="SupplyKgAreEstimates"/>).
    /// </summary>
    public sealed class CircuitLeg
    {
        private readonly List<CircuitFreight> _freight;

        public int FromId { get; }
        public int ToId { get; }
        public long DistanceMilliLy { get; }
        public long TransitTicks { get; }
        public bool IsEmptyLeg { get; }
        public bool CanBeFlown { get; }

        public int EstimatedPassengers { get; }
        public IReadOnlyList<CircuitFreight> EstimatedFreight => _freight;
        public long EstimatedFreightTonnes { get; }

        public long FuelKg { get; }
        public long FoodKg { get; }
        public long WaterKg { get; }
        public bool SupplyKgAreEstimates => !IsEmptyLeg;

        public long EstimatedRevenue { get; }
        public long EstimatedSupplyCost { get; }
        public long EstimatedProfit => EstimatedRevenue - EstimatedSupplyCost;

        internal CircuitLeg(int fromId, int toId, long distanceMilliLy, long transitTicks, bool isEmptyLeg, bool canBeFlown,
            int passengers, IEnumerable<CircuitFreight> freight, long freightTonnes, long fuelKg, long foodKg, long waterKg, long revenue, long supplyCost)
        {
            FromId = fromId;
            ToId = toId;
            DistanceMilliLy = distanceMilliLy;
            TransitTicks = transitTicks;
            IsEmptyLeg = isEmptyLeg;
            CanBeFlown = canBeFlown;
            EstimatedPassengers = passengers;
            _freight = new List<CircuitFreight>(freight);
            EstimatedFreightTonnes = freightTonnes;
            FuelKg = fuelKg;
            FoodKg = foodKg;
            WaterKg = waterKg;
            EstimatedRevenue = revenue;
            EstimatedSupplyCost = supplyCost;
        }
    }

    /// <summary>
    /// The route editor's whole-circuit estimate (spec 8.6): today's rates and stock, each stop loaded by its own
    /// rules, supplies priced as bought at each stop today with nothing aboard, no waiting time. Total transit time
    /// is exact; every figure whose name starts with "Estimated" is an estimate. Changes nothing.
    /// </summary>
    public sealed class CircuitEstimate
    {
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _notes = new List<string>();
        private readonly List<CircuitLeg> _legs = new List<CircuitLeg>();

        public int ShipId { get; }

        /// <summary>Why no estimate could be made (bad orders, unknown ship). Empty when the estimate is valid.</summary>
        public IReadOnlyList<string> Problems => _problems;
        public bool IsValid => _problems.Count == 0;

        /// <summary>Stops that would wait today and legs that cannot be flown.</summary>
        public IReadOnlyList<string> Notes => _notes;
        public IReadOnlyList<CircuitLeg> Legs => _legs;

        public bool AllLegsCanBeFlown { get; internal set; }
        public long TotalTransitTicks { get; internal set; }
        public long EstimatedRevenue { get; internal set; }
        public long EstimatedSupplyCost { get; internal set; }
        public long EstimatedProfit => EstimatedRevenue - EstimatedSupplyCost;
        public long EstimatedProfitPerGameYear { get; internal set; }

        internal CircuitEstimate(int shipId)
        {
            ShipId = shipId;
        }

        internal void AddProblem(string problem) => _problems.Add(problem);
        internal void AddProblems(IEnumerable<string> problems) => _problems.AddRange(problems);
        internal void AddNote(string note) => _notes.Add(note);
        internal void AddLeg(CircuitLeg leg) => _legs.Add(leg);
    }
}
