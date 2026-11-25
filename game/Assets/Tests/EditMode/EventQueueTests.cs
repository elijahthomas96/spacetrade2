using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceTrade.Sim.Events;

namespace SpaceTrade.Sim.Tests
{
    public class EventQueueTests
    {
        [Test]
        public void SameTick_FollowsSpecOrder()
        {
            var log = new List<string>();
            var sim = new Simulation();
            const long t = 500;

            // Scheduled deliberately out of order.
            sim.Schedule(t, new RecordingEvent(log, EventPhase.AutomatedDeparture, 1));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.Arrival, 3));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.MarketReplenishment, 2));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.InSystemTransport, 2));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.Arrival, 1));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.MarketReplenishment, 1));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.AutomatedDeparture, 0));
            sim.Schedule(t, new RecordingEvent(log, EventPhase.InSystemTransport, 1));
            sim.Schedule(t - 1, new RecordingEvent(log, EventPhase.AutomatedDeparture, 9));

            sim.Advance(t);

            CollectionAssert.AreEqual(new[]
            {
                "499:AutomatedDeparture:9",
                "500:Arrival:1",
                "500:Arrival:3",
                "500:MarketReplenishment:1",
                "500:MarketReplenishment:2",
                "500:InSystemTransport:1",
                "500:InSystemTransport:2",
                "500:AutomatedDeparture:0",
                "500:AutomatedDeparture:1",
            }, log);
        }

        [Test]
        public void IdenticalKeys_RunInSchedulingOrder()
        {
            var log = new List<string>();
            var sim = new Simulation();
            sim.Schedule(10, new RecordingEvent(log, EventPhase.Arrival, 4, "first"));
            sim.Schedule(10, new RecordingEvent(log, EventPhase.Arrival, 4, "second"));
            sim.Schedule(10, new RecordingEvent(log, EventPhase.Arrival, 4, "third"));
            sim.Advance(10);
            CollectionAssert.AreEqual(new[] { "10:first", "10:second", "10:third" }, log);
        }

        [Test]
        public void Advance_RunsEventsAtTheirOwnTickAndStopsAtTarget()
        {
            var log = new List<string>();
            var sim = new Simulation();
            sim.Schedule(100, new RecordingEvent(log, EventPhase.Arrival, 1, "a"));
            sim.Schedule(200, new RecordingEvent(log, EventPhase.Arrival, 1, "b"));
            sim.Schedule(201, new RecordingEvent(log, EventPhase.Arrival, 1, "c"));

            sim.Advance(200);

            CollectionAssert.AreEqual(new[] { "100:a", "200:b" }, log);
            Assert.AreEqual(200, sim.CurrentTick);
            Assert.AreEqual(1, sim.Events.Count);
            Assert.AreEqual(201L, sim.Events.NextTick);
        }

        [Test]
        public void EventsScheduledDuringAdvance_AreProcessedIfInRange()
        {
            var log = new List<string>();
            var sim = new Simulation();
            sim.Schedule(100, new RecordingEvent(log, EventPhase.Arrival, 1, "arrive", s =>
            {
                s.Schedule(s.CurrentTick, new RecordingEvent(log, EventPhase.AutomatedDeparture, 1, "depart-same-tick"));
                s.Schedule(150, new RecordingEvent(log, EventPhase.Arrival, 1, "later"));
                s.Schedule(250, new RecordingEvent(log, EventPhase.Arrival, 1, "out-of-range"));
            }));

            sim.Advance(200);

            CollectionAssert.AreEqual(new[] { "100:arrive", "100:depart-same-tick", "150:later" }, log);
            Assert.AreEqual(200, sim.CurrentTick);
            Assert.AreEqual(250L, sim.Events.NextTick);
        }

        [Test]
        public void Advance_RejectsBackwardTime()
        {
            var sim = new Simulation();
            sim.Advance(1_000);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Advance(999));
            Assert.AreEqual(1_000, sim.CurrentTick);
        }

        [Test]
        public void Schedule_RejectsPastTick()
        {
            var sim = new Simulation();
            sim.Advance(1_000);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Schedule(999, new RecordingEvent(new List<string>(), EventPhase.Arrival, 1)));
            Assert.AreEqual(0, sim.Events.Count);
        }

        [Test]
        public void Advance_CannotBeCalledFromInsideAnEvent()
        {
            var sim = new Simulation();
            sim.Schedule(10, new RecordingEvent(new List<string>(), EventPhase.Arrival, 1, "x", s => s.Advance(20)));
            Assert.Throws<InvalidOperationException>(() => sim.Advance(10));
        }

        [Test]
        public void NextTick_IsNullWhenEmpty()
        {
            Assert.IsNull(new Simulation().Events.NextTick);
        }
    }
}
