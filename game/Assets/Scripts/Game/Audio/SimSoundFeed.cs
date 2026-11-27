using System.Collections.Generic;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Game.Audio
{
    /// <summary>
    /// Turns what happened in the simulation into sounds (spec 9.6). Once a frame, after the clock has moved, it compares
    /// the simulation with what it saw the frame before and asks for one sound per kind of event. It never works anything
    /// out for itself and never changes the simulation.
    ///
    /// A time jump (offline catch-up, sleep-wake catch-up, debug skip) is declared by the code that performs it, with
    /// <see cref="BeginJump"/> and <see cref="EndJump"/>. Everything that happens between the two is dropped, and the return
    /// summary sound plays once at the end. A jump is never guessed from the size of a time step: an ordinary frame,
    /// however slow, plays its events normally.
    /// </summary>
    public sealed class SimSoundFeed
    {
        private struct ShipSeen
        {
            public long? DepartureTick;
            public string DriveId;
            public bool InSystem;
            public bool Waiting;
        }

        private Simulation _sim;
        private Notification _newest;
        private long _grossRevenue, _inSystemIncome, _suppliesBought;
        private bool _paused;
        private ClockSpeed _speed;
        private readonly Dictionary<int, ShipSeen> _ships = new Dictionary<int, ShipSeen>();

        public bool IsJumping { get; private set; }

        /// <summary>How many jumps have been declared and finished.</summary>
        public int JumpsFinished { get; private set; }

        /// <summary>Starts watching a game. What is already true of it makes no sound.</summary>
        public void Attach(Simulation sim)
        {
            _sim = sim;
            IsJumping = false;
            Baseline();
        }

        private void Baseline()
        {
            _ships.Clear();
            if (_sim == null) return;
            var notes = _sim.Notifications;
            _newest = notes.Count > 0 ? notes[notes.Count - 1] : null;
            _grossRevenue = _sim.DeliveryTotals.GrossRevenue;
            _inSystemIncome = _sim.InSystemIncomeTotal;
            _suppliesBought = _sim.SuppliesBoughtTotal;
            _paused = _sim.Clock.IsPaused;
            _speed = _sim.Clock.Speed;
            foreach (var ship in _sim.Ships) _ships[ship.Id] = See(ship);
        }

        private static ShipSeen See(SpaceTrade.Sim.Ships.Ship ship) => new ShipSeen
        {
            DepartureTick = ship.Voyage?.DepartureTick,
            DriveId = ship.DriveId,
            InSystem = ship.IsOnInSystemTransport,
            Waiting = ship.Route != null && ship.Route.Status == RouteStatus.Waiting,
        };

        /// <summary>The code that is about to jump time says so. Events up to now still play; events from now on are dropped.</summary>
        public void BeginJump()
        {
            if (IsJumping) return;
            Observe();
            IsJumping = true;
        }

        /// <summary>The jump is over: whatever happened in it stays silent, and the return summary sound plays once.</summary>
        public void EndJump()
        {
            if (!IsJumping) return;
            IsJumping = false;
            JumpsFinished++;
            Baseline();
            if (_sim != null) GameAudio.Play(SoundId.ReturnSummary, $"{_sim.CurrentDate} · time jump finished");
        }

        /// <summary>The jump was called off before any time passed (for example a refused debug skip): no summary sound.</summary>
        public void CancelJump()
        {
            if (!IsJumping) return;
            IsJumping = false;
            Baseline();
        }

        /// <summary>Compares the simulation with the last frame and asks for the sounds of what changed. Called once a frame.</summary>
        public void Observe()
        {
            if (_sim == null || IsJumping) return;
            string Date() => _sim.CurrentDate.ToString();

            bool paused = _sim.Clock.IsPaused;
            if (paused != _paused) GameAudio.Play(paused ? SoundId.Pause : SoundId.Resume, $"{Date()} · clock {(paused ? "paused" : "resumed")}");
            _paused = paused;
            var speed = _sim.Clock.Speed;
            if (speed != _speed) GameAudio.Play(SoundId.SpeedChange, $"{Date()} · speed now {ClockSpeeds.Multiplier(speed)}x");
            _speed = speed;

            var notes = _sim.Notifications;
            int firstNew = notes.Count;
            while (firstNew > 0 && !ReferenceEquals(notes[firstNew - 1], _newest)) firstNew--;
            if (_newest == null) firstNew = 0;
            for (int i = firstNew; i < notes.Count; i++)
            {
                var n = notes[i];
                GameAudio.Play(n.Kind == NotificationKind.RouteBlocked ? SoundId.Blocked : SoundId.Arrival, $"{n.Date} · {n.Text}");
            }
            _newest = notes.Count > 0 ? notes[notes.Count - 1] : null;

            long gross = _sim.DeliveryTotals.GrossRevenue;
            if (gross > _grossRevenue) GameAudio.Play(SoundId.Payment, $"{Date()} · deliveries paid {Format.Credits(gross - _grossRevenue)} credits");
            _grossRevenue = gross;

            long income = _sim.InSystemIncomeTotal;
            if (income > _inSystemIncome) GameAudio.Play(SoundId.InSystemIncome, $"{Date()} · in-system transport paid {Format.Credits(income - _inSystemIncome)} credits");
            _inSystemIncome = income;

            bool anyDispatch = false;
            foreach (var ship in _sim.Ships)
            {
                var now = See(ship);
                if (!_ships.TryGetValue(ship.Id, out var before))
                {
                    GameAudio.Play(SoundId.PurchaseShip, $"{Date()} · {ship.Name} joined the fleet");
                    before = now;
                    before.DepartureTick = null;
                }
                bool dispatched = now.DepartureTick.HasValue && now.DepartureTick != before.DepartureTick;
                if (dispatched)
                {
                    anyDispatch = true;
                    GameAudio.Play(SoundId.Dispatch, $"{Date()} · {ship.Name} departed");
                }
                if (now.DriveId != before.DriveId) GameAudio.Play(SoundId.DriveInstall, $"{Date()} · {ship.Name} fitted {now.DriveId}");
                if (now.InSystem != before.InSystem && !dispatched)
                    GameAudio.Play(now.InSystem ? SoundId.ToggleOn : SoundId.ToggleOff, $"{Date()} · {ship.Name} in-system transport {(now.InSystem ? "on" : "off")}");
                if (now.Waiting && !before.Waiting) GameAudio.Play(SoundId.Waiting, $"{Date()} · {ship.Name} is waiting for a load");
                _ships[ship.Id] = now;
            }

            long bought = _sim.SuppliesBoughtTotal;
            if (bought > _suppliesBought && !anyDispatch) GameAudio.Play(SoundId.Provision, $"{Date()} · supplies bought for {Format.Credits(bought - _suppliesBought)} credits");
            _suppliesBought = bought;
        }
    }
}
