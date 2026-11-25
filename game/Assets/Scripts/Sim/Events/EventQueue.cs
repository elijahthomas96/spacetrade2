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
