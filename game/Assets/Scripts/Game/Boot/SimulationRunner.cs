using SpaceTrade.Sim;
using SpaceTrade.Sim.Timing;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Moves the simulation forward every frame through the real-time driver (spec 3.2). The driver reads a monotonic
    /// clock in whole microseconds, so game time ignores Unity's time scale and frame rate.
    /// The clock moves before anything reads it in the same frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        public Simulation Simulation { get; private set; }
        public RealTimeDriver Driver { get; private set; }

        public void Run(Simulation sim, IRealTimeSource source = null)
        {
            Simulation = sim;
            Driver = new RealTimeDriver(sim, source ?? new StopwatchTimeSource());
        }

        private void Update()
        {
            Driver?.Pump();
        }
    }
}
