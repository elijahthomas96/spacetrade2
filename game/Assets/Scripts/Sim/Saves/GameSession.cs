using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Saves
{
    public sealed class GameSessionOpenResult
    {
        /// <summary>The running game, or null when nothing was opened.</summary>
        public GameSession Session { get; }

        /// <summary>What went wrong or what was done instead (for example, the backup was loaded), for the player.</summary>
        public IReadOnlyList<string> Messages { get; }

        public bool UsedBackup { get; }

        /// <summary>What happened while the game was closed; null for a new game or when nothing was opened.</summary>
        public ReturnSummary Summary { get; }

        internal GameSessionOpenResult(GameSession session, IReadOnlyList<string> messages, bool usedBackup, ReturnSummary summary)
        {
            Session = session;
            Messages = messages;
            UsedBackup = usedBackup;
            Summary = summary;
        }
    }

    /// <summary>
    /// A running game and its save slot (spec 3.2, 11.2, 11.3). Call <see cref="Pump"/> every frame. It moves time with the
    /// real-time driver, notices waking from sleep (a gap of <see cref="OfflineCatchUp.WakeGapRealMs"/> or more on the UTC
    /// clock between two frames) and runs that gap through the offline catch-up rule with one atomic save, and autosaves on
    /// every dispatch, every arrival, and every 60 real seconds. <see cref="Quit"/> saves too. While any save fails,
    /// <see cref="SaveWarning"/> holds the reason; it stays until a save succeeds.
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>Autosave every this many real microseconds on the monotonic clock (60 seconds).</summary>
        public const long AutosaveIntervalRealUs = 60_000_000;

        private readonly IRealTimeSource _monotonic;
        private readonly IUtcClock _utc;
        private long _lastPumpUtcMs;
        private long _lastSaveAttemptUs;
        private int _receiptCount;
        private List<(int, long)> _voyages;

        public Simulation Simulation { get; }
        public RealTimeDriver Driver { get; }
        public SaveStore Store { get; }

        /// <summary>The slot autosaves and quit saves write to.</summary>
        public string Slot { get; private set; }

        /// <summary>Why the last save failed; null once a save succeeds. The interface keeps it on screen while it is set.</summary>
        public string SaveWarning { get; private set; }

        /// <summary>The UTC time of the last successful save.</summary>
        public long LastSaveUtcMs { get; private set; }

        /// <summary>How many saves this session has written successfully.</summary>
        public int SavesWritten { get; private set; }

        /// <summary>The summary of the latest wake from sleep, or null if there has been none.</summary>
        public ReturnSummary LastWakeSummary { get; private set; }

        private GameSession(SaveStore store, string slot, Simulation sim, IRealTimeSource monotonic, IUtcClock utc, long nowUtcMs)
        {
            Store = store;
            Slot = slot;
            Simulation = sim;
            _monotonic = monotonic;
            _utc = utc;
            Driver = new RealTimeDriver(sim, monotonic);
            _lastPumpUtcMs = nowUtcMs;
            _lastSaveAttemptUs = monotonic.NowMicroseconds;
            RememberVoyages();
        }

        /// <summary>
        /// Opens a saved game: loads it (falling back to the backup if it is damaged), runs the offline catch-up from its
        /// timestamp to now, and writes the result with the new timestamp in one atomic save before handing over control.
        /// If that save fails the game still opens, with <see cref="SaveWarning"/> set; the file on disk is unchanged, so
        /// opening it again cannot pay twice.
        /// </summary>
        public static GameSessionOpenResult Open(SaveStore store, string slot, TuningData tuning, IRealTimeSource monotonic, IUtcClock utc, DebugCapability debug = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (monotonic == null) throw new ArgumentNullException(nameof(monotonic));
            if (utc == null) throw new ArgumentNullException(nameof(utc));
            long now = utc.NowUtcMilliseconds;
            var load = store.Load(slot, tuning, now, debug);
            if (!load.Succeeded) return new GameSessionOpenResult(null, load.Messages, false, null);
            var summary = OfflineCatchUp.Run(load.Simulation, load.SavedUtcMs, now);
            var session = new GameSession(store, slot, load.Simulation, monotonic, utc, now);
            session.WriteNow(now);
            return new GameSessionOpenResult(session, load.Messages, load.UsedBackup, summary);
        }

        /// <summary>Starts a session for a new game and saves it at once. Refused, writing nothing, when a save with that name already exists.</summary>
        public static GameSessionOpenResult Start(SaveStore store, string slot, Simulation sim, IRealTimeSource monotonic, IUtcClock utc)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            string bad = SaveStore.CheckSlotName(slot);
            if (bad != null) return new GameSessionOpenResult(null, new[] { bad }, false, null);
            if (store.ListSlots().Contains(slot) || System.IO.File.Exists(store.BackupPathFor(slot)))
                return new GameSessionOpenResult(null, new[] { $"A save named \"{slot}\" already exists. Choose another name." }, false, null);
            long now = utc.NowUtcMilliseconds;
            var session = new GameSession(store, slot, sim, monotonic, utc, now);
            session.WriteNow(now);
            return new GameSessionOpenResult(session, new string[0], false, null);
        }

        /// <summary>
        /// Moves time to now. After a wake from sleep, the gap goes through the offline catch-up rule, is saved at once, and
        /// its summary is returned (and kept in <see cref="LastWakeSummary"/>). Otherwise returns null; autosaves if due.
        /// </summary>
        public ReturnSummary Pump()
        {
            long now = _utc.NowUtcMilliseconds;
            if (now - _lastPumpUtcMs >= OfflineCatchUp.WakeGapRealMs)
            {
                Driver.Resync();
                var summary = OfflineCatchUp.Run(Simulation, _lastPumpUtcMs, now);
                _lastPumpUtcMs = now;
                LastWakeSummary = summary;
                WriteNow(now);
                return summary;
            }
            Driver.Pump();
            _lastPumpUtcMs = now;
            AutosaveIfDue(now);
            return null;
        }

        /// <summary>Runs a player command and autosaves if it dispatched a ship. Send every player command through here.</summary>
        public CommandResult Command(Func<Simulation, CommandResult> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var result = command(Simulation);
            AutosaveIfDue(_utc.NowUtcMilliseconds);
            return result;
        }

        public CommandResult Pause() => Driver.Pause();
        public CommandResult Play() => Driver.Play();
        public CommandResult SetSpeed(ClockSpeed speed) => Driver.SetSpeed(speed);

        /// <summary>Saves to the current slot now.</summary>
        public SaveWriteResult Save()
        {
            Pump();
            return WriteNow(_lastPumpUtcMs);
        }

        /// <summary>Saves to a named slot, which then becomes the current slot. On failure the current slot stays as it was.</summary>
        public SaveWriteResult SaveAs(string slot)
        {
            string bad = SaveStore.CheckSlotName(slot);
            if (bad != null) return new SaveWriteResult(false, bad + " Nothing was saved.", 0);
            Pump();
            string previous = Slot;
            Slot = slot;
            var result = WriteNow(_lastPumpUtcMs);
            if (!result.Succeeded) Slot = previous;
            return result;
        }

        /// <summary>The save made when the game closes.</summary>
        public SaveWriteResult Quit() => Save();

        private void AutosaveIfDue(long nowUtcMs)
        {
            bool travelChanged = Simulation.Receipts.Count != _receiptCount || !CurrentVoyages().SequenceEqual(_voyages);
            if (travelChanged || _monotonic.NowMicroseconds - _lastSaveAttemptUs >= AutosaveIntervalRealUs) WriteNow(nowUtcMs);
        }

        private SaveWriteResult WriteNow(long nowUtcMs)
        {
            var result = Store.Write(Slot, Simulation, nowUtcMs);
            _lastSaveAttemptUs = _monotonic.NowMicroseconds;
            RememberVoyages();
            if (result.Succeeded)
            {
                SaveWarning = null;
                LastSaveUtcMs = nowUtcMs;
                SavesWritten++;
            }
            else SaveWarning = result.Problem;
            return result;
        }

        private List<(int, long)> CurrentVoyages() => Simulation.Ships.Where(s => s.Voyage != null).Select(s => (s.Id, s.Voyage.DepartureTick)).ToList();

        private void RememberVoyages()
        {
            _receiptCount = Simulation.Receipts.Count;
            _voyages = CurrentVoyages();
        }
    }
}
