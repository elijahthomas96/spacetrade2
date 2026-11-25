using System.Linq;

namespace SpaceTrade.Sim
{
    /// <summary>Debug commands (spec 13). Refused unless debug permission was granted when the simulation was created.</summary>
    public sealed partial class Simulation
    {
        private readonly DebugCapability _debug;

        /// <summary>True when debug permission was granted when this simulation was created.</summary>
        public bool HasDebugCapability => _debug != null;

        /// <summary>
        /// Skip to next arrival: moves time to the earliest pending arrival through <see cref="Advance"/>, processing every
        /// event in between and every event at that tick. Works while paused and leaves the pause state as it is.
        /// Refused, changing nothing, without debug permission or when no ship is in flight.
        /// </summary>
        public CommandResult DebugSkipToNextArrival()
        {
            if (_debug == null)
                return CommandResult.Fail("Skip to next arrival is a debug command. Debug mode was not switched on when this game was started.");
            var inFlight = _ships.Where(s => s.Voyage != null).ToList();
            if (inFlight.Count == 0) return CommandResult.Fail("No ship is in flight, so there is no arrival to skip to.");
            Advance(inFlight.Min(s => s.Voyage.ArrivalTick));
            return CommandResult.Ok();
        }
    }
}
