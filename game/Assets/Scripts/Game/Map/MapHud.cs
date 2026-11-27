using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using SpaceTrade.Sim.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// The map's on-screen text (UI Toolkit): a name beside each star, the name of a moving ship that is selected or under
    /// the mouse, the selected system's or ship's panel, messages, and the debug line. Names that would overlap are hidden,
    /// most important first, so a crowded map stays readable; the selected and hovered systems always keep their names.
    /// Every colour, size, and spacing comes from the theme.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public sealed class MapHud : MonoBehaviour
    {
        public const string PanelSettingsResource = "UI/GamePanelSettings";

        private UIDocument _doc;
        private VisualElement _root, _labelLayer, _info, _messages;
        private Label _debugLine;
        private readonly List<(StarSystem System, Label Label)> _labels = new List<(StarSystem, Label)>();
        private readonly List<Label> _countLabels = new List<Label>();
        private readonly List<(int SystemId, string Text, Rect Rect)> _counts = new List<(int, string, Rect)>();
        private readonly List<Label> _shipNameLabels = new List<Label>();
        private readonly List<(int ShipId, string Text, Rect Rect)> _shipNames = new List<(int, string, Rect)>();
        private readonly List<int> _namedShips = new List<int>();
        private Camera _camera;
        private Simulation _sim;
        private int[] _priority = new int[0];
        private Label _shipStatus, _shipArrival, _shipProgress, _followText;
        private string _dockedKey;

        /// <summary>The map screen, for the ship panel's buttons and the docked-ship counts.</summary>
        public MapScreen Map { get; set; }

        public int SelectedSystemId { get; private set; } = -1;
        public int SelectedShipId { get; private set; } = -1;
        public int HoveredSystemId { get; set; } = -1;

        /// <summary>The ship under the mouse, or -1. A moving ship under the mouse shows its name.</summary>
        public int HoveredShipId { get; set; } = -1;

        public int LabelCount => _labels.Count;
        public int VisibleLabelCount { get; private set; }
        public bool InfoPanelVisible => _info != null && _info.style.display == DisplayStyle.Flex;
        public Rect InfoPanelRect => _info != null ? _info.layout : default;
        public Vector2 PanelSize => _root != null ? _root.layout.size : Vector2.zero;
        public string InfoTitle { get; private set; }

        /// <summary>Every line of text in the info panel, top to bottom.</summary>
        public IReadOnlyList<string> InfoLines => _info == null ? new string[0] : _info.Query<Label>().ToList().Where(l => l.style.display != DisplayStyle.None).Select(l => l.text).ToArray();

        /// <summary>The "+N" counts for docked ships not drawn, shown in the last frame, with their rectangles in panel pixels.</summary>
        public IReadOnlyList<(int SystemId, string Text, Rect Rect)> DockedCounts => _counts;

        /// <summary>The ship names shown on the map in the last frame, with their rectangles in panel pixels.</summary>
        public IReadOnlyList<(int ShipId, string Text, Rect Rect)> ShipNames => _shipNames;

        private readonly List<(int SystemId, Rect Rect)> _shown = new List<(int, Rect)>();

        /// <summary>The names shown in the last frame, with their rectangles in panel pixels.</summary>
        public IReadOnlyList<(int SystemId, Rect Rect)> ShownLabels => _shown;

        /// <summary>Each star's marker rectangle in panel pixels in the last frame, by system id.</summary>
        public IReadOnlyList<Rect> StarRects { get; private set; } = new Rect[0];

        /// <summary>The root every map text element sits under, for checks of the whole screen's look.</summary>
        public VisualElement Root => _root;

        public static MapHud Create(Transform parent)
        {
            var go = new GameObject("Map HUD");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);
            var hud = go.AddComponent<MapHud>();
            hud._doc = doc;
            go.SetActive(true);
            return hud;
        }

        private void OnEnable()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;
            UiKit.Root(_root);
            var panels = Theme.Panels;
            float top = Theme.TopBar.Height + panels.Margin;

            _labelLayer = new VisualElement { name = "labels", pickingMode = PickingMode.Ignore };
            UiKit.Fill(_labelLayer);
            _root.Add(_labelLayer);

            _info = new VisualElement { name = "system-info" };
            _info.style.position = Position.Absolute;
            _info.style.top = top; _info.style.right = panels.Margin;
            _info.style.width = panels.InfoWidth;
            _info.style.overflow = Overflow.Hidden;
            UiKit.Panel(_info, Theme.Colours.Panel, panels.Padding);
            _info.style.display = DisplayStyle.None;
            _root.Add(_info);

            _messages = new VisualElement { name = "messages" };
            _messages.style.position = Position.Absolute;
            _messages.style.top = top; _messages.style.left = panels.Margin;
            _messages.style.maxWidth = panels.MessagesWidth;
            UiKit.Panel(_messages, Theme.Colours.Panel, panels.MessagesPadding);
            _messages.style.display = DisplayStyle.None;
            _root.Add(_messages);

            _debugLine = UiKit.Label("", Theme.Colours.TextDim, Theme.Text.Small);
            _debugLine.name = "debug-line";
            _debugLine.pickingMode = PickingMode.Ignore;
            _debugLine.style.position = Position.Absolute;
            _debugLine.style.left = panels.DebugLineLeft; _debugLine.style.bottom = panels.DebugLineBottom;
            _root.Add(_debugLine);
        }

        public void Show(Simulation sim, Camera camera, IReadOnlyList<string> messages, string debugLine)
        {
            _sim = sim;
            _camera = camera;
            _labelLayer.Clear();
            _labels.Clear();
            _countLabels.Clear();
            _shipNameLabels.Clear();
            _shipNames.Clear();
            HoveredShipId = -1;
            foreach (var s in sim.Market.Galaxy.Systems)
            {
                var label = MapLabel(s.Name, Theme.Colours.Text, Theme.Text.MapLabel, TextAnchor.MiddleLeft);
                _labelLayer.Add(label);
                _labels.Add((s, label));
            }
            _priority = sim.Market.Galaxy.Systems
                .OrderByDescending(s => s.HasShipyard)
                .ThenByDescending(s => s.PopulationMillions)
                .ThenBy(s => s.Id)
                .Select(s => s.Id).ToArray();

            _messages.Clear();
            foreach (var m in messages)
            {
                var l = UiKit.Body(m, Theme.Colours.Warning);
                l.style.whiteSpace = WhiteSpace.Normal;
                _messages.Add(l);
            }
            _messages.style.display = messages.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _debugLine.text = debugLine ?? "";
            ShowSystem(-1);
        }

        private static Label MapLabel(string text, Color colour, float size, TextAnchor align)
        {
            var label = UiKit.Label(text, colour, size);
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.unityTextAlign = align;
            label.style.height = Theme.Labels.Height;
            UiKit.Padding(label, 0);
            return label;
        }

        public void ShowSystem(int systemId)
        {
            SelectedSystemId = systemId;
            SelectedShipId = -1;
            ClearShipRows();
            _info.Clear();
            InfoTitle = null;
            _dockedKey = null;
            if (systemId < 0 || _sim == null)
            {
                _info.style.display = DisplayStyle.None;
                return;
            }
            var colours = Theme.Colours;
            var s = _sim.Market.Galaxy.Systems[systemId];
            var tuning = _sim.Market.Tuning;
            InfoTitle = s.Name;
            _info.Add(UiKit.Title(s.Name, Theme.Text.Title));
            _info.Add(TextRow($"Population {s.PopulationMillions:N0} million", colours.TextDim));
            foreach (var id in s.IndustryIds)
            {
                string name = tuning.Industries.FirstOrDefault(i => i.Id == id)?.Name ?? id;
                _info.Add(IconRow(ArtLibrary.IndustryIcon(id), name, colours.Text));
            }
            if (s.HasShipyard) _info.Add(IconRow(ArtLibrary.Get("Map/shipyard_badge"), "Shipyard", colours.ShipyardBadge));
            var docked = DockedAt(systemId);
            _dockedKey = string.Join(",", docked.Select(d => d.Id));
            if (docked.Count > 0)
            {
                _info.Add(TextRow($"Docked here ({docked.Count})", colours.TextDim));
                foreach (var row in _info.Children()) row.style.flexShrink = 0;
                var list = new ScrollView(ScrollViewMode.Vertical) { name = "docked-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
                list.style.flexShrink = 1;
                list.style.minHeight = Theme.Panels.ListMinHeight;
                UiKit.ScrollBars(list);
                foreach (var ship in docked)
                    list.Add(IconRow(ArtLibrary.ShipMapIcon(ship.ClassId), $"{ship.Name} · {ShipText.ClassName(_sim, ship)}", colours.Icon));
                _info.Add(list);
            }
            _info.style.display = DisplayStyle.Flex;
        }

        /// <summary>Shows the selected ship's panel: name, class, where it is, and the Follow and Close-up buttons.</summary>
        public void ShowShip(int shipId)
        {
            SelectedSystemId = -1;
            SelectedShipId = shipId;
            _info.Clear();
            InfoTitle = null;
            _dockedKey = null;
            ClearShipRows();
            var ship = _sim?.FindShip(shipId);
            if (ship == null)
            {
                SelectedShipId = -1;
                _info.style.display = DisplayStyle.None;
                return;
            }
            var colours = Theme.Colours;
            var panels = Theme.Panels;
            InfoTitle = ship.Name;
            _info.Add(UiKit.Title(ship.Name, Theme.Text.Title));
            _info.Add(IconRow(ArtLibrary.ShipMapIcon(ship.ClassId), ShipText.ClassName(_sim, ship), colours.Icon));
            _shipStatus = TextRow("", colours.Text);
            _shipArrival = TextRow("", colours.TextDim);
            _shipArrival.name = "ship-eta";
            _shipProgress = TextRow("", colours.TextDim);
            _info.Add(_shipStatus);
            _info.Add(_shipArrival);
            _info.Add(_shipProgress);
            var buttons = UiKit.Row();
            buttons.style.marginTop = panels.ButtonRowGap;
            var follow = UiKit.IconButton(ArtLibrary.Get("UI/follow"), panels.IconButtonSize, () => Map?.ToggleFollow(), null);
            follow.name = "follow";
            buttons.Add(follow);
            _followText = UiKit.Body("", colours.Text);
            _followText.name = "follow-text";
            _followText.style.marginRight = panels.InlineGap;
            buttons.Add(_followText);
            var closeUp = UiKit.TextButton("Close-up", () => Map?.OpenCloseUp(), null);
            closeUp.name = "close-up";
            buttons.Add(closeUp);
            _info.Add(buttons);
            _info.style.display = DisplayStyle.Flex;
            RefreshShipRows();
        }

        private void ClearShipRows()
        {
            _shipStatus = _shipArrival = _shipProgress = _followText = null;
        }

        private void RefreshShipRows()
        {
            if (_shipStatus == null || _sim == null) return;
            var ship = _sim.FindShip(SelectedShipId);
            if (ship == null) return;
            var p = _sim.PositionOf(ship.Id);
            SetText(_shipStatus, ShipText.Status(_sim, ship, p));
            var arrival = p.InTransit ? DisplayStyle.Flex : DisplayStyle.None;
            if (_shipArrival.style.display != arrival) _shipArrival.style.display = arrival;
            SetText(_shipArrival, ShipText.Eta(_sim, ship.Id));
            SetText(_shipProgress, p.InTransit ? "Progress " + ShipText.Progress(_sim, p) : "Mouse wheel in at the closest zoom for the close-up");
            if (_followText != null) SetText(_followText, Map != null && Map.IsFollowing ? "Following (F)" : "Follow (F)");
        }

        private static void SetText(Label l, string text)
        {
            if (l.text != text) l.text = text;
        }

        private List<SpaceTrade.Sim.Ships.Ship> DockedAt(int systemId)
        {
            var list = new List<SpaceTrade.Sim.Ships.Ship>();
            foreach (var ship in _sim.Ships) if (ship.DockedSystemId == systemId) list.Add(ship);
            return list;
        }

        /// <summary>True when the screen point (pixels, origin bottom left) is over a panel that takes clicks, on any layer of the screen.</summary>
        public bool IsOverPanel(Vector2 screenPoint)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            var picked = panel.Pick(p);
            return picked != null && picked != _root;
        }

        private void LateUpdate()
        {
            if (_camera == null || _labels.Count == 0 || _labelLayer.panel == null) return;
            var panel = _labelLayer.panel;
            var labels = Theme.Labels;
            float gap = labels.Gap, height = labels.Height;
            float tallest = _root.layout.height - Theme.TopBar.Height - 2f * Theme.Panels.Margin;
            if (!float.IsNaN(tallest) && tallest > 0f && _info.style.maxHeight.value.value != tallest) _info.style.maxHeight = tallest;
            if (SelectedShipId >= 0) RefreshShipRows();
            if (SelectedSystemId >= 0 && _dockedKey != string.Join(",", DockedAt(SelectedSystemId).Select(d => d.Id))) ShowSystem(SelectedSystemId);
            PlaceDockedCounts(panel);
            var placed = new List<Rect>();
            PlaceShipNames(panel, placed);
            _shown.Clear();
            var order = new List<int>();
            if (SelectedSystemId >= 0) order.Add(SelectedSystemId);
            if (HoveredSystemId >= 0 && HoveredSystemId != SelectedSystemId) order.Add(HoveredSystemId);
            order.AddRange(_priority.Where(id => id != SelectedSystemId && id != HoveredSystemId));
            var panelSize = _root.layout.size;
            var panels = new[] { _info, _messages }
                .Where(e => e.style.display == DisplayStyle.Flex && e.layout.width > 0f)
                .Select(e => e.layout).ToList();
            if (Map != null && Map.Bar != null) panels.AddRange(Map.Bar.CoveredRects);
            var starAt = new Vector2[_labels.Count];
            var stars = new Rect[_labels.Count];
            float half = Theme.Map.StarPixels / 2f;
            for (int i = 0; i < _labels.Count; i++)
            {
                starAt[i] = RuntimePanelUtils.CameraTransformWorldToPanel(panel, GalaxyMapView.WorldPosition(_labels[i].System), _camera);
                stars[i] = new Rect(starAt[i].x - half, starAt[i].y - half, 2f * half, 2f * half);
            }
            int visible = 0;
            foreach (int id in order)
            {
                var (system, label) = _labels[id];
                Vector2 p = starAt[id];
                float width = label.resolvedStyle.width;
                if (float.IsNaN(width) || width <= 0f) width = system.Name.Length * labels.FallbackLetterWidth;
                var right = new Rect(p.x + gap, p.y - height / 2f, width, height);
                var left = new Rect(p.x - gap - width, right.y, width, height);
                bool forced = id == SelectedSystemId || id == HoveredSystemId;
                Rect? chosen = null;
                foreach (var candidate in new[] { right, left })
                {
                    if (candidate.xMin < 0 || candidate.yMin < 0 || candidate.xMax > panelSize.x || candidate.yMax > panelSize.y) continue;
                    var padded = Inflate(candidate, labels.Clearance);
                    bool blocked = placed.Any(r => r.Overlaps(padded)) || panels.Any(r => r.Overlaps(padded));
                    for (int i = 0; i < stars.Length && !blocked; i++) blocked = i != id && stars[i].Overlaps(padded);
                    if (!blocked) { chosen = candidate; break; }
                }
                if (chosen == null && forced) chosen = right;
                bool show = chosen.HasValue;
                if (chosen.HasValue)
                {
                    label.style.left = chosen.Value.x;
                    label.style.top = chosen.Value.y;
                }
                label.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                if (show) { placed.Add(chosen.Value); visible++; }
                if (show) _shown.Add((id, chosen.Value));
            }
            VisibleLabelCount = visible;
            StarRects = stars;
        }

        /// <summary>
        /// Draws the names of moving ships by the theme's rule: never, only the selected ship and the one under the mouse,
        /// or all of them. Star names then keep clear of them.
        /// </summary>
        private void PlaceShipNames(IPanel panel, List<Rect> placed)
        {
            _shipNames.Clear();
            _namedShips.Clear();
            var labels = Theme.Labels;
            var ships = Map != null ? Map.Ships : null;
            if (ships != null && _sim != null && labels.MovingShipNames != ShipNameRule.Never)
            {
                if (labels.MovingShipNames == ShipNameRule.Always)
                {
                    foreach (var ship in _sim.Ships) if (ships.IsInTransit(ship.Id)) _namedShips.Add(ship.Id);
                }
                else
                {
                    if (SelectedShipId >= 0 && ships.IsInTransit(SelectedShipId)) _namedShips.Add(SelectedShipId);
                    if (HoveredShipId >= 0 && HoveredShipId != SelectedShipId && ships.IsInTransit(HoveredShipId)) _namedShips.Add(HoveredShipId);
                }
            }
            for (int i = 0; i < _namedShips.Count; i++)
            {
                if (i == _shipNameLabels.Count)
                {
                    var l = MapLabel("", Theme.Colours.Text, Theme.Text.MapLabel, TextAnchor.MiddleLeft);
                    l.name = "ship-name";
                    _labelLayer.Add(l);
                    _shipNameLabels.Add(l);
                }
                int id = _namedShips[i];
                var label = _shipNameLabels[i];
                string text = _sim.FindShip(id).Name;
                SetText(label, text);
                if (!ships.TryGetDrawn(id, out var world)) { label.style.visibility = Visibility.Hidden; continue; }
                Vector2 at = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);
                float width = label.resolvedStyle.width;
                if (float.IsNaN(width) || width <= 0f) width = text.Length * labels.FallbackLetterWidth;
                var rect = new Rect(at.x + labels.ShipNameGap, at.y - labels.Height / 2f, width, labels.Height);
                label.style.left = rect.x;
                label.style.top = rect.y;
                label.style.visibility = Visibility.Visible;
                _shipNames.Add((id, text, rect));
                placed.Add(rect);
            }
            for (int i = _namedShips.Count; i < _shipNameLabels.Count; i++) _shipNameLabels[i].style.visibility = Visibility.Hidden;
        }

        /// <summary>Puts a "+N" under each star with more docked ships than the map draws beside it.</summary>
        private void PlaceDockedCounts(IPanel panel)
        {
            _counts.Clear();
            var overflow = Map != null && Map.Ships != null ? Map.Ships.Overflow : null;
            int n = overflow?.Count ?? 0;
            var labels = Theme.Labels;
            float below = Theme.Ships.OverflowBelowPixels;
            for (int i = 0; i < n; i++)
            {
                if (i == _countLabels.Count)
                {
                    var l = MapLabel("", Theme.Colours.Highlight, Theme.Text.Small, TextAnchor.MiddleCenter);
                    l.style.width = labels.CountWidth;
                    _labelLayer.Add(l);
                    _countLabels.Add(l);
                }
                var (systemId, hidden) = overflow[i];
                var label = _countLabels[i];
                string text = "+" + hidden;
                SetText(label, text);
                Vector2 star = RuntimePanelUtils.CameraTransformWorldToPanel(panel, GalaxyMapView.WorldPosition(_sim.Market.Galaxy.Systems[systemId]), _camera);
                var rect = new Rect(star.x - labels.CountWidth / 2f, star.y + below - labels.Height / 2f, labels.CountWidth, labels.Height);
                label.style.left = rect.x;
                label.style.top = rect.y;
                label.style.visibility = Visibility.Visible;
                _counts.Add((systemId, text, rect));
            }
            for (int i = n; i < _countLabels.Count; i++) _countLabels[i].style.visibility = Visibility.Hidden;
        }

        private static Rect Inflate(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2 * by, r.height + 2 * by);

        private static Label TextRow(string text, Color colour)
        {
            var l = UiKit.Body(text, colour);
            l.style.marginTop = Theme.Panels.RowGap;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static VisualElement IconRow(Sprite icon, string text, Color tint)
        {
            var row = UiKit.Row();
            row.style.marginTop = Theme.Panels.IconRowGap;
            var image = UiKit.Icon(icon, Theme.Panels.IconSize, tint);
            image.style.marginRight = Theme.Panels.IconGap;
            row.Add(image);
            row.Add(UiKit.Body(text, Theme.Colours.Text));
            return row;
        }
    }
}
