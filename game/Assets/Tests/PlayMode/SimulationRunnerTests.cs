using System.Collections;
using NUnit.Framework;
using SpaceTrade.Game.Boot;
using SpaceTrade.Sim.Timing;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    public class SimulationRunnerTests
    {
        private sealed class FakeTime : IRealTimeSource
        {
            public long NowMicroseconds { get; set; }
        }

        private GameObject _go;
        private float _oldTimeScale;

        [SetUp]
        public void SetUp() => _oldTimeScale = Time.timeScale;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _oldTimeScale;
            if (_go != null) Object.Destroy(_go);
        }

        [UnityTest]
        public IEnumerator EachFrame_AdvancesByRealElapsedTime_IgnoringUnitysTimeScale()
        {
            var sim = GameTestTuning.NewGameWithSystems(10);
            var time = new FakeTime { NowMicroseconds = 5_000_000 };
            _go = new GameObject("Runner");
            var runner = _go.AddComponent<SimulationRunner>();
            runner.Run(sim, time);
            Time.timeScale = 0f;

            Assert.IsTrue(sim.Clock.IsPaused, "a new game opens paused");
            time.NowMicroseconds += 10_000_000;
            yield return null;
            Assert.AreEqual(0, sim.CurrentTick, "paused: real time is discarded");

            Assert.IsTrue(runner.Driver.Play().Succeeded);
            time.NowMicroseconds += 1_234_567;
            yield return null;
            Assert.AreEqual(1_234, sim.CurrentTick, "1x: one tick per real millisecond, remainder carried");

            Assert.IsTrue(runner.Driver.SetSpeed(ClockSpeed.X5).Succeeded);
            time.NowMicroseconds += 1_000_000;
            yield return null;
            Assert.AreEqual(1_234 + 5_000, sim.CurrentTick, "5x: five ticks per real millisecond");
        }

        [UnityTest]
        public IEnumerator WithTheRealClock_GameTimeMoves_WhileUnitysTimeScaleIsZero()
        {
            var sim = GameTestTuning.NewGameWithSystems(10);
            _go = new GameObject("Runner");
            var runner = _go.AddComponent<SimulationRunner>();
            runner.Run(sim);
            Time.timeScale = 0f;
            runner.Driver.Play();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            yield return new WaitForSecondsRealtime(0.5f);
            runner.Driver.Pump();
            long realMs = watch.ElapsedMilliseconds;
            TestContext.WriteLine($"{realMs} real ms, {sim.CurrentTick} ticks at 1x with timeScale 0");
            Assert.That(sim.CurrentTick, Is.InRange(realMs - 50, realMs + 50));
        }
    }
}
