using System.Collections.Generic;

namespace SpaceTrade.Sim.Yards
{
    /// <summary>One leg of a previewed route, with the ship's current drive and the drive being considered.</summary>
    public sealed class PreviewLeg
    {
        public int FromId { get; }
        public int ToId { get; }
        public long DistanceMilliLy { get; }
        public long CurrentTicks { get; }
        public long NewTicks { get; }

        /// <summary>Current minus new transit ticks. Negative when the new drive is slower.</summary>
        public long SavedTicks => CurrentTicks - NewTicks;

        /// <summary>Fuel this leg needs with the current drive, for an empty ship with crew only (no freight, no passengers, only the food and water the crew needs), in kilograms.</summary>
        public long CurrentFuelKgEmptyCrewOnly { get; }

        /// <summary>The same with the new drive.</summary>
        public long NewFuelKgEmptyCrewOnly { get; }

        internal PreviewLeg(int fromId, int toId, long distanceMilliLy, long currentTicks, long newTicks, long currentFuelKg, long newFuelKg)
        {
            FromId = fromId;
            ToId = toId;
            DistanceMilliLy = distanceMilliLy;
            CurrentTicks = currentTicks;
            NewTicks = newTicks;
            CurrentFuelKgEmptyCrewOnly = currentFuelKg;
            NewFuelKgEmptyCrewOnly = newFuelKg;
        }
    }

    /// <summary>
    /// What a drive would cost at a yard and what it would change on a route (spec 7): each leg's transit
    /// time and crew-only fuel with the current and the new drive, and the total time saved. Read-only;
    /// works for any ship from anywhere. The price is the same figure <see cref="Simulation.BuyDrive"/> charges at that yard.
    /// </summary>
    public sealed class DriveUpgradePreview
    {
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _notes = new List<string>();
        private readonly List<PreviewLeg> _legs = new List<PreviewLeg>();

        public int ShipId { get; }
        public int YardSystemId { get; }
        public string NewDriveId { get; }
        public string CurrentDriveId { get; internal set; }

        /// <summary>The drive's price at the yard, or null when that system has no yard or the yard does not stock it.</summary>
        public long? Price { get; internal set; }

        /// <summary>Units of the drive in stock at the yard now (0 when it has none or does not stock it).</summary>
        public int InStock { get; internal set; }

        /// <summary>Why no preview could be made (unknown ship or drive, a bad route). Empty when the legs are filled in.</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>Things to know that do not stop the preview, for example the yard does not stock the drive.</summary>
        public IReadOnlyList<string> Notes => _notes;

        /// <summary>The route's legs in order, the last returning to the first stop.</summary>
        public IReadOnlyList<PreviewLeg> Legs => _legs;

        public long CurrentTotalTicks { get; internal set; }
        public long NewTotalTicks { get; internal set; }

        /// <summary>Current minus new total ticks for one trip round the route. Negative when the new drive is slower.</summary>
        public long TimeSavedTicks => CurrentTotalTicks - NewTotalTicks;

        public bool IsValid => _problems.Count == 0;

        internal DriveUpgradePreview(int shipId, int yardSystemId, string newDriveId)
        {
            ShipId = shipId;
            YardSystemId = yardSystemId;
            NewDriveId = newDriveId;
        }

        internal void AddProblem(string problem) => _problems.Add(problem);
        internal void AddNote(string note) => _notes.Add(note);
        internal void AddLeg(PreviewLeg leg) => _legs.Add(leg);
    }
}
