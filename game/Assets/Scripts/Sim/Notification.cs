namespace SpaceTrade.Sim
{
    public enum NotificationKind
    {
        Arrival = 1,
        RouteBlocked = 2,
    }

    /// <summary>Something the player is told about (spec 3.4): an arrival, or a ship on a route becoming blocked.</summary>
    public sealed class Notification
    {
        public long Tick { get; }
        public NotificationKind Kind { get; }
        public int ShipId { get; }
        public string Text { get; }

        internal Notification(long tick, NotificationKind kind, int shipId, string text)
        {
            Tick = tick;
            Kind = kind;
            ShipId = shipId;
            Text = text;
        }

        /// <summary>Every stored value in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState() => $"notification {Tick} {Kind} ship={ShipId} {Text}";
    }
}
