using System.Collections.Generic;

namespace SpaceTrade.Sim.Yards
{
    public enum YardItemKind
    {
        Ship,
        Drive,
    }

    /// <summary>
    /// One ship class or drive at one yard (spec 7): how many are in stock, and the stock-limit roll made
    /// once when the yard was created. The limit itself is worked out from the roll and the current tuning.
    /// </summary>
    public sealed class YardStock
    {
        public YardItemKind Kind { get; }

        /// <summary>A ship class id or a drive id from the tuning.</summary>
        public string ItemId { get; }

        public int Count { get; internal set; }

        /// <summary>Where the stock limit sits between the tuning minimum (0) and maximum (100), as a whole percent.</summary>
        public int LimitRollPercent { get; internal set; }

        internal YardStock(YardItemKind kind, string itemId, int count, int limitRollPercent)
        {
            Kind = kind;
            ItemId = itemId;
            Count = count;
            LimitRollPercent = limitRollPercent;
        }
    }

    /// <summary>
    /// The shipyard at one system (spec 7). Its id is its system's id. Prices are worked out from the
    /// yard's one stored price roll and the current tuning, so tuning edits reach existing saves.
    /// </summary>
    public sealed class Shipyard
    {
        private readonly List<YardStock> _ships = new List<YardStock>();
        private readonly List<YardStock> _drives = new List<YardStock>();

        public int SystemId { get; }

        /// <summary>One roll for every ship and drive at this yard, as a percent of the tuning base price.</summary>
        public int PriceRollPercent { get; internal set; }

        /// <summary>One entry per ship class, in the tuning order when the yard was created.</summary>
        public IReadOnlyList<YardStock> Ships => _ships;

        /// <summary>One entry per drive, in the tuning order when the yard was created.</summary>
        public IReadOnlyList<YardStock> Drives => _drives;

        internal Shipyard(int systemId, int priceRollPercent)
        {
            SystemId = systemId;
            PriceRollPercent = priceRollPercent;
        }

        internal void Add(YardStock stock) => (stock.Kind == YardItemKind.Ship ? _ships : _drives).Add(stock);

        public YardStock Find(YardItemKind kind, string itemId) =>
            (kind == YardItemKind.Ship ? _ships : _drives).Find(s => s.ItemId == itemId);
    }
}
