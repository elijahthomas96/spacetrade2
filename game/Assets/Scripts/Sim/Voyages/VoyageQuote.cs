using System.Collections.Generic;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>One supply in a quote: what is aboard, what the voyage needs, and what topping up costs.</summary>
    public sealed class SupplyQuote
    {
        public SupplyKind Kind { get; }
        public long AboardKg { get; }
        public long AboardCost { get; }
        public long RequiredKg { get; }
        public long ShortfallKg { get; }
        public long PricePerTonne { get; }
        public long TopUpCost { get; }

        /// <summary>What the supplies this voyage uses cost, at acquisition cost (bought earlier or in this top-up).</summary>
        public long UsedCost { get; }

        internal SupplyQuote(SupplyKind kind, long aboardKg, long aboardCost, long requiredKg, long shortfallKg, long pricePerTonne, long topUpCost, long usedCost)
        {
            Kind = kind;
            AboardKg = aboardKg;
            AboardCost = aboardCost;
            RequiredKg = requiredKg;
            ShortfallKg = shortfallKg;
            PricePerTonne = pricePerTonne;
            TopUpCost = topUpCost;
            UsedCost = usedCost;
        }
    }

    /// <summary>One commodity in a quote, merged across the plan's lines, at the rate published now.</summary>
    public sealed class QuotedFreight
    {
        public string CommodityId { get; }
        public long Tonnes { get; }
        public long RatePerTonne { get; }
        public long Revenue => Tonnes * RatePerTonne;

        internal QuotedFreight(string commodityId, long tonnes, long ratePerTonne)
        {
            CommodityId = commodityId;
            Tonnes = tonnes;
            RatePerTonne = ratePerTonne;
        }
    }

    /// <summary>
    /// The voyage quote (spec 6.5). Produced only by <see cref="Simulation.Quote"/>, which the voyage
    /// preview and dispatch both call. When <see cref="IsPlanValid"/> is false only
    /// <see cref="Problems"/> is filled in.
    /// </summary>
    public sealed class VoyageQuote
    {
        private readonly List<string> _problems = new List<string>();
        private readonly List<QuotedFreight> _freight = new List<QuotedFreight>();
        private readonly List<SupplyQuote> _supplies = new List<SupplyQuote>();

        public VoyagePlan Plan { get; }

        /// <summary>False when the plan cannot be priced at all (no such ship or destination, for example).</summary>
        public bool IsPlanValid { get; internal set; }

        /// <summary>Every problem, each with its numbers and a fix. Empty when the voyage can be dispatched.</summary>
        public IReadOnlyList<string> Problems => _problems;

        public bool IsFeasible => IsPlanValid && _problems.Count == 0;

        public int OriginId { get; internal set; }
        public int DestinationId { get; internal set; }
        public long DistanceMilliLy { get; internal set; }
        public long DepartureTick { get; internal set; }
        public long TransitTicks { get; internal set; }
        public long ArrivalTick => DepartureTick + TransitTicks;
        public GameDate ArrivalDate => GameDate.FromTick(ArrivalTick);

        /// <summary>Real milliseconds the voyage takes at the currently selected speed.</summary>
        public long RealMillisecondsAtCurrentSpeed { get; internal set; }

        public VoyageNeeds Needs { get; internal set; }
        public long HoldCapacityKg { get; internal set; }
        public long TankCapacityKg { get; internal set; }
        public int Berths { get; internal set; }

        public bool BerthsFit { get; internal set; }
        public bool HoldFits { get; internal set; }
        public bool TankFits { get; internal set; }
        public bool CanAffordTopUp { get; internal set; }

        /// <summary>Fuel, food, water, in that order.</summary>
        public IReadOnlyList<SupplyQuote> Supplies => _supplies;
        public SupplyQuote Fuel => _supplies[0];
        public SupplyQuote Food => _supplies[1];
        public SupplyQuote Water => _supplies[2];

        public long TopUpCost { get; internal set; }
        public long CreditsAvailable { get; internal set; }

        public IReadOnlyList<QuotedFreight> Freight => _freight;
        public long FarePerPassenger { get; internal set; }
        public long FreightRevenue { get; internal set; }
        public long FareRevenue { get; internal set; }
        public long GrossRevenue => FreightRevenue + FareRevenue;

        /// <summary>The supplies this voyage uses, at acquisition cost.</summary>
        public long OperatingCost { get; internal set; }
        public long NetProfit => GrossRevenue - OperatingCost;
        public long ProfitPerGameYear { get; internal set; }

        internal VoyageQuote(VoyagePlan plan)
        {
            Plan = plan;
        }

        internal void AddProblem(string problem) => _problems.Add(problem);
        internal void AddFreight(QuotedFreight line) => _freight.Add(line);
        internal void AddSupply(SupplyQuote supply) => _supplies.Add(supply);
    }
}
