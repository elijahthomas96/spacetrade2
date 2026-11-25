using System.Collections.Generic;
using SpaceTrade.Sim.Markets;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>What the player has picked for one voyage: which ship, where to, what freight, how many passengers.</summary>
    public sealed class VoyagePlan
    {
        public int ShipId { get; }
        public int DestinationId { get; }
        public IReadOnlyList<FreightLine> Freight { get; }
        public int Passengers { get; }

        public VoyagePlan(int shipId, int destinationId, IEnumerable<FreightLine> freight = null, int passengers = 0)
        {
            ShipId = shipId;
            DestinationId = destinationId;
            Freight = freight == null ? new List<FreightLine>() : new List<FreightLine>(freight);
            Passengers = passengers;
        }
    }
}
