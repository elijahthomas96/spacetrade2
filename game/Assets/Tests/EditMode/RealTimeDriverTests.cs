using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Tests
{
    public class RealTimeDriverTests
    {
        // About 2.16 real hours, deliberately not a whole number of milliseconds.
        private const long TotalMicroseconds = 7_777_777_777;

        private static readonly long[] EventTicks = { 1, 999, 1_000_000, 3_600_000, 12_345_678, 38_888_888 };

        [TestCase(ClockSpeed.X1)]
        [TestCase(ClockSpeed.X3)]
        [TestCase(ClockSpeed.X5)]
        public void FrameRateIndependence_OneChunkEqualsTenThousandChunks(ClockSpeed speed)
        {
            var (tickOne, logOne) = RunInChunks(speed, new[] { TotalMicroseconds });
            var (tickMany, logMany) = RunInChunks(speed, UnevenChunks(TotalMicroseconds, 10_000, seed: 12345));

            long expected = TotalMicroseconds * (int)speed / 1_000;
            Assert.AreEqual(expected, tickOne);
            Assert.AreEqual(tickOne, tickMany, "Same elapsed time gives the same tick regardless of frame split.");
            CollectionAssert.AreEqual(logOne, logMany, "Events fire at the same ticks regardless of frame split.");
        }

        [Test]
        public void SubTickRemainders_Accumulate()
        {
            var sim = new Simulation();
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);

            for (int i = 0; i < 1_000; i++)
            {
                clock.AdvanceMicroseconds(1);
                driver.Pump();
            }
            Assert.AreEqual(1, sim.CurrentTick, "1,000 frames of 1 microsecond at 1x make exactly one tick.");
        }

        [Test]
        public void Driver_MatchesDirectAdvance()
        {
            var logDriven = new List<string>();
            var driven = new Simulation(speed: ClockSpeed.X3);
            ScheduleAll(driven, logDriven);
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(driven, clock);
            foreach (long chunk in UnevenChunks(TotalMicroseconds, 500, seed: 7))
            {
                clock.AdvanceMicroseconds(chunk);
                driver.Pump();
            }

            var logDirect = new List<string>();
            var direct = new Simulation(speed: ClockSpeed.X3);
            ScheduleAll(direct, logDirect);
            direct.Advance(driven.CurrentTick);

            Assert.AreEqual(direct.CurrentTick, driven.CurrentTick);
            CollectionAssert.AreEqual(logDirect, logDriven);
            Assert.AreEqual(direct.Events.Count, driven.Events.Count);
        }

        [Test]
        public void ClockGoingBackward_AdvancesNothing()
        {
            var sim = new Simulation();
            var clock = new FakeRealTimeSource { NowMicroseconds = 5_000_000 };
            var driver = new RealTimeDriver(sim, clock);

            clock.NowMicroseconds = 1_000_000;
            Assert.AreEqual(0, driver.Pump());
            Assert.AreEqual(0, sim.CurrentTick);

            clock.AdvanceMilliseconds(10);
            driver.Pump();
            Assert.AreEqual(10, sim.CurrentTick, "Counts forward again from the new reading.");
        }

        private static (long tick, List<string> log) RunInChunks(ClockSpeed speed, IEnumerable<long> chunks)
        {
            var log = new List<string>();
            var sim = new Simulation(speed: speed);
            ScheduleAll(sim, log);
            var clock = new FakeRealTimeSource();
            var driver = new RealTimeDriver(sim, clock);
            foreach (long chunk in chunks)
            {
                clock.AdvanceMicroseconds(chunk);
                driver.Pump();
            }
            return (sim.CurrentTick, log);
        }

        private static void ScheduleAll(Simulation sim, List<string> log)
        {
            for (int i = 0; i < EventTicks.Length; i++)
            {
                sim.Schedule(EventTicks[i], new RecordingEvent(log, EventPhase.Arrival, i));
                sim.Schedule(EventTicks[i], new RecordingEvent(log, EventPhase.MarketReplenishment, i));
            }
        }

        /// <summary>Splits a total into the given number of positive, uneven chunks that sum exactly to it.</summary>
        private static long[] UnevenChunks(long total, int count, int seed)
        {
            var rng = new Random(seed);
            var weights = new long[count];
            long weightSum = 0;
            for (int i = 0; i < count; i++)
            {
                weights[i] = rng.Next(1, 1_000);
                weightSum += weights[i];
            }

            var chunks = new long[count];
            long assigned = 0;
            for (int i = 0; i < count - 1; i++)
            {
                chunks[i] = total / weightSum * weights[i];
                assigned += chunks[i];
            }
            chunks[count - 1] = total - assigned;
            Assert.Greater(chunks[count - 1], 0);
            return chunks;
        }
    }
}
