using NUnit.Framework;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// The ETA every screen shows (spec 3.4): the arrival date and the real time left at the current speed, both worked out
    /// by the simulation. Hand example from spec 3.3: 4.5 ly at 1.5c is 3 game years, 10,800,000 ticks: 3 real hours at 1x,
    /// 1 hour at 3x, 36 minutes at 5x.
    /// </summary>
    public class VoyageEtaTests
    {
        private const long Transit = 10_800_000;

        private sealed class FakeTime : IRealTimeSource
        {
            public long NowMicroseconds { get; set; }
        }

        private static Simulation Flying()
        {
            var sim = VoyageTestSupport.NewSim(1_000_000, VoyageTestSupport.FlatTuning());
            Assert.IsTrue(sim.Dispatch(VoyageTestSupport.Plan(0, 0), true).Succeeded);
            Assert.AreEqual(Transit, VoyageTestSupport.Courier(sim).Voyage.TransitTicks);
            return sim;
        }

        private static VoyageEta Eta(Simulation sim) => sim.EtaOf(VoyageTestSupport.CourierId);

        [Test]
        public void SpeedChanges_ChangeTheRealTimeLeft_AndNeverTheArrivalDate()
        {
            var sim = Flying();
            var at1 = Eta(sim);
            Assert.IsTrue(at1.InTransit);
            Assert.AreEqual(Transit, at1.ArrivalTick);
            Assert.AreEqual(new GameDate(4, 1, 1), at1.ArrivalDate);
            Assert.AreEqual(3 * 3600, at1.RealSecondsRemaining, "3 real hours at 1x");
            Assert.AreEqual("Arrives Y4 M01 D01 · 3 h 00 min 00 s at 1x", at1.Describe());

            Assert.IsTrue(sim.SetSpeed(ClockSpeed.X3).Succeeded);
            var at3 = Eta(sim);
            Assert.AreEqual(3600, at3.RealSecondsRemaining, "1 real hour at 3x");
            Assert.AreEqual(3, at3.SpeedMultiplier);
            Assert.AreEqual("Arrives Y4 M01 D01 · 1 h 00 min 00 s at 3x", at3.Describe());

            Assert.IsTrue(sim.SetSpeed(ClockSpeed.X5).Succeeded);
            var at5 = Eta(sim);
            Assert.AreEqual(36 * 60, at5.RealSecondsRemaining, "36 real minutes at 5x");
            Assert.AreEqual("Arrives Y4 M01 D01 · 36 min 00 s at 5x", at5.Describe());

            foreach (var eta in new[] { at1, at3, at5 })
            {
                Assert.AreEqual(Transit, eta.ArrivalTick, "the arrival tick never moves");
                Assert.AreEqual(at1.ArrivalDate, eta.ArrivalDate, "the arrival date never moves");
            }
            Assert.AreEqual(Transit, VoyageTestSupport.Courier(sim).Voyage.ArrivalTick);
        }

        [Test]
        public void TheRealTimeLeft_IsRoundedUp_SoItNeverReachesZeroEarly()
        {
            var sim = Flying();
            sim.Advance(Transit - 1);
            Assert.AreEqual(1, Eta(sim).RealSecondsRemaining, "one tick left is still 1 second at 1x");
            sim.SetSpeed(ClockSpeed.X5);
            Assert.AreEqual(1, Eta(sim).RealSecondsRemaining, "and at 5x");
        }

        [Test]
        public void RealTimeLeft_FallsAsTimePasses_AtEachSpeed()
        {
            var sim = Flying();
            sim.Advance(1_000_000);
            Assert.AreEqual(9_800, Eta(sim).RealSecondsRemaining, "9,800,000 ticks at 1x");
            sim.SetSpeed(ClockSpeed.X3);
            Assert.AreEqual(3_267, Eta(sim).RealSecondsRemaining, "9,800,000 / 3 = 3,266,666.7 ms, rounded up to 3,267 s");
            sim.SetSpeed(ClockSpeed.X5);
            Assert.AreEqual(1_960, Eta(sim).RealSecondsRemaining);
            Assert.AreEqual(9_800_000, Eta(sim).TicksRemaining);
        }

        [Test]
        public void WhilePaused_TheCountdownIsFrozen_AndSaysPaused()
        {
            var sim = Flying();
            var time = new FakeTime();
            var driver = new RealTimeDriver(sim, time);
            time.NowMicroseconds += 600_000_000;
            driver.Pump();
            Assert.AreEqual(600_000, sim.CurrentTick);
            Assert.AreEqual(10_200, Eta(sim).RealSecondsRemaining);

            Assert.IsTrue(driver.Pause().Succeeded);
            var frozen = Eta(sim);
            Assert.IsTrue(frozen.IsPaused);
            Assert.AreEqual("Arrives Y4 M01 D01 · Paused", frozen.Describe());
            for (int i = 0; i < 5; i++)
            {
                time.NowMicroseconds += 60_000_000;
                driver.Pump();
                var eta = Eta(sim);
                Assert.AreEqual(frozen.RealSecondsRemaining, eta.RealSecondsRemaining, "a real minute passed while paused: nothing moved");
                Assert.AreEqual(frozen.TicksRemaining, eta.TicksRemaining);
                Assert.AreEqual(frozen.ArrivalDate, eta.ArrivalDate);
                Assert.AreEqual("Arrives Y4 M01 D01 · Paused", eta.Describe());
            }

            Assert.IsTrue(driver.SetSpeed(ClockSpeed.X5).Succeeded);
            Assert.IsTrue(Eta(sim).IsPaused, "choosing a speed while paused stays paused");
            Assert.IsTrue(driver.Play().Succeeded);
            Assert.AreEqual("Arrives Y4 M01 D01 · 34 min 00 s at 5x", Eta(sim).Describe(), "10,200,000 ticks at 5x is 2,040 s");
        }

        [Test]
        public void ADockedShip_HasNoArrival()
        {
            var sim = VoyageTestSupport.NewSim(1_000_000, VoyageTestSupport.FlatTuning());
            var eta = Eta(sim);
            Assert.IsFalse(eta.InTransit);
            Assert.AreEqual(0, eta.RealSecondsRemaining);
            Assert.AreEqual("Docked", eta.Describe());
        }

        [Test]
        public void AskingForTheEta_ChangesNothing()
        {
            var sim = Flying();
            sim.Advance(123_456);
            string before = sim.DescribeState();
            for (int i = 0; i < 10; i++) Eta(sim);
            Assert.AreEqual(before, sim.DescribeState());
        }

        [Test]
        public void RealDuration_ReadsAsHoursMinutesSeconds()
        {
            Assert.AreEqual("7 s", Format.RealDuration(7));
            Assert.AreEqual("5 min 07 s", Format.RealDuration(307));
            Assert.AreEqual("2 h 05 min 07 s", Format.RealDuration(7507));
            Assert.AreEqual("0 s", Format.RealDuration(0));
        }

        [Test]
        public void CurrentDate_IsTheTicksDate()
        {
            var sim = Flying();
            Assert.AreEqual("Y1 M01 D01", sim.CurrentDate.ToString());
            sim.Advance(GameTime.TicksPerYear * 2 + GameTime.TicksPerDay * 41);
            Assert.AreEqual("Y3 M02 D12", sim.CurrentDate.ToString());
        }
    }
}
