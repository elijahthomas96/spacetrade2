using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim.World
{
    public sealed class StarSystem
    {
        public int Id { get; }
        public string Name { get; }
        public long XMilliLy { get; }
        public long YMilliLy { get; }
        public int PopulationMillions { get; }

        /// <summary>Industry ids from the tuning data, as they were when the galaxy was generated.</summary>
        public IReadOnlyList<string> IndustryIds { get; }

        public bool HasShipyard { get; }

        public StarSystem(int id, string name, long xMilliLy, long yMilliLy, int populationMillions, IReadOnlyList<string> industryIds, bool hasShipyard)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            XMilliLy = xMilliLy;
            YMilliLy = yMilliLy;
            PopulationMillions = populationMillions;
            IndustryIds = industryIds ?? throw new ArgumentNullException(nameof(industryIds));
            HasShipyard = hasShipyard;
        }
    }

    /// <summary>
    /// The generated map (spec 4). Stored in the save once created and never regenerated, so
    /// later tuning edits cannot reshape it. System ids equal their index in <see cref="Systems"/>.
    /// </summary>
    public sealed class Galaxy
    {
        public long Seed { get; }
        public int StartSystemId { get; }
        public IReadOnlyList<StarSystem> Systems { get; }

        public Galaxy(long seed, int startSystemId, IReadOnlyList<StarSystem> systems)
        {
            Systems = systems ?? throw new ArgumentNullException(nameof(systems));
            for (int i = 0; i < systems.Count; i++)
                if (systems[i].Id != i) throw new ArgumentException($"System at index {i} has id {systems[i].Id}; ids must equal their index.");
            if (startSystemId < 0 || startSystemId >= systems.Count)
                throw new ArgumentOutOfRangeException(nameof(startSystemId), startSystemId, "Start system must be one of the systems.");
            Seed = seed;
            StartSystemId = startSystemId;
        }

        public StarSystem StartSystem => Systems[StartSystemId];

        /// <summary>Straight-line distance, rounded to the nearest milli-light-year.</summary>
        public long DistanceMilliLy(int fromId, int toId) => Distance(Systems[fromId].XMilliLy, Systems[fromId].YMilliLy, Systems[toId].XMilliLy, Systems[toId].YMilliLy);

        public static long Distance(long x1, long y1, long x2, long y2)
        {
            long dx = x2 - x1;
            long dy = y2 - y1;
            return IntMath.RoundedSqrt(checked(dx * dx + dy * dy));
        }
    }
}
