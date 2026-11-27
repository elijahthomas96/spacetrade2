using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Game.Art;
using SpaceTrade.Sim;
using SpaceTrade.Sim.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// The map's on-screen text (UI Toolkit): a name beside each star, the selected system's panel, messages, and the
    /// debug line. Names that would overlap are hidden, most important first, so a crowded map stays readable; the
    /// selected and hovered systems always keep their names.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public sealed class MapHud : MonoBehaviour
    {
        public const string PanelSettingsResource = "UI/GamePanelSettings";
        private const float LabelGapPixels = 16f;
        private const float LabelHeightPixels = 18f;

        private static readonly Color TextColour = new Color(0.86f, 0.9f, 0.96f, 1f);
        private static readonly Color DimText = new Color(0.62f, 0.68f, 0.76f, 1f);
        private static readonly Color PanelColour = new Color(0.05f, 0.07f, 0.11f, 0.88f);
        private static readonly Color BadgeTint = new Color(1f, 0.8f, 0.35f, 1f);
        private static readonly Color WarningColour = new Color(1f, 0.75f, 0.35f, 1f);

        private UIDocument _doc;
        private VisualElement _root, _labelLayer, _info, _messages;
        private Label _debugLine;
        private readonly List<(StarSystem System, Label Label)> _labels = new List<(StarSystem, Label)>();
        private readonly List<Label> _countLabels = new List<Label>();
        private readonly List<(int SystemId, string Text, Rect Rect)> _counts = new List<(int, string, Rect)>();
        private Camera _camera;
        private Simulation _sim;
        private int[] _priority = new int[0];
        private Label _shipStatus, _shipArrival, _shipProgress;
        private Button _followButton;
        private string _dockedKey;

        /// <summary>The map screen, for the ship panel's buttons and the docked-ship counts.</summary>
        public MapScreen Map { get; set; }

        public int SelectedSystemId { get; private set; } = -1;
        public int SelectedShipId { get; private set; } = -1;
        public int HoveredSystemId { get; set; } = -1;
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

        private readonly List<(int SystemId, Rect Rect)> _shown = new List<(int, Rect)>();

        /// <summary>The names shown in the last frame, with their rectangles in panel pixels.</summary>
        public IReadOnlyList<(int SystemId, Rect Rect)> ShownLabels => _shown;

        /// <summary>Each star's marker rectangle in panel pixels in the last frame, by system id.</summary>
        public IReadOnlyList<Rect> StarRects { get; private set; } = new Rect[0];

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

            _labelLayer = new VisualElement { name = "labels", pickingMode = PickingMode.Ignore };
            _labelLayer.style.position = Position.Absolute;
            _labelLayer.style.left = 0; _labelLayer.style.top = 0; _labelLayer.style.right = 0; _labelLayer.style.bottom = 0;
            _root.Add(_labelLayer);

            _info = new VisualElement { name = "system-info" };
            _info.style.position = Position.Absolute;
            _info.style.top = 16; _info.style.right = 16;
            _info.style.width = 260;
            _info.style.overflow = Overflow.Hidden;
            _info.style.backgroundColor = PanelColour;
            SetPadding(_info, 12);
            _info.style.display = DisplayStyle.None;
            _root.Add(_info);

            _messages = new VisualElement { name = "messages" };
            _messages.style.position = Position.Absolute;
            _messages.style.top = 16; _messages.style.left = 16;
            _messages.style.maxWidth = 640;
            _messages.style.backgroundColor = PanelColour;
            SetPadding(_messages, 10);
            _messages.style.display = DisplayStyle.None;
            _root.Add(_messages);

            _debugLine = new Label { name = "debug-line", pickingMode = PickingMode.Ignore };
            _debugLine.style.position = Position.Absolute;
            _debugLine.style.left = 12; _debugLine.style.bottom = 8;
            _debugLine.style.color = DimText;
            _debugLine.style.fontSize = 12;
            _root.Add(_debugLine);
        }

        public void Show(Simulation sim, Camera camera, IReadOnlyList<string> messages, string debugLine)
        {
            _sim = sim;
            _camera = camera;
            _labelLayer.Clear();
            _labels.Clear();
            foreach (var s in sim.Market.Galaxy.Systems)
            {
                var label = new Label(s.Name) { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.color = TextColour;
                label.style.fontSize = 13;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.height = LabelHeightPixels;
                SetPadding(label, 0);
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
                var l = new Label(m);
                l.style.color = WarningColour;
                l.style.whiteSpace = WhiteSpace.Normal;
                _messages.Add(l);
            }
            _messages.style.display = messages.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _debugLine.text = debugLine ?? "";
            ShowSystem(-1);
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
            var s = _sim.Market.Galaxy.Systems[systemId];
            var tuning = _sim.Market.Tuning;
            InfoTitle = s.Name;
            _info.Add(Title(s.Name));
            _info.Add(TextRow($"Population {s.PopulationMillions:N0} million", DimText));
            foreach (var id in s.IndustryIds)
            {
                string name = tuning.Industries.FirstOrDefault(i => i.Id == id)?.Name ?? id;
                _info.Add(IconRow(ArtLibrary.IndustryIcon(id), name, TextColour));
            }
            if (s.HasShipyard) _info.Add(IconRow(ArtLibrary.Get("Map/shipyard_badge"), "Shipyard", BadgeTint));
            var docked = DockedAt(systemId);
            _dockedKey = string.Join(",", docked.Select(d => d.Id));
            if (docked.Count > 0)
            {
                _info.Add(TextRow($"Docked here ({docked.Count})", DimText));
                foreach (var row in _info.Children()) row.style.flexShrink = 0;
                var list = new ScrollView(ScrollViewMode.Vertical) { name = "docked-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
                list.style.flexShrink = 1;
                list.style.minHeight = 40;
                foreach (var ship in docked)
                    list.Add(IconRow(ArtLibrary.ShipMapIcon(ship.ClassId), $"{ship.Name} · {ShipText.ClassName(_sim, ship)}", TextColour, false));
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
            InfoTitle = ship.Name;
            _info.Add(Title(ship.Name));
            _info.Add(IconRow(ArtLibrary.ShipMapIcon(ship.ClassId), ShipText.ClassName(_sim, ship), DimText, false));
            _shipStatus = TextRow("", TextColour);
            _shipArrival = TextRow("", DimText);
            _shipProgress = TextRow("", DimText);
            _info.Add(_shipStatus);
            _info.Add(_shipArrival);
            _info.Add(_shipProgress);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 10;
            _followButton = new Button(() => Map?.ToggleFollow()) { name = "follow" };
            _followButton.style.backgroundImage = new StyleBackground(ArtLibrary.Get("UI/follow"));
            _followButton.style.width = 30; _followButton.style.height = 30;
            buttons.Add(_followButton);
            var followText = new Label { name = "follow-text" };
            followText.style.color = TextColour;
            followText.style.alignSelf = Align.Center;
            followText.style.marginRight = 12;
            buttons.Add(followText);
            var closeUp = new Button(() => Map?.OpenCloseUp()) { name = "close-up", text = "Close-up" };
            buttons.Add(closeUp);
            _info.Add(buttons);
            _info.style.display = DisplayStyle.Flex;
            RefreshShipRows();
        }

        private void ClearShipRows()
        {
            _shipStatus = _shipArrival = _shipProgress = null;
            _followButton = null;
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
            SetText(_shipArrival, ShipText.Eta(_sim, p));
            SetText(_shipProgress, p.InTransit ? "Progress " + ShipText.Progress(_sim, p) : "Mouse wheel in at the closest zoom for the close-up");
            var followText = _followButton?.parent?.Q<Label>("follow-text");
            if (followText != null) SetText(followText, Map != null && Map.IsFollowing ? "Following (F)" : "Follow (F)");
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

        /// <summary>True when the screen point (pixels, origin bottom left) is over a panel that takes clicks.</summary>
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
            float tallest = _root.layout.height - 32f;
            if (!float.IsNaN(tallest) && tallest > 0f && _info.style.maxHeight.value.value != tallest) _info.style.maxHeight = tallest;
            if (SelectedShipId >= 0) RefreshShipRows();
            if (SelectedSystemId >= 0 && _dockedKey != string.Join(",", DockedAt(SelectedSystemId).Select(d => d.Id))) ShowSystem(SelectedSystemId);
            PlaceDockedCounts(panel);
            var placed = new List<Rect>();
            _shown.Clear();
            var order = new List<int>();
            if (SelectedSystemId >= 0) order.Add(SelectedSystemId);
            if (HoveredSystemId >= 0 && HoveredSystemId != SelectedSystemId) order.Add(HoveredSystemId);
            order.AddRange(_priority.Where(id => id != SelectedSystemId && id != HoveredSystemId));
            var panelSize = _root.layout.size;
            var panels = new[] { _info, _messages }
                .Where(e => e.style.display == DisplayStyle.Flex && e.layout.width > 0f)
                .Select(e => e.layout).ToList();
            var starAt = new Vector2[_labels.Count];
            var stars = new Rect[_labels.Count];
            float half = GalaxyMapView.StarPixels / 2f;
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
                if (float.IsNaN(width) || width <= 0f) width = system.Name.Length * 7.5f;
                var right = new Rect(p.x + LabelGapPixels, p.y - LabelHeightPixels / 2f, width, LabelHeightPixels);
                var left = new Rect(p.x - LabelGapPixels - width, right.y, width, LabelHeightPixels);
                bool forced = id == SelectedSystemId || id == HoveredSystemId;
                Rect? chosen = null;
                foreach (var candidate in new[] { right, left })
                {
                    if (candidate.xMin < 0 || candidate.yMin < 0 || candidate.xMax > panelSize.x || candidate.yMax > panelSize.y) continue;
                    var padded = Inflate(candidate, 3f);
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

        /// <summary>Puts a "+N" under each star with more docked ships than the map draws beside it.</summary>
        private void PlaceDockedCounts(IPanel panel)
        {
            _counts.Clear();
            var overflow = Map != null && Map.Ships != null ? Map.Ships.Overflow : null;
            int n = overflow?.Count ?? 0;
            float below = ShipMapView.OverflowBelowPixels;
            for (int i = 0; i < n; i++)
            {
                if (i == _countLabels.Count)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.color = BadgeTint;
                    l.style.fontSize = 12;
                    l.style.unityTextAlign = TextAnchor.MiddleCenter;
                    l.style.width = 40;
                    l.style.height = LabelHeightPixels;
                    SetPadding(l, 0);
                    _labelLayer.Add(l);
                    _countLabels.Add(l);
                }
                var (systemId, hidden) = overflow[i];
                var label = _countLabels[i];
                string text = "+" + hidden;
                SetText(label, text);
                Vector2 star = RuntimePanelUtils.CameraTransformWorldToPanel(panel, GalaxyMapView.WorldPosition(_sim.Market.Galaxy.Systems[systemId]), _camera);
                var rect = new Rect(star.x - 20f, star.y + below - LabelHeightPixels / 2f, 40f, LabelHeightPixels);
                label.style.left = rect.x;
                label.style.top = rect.y;
                label.style.visibility = Visibility.Visible;
                _counts.Add((systemId, text, rect));
            }
            for (int i = n; i < _countLabels.Count; i++) _countLabels[i].style.visibility = Visibility.Hidden;
        }

        private static Label Title(string text)
        {
            var title = new Label(text);
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = TextColour;
            return title;
        }

        private static Rect Inflate(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2 * by, r.height + 2 * by);

        private static Label TextRow(string text, Color colour)
        {
            var l = new Label(text);
            l.style.color = colour;
            l.style.marginTop = 4;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static VisualElement IconRow(Sprite icon, string text, Color tint, bool tintIcon = true)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6;
            var image = new Image { sprite = icon, tintColor = tintIcon ? tint : Color.white, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = 20;
            image.style.height = 20;
            image.style.marginRight = 8;
            row.Add(image);
            var l = new Label(text);
            l.style.color = TextColour;
            row.Add(l);
            return row;
        }

        private static void SetPadding(VisualElement e, float px)
        {
            e.style.paddingLeft = px; e.style.paddingRight = px; e.style.paddingTop = px; e.style.paddingBottom = px;
        }
    }
}
