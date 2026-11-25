using System.Runtime.CompilerServices;

// State-building helpers (Simulation.AddShip, Simulation.SetCredits, SupplyStock.Add and Use) are
// internal. Only the EditMode test assembly may see them; SpaceTrade.Game must not be added here.
[assembly: InternalsVisibleTo("SpaceTrade.Sim.Tests")]
