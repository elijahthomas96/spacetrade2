using System;
using System.Collections.Generic;
using System.Text;

namespace SpaceTrade.Sim.Events
{
    /// <summary>
    /// Pending events ordered by tick, then phase, then entity id, then scheduling order.
    /// </summary>
    public sealed class EventQueue
    {
        private readonly SortedSet<ScheduledEvent> _pending = new SortedSet<ScheduledEvent>(new ScheduledEventComparer());
        private long _nextSequence;

        public int Count => _pending.Count;

        /// <summary>Tick of the earliest pending event, or null when nothing is pending.</summary>
        public long? NextTick => _pending.Count == 0 ? (long?)null : _pending.Min.Tick;

        internal void Enqueue(long tick, SimEvent ev)
        {
            if (ev == null) throw new ArgumentNullException(nameof(ev));
            _pending.Add(new ScheduledEvent(tick, _nextSequence++, ev));
        }

        /// <summary>Removes every pending event that matches. Returns how many were removed.</summary>
        internal int RemoveWhere(Func<SimEvent, bool> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return _pending.RemoveWhere(e => match(e.Event));
        }

        /// <summary>The scheduling counter: the ordering number the next scheduled event will get.</summary>
        internal long NextSequence => _nextSequence;

        /// <summary>Every pending event in processing order.</summary>
        internal IEnumerable<ScheduledEvent> Pending => _pending;

        /// <summary>
        /// Puts back saved events with their exact ordering numbers and the saved counter, so a loaded
        /// game orders same-tick events exactly as the saved one would. Only for an empty queue.
        /// </summary>
        internal void Restore(IEnumerable<ScheduledEvent> events, long nextSequence)
        {
            if (_pending.Count > 0 || _nextSequence != 0) throw new InvalidOperationException("Events can only be restored into an empty queue.");
            if (nextSequence < 0) throw new ArgumentOutOfRangeException(nameof(nextSequence), nextSequence, "The scheduling counter cannot be negative.");
            var seen = new HashSet<long>();
            foreach (var e in events)
            {
                if (e.Event == null) throw new ArgumentException("A saved event is missing.");
                if (e.Sequence < 0 || e.Sequence >= nextSequence)
                    throw new ArgumentException($"Event ordering number {e.Sequence} is outside 0 to {nextSequence - 1}.");
                if (!seen.Add(e.Sequence)) throw new ArgumentException($"Event ordering number {e.Sequence} appears twice.");
                _pending.Add(e);
            }
            _nextSequence = nextSequence;
        }

        internal bool TryDequeueDueBy(long tick, out ScheduledEvent next)
        {
            if (_pending.Count == 0 || _pending.Min.Tick > tick)
            {
                next = default;
                return false;
            }
            next = _pending.Min;
            _pending.Remove(next);
            return true;
        }

        /// <summary>Every pending event and the scheduling counter, in processing order, for comparing states exactly.</summary>
        internal string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("events next=").Append(_nextSequence).Append(" pending=").Append(_pending.Count).Append('\n');
            foreach (var e in _pending)
                sb.Append("event ").Append(e.Tick).Append(' ').Append(e.Event.Phase).Append(' ').Append(e.Event.EntityId)
                  .Append(' ').Append(e.Sequence).Append(' ').Append(e.Event.GetType().Name).Append('\n');
            return sb.ToString();
        }

        internal readonly struct ScheduledEvent
        {
            public readonly long Tick;
            public readonly long Sequence;
            public readonly SimEvent Event;

            public ScheduledEvent(long tick, long sequence, SimEvent ev)
            {
                Tick = tick;
                Sequence = sequence;
                Event = ev;
            }
        }

        private sealed class ScheduledEventComparer : IComparer<ScheduledEvent>
        {
            public int Compare(ScheduledEvent a, ScheduledEvent b)
            {
                int c = a.Tick.CompareTo(b.Tick);
                if (c != 0) return c;
                c = ((int)a.Event.Phase).CompareTo((int)b.Event.Phase);
                if (c != 0) return c;
                c = a.Event.EntityId.CompareTo(b.Event.EntityId);
                if (c != 0) return c;
                return a.Sequence.CompareTo(b.Sequence);
            }
        }
    }
}
