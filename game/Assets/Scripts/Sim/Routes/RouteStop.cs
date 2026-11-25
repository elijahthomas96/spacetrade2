using System.Collections.Generic;
using System.Text;

namespace SpaceTrade.Sim.Routes
{
    /// <summary>
    /// One stop of a route (spec 8, 8.2): the system, which freight to load for the next stop (in loading
    /// order), whether to carry passengers, automatic provisioning (the dispatch top-up yes/no), the two
    /// minimum departure loads, and whether the leg to the next stop is an explicit empty repositioning leg.
    /// </summary>
    public sealed class RouteStop
    {
        public int SystemId { get; }

        /// <summary>Eligible commodity ids, in the order they load.</summary>
        public IReadOnlyList<string> Freight { get; }
        public bool CarryPassengers { get; }
        public bool AutoProvision { get; }
        public int MinFreightTonnes { get; }
        public int MinPassengers { get; }

        /// <summary>The leg from this stop to the next is an explicit empty repositioning leg.</summary>
        public bool EmptyLeg { get; }

        public RouteStop(int systemId, IEnumerable<string> freight = null, bool carryPassengers = false, bool autoProvision = true,
            int minFreightTonnes = 0, int minPassengers = 0, bool emptyLeg = false)
        {
            SystemId = systemId;
            Freight = freight == null ? new List<string>() : new List<string>(freight);
            CarryPassengers = carryPassengers;
            AutoProvision = autoProvision;
            MinFreightTonnes = minFreightTonnes;
            MinPassengers = minPassengers;
            EmptyLeg = emptyLeg;
        }

        /// <summary>A stop that loads nothing: the leg to the next stop is an empty repositioning leg.</summary>
        public static RouteStop Empty(int systemId, bool autoProvision = true) => new RouteStop(systemId, autoProvision: autoProvision, emptyLeg: true);

        /// <summary>Every stored value in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append(SystemId).Append(" freight=[").Append(string.Join(",", Freight)).Append(']')
              .Append(" pax=").Append(CarryPassengers).Append(" auto=").Append(AutoProvision)
              .Append(" min=").Append(MinFreightTonnes).Append("t/").Append(MinPassengers).Append('p')
              .Append(" empty=").Append(EmptyLeg);
            return sb.ToString();
        }
    }
}
