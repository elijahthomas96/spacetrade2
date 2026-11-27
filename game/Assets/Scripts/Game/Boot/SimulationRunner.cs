using System;
using SpaceTrade.Game.Audio;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Moves the simulation forward every frame through the real-time driver (spec 3.2). The driver reads a monotonic
    /// clock in whole microseconds, so game time ignores Unity's time scale and frame rate.
    /// The clock moves before anything reads it in the same frame, and the sound feed looks at the result once.
    ///
    /// Anything that jumps time (offline catch-up, sleep-wake catch-up, debug skip) goes through this class, which
    /// declares the jump to the sound feed, so the events inside it stay silent and the return summary sound plays once.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        public Simulation Simulation { get; private set; }
        public RealTimeDriver Driver { get; private set; }

        /// <summary>Turns simulation events into sounds, once a frame.</summary>
        public SimSoundFeed Sounds { get; } = new SimSoundFeed();

        public void Run(Simulation sim, IRealTimeSource source = null)
        {
            Simulation = sim;
            Driver = new RealTimeDriver(sim, source ?? new StopwatchTimeSource());
            Sounds.Attach(sim);
        }

        private void Update()
        {
            if (Driver == null) return;
            if (!Sounds.IsJumping) Driver.Pump();
            Sounds.Observe();
        }

        /// <summary>
        /// Declares that the code calling this is about to jump time. Until <see cref="EndTimeJump"/> the frame clock does
        /// not move the game and no event makes a sound. A catch-up spread over several frames calls this before its first
        /// slice and <see cref="EndTimeJump"/> after its last.
        /// </summary>
        public void BeginTimeJump()
        {
            if (Driver == null) return;
            Sounds.BeginJump();
        }

        /// <summary>The jump is done: real time spent in it is not counted again, and the return summary sound plays once.</summary>
        public void EndTimeJump()
        {
            if (Driver == null) return;
            Driver.Resync();
            Sounds.EndJump();
        }

        /// <summary>The offline catch-up rule (spec 11.3) in one step, declared as a time jump.</summary>
        public ReturnSummary CatchUp(long fromUtcMs, long nowUtcMs)
        {
            BeginTimeJump();
            try
            {
                return OfflineCatchUp.Run(Simulation, fromUtcMs, nowUtcMs);
            }
            finally
            {
                EndTimeJump();
            }
        }

        /// <summary>Debug skip to the next arrival (spec 13), declared as a time jump. A refused skip plays the error sound.</summary>
        public CommandResult DebugSkipToNextArrival()
        {
            BeginTimeJump();
            var result = Simulation.DebugSkipToNextArrival();
            if (result.Succeeded) EndTimeJump();
            else
            {
                Sounds.CancelJump();
                GameAudio.Play(SoundId.Error, result.Reason);
            }
            return result;
        }

        /// <summary>Runs a player's command. A refused command plays the error sound; what a command changes is heard through the sound feed.</summary>
        public CommandResult Command(Func<Simulation, CommandResult> command)
        {
            var result = command(Simulation);
            if (!result.Succeeded) GameAudio.Play(SoundId.Error, result.Reason);
            return result;
        }

        public CommandResult Pause() => Report(Driver.Pause());
        public CommandResult Play() => Report(Driver.Play());
        public CommandResult SetSpeed(ClockSpeed speed) => Report(Driver.SetSpeed(speed));

        private static CommandResult Report(CommandResult result)
        {
            if (!result.Succeeded) GameAudio.Play(SoundId.Error, result.Reason);
            return result;
        }
    }
}
