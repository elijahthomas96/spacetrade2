using System;

namespace SpaceTrade.Sim.Ships
{
    /// <summary>
    /// One ship in the player's fleet (spec 6.1). Class and drive stats are read from the current
    /// tuning by id, so tuning edits reach existing ships.
    /// </summary>
    public sealed class Ship
    {
        public int Id { get; }
        public string Name { get; }
        public string ClassId { get; }
        public string DriveId { get; internal set; }
        public int DockedSystemId { get; internal set; }

        public SupplyStock Fuel { get; } = new SupplyStock(SupplyKind.Fuel);
        public SupplyStock Food { get; } = new SupplyStock(SupplyKind.Food);
        public SupplyStock Water { get; } = new SupplyStock(SupplyKind.Water);

        internal Ship(int id, string name, string classId, string driveId, int dockedSystemId)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ClassId = classId ?? throw new ArgumentNullException(nameof(classId));
            DriveId = driveId ?? throw new ArgumentNullException(nameof(driveId));
            DockedSystemId = dockedSystemId;
        }

        public SupplyStock Supply(SupplyKind kind)
        {
            switch (kind)
            {
                case SupplyKind.Fuel: return Fuel;
                case SupplyKind.Food: return Food;
                case SupplyKind.Water: return Water;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown supply.");
            }
        }

        /// <summary>Every stored value in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState() =>
            $"ship {Id} \"{Name}\" class={ClassId} drive={DriveId} docked={DockedSystemId} " +
            $"fuel={Fuel.Kg}kg/{Fuel.CostCredits}cr food={Food.Kg}kg/{Food.CostCredits}cr water={Water.Kg}kg/{Water.CostCredits}cr";
    }
}
