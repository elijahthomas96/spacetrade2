using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Theming;
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
    /// It sits below the always-visible bar. Every colour, size, and spacing comes from the theme.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShipCloseUp : MonoBehaviour
    {
        /// <summary>How far the backdrop drifts per game tick, in screen pixels (from the theme).</summary>
        public static float BackdropPixelsPerTick => Theme.CloseUp.BackdropPixelsPerTick;

        /// <summary>Where the close-up sits among the screen's layers: above the map, below the always-visible bar.</summary>
        public const int SortingOrder = 10;

        private const int BackdropTiles = 3;

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

        /// <summary>The root every close-up element sits under, for checks of the whole screen's look.</summary>
        public VisualElement Root => _root;

        public static ShipCloseUp Create(Transform parent)
        {
            var go = new GameObject("Ship close-up");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>(MapHud.PanelSettingsResource);
            doc.sortingOrder = SortingOrder;
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
            var t = Theme.CloseUp;
            var colours = Theme.Colours;
            float barHeight = Theme.TopBar.Height;
            _root = _doc.rootVisualElement;
            _root.Clear();
            _values.Clear();
            _rows.Clear();
            UiKit.Fill(_root);
            UiKit.Root(_root);
            _root.style.backgroundColor = colours.CloseUpBackground;
            _root.style.overflow = Overflow.Hidden;
            _root.style.display = DisplayStyle.None;

            var backdropSprite = ArtLibrary.Get("Map/closeup_backdrop");
            _backdrop = new Image[BackdropTiles];
            for (int i = 0; i < BackdropTiles; i++)
            {
                var img = new Image { name = "backdrop", sprite = backdropSprite, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                img.style.position = Position.Absolute;
                img.style.top = 0; img.style.bottom = 0;
                if (i == 1) img.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                _backdrop[i] = img;
                _root.Add(img);
            }

            _shipBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _shipBox.style.position = Position.Absolute;
            _shipBox.style.left = Length.Percent(t.ShipLeftPercent); _shipBox.style.width = Length.Percent(t.ShipWidthPercent);
            _shipBox.style.top = Length.Percent(t.ShipTopPercent); _shipBox.style.height = Length.Percent(t.ShipHeightPercent);
            _root.Add(_shipBox);
            _glow = AddFill(_shipBox, ArtLibrary.Get("Ships/warp_glow"), -t.GlowOutsetPixels);
            _trail = new Image { name = "engine-trail", sprite = ArtLibrary.Get("Ships/engine_trail"), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _trail.style.position = Position.Absolute;
            _trail.style.right = Length.Percent(t.TrailRightPercent); _trail.style.width = Length.Percent(t.TrailWidthPercent);
            _trail.style.top = Length.Percent(t.TrailTopPercent); _trail.style.height = Length.Percent(t.TrailHeightPercent);
            _shipBox.Add(_trail);
            _ship = AddFill(_shipBox, null, 0f);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.position = Position.Absolute;
            header.style.left = t.HeaderLeft; header.style.top = barHeight + t.HeaderTop; header.style.right = t.PanelWidth + t.HeaderGap;
            _title = UiKit.Title("", Theme.Text.CloseUpTitle);
            _subtitle = UiKit.Label("", colours.TextDim, Theme.Text.CloseUpSubtitle);
            header.Add(_title);
            header.Add(_subtitle);
            var supplies = UiKit.Row();
            supplies.style.flexWrap = Wrap.Wrap;
            supplies.style.marginTop = t.SuppliesTop;
            supplies.Add(UiKit.Body("Remaining supplies", colours.TextDim));
            AddSupply(supplies, "fuel", ArtLibrary.Get("Icons/supply_fuel"));
            AddSupply(supplies, "food", ArtLibrary.Get("Icons/supply_food"));
            AddSupply(supplies, "water", ArtLibrary.Get("Icons/supply_water"));
            header.Add(supplies);
            _root.Add(header);

            var panel = new VisualElement { name = "vessel-panel" };
            panel.style.position = Position.Absolute;
            panel.style.right = Theme.Panels.Margin; panel.style.top = barHeight + Theme.Panels.Margin;
            panel.style.width = t.PanelWidth;
            panel.style.backgroundColor = colours.CloseUpPanel;
            UiKit.Padding(panel, t.PanelPaddingSide, t.PanelPaddingTop);
            _root.Add(panel);

            AddRow(panel, "status", "Status", null);
            AddRow(panel, "destination", "Destination", null);
            AddRow(panel, "progress", "Progress", null);
            var bar = new VisualElement { name = "progress-bar" };
            bar.style.height = t.BarHeight;
            bar.style.marginTop = t.BarGap;
            bar.style.backgroundColor = colours.ProgressTrack;
            _barFill = new VisualElement { name = "progress-fill" };
            _barFill.style.height = t.BarHeight;
            _barFill.style.backgroundColor = colours.ProgressFill;
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

            var footer = UiKit.Row();
            footer.style.position = Position.Absolute;
            footer.style.left = t.FooterLeft; footer.style.bottom = t.FooterBottom;
            var close = UiKit.IconButton(ArtLibrary.Get("UI/close"), t.CloseButtonSize, () => CloseRequested?.Invoke(), null);
            close.name = "close-up-close";
            footer.Add(close);
            var hint = UiKit.Body("Mouse wheel out or Esc to return to the map", colours.TextDim);
            hint.style.marginLeft = t.HintGap;
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
            if (GameAudio.Current != null) GameAudio.Current.SetAmbience(null);
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
            for (int i = 0; i < BackdropTiles; i++)
            {
                _backdrop[i].style.width = width;
                _backdrop[i].style.left = i * width - BackdropOffsetPixels;
            }
            _glow.style.display = p.InTransit ? DisplayStyle.Flex : DisplayStyle.None;
            _trail.style.display = p.InTransit ? DisplayStyle.Flex : DisplayStyle.None;
            if (GameAudio.Current != null) GameAudio.Current.SetAmbience(p.InTransit ? SoundId.ShipCloseUpDrive : SoundId.ShipCloseUpDocked);

            Set("status", ShipText.Status(_sim, ship, p));
            Show("destination", p.InTransit);
            if (p.InTransit) Set("destination", ShipText.SystemName(_sim, p.DestinationId.Value));
            Set("progress", ShipText.Progress(_sim, p));
            _barFill.style.width = Length.Percent(p.ProgressHundredthsOfPercent / 100f);
            Show("eta", p.InTransit);
            Set("eta", ShipText.Eta(_sim, ShipId));
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
                _manifest.Add(Text(v == null ? "Nothing aboard (docked)" : "Empty voyage: no freight, no passengers", Theme.Colours.TextDim));
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
            var t = Theme.CloseUp;
            var row = UiKit.Row();
            row.style.marginTop = t.RowGap;
            row.style.display = DisplayStyle.Flex;
            if (icon != null)
            {
                var image = UiKit.Icon(icon, t.IconSize, Theme.Colours.Icon);
                image.style.marginRight = t.IconGap;
                row.Add(image);
            }
            var cap = Tight(UiKit.Body(caption, Theme.Colours.TextDim));
            cap.style.width = icon != null ? t.IconCaptionWidth : t.CaptionWidth;
            row.Add(cap);
            var value = Tight(UiKit.Body("", Theme.Colours.Text));
            value.style.whiteSpace = WhiteSpace.Normal;
            value.style.flexShrink = 1;
            row.Add(value);
            panel.Add(row);
            _values[key] = value;
            _rows[key] = row;
        }

        private void AddSupply(VisualElement row, string key, Sprite icon)
        {
            var t = Theme.CloseUp;
            var item = UiKit.Row();
            item.style.marginLeft = t.SupplyGap;
            item.style.display = DisplayStyle.Flex;
            var image = UiKit.Icon(icon, t.IconSize, Theme.Colours.Icon);
            image.style.marginRight = t.SupplyIconGap;
            item.Add(image);
            var value = UiKit.Body("", Theme.Colours.Text);
            item.Add(value);
            row.Add(item);
            _values[key] = value;
            _rows[key] = item;
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
            var l = Tight(UiKit.Body(text, Theme.Colours.TextDim));
            if (Theme.Text.TitlesBold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = Theme.CloseUp.HeadingGap;
            return l;
        }

        private static Label Text(string text, Color colour)
        {
            var l = Tight(UiKit.Body(text, colour));
            l.style.marginTop = Theme.CloseUp.LineGap;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static VisualElement IconLine(Sprite icon, string text)
        {
            var row = UiKit.Row();
            row.style.marginTop = Theme.CloseUp.LineGap;
            var image = UiKit.Icon(icon, Theme.CloseUp.IconSize, Theme.Colours.Icon);
            image.style.marginRight = Theme.CloseUp.IconGap;
            row.Add(image);
            var l = Tight(UiKit.Body(text, Theme.Colours.Text));
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            row.Add(l);
            return row;
        }

        private static Image AddFill(VisualElement parent, Sprite sprite, float inset)
        {
            var img = new Image { sprite = sprite, tintColor = Theme.Colours.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.position = Position.Absolute;
            img.style.left = inset; img.style.right = inset; img.style.top = inset; img.style.bottom = inset;
            parent.Add(img);
            return img;
        }
    }
}
