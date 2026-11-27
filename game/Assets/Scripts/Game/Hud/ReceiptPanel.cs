using System;
using System.Collections.Generic;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The delivery receipt (spec 6.7): fares, freight revenue, the supplies the voyage used at their acquisition cost,
    /// the elapsed game time, and the net profit. Every figure is read from the simulation's own receipt.
    /// </summary>
    public sealed class ReceiptPanel : IWorkPanel
    {
        private readonly VisualElement _panel, _lines;
        private Simulation _sim;

        public VisualElement Panel => _panel;
        public string Title => "receipt";

        /// <summary>The receipt on show, or null.</summary>
        public VoyageReceipt Receipt { get; private set; }

        /// <summary>Raised when the player closes the receipt with its own button.</summary>
        public event Action CloseRequested;

        public ReceiptPanel(VisualElement root)
        {
            var t = Theme.Receipt;
            _panel = new VisualElement { name = "receipt-panel" };
            _panel.style.position = Position.Absolute;
            _panel.style.top = Theme.TopBar.Height + Theme.Panels.Margin;
            _panel.style.right = Theme.Panels.Margin;
            _panel.style.width = t.Width;
            UiKit.Panel(_panel, Theme.Colours.Dropdown, Theme.Panels.Padding);
            _panel.style.display = DisplayStyle.None;

            var header = UiKit.Row();
            var title = UiKit.Title("Delivery receipt", Theme.Text.Title);
            title.style.flexGrow = 1;
            header.Add(title);
            var close = UiKit.TextButton("Close", () => CloseRequested?.Invoke(), null);
            close.name = "receipt-close";
            header.Add(close);
            _panel.Add(header);

            _lines = UiKit.Scroll("receipt-lines");
            _lines.style.flexShrink = 1;
            _panel.Add(_lines);
            root.Add(_panel);
        }

        public void Bind(Simulation sim)
        {
            _sim = sim;
            Receipt = null;
            _lines.Clear();
        }

        /// <summary>Writes a receipt on the panel. The panel itself is shown by the work-panel manager.</summary>
        public void Show(VoyageReceipt receipt)
        {
            Receipt = receipt;
            _lines.Clear();
            if (receipt == null || _sim == null) return;
            var colours = Theme.Colours;
            Add("receipt-route", JobText.ReceiptRoute(_sim, receipt), colours.Text, false);
            Add("receipt-dates", JobText.ReceiptDates(receipt), colours.TextDim, false);

            foreach (var f in receipt.Freight)
                Add("receipt-freight", JobText.FreightLine(_sim, f.CommodityId, f.Tonnes, f.RatePerTonne, f.Revenue), colours.TextDim, false);
            if (receipt.Passengers > 0)
                Add("receipt-fares", JobText.FaresLine(receipt.Passengers, receipt.FarePerPassenger, receipt.FareRevenue), colours.TextDim, false);
            if (receipt.Freight.Count == 0 && receipt.Passengers == 0)
                Add("receipt-empty", "An empty voyage: nothing was carried, so nothing was paid.", colours.TextDim, false);
            Add("receipt-revenue", JobText.RevenueLine(receipt.FreightRevenue, receipt.FareRevenue, receipt.GrossRevenue), colours.Text, true);

            foreach (var s in receipt.Supplies)
                Add("receipt-supply", JobText.UsedLine(s.Kind, s.Kg, s.Cost), colours.TextDim, false);
            Add("receipt-cost", JobText.SuppliesCostLine(receipt.SuppliesCost), colours.Text, true);
            Add("receipt-net", JobText.ReceiptNet(receipt), receipt.NetProfit < 0 ? colours.Loss : colours.Text, true);
        }

        private void Add(string name, string text, Color colour, bool gapAbove)
        {
            var l = UiKit.Body(text, colour);
            l.name = name;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = gapAbove ? Theme.Receipt.SectionGap : Theme.Receipt.LineGap;
            UiKit.Padding(l, 0);
            _lines.Add(l);
        }

        /// <summary>Every line on the receipt, top to bottom.</summary>
        public List<string> Lines()
        {
            var lines = new List<string>();
            foreach (var l in _lines.Query<Label>().ToList()) lines.Add(l.text);
            return lines;
        }

        /// <summary>The text of the first line with this name, or "".</summary>
        public string LineText(string name) => _lines.Q<Label>(name)?.text ?? "";
    }
}
