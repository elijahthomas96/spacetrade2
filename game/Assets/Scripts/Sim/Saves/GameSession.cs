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

        /// <summary>
        /// What happened while the game was closed. Null for a new game, when nothing was opened, and after
        /// <see cref="GameSession.BeginOpen"/>, whose summary comes from <see cref="GameSession.Pump"/> when the catch-up ends.
        /// </summary>
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
    /// <para>
    /// A catch-up (after <see cref="BeginOpen"/> or a wake) runs in slices: each <see cref="Pump"/> spends about
    /// <see cref="CatchUpBudgetRealUs"/> on it, so the screen can show <see cref="CatchUp"/>'s progress. Real time that
    /// passes while catching up is counted too. Player and clock commands are refused until it ends; it ends with one save.
    /// </para>
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>Autosave every this many real microseconds on the monotonic clock (60 seconds).</summary>
        public const long AutosaveIntervalRealUs = 60_000_000;

        /// <summary>Real time each <see cref="Pump"/> spends on a catch-up in progress, in microseconds (25 milliseconds).</summary>
        public const long CatchUpBudgetRealUs = 25_000;

        private readonly IRealTimeSource _monotonic;
        private readonly IUtcClock _utc;
        private long _lastPumpUtcMs;
        private long _lastSaveAttemptUs;
        private long _deliveryCount;
        private List<(int, long)> _voyages;
        private CatchUpRun _catchUp;
        private long _catchUpMonotonicUs;
        private SaveWriteResult _lastWrite;

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

        /// <summary>The summary of the latest catch-up (opening or waking from sleep), or null if there has been none.</summary>
        public ReturnSummary LastSummary { get; private set; }

        /// <summary>The catch-up in progress, with its progress figure, or null when there is none.</summary>
        public CatchUpRun CatchUp => _catchUp;

        public bool IsCatchingUp => _catchUp != null;

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
            RememberTravel();
        }

        /// <summary>
        /// Opens a saved game and runs the whole offline catch-up before returning: loads it (falling back to the backup if
        /// it is damaged), catches up from its timestamp to now, and writes the result with the new timestamp in one atomic
        /// save. If that save fails the game still opens, with <see cref="SaveWarning"/> set; the file on disk is unchanged,
        /// so opening it again cannot pay twice. The game's screen should use <see cref="BeginOpen"/> instead.
        /// </summary>
        public static GameSessionOpenResult Open(SaveStore store, string slot, TuningData tuning, IRealTimeSource monotonic, IUtcClock utc, DebugCapability debug = null)
        {
            var r = BeginOpen(store, slot, tuning, monotonic, utc, debug);
            if (r.Session == null) return r;
            var summary = r.Session.FinishCatchUp();
            return new GameSessionOpenResult(r.Session, r.Messages, r.UsedBackup, summary);
        }

        /// <summary>
        /// Opens a saved game with its offline catch-up still to run. Each <see cref="Pump"/> then runs part of it, and the
        /// one that finishes it saves the game and returns the summary. Until then <see cref="IsCatchingUp"/> is true.
        /// </summary>
        public static GameSessionOpenResult BeginOpen(SaveStore store, string slot, TuningData tuning, IRealTimeSource monotonic, IUtcClock utc, DebugCapability debug = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (monotonic == null) throw new ArgumentNullException(nameof(monotonic));
            if (utc == null) throw new ArgumentNullException(nameof(utc));
            long now = utc.NowUtcMilliseconds;
            var load = store.Load(slot, tuning, now, debug);
            if (!load.Succeeded) return new GameSessionOpenResult(null, load.Messages, false, null);
            var session = new GameSession(store, slot, load.Simulation, monotonic, utc, now);
            session.StartCatchUp(load.SavedUtcMs, now);
            return new GameSessionOpenResult(session, load.Messages, load.UsedBackup, null);
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
        /// Moves time to now. During a catch-up it runs the next part; the frame that finishes it saves at once and returns
        /// the summary (also kept in <see cref="LastSummary"/>). A wake from sleep starts a catch-up of the gap. Otherwise
        /// returns null; autosaves if due.
        /// </summary>
        public ReturnSummary Pump()
        {
            long now = _utc.NowUtcMilliseconds;
            if (_catchUp == null && now - _lastPumpUtcMs >= OfflineCatchUp.WakeGapRealMs) StartCatchUp(_lastPumpUtcMs, now);
            if (_catchUp != null)
            {
                ExtendCatchUp(now);
                _catchUp.StepFor(_monotonic, CatchUpBudgetRealUs);
                return _catchUp.IsDone ? EndCatchUp() : null;
            }
            Driver.Pump();
            _lastPumpUtcMs = now;
            AutosaveIfDue(now);
            return null;
        }

        /// <summary>Runs the rest of a catch-up in progress at once, saves, and returns its summary. Null when there is none.</summary>
        public ReturnSummary FinishCatchUp()
        {
            if (_catchUp == null) return null;
            ExtendCatchUp(_utc.NowUtcMilliseconds);
            _catchUp.RunToEnd();
            return EndCatchUp();
        }

        private void StartCatchUp(long fromUtcMs, long nowUtcMs)
        {
            _catchUp = OfflineCatchUp.Begin(Simulation, fromUtcMs, nowUtcMs);
            _catchUpMonotonicUs = _monotonic.NowMicroseconds;
            _lastPumpUtcMs = nowUtcMs;
        }

        /// <summary>Counts real time up to now in the catch-up, remembering the frame clock's reading at that moment.</summary>
        private void ExtendCatchUp(long nowUtcMs)
        {
            if (nowUtcMs > _catchUp.ToUtcMs && !_catchUp.ClockWentBackward)
            {
                _catchUp.ExtendTo(nowUtcMs);
                _catchUpMonotonicUs = _monotonic.NowMicroseconds;
            }
            _lastPumpUtcMs = nowUtcMs;
        }

        /// <summary>
        /// Saves the caught-up game with the time it was caught up to, and lets the frame clock count from the moment that
        /// time was read, so no real time is counted twice or lost.
        /// </summary>
        private ReturnSummary EndCatchUp()
        {
            var summary = _catchUp.Finish();
            long at = _catchUp.ToUtcMs;
            _catchUp = null;
            Driver.ResyncTo(_catchUpMonotonicUs);
            _lastPumpUtcMs = at;
            LastSummary = summary;
            WriteNow(at);
            return summary;
        }

        private CommandResult RefuseWhileCatchingUp() =>
            CommandResult.Fail($"The game is still catching up on the time it was away ({_catchUp.ProgressPerMille / 10}% done). Wait for it to finish.");

        /// <summary>Runs a player command and autosaves if it dispatched a ship. Send every player command through here. Refused during a catch-up.</summary>
        public CommandResult Command(Func<Simulation, CommandResult> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_catchUp != null) return RefuseWhileCatchingUp();
            var result = command(Simulation);
            AutosaveIfDue(_utc.NowUtcMilliseconds);
            return result;
        }

        public CommandResult Pause() => _catchUp != null ? RefuseWhileCatchingUp() : Driver.Pause();
        public CommandResult Play() => _catchUp != null ? RefuseWhileCatchingUp() : Driver.Play();
        public CommandResult SetSpeed(ClockSpeed speed) => _catchUp != null ? RefuseWhileCatchingUp() : Driver.SetSpeed(speed);

        /// <summary>Saves to the current slot now. A catch-up in progress is finished first, and its own save is the result.</summary>
        public SaveWriteResult Save()
        {
            if (_catchUp != null)
            {
                FinishCatchUp();
                return _lastWrite;
            }
            Pump();
            return WriteNow(_lastPumpUtcMs);
        }

        /// <summary>
        /// Saves to a named slot, which then becomes the current slot. On failure the current slot stays as it was. A catch-up
        /// in progress is finished (and saved to the current slot) first.
        /// </summary>
        public SaveWriteResult SaveAs(string slot)
        {
            string bad = SaveStore.CheckSlotName(slot);
            if (bad != null) return new SaveWriteResult(false, bad + " Nothing was saved.", 0);
            if (_catchUp != null) FinishCatchUp();
            else Pump();
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
            bool travelChanged = Simulation.DeliveryTotals.Deliveries != _deliveryCount || !CurrentVoyages().SequenceEqual(_voyages);
            if (travelChanged || _monotonic.NowMicroseconds - _lastSaveAttemptUs >= AutosaveIntervalRealUs) WriteNow(nowUtcMs);
        }

        private SaveWriteResult WriteNow(long nowUtcMs)
        {
            var result = Store.Write(Slot, Simulation, nowUtcMs);
            _lastWrite = result;
            _lastSaveAttemptUs = _monotonic.NowMicroseconds;
            RememberTravel();
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

        private void RememberTravel()
        {
            _deliveryCount = Simulation.DeliveryTotals.Deliveries;
            _voyages = CurrentVoyages();
        }
    }
}
