using System.Collections.Generic;
using NUnit.Framework;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Tests
{
    public class ClockTests
    {
        [Test]
        public void SetSpeed_AcceptsOnlyThreeSpeeds()
        {
            var sim = new Simulation();
            Assert.IsTrue(sim.SetSpeed(ClockSpeed.X3).Succeeded);
            Assert.AreEqual(ClockSpeed.X3, sim.Clock.Speed);

            foreach (int bad in new[] { 0, 2, 4, 10, -1 })
            {
                var result = sim.SetSpeed((ClockSpeed)bad);
                Assert.IsFalse(result.Succeeded, $"{bad}x should be refused");
                StringAssert.Contains("1x, 3x, or 5x", result.Reason);
                Assert.AreEqual(ClockSpeed.X3, sim.Clock.Speed, "A refused speed change must change nothing.");
            }
        }

        [Test]
        public void Play_ResumesAtLastSelectedSpeed()
        {
            var sim = new Simulation();
            sim.SetSpeed(ClockSpeed.X5);
            sim.Pause();
            Assert.IsTrue(sim.Clock.IsPaused);
            Assert.IsTrue(sim.Play().Succeeded);
            Assert.IsFalse(sim.Clock.IsPaused);
            Assert.AreEqual(ClockSpeed.X5, sim.Clock.Speed);
        }

        [Test]
        public void Pause_FreezesTime()
        {
            var log = new List<string>();
            var sim = new Simulation();
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);
            sim.Schedule(1_500, new RecordingEvent(log, EventPhase.Arrival, 1, "arrive"));

            clock.AdvanceMilliseconds(1_000);
            driver.SetSpeed(ClockSpeed.X5);
            Assert.AreEqual(1_000, sim.CurrentTick, "1 real second at 1x before the speed change.");

            driver.Pause();
            clock.AdvanceSeconds(60);
            Assert.AreEqual(0, driver.Pump());
            Assert.AreEqual(1_000, sim.CurrentTick, "No ticks while paused.");
            CollectionAssert.IsEmpty(log, "No events while paused.");

            driver.Play();
            Assert.AreEqual(ClockSpeed.X5, sim.Clock.Speed, "Play resumes at the last selected speed.");
            Assert.AreEqual(1_000, sim.CurrentTick, "The paused minute is discarded, not caught up.");

            clock.AdvanceMilliseconds(100);
            driver.Pump();
            Assert.AreEqual(1_500, sim.CurrentTick, "100 ms at 5x = 500 ticks.");
            CollectionAssert.AreEqual(new[] { "1500:arrive" }, log);
        }

        [Test]
        public void SpeedChange_DoesNotMoveArrivalTick()
        {
            var log = new List<string>();
            var sim = new Simulation();
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);

            long arrival = TransitMath.TransitTicks(4_500, 1_500);
            sim.Schedule(arrival, new RecordingEvent(log, EventPhase.Arrival, 1, "arrive"));

            clock.AdvanceSeconds(3_600);
            driver.SetSpeed(ClockSpeed.X5);
            Assert.AreEqual(GameTime.TicksPerYear, sim.CurrentTick, "One real hour at 1x = one game year.");
            Assert.AreEqual(arrival, sim.Events.NextTick, "Arrival tick unchanged by speed change.");

            long remainingTicks = arrival - sim.CurrentTick;
            Assert.AreEqual(2 * 3_600_000L, GameTime.RealMillisecondsFor(remainingTicks, ClockSpeed.X1));
            Assert.AreEqual(2 * 3_600_000L / 5, GameTime.RealMillisecondsFor(remainingTicks, sim.Clock.Speed), "Remaining real time drops to one fifth at 5x.");

            clock.AdvanceMilliseconds(2 * 3_600_000L / 5 - 1);
            driver.Pump();
            CollectionAssert.IsEmpty(log, "Not arrived 1 ms early.");

            driver.SetSpeed(ClockSpeed.X3);
            Assert.AreEqual(arrival, sim.Events.NextTick, "Arrival tick unchanged by second speed change.");
            Assert.AreEqual(5, arrival - sim.CurrentTick);

            clock.AdvanceMilliseconds(2);
            driver.Pump();
            CollectionAssert.AreEqual(new[] { $"{arrival}:arrive" }, log, "Arrives at exactly the original tick.");
        }

        [Test]
        public void SixRealMinutesAt1x_Is3Point33PercentOf4_5lyAt1_5c()
        {
            var sim = new Simulation();
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);
            long transit = TransitMath.TransitTicks(4_500, 1_500);

            clock.AdvanceSeconds(6 * 60);
            driver.Pump();

            Assert.AreEqual(360_000, sim.CurrentTick);
            Assert.AreEqual(333, sim.CurrentTick * 10_000 / transit, "3.33 percent, in hundredths of a percent.");
            Assert.AreEqual(150, sim.CurrentTick * 4_500 / transit, "0.15 ly travelled, in milli-ly.");
        }
    }
}
