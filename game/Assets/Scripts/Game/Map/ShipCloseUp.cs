using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// The zoomed-in view of one ship (spec 9.1): its close-up image over a streaked backdrop that drifts while the ship
    /// flies and stops while paused or docked, and the vessel panel with live values read from the simulation every frame.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShipCloseUp : MonoBehaviour
    {
        /// <summary>How far the backdrop drifts per game tick, in screen pixels: 50 pixels a real second at 1x, 250 at 5x.</summary>
        public const float BackdropPixelsPerTick = 0.05f;
        private const float PanelWidth = 340f;

        private static readonly Color TextColour = new Color(0.86f, 0.9f, 0.96f, 1f);
        private static readonly Color DimText = new Color(0.62f, 0.68f, 0.76f, 1f);
        private static readonly Color PanelColour = new Color(0.05f, 0.07f, 0.11f, 0.9f);
        private static readonly Color BarColour = new Color(0.45f, 0.85f, 1f, 1f);

        private UIDocument _doc;
        private VisualElement _root, _shipBox, _manifest, _barFill;
        private Image[] _backdrop;
        private Image _ship, _glow, _trail;
        private Label _title, _subtitle;
        private readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();
        private readonly Dictionary<string, VisualElement> _rows = new Dictionary<string, VisualElement>();
        private Simulation _sim;
        private long _lastTick;
        private string _manifestKey;

        public bool IsOpen { get; private set; }
        public int ShipId { get; private set; } = -1;
        public float BackdropOffsetPixels { get; private set; }

        /// <summary>Raised by the close button; the map screen closes the close-up.</summary>
        public event System.Action CloseRequested;

        public static ShipCloseUp Create(Transform parent)
        {
            var go = new GameObject("Ship close-up");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>(MapHud.PanelSettingsResource);
            doc.sortingOrder = 10;
            var view = go.AddComponent<ShipCloseUp>();
            view._doc = doc;
            go.SetActive(true);
            return view;
        }

        /// <summary>The text of a panel value ("status", "destination", "progress", "eta", "drive", "crew", "passengers", "manifest", "fuel", "food", "water"), or null.</summary>
        public string Value(string key)
        {
            if (key == "manifest")
            {
                var parts = new List<string>();
                foreach (var child in _manifest.Children())
                    if (child is Label l) parts.Add(l.text);
                    else foreach (var inner in child.Children()) if (inner is Label il) parts.Add(il.text);
                return string.Join("; ", parts);
            }
            return _values.TryGetValue(key, out var label) ? label.text : null;
        }

        /// <summary>True when a panel value is shown (some are hidden for a docked ship).</summary>
        public bool IsShown(string key) => _rows.TryGetValue(key, out var row) && row.style.display == DisplayStyle.Flex;

        public string Title => _title?.text;
        public Sprite ShipImage => _ship?.sprite;

        private void OnEnable()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            _root = _doc.rootVisualElement;
            _root.Clear();
            _values.Clear();
            _rows.Clear();
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            _root.style.backgroundColor = Color.black;
            _root.style.overflow = Overflow.Hidden;
            _root.style.display = DisplayStyle.None;

            var backdropSprite = ArtLibrary.Get("Map/closeup_backdrop");
            _backdrop = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var img = new Image { sprite = backdropSprite, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                img.style.position = Position.Absolute;
                img.style.top = 0; img.style.bottom = 0;
                if (i == 1) img.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                _backdrop[i] = img;
                _root.Add(img);
            }

            _shipBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _shipBox.style.position = Position.Absolute;
            _shipBox.style.left = Length.Percent(6); _shipBox.style.width = Length.Percent(52);
            _shipBox.style.top = Length.Percent(30); _shipBox.style.height = Length.Percent(48);
            _root.Add(_shipBox);
            _glow = AddFill(_shipBox, ArtLibrary.Get("Ships/warp_glow"), -20f);
            _trail = new Image { sprite = ArtLibrary.Get("Ships/engine_trail"), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _trail.style.position = Position.Absolute;
            _trail.style.right = Length.Percent(88); _trail.style.width = Length.Percent(60);
            _trail.style.top = Length.Percent(40); _trail.style.height = Length.Percent(20);
            _shipBox.Add(_trail);
            _ship = AddFill(_shipBox, null, 0f);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.position = Position.Absolute;
            header.style.left = 24; header.style.top = 20; header.style.right = PanelWidth + 32;
            _title = new Label();
            _title.style.fontSize = 30;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.color = TextColour;
            _subtitle = new Label();
            _subtitle.style.fontSize = 15;
            _subtitle.style.color = DimText;
            header.Add(_title);
            header.Add(_subtitle);
            var supplies = new VisualElement();
            supplies.style.flexDirection = FlexDirection.Row;
            supplies.style.alignItems = Align.Center;
            supplies.style.flexWrap = Wrap.Wrap;
            supplies.style.marginTop = 8;
            supplies.Add(Caption("Remaining supplies", 0));
            AddSupply(supplies, "fuel", ArtLibrary.Get("Icons/supply_fuel"));
            AddSupply(supplies, "food", ArtLibrary.Get("Icons/supply_food"));
            AddSupply(supplies, "water", ArtLibrary.Get("Icons/supply_water"));
            header.Add(supplies);
            _root.Add(header);

            var panel = new VisualElement { name = "vessel-panel" };
            panel.style.position = Position.Absolute;
            panel.style.right = 16; panel.style.top = 16;
            panel.style.width = PanelWidth;
            panel.style.backgroundColor = PanelColour;
            panel.style.paddingLeft = 14; panel.style.paddingRight = 14; panel.style.paddingTop = 12; panel.style.paddingBottom = 12;
            _root.Add(panel);

            AddRow(panel, "status", "Status", null);
            AddRow(panel, "destination", "Destination", null);
            AddRow(panel, "progress", "Progress", null);
            var bar = new VisualElement();
            bar.style.height = 6;
            bar.style.marginTop = 4;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            _barFill = new VisualElement();
            _barFill.style.height = 6;
            _barFill.style.backgroundColor = BarColour;
            bar.Add(_barFill);
            panel.Add(bar);
            _rows["bar"] = bar;
            AddRow(panel, "eta", "ETA", null);
            AddRow(panel, "drive", "Drive", null);
            AddRow(panel, "crew", "Crew", ArtLibrary.Get("Icons/crew"));
            AddRow(panel, "passengers", "Passengers", ArtLibrary.Get("Icons/passenger"));
            panel.Add(Heading("Manifest"));
            _manifest = new VisualElement();
            panel.Add(_manifest);

            var footer = new VisualElement();
            footer.style.position = Position.Absolute;
            footer.style.left = 24; footer.style.bottom = 20;
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            var close = new Button(() => CloseRequested?.Invoke()) { name = "close-up-close" };
            close.text = "";
            close.style.width = 34; close.style.height = 34;
            close.style.backgroundImage = new StyleBackground(ArtLibrary.Get("UI/close"));
            close.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            close.style.borderLeftWidth = 0; close.style.borderRightWidth = 0; close.style.borderTopWidth = 0; close.style.borderBottomWidth = 0;
            footer.Add(close);
            var hint = new Label("Mouse wheel out or Esc to return to the map");
            hint.style.color = DimText;
            hint.style.marginLeft = 10;
            footer.Add(hint);
            _root.Add(footer);
        }

        public void Open(Simulation sim, int shipId)
        {
            _sim = sim;
            ShipId = shipId;
            IsOpen = true;
            _lastTick = sim.CurrentTick;
            _manifestKey = null;
            var ship = sim.FindShip(shipId);
            _title.text = ship.Name;
            _subtitle.text = ShipText.ClassName(sim, ship);
            _ship.sprite = ArtLibrary.ShipCloseUp(ship.ClassId);
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            ShipId = -1;
            _sim = null;
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        private void LateUpdate()
        {
            if (IsOpen) Refresh();
        }

        private void Refresh()
        {
            var ship = _sim?.FindShip(ShipId);
            if (ship == null) return;
            var p = _sim.PositionOf(ShipId);
            long tick = _sim.CurrentTick;

            float width = _root.layout.width;
            if (float.IsNaN(width) || width <= 0f) width = Screen.width;
            if (p.InTransit) BackdropOffsetPixels = (BackdropOffsetPixels + (tick - _lastTick) * BackdropPixelsPerTick) % (2f * width);
            _lastTick = tick;
            for (int i = 0; i < 3; i++)
            {
                _backdrop[i].style.width = width;
                _backdrop[i].style.left = i * width - BackdropOffsetPixels;
            }
            _glow.style.display = p.InTransit ? DisplayStyle.Flex : DisplayStyle.None;
            _trail.style.display = p.InTransit ? DisplayStyle.Flex : DisplayStyle.None;

            Set("status", ShipText.Status(_sim, ship, p));
            Show("destination", p.InTransit);
            if (p.InTransit) Set("destination", ShipText.SystemName(_sim, p.DestinationId.Value));
            Set("progress", ShipText.Progress(_sim, p));
            _barFill.style.width = Length.Percent(p.ProgressHundredthsOfPercent / 100f);
            Show("eta", p.InTransit);
            Set("eta", ShipText.Eta(_sim, p));
            Set("drive", ShipText.Drive(_sim, ship));
            Set("crew", $"{_sim.Market.Tuning.FindShip(ship.ClassId)?.Crew ?? 0} crew");
            int passengers = ship.Voyage?.Passengers ?? 0;
            Set("passengers", passengers == 0 ? "None aboard" : passengers == 1 ? "1 passenger" : $"{passengers} passengers");
            Set("fuel", ShipText.Kg(_sim.RemainingSupplyKg(ShipId, SupplyKind.Fuel)));
            Set("food", ShipText.Kg(_sim.RemainingSupplyKg(ShipId, SupplyKind.Food)));
            Set("water", ShipText.Kg(_sim.RemainingSupplyKg(ShipId, SupplyKind.Water)));
            RefreshManifest(ship.Voyage);
        }

        private void RefreshManifest(Voyage v)
        {
            string key = v == null ? "docked" : $"{v.ShipId}:{v.DepartureTick}:{v.DestinationId}";
            if (key == _manifestKey) return;
            _manifestKey = key;
            _manifest.Clear();
            if (v == null || (v.Freight.Count == 0 && v.Passengers == 0))
            {
                _manifest.Add(Text(v == null ? "Nothing aboard (docked)" : "Empty voyage: no freight, no passengers", DimText));
                return;
            }
            foreach (var f in v.Freight)
                _manifest.Add(IconLine(ArtLibrary.CommodityIcon(f.CommodityId), $"{ShipText.CommodityName(_sim, f.CommodityId)}: {f.Tonnes} t at {Format.Credits(f.RatePerTonne)} per tonne"));
            if (v.Passengers > 0)
                _manifest.Add(IconLine(ArtLibrary.Get("Icons/passenger"), $"Passengers: {v.Passengers} at {Format.Credits(v.FarePerPassenger)} each"));
            _manifest.Add(IconLine(ArtLibrary.Get("Icons/credits"), $"Payout on arrival: {Format.Credits(v.Payout)} credits"));
        }

        private void Set(string key, string text)
        {
            var l = _values[key];
            if (l.text != text) l.text = text;
        }

        private void Show(string key, bool on)
        {
            var display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (_rows[key].style.display != display) _rows[key].style.display = display;
            if (key == "progress") _rows["bar"].style.display = display;
        }

        private void AddRow(VisualElement panel, string key, string caption, Sprite icon)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 5;
            row.style.display = DisplayStyle.Flex;
            if (icon != null)
            {
                var image = new Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = 18; image.style.height = 18; image.style.marginRight = 6;
                row.Add(image);
            }
            var cap = Tight(new Label(caption));
            cap.style.color = DimText;
            cap.style.width = icon != null ? 76 : 100;
            row.Add(cap);
            var value = Tight(new Label());
            value.style.color = TextColour;
            value.style.whiteSpace = WhiteSpace.Normal;
            value.style.flexShrink = 1;
            row.Add(value);
            panel.Add(row);
            _values[key] = value;
            _rows[key] = row;
        }

        private void AddSupply(VisualElement row, string key, Sprite icon)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.marginLeft = 14;
            item.style.display = DisplayStyle.Flex;
            var image = new Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = 18; image.style.height = 18; image.style.marginRight = 5;
            item.Add(image);
            var value = new Label();
            value.style.color = TextColour;
            item.Add(value);
            row.Add(item);
            _values[key] = value;
            _rows[key] = item;
        }

        private static Label Caption(string text, float marginTop)
        {
            var l = new Label(text);
            l.style.color = DimText;
            l.style.marginTop = marginTop;
            return l;
        }

        /// <summary>No space above or below the text, so the panel fits a short window.</summary>
        private static Label Tight(Label l)
        {
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            return l;
        }

        private static Label Heading(string text)
        {
            var l = Tight(new Label(text));
            l.style.color = DimText;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 6;
            return l;
        }

        private static Label Text(string text, Color colour)
        {
            var l = Tight(new Label(text));
            l.style.color = colour;
            l.style.marginTop = 4;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static VisualElement IconLine(Sprite icon, string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            var image = new Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = 18; image.style.height = 18; image.style.marginRight = 6;
            row.Add(image);
            var l = Tight(new Label(text));
            l.style.color = TextColour;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            row.Add(l);
            return row;
        }

        private static Image AddFill(VisualElement parent, Sprite sprite, float inset)
        {
            var img = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.position = Position.Absolute;
            img.style.left = inset; img.style.right = inset; img.style.top = inset; img.style.bottom = inset;
            parent.Add(img);
            return img;
        }
    }
}
