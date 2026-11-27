using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Map;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Timing;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The always-visible bar (spec 9.4): date, pause/play, the three speed buttons, credits, the notification bell with
    /// its list, and the settings button. It stays on top of every screen, the ship close-up included. Everything it shows
    /// is read from the simulation; the buttons send the clock commands through the game's runner.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(90)]
    public sealed class TopBar : MonoBehaviour
    {
        /// <summary>Where the bar sits among the screen's layers: above everything else.</summary>
        public const int SortingOrder = 20;

        private readonly ClockSpeed[] Speeds = { ClockSpeed.X1, ClockSpeed.X3, ClockSpeed.X5 };

        private UIDocument _doc;
        private VisualElement _root, _bar;
        private Label _date, _credits, _badge;
        private Button _pause, _bell, _gear;
        private readonly Button[] _speedButtons = new Button[3];
        private Simulation _sim;
        private GameDate _shownDate;
        private long _shownCredits = long.MinValue;
        private bool _shownPaused, _clockShown;
        private ClockSpeed _shownSpeed;
        private Notification _seenNewest, _countedNewest;
        private int _unseen = -1;
        private readonly List<Rect> _covered = new List<Rect>();

        public MapScreen Map { get; set; }
        public NotificationList Notifications { get; private set; }
        public SettingsPanel Settings { get; private set; }

        public string DateText => _date?.text;
        public string CreditsText => _credits?.text;

        /// <summary>How many notifications have arrived since the list was last open.</summary>
        public int UnseenCount => Mathf.Max(0, _unseen);

        /// <summary>The number shown on the bell, or "" when there is nothing new.</summary>
        public string BadgeText => _badge != null && _badge.style.display == DisplayStyle.Flex ? _badge.text : "";

        /// <summary>True when the pause/play button shows "play" (the clock is paused).</summary>
        public bool ShowsPaused => _shownPaused;

        /// <summary>The speed whose button is lit.</summary>
        public ClockSpeed LitSpeed => _shownSpeed;

        public bool IsSpeedLit(ClockSpeed speed) => UiKit.IsSelected(_speedButtons[System.Array.IndexOf(Speeds, speed)]);

        public Rect BarRect => _bar != null ? _bar.layout : default;

        /// <summary>The root every bar element sits under, for checks of the whole screen's look.</summary>
        public VisualElement Root => _root;

        /// <summary>The parts of the screen the bar and its open panels cover, in panel pixels. Star names keep clear of them.</summary>
        public IReadOnlyList<Rect> CoveredRects
        {
            get
            {
                _covered.Clear();
                if (_bar != null && _bar.layout.width > 0f) _covered.Add(_bar.layout);
                if (Notifications != null && Notifications.IsOpen) _covered.Add(Notifications.Rect);
                if (Settings != null && Settings.IsOpen) _covered.Add(Settings.Rect);
                return _covered;
            }
        }

        public static TopBar Create(Transform parent)
        {
            var go = new GameObject("Top bar");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>(MapHud.PanelSettingsResource);
            doc.sortingOrder = SortingOrder;
            var bar = go.AddComponent<TopBar>();
            bar._doc = doc;
            go.SetActive(true);
            return bar;
        }

        private void OnEnable()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            var t = Theme.TopBar;
            var colours = Theme.Colours;
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            UiKit.Fill(_root);
            UiKit.Root(_root);

            _bar = UiKit.Row();
            _bar.name = "top-bar";
            _bar.style.position = Position.Absolute;
            _bar.style.left = 0; _bar.style.right = 0; _bar.style.top = 0;
            _bar.style.height = t.Height;
            _bar.style.backgroundColor = colours.TopBar;
            _bar.style.paddingLeft = t.Padding; _bar.style.paddingRight = t.Padding;
            _root.Add(_bar);

            _date = UiKit.Body("", colours.Text);
            _date.name = "date";
            _date.style.width = t.DateWidth;
            _bar.Add(_date);

            _pause = UiKit.IconButton(ArtLibrary.Get("UI/pause"), t.ButtonSize, TogglePause, null);
            _pause.name = "pause-play";
            _pause.style.marginLeft = t.Gap;
            _bar.Add(_pause);

            for (int i = 0; i < Speeds.Length; i++)
            {
                var speed = Speeds[i];
                var b = UiKit.TextButton(ClockSpeeds.Multiplier(speed) + "x", () => ChooseSpeed(speed), null);
                b.name = "speed-" + ClockSpeeds.Multiplier(speed) + "x";
                b.style.width = t.SpeedButtonWidth;
                b.style.height = t.ButtonSize;
                UiKit.Margin(b, 0);
                UiKit.Padding(b, 0);
                b.style.marginLeft = t.IconGap;
                _speedButtons[i] = b;
                _bar.Add(b);
            }

            var creditsIcon = UiKit.Icon(ArtLibrary.Get("Icons/credits"), t.IconSize, colours.Icon);
            creditsIcon.style.marginLeft = t.Gap + t.Gap;
            creditsIcon.style.marginRight = t.IconGap;
            _bar.Add(creditsIcon);
            _credits = UiKit.Body("", colours.Text);
            _credits.name = "credits";
            _bar.Add(_credits);

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;
            _bar.Add(spacer);

            _bell = UiKit.IconButton(ArtLibrary.Get("UI/notification"), t.ButtonSize, ToggleNotifications, null);
            _bell.name = "notifications";
            _bar.Add(_bell);
            _badge = UiKit.Label("", colours.Highlight, Theme.Text.Small);
            _badge.name = "notification-count";
            _badge.style.marginLeft = t.IconGap;
            _badge.style.display = DisplayStyle.None;
            _bar.Add(_badge);

            _gear = UiKit.IconButton(ArtLibrary.Get("UI/settings"), t.ButtonSize, ToggleSettings, null);
            _gear.name = "settings";
            _gear.style.marginLeft = t.Gap;
            _bar.Add(_gear);

            Notifications = new NotificationList(_root, n => Map != null && Map.JumpToShip(n.ShipId));
            Settings = new SettingsPanel(_root);
            _clockShown = false;
            _shownCredits = long.MinValue;
            _unseen = -1;
        }

        private void OnDisable()
        {
            Settings?.Commit();
        }

        /// <summary>Starts showing a game. Notifications it already holds count as seen.</summary>
        public void Show(Simulation sim)
        {
            _sim = sim;
            var notes = sim.Notifications;
            _seenNewest = notes.Count > 0 ? notes[notes.Count - 1] : null;
            _countedNewest = null;
            _unseen = -1;
            _clockShown = false;
            _shownCredits = long.MinValue;
            Notifications.Close();
            Notifications.Bind(sim);
            Settings.Close();
            Settings.Bind(GameAudio.Current, Map != null ? Map.SettingsStore : null);
            Refresh();
        }

        /// <summary>Space, or the pause/play button. The pause or resume sound follows from the clock itself.</summary>
        public void TogglePause()
        {
            if (_sim == null) return;
            bool paused = _sim.Clock.IsPaused;
            var runner = Map != null ? Map.Runner : null;
            if (runner != null && runner.Driver != null)
            {
                if (paused) runner.Play(); else runner.Pause();
            }
            else if (paused) _sim.Play();
            else _sim.Pause();
            Refresh();
        }

        /// <summary>1, 2, 3, or a speed button. Choosing the speed already selected only clicks.</summary>
        public void ChooseSpeed(ClockSpeed speed)
        {
            if (_sim == null) return;
            if (_sim.Clock.Speed == speed)
            {
                GameAudio.Play(SoundId.Click, "speed already selected");
                return;
            }
            var runner = Map != null ? Map.Runner : null;
            if (runner != null && runner.Driver != null) runner.SetSpeed(speed);
            else _sim.SetSpeed(speed);
            Refresh();
        }

        public void ToggleNotifications()
        {
            if (Notifications.IsOpen) { CloseNotifications(); return; }
            if (Settings.IsOpen) Settings.Close();
            Notifications.Open();
            MarkAllSeen();
            GameAudio.Play(SoundId.PanelOpen, "notification list opened");
        }

        private void CloseNotifications()
        {
            MarkAllSeen();
            Notifications.Close();
            GameAudio.Play(SoundId.PanelClose, "notification list closed");
        }

        public void ToggleSettings()
        {
            if (Settings.IsOpen)
            {
                Settings.Close();
                GameAudio.Play(SoundId.PanelClose, "settings closed");
                return;
            }
            if (Notifications.IsOpen) { MarkAllSeen(); Notifications.Close(); }
            Settings.Open();
            GameAudio.Play(SoundId.PanelOpen, "settings opened");
        }

        /// <summary>Esc: closes the open list or settings panel. Returns true when one was open.</summary>
        public bool CloseDropdowns()
        {
            if (Notifications.IsOpen) { CloseNotifications(); return true; }
            if (Settings.IsOpen) { ToggleSettings(); return true; }
            return false;
        }

        /// <summary>True when the screen point (pixels, origin bottom left) is over the bar or one of its open panels.</summary>
        public bool IsOver(Vector2 screenPoint)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            foreach (var r in CoveredRects) if (r.Contains(p)) return true;
            return false;
        }

        private void MarkAllSeen()
        {
            if (_sim == null) return;
            var notes = _sim.Notifications;
            _seenNewest = notes.Count > 0 ? notes[notes.Count - 1] : null;
            _unseen = -1;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (_sim == null || _date == null) return;
            var date = _sim.CurrentDate;
            if (!_clockShown || !date.Equals(_shownDate))
            {
                _shownDate = date;
                _date.text = date.ToString();
            }
            long credits = _sim.Credits;
            if (credits != _shownCredits)
            {
                _shownCredits = credits;
                _credits.text = Format.Credits(credits);
            }
            bool paused = _sim.Clock.IsPaused;
            var speed = _sim.Clock.Speed;
            if (!_clockShown || paused != _shownPaused)
            {
                _shownPaused = paused;
                _pause.style.backgroundImage = new StyleBackground(ArtLibrary.Get(paused ? "UI/play" : "UI/pause"));
            }
            if (!_clockShown || speed != _shownSpeed)
            {
                _shownSpeed = speed;
                for (int i = 0; i < Speeds.Length; i++) UiKit.SetSelected(_speedButtons[i], Speeds[i] == speed);
            }
            _clockShown = true;

            Notifications.Refresh();
            var notes = _sim.Notifications;
            var newest = notes.Count > 0 ? notes[notes.Count - 1] : null;
            if (Notifications.IsOpen)
            {
                _seenNewest = newest;
                if (_unseen != 0) SetUnseen(0);
            }
            else if (_unseen < 0 || !ReferenceEquals(newest, _countedNewest))
            {
                int n = 0;
                for (int i = notes.Count - 1; i >= 0 && !ReferenceEquals(notes[i], _seenNewest); i--) n++;
                SetUnseen(n);
            }
            _countedNewest = newest;
        }

        private void SetUnseen(int n)
        {
            _unseen = n;
            int most = Theme.Notifications.BadgeMaxCount;
            _badge.text = n > most ? most + "+" : n.ToString();
            _badge.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
