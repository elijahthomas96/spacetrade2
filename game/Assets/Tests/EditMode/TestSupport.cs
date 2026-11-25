using System;
using System.Collections.Generic;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Tests
{
    internal sealed class FakeRealTimeSource : IRealTimeSource
    {
        public long NowMicroseconds { get; set; }

        public void AdvanceMicroseconds(long us) => NowMicroseconds += us;
        public void AdvanceMilliseconds(long ms) => NowMicroseconds += ms * 1_000;
        public void AdvanceSeconds(long s) => NowMicroseconds += s * 1_000_000;
    }

    /// <summary>Stand-in event that logs the tick it ran at, and can run extra work.</summary>
    internal sealed class RecordingEvent : SimEvent
    {
        private readonly EventPhase _phase;
        private readonly int _entityId;
        private readonly string _label;
        private readonly List<string> _log;
        private readonly Action<Simulation> _onApply;

        public RecordingEvent(List<string> log, EventPhase phase, int entityId, string label = null, Action<Simulation> onApply = null)
        {
            _log = log;
            _phase = phase;
            _entityId = entityId;
            _label = label;
            _onApply = onApply;
        }

        public override EventPhase Phase => _phase;
        public override int EntityId => _entityId;

        public override void Apply(Simulation sim)
        {
            _log.Add(_label == null ? $"{sim.CurrentTick}:{_phase}:{_entityId}" : $"{sim.CurrentTick}:{_label}");
            _onApply?.Invoke(sim);
        }
    }
}
