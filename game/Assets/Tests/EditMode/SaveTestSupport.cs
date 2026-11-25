using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.Yards;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// A fresh folder under the system's temporary folder for one test's saves. Only this folder is ever deleted, and only
    /// after checking that it is the one this object made, so a test can never touch the real save folder.
    /// </summary>
    internal sealed class TempSaveFolder : IDisposable
    {
        private const string Parent = "spacetrade2-tests";

        public string Path { get; }
        public SaveStore Store { get; }

        public TempSaveFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Parent, Guid.NewGuid().ToString("N"));
            string real = System.IO.Path.GetFullPath(UnityEngine.Application.persistentDataPath);
            Assert.IsFalse(System.IO.Path.GetFullPath(Path).StartsWith(real, StringComparison.OrdinalIgnoreCase), "Test saves must never go in the real save folder.");
            Assert.IsFalse(Directory.Exists(Path));
            Directory.CreateDirectory(Path);
            Store = new SaveStore(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string[] FileNames() => Directory.GetFileSystemEntries(Path).Select(System.IO.Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        public void Dispose()
        {
            string full = System.IO.Path.GetFullPath(Path);
            string expectedParent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Parent));
            if (Directory.Exists(full) && System.IO.Path.GetDirectoryName(full) == expectedParent) Directory.Delete(full, recursive: true);
        }
    }

    internal sealed class FakeUtcClock : IUtcClock
    {
        public long NowUtcMilliseconds { get; set; }

        public FakeUtcClock(long now) { NowUtcMilliseconds = now; }

        public void AdvanceMilliseconds(long ms) => NowUtcMilliseconds += ms;
    }

    internal static class SaveTestSupport
    {
        /// <summary>2026-11-25 12:00:00 UTC.</summary>
        public const long T0 = 1_795_608_000_000;

        public const long Second = 1_000;
        public const long Minute = 60 * Second;
        public const long Hour = 60 * Minute;
        public const long RealDay = 24 * Hour;

        public static void Ok(SaveWriteResult r) => Assert.IsTrue(r.Succeeded, r.Problem);

        public static byte[] Bytes(TempSaveFolder f, string name) => System.IO.File.ReadAllBytes(f.File(name));

        public static Simulation Load(TempSaveFolder f, string slot, TuningData tuning = null, DebugCapability debug = null)
        {
            var r = f.Store.Load(slot, tuning ?? FlatTuning(), T0, debug);
            Assert.IsTrue(r.Succeeded, string.Join(" ", r.Messages));
            return r.Simulation;
        }

        /// <summary>
        /// The triangle map with five ships, run until several are mid-voyage: ship 1 on the three-stop circuit, ship 2 on an
        /// ore shuttle with a 25 t minimum (often waiting), ship 3 on in-system transport at Gamma, ship 4 on a shuttle with
        /// automatic provisioning off and empty supplies (blocked), ship 5 with a paused route.
        /// </summary>
        public static Simulation RouteFleet(TuningData tuning = null, long runTicks = 7 * Year + 123_457)
        {
            tuning = tuning ?? FlatTuning();
            var sim = Triangle(5_000_000, tuning);
            var courier = tuning.Start.StarterShipClass;
            var drive = tuning.Start.StarterDrive;
            sim.AddShip("Shuttle", courier, drive, Alpha);
            sim.AddShip("Local", courier, drive, Gamma);
            sim.AddShip("Dry", courier, drive, Alpha);
            sim.AddShip("Resting", courier, drive, Beta);
            RouteTestSupport.Ok(sim.SetRoute(1, Circuit()));
            RouteTestSupport.Ok(sim.SetRoute(2, OreShuttle(minFreight: 25)));
            RouteTestSupport.Ok(sim.SetInSystemTransport(3, true));
            RouteTestSupport.Ok(sim.SetRoute(4, OreShuttle(autoProvision: false)));
            RouteTestSupport.Ok(sim.SetRoute(5, OreShuttle()));
            RouteTestSupport.Ok(sim.PauseRoute(5));
            sim.Advance(runTicks);
            return sim;
        }

        /// <summary>
        /// A generated galaxy from the fixture with yards: a bought ship fitted with a faster drive and put on in-system
        /// transport, and the starter flying its most profitable opening job.
        /// </summary>
        public static Simulation YardGame(TuningData tuning = null, long runTicks = 400 * Day + 77)
        {
            tuning = tuning ?? TestTuning.Load();
            var sim = NewGame.Create(1, tuning).Simulation;
            sim.SetCredits(20_000_000);
            RouteTestSupport.Ok(sim.Play());
            var yard = sim.Market.Shipyards[0];
            RouteTestSupport.Ok(sim.BuyShip(yard.SystemId, "courier", out int bought));
            var faster = tuning.Drives.First(d => d.Id != sim.FindShip(bought).DriveId && sim.Market.Stocks(yard.Find(YardItemKind.Drive, d.Id)));
            RouteTestSupport.Ok(sim.BuyDrive(bought, faster.Id));
            RouteTestSupport.Ok(sim.SetInSystemTransport(bought, true));
            var job = OpeningOffers.FindJobs(sim, 1).OrderByDescending(j => j.NetProfit).First();
            RouteTestSupport.Ok(sim.Dispatch(job.Quote.Plan, buyTopUp: true));
            sim.Advance(runTicks);
            return sim;
        }

        /// <summary>Writes the game to a slot at time T0 and returns the file's bytes.</summary>
        public static byte[] SaveAt(TempSaveFolder f, string slot, Simulation sim, long utcMs = T0)
        {
            Ok(f.Store.Write(slot, sim, utcMs));
            return Bytes(f, slot + SaveStore.Extension);
        }

        /// <summary>Opens a slot as a session at <paramref name="utcMs"/>, failing the test if it does not open.</summary>
        public static GameSessionOpenResult Open(TempSaveFolder f, string slot, long utcMs, TuningData tuning = null, FakeRealTimeSource mono = null, FakeUtcClock utc = null)
        {
            utc = utc ?? new FakeUtcClock(utcMs);
            utc.NowUtcMilliseconds = utcMs;
            var r = GameSession.Open(f.Store, slot, tuning ?? FlatTuning(), mono ?? new FakeRealTimeSource(), utc);
            Assert.IsNotNull(r.Session, string.Join(" ", r.Messages));
            return r;
        }

        public static long SavedUtcMs(TempSaveFolder f, string slot) => f.Store.Load(slot, FlatTuning(), T0).SavedUtcMs;
    }
}
