using System;
using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The notification list under the bell (spec 9.4): the simulation's own notifications, newest first, each with the
    /// in-game date it was raised, an icon for its kind, and the simulation's text. Clicking one jumps to its ship. Only the
    /// rows on screen exist, and the list is rebuilt only when the simulation's notifications change, so a full list costs
    /// nothing per frame.
    /// </summary>
    public sealed class NotificationList
    {
        private readonly VisualElement _panel;
        private readonly ListView _list;
        private readonly Label _empty;
        private readonly List<Notification> _items = new List<Notification>();
        private readonly Func<Notification, bool> _onClick;
        private Simulation _sim;
        private Notification _newestShown;
        private int _countShown = -1;

        public bool IsOpen { get; private set; }
        public Rect Rect => _panel.layout;

        /// <summary>How many notifications the list holds, newest first.</summary>
        public int Count => _items.Count;

        /// <summary>The notifications in the order shown: newest first.</summary>
        public IReadOnlyList<Notification> Items => _items;

        /// <summary>How many times the rows were rebuilt from the simulation.</summary>
        public int Rebuilds { get; private set; }

        public VisualElement Panel => _panel;

        public NotificationList(VisualElement root, Func<Notification, bool> onClick)
        {
            _onClick = onClick;
            var t = Theme.Notifications;
            _panel = new VisualElement { name = "notification-list" };
            _panel.style.position = Position.Absolute;
            _panel.style.top = Theme.TopBar.Height;
            _panel.style.right = Theme.Panels.Margin;
            _panel.style.width = t.Width;
            UiKit.Panel(_panel, Theme.Colours.Dropdown, Theme.Panels.Padding);
            _panel.style.display = DisplayStyle.None;

            _panel.Add(UiKit.Title("Notifications", Theme.Text.Title));
            _empty = UiKit.Body("Nothing yet. Arrivals and blocked routes are listed here.", Theme.Colours.TextDim);
            _empty.style.marginTop = Theme.Panels.RowGap;
            _empty.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_empty);

            _list = new ListView(_items, t.RowHeight, MakeRow, BindRow)
            {
                name = "notification-rows",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _list.style.marginTop = Theme.Panels.RowGap;
            _list.style.height = t.MaxHeight;
            _list.style.flexGrow = 0;
            _panel.Add(_list);
            _list.RegisterCallback<GeometryChangedEvent>(_ => UiKit.ScrollBars(_list));
            root.Add(_panel);
        }

        private VisualElement MakeRow()
        {
            var t = Theme.Notifications;
            var row = UiKit.Row();
            row.AddToClassList("notification-row");
            row.style.height = t.RowHeight;
            row.style.paddingLeft = t.RowPadding; row.style.paddingRight = t.RowPadding;
            row.style.overflow = Overflow.Hidden;
            var icon = UiKit.Icon(null, t.IconSize, Theme.Colours.Icon);
            icon.name = "kind";
            icon.style.marginRight = t.IconGap;
            row.Add(icon);
            var date = UiKit.Label("", Theme.Colours.TextDim, Theme.Text.Small);
            date.name = "date";
            date.style.width = t.DateWidth;
            date.style.flexShrink = 0;
            row.Add(date);
            var text = UiKit.Label("", Theme.Colours.Text, Theme.Text.Small);
            text.name = "text";
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexShrink = 1;
            text.style.flexGrow = 1;
            row.Add(text);
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Theme.Colours.RowHover);
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = Color.clear);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is Notification n) Click(n);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var n = _items[index];
            row.userData = n;
            bool blocked = n.Kind == NotificationKind.RouteBlocked;
            var icon = row.Q<Image>("kind");
            icon.sprite = ArtLibrary.Get(blocked ? "UI/warning" : "UI/notification");
            icon.tintColor = blocked ? Theme.Colours.Warning : Theme.Colours.Icon;
            row.Q<Label>("date").text = n.Date.ToString();
            var text = row.Q<Label>("text");
            text.text = n.Text;
            text.style.color = blocked ? Theme.Colours.Warning : Theme.Colours.Text;
        }

        /// <summary>What clicking a row does: jumps to the notification's ship, wherever it is now.</summary>
        public bool Click(Notification n) => n != null && _onClick != null && _onClick(n);

        /// <summary>Clicks the row at this place in the list (0 is the newest).</summary>
        public bool ClickAt(int index) => index >= 0 && index < _items.Count && Click(_items[index]);

        public void Bind(Simulation sim)
        {
            _sim = sim;
            _newestShown = null;
            _countShown = -1;
            Refresh();
        }

        public void Open()
        {
            IsOpen = true;
            _panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            _panel.style.display = DisplayStyle.None;
        }

        /// <summary>Rebuilds the rows only when the simulation's notifications have changed. Called once a frame.</summary>
        public void Refresh()
        {
            if (_sim == null) return;
            var notes = _sim.Notifications;
            var newest = notes.Count > 0 ? notes[notes.Count - 1] : null;
            if (notes.Count == _countShown && ReferenceEquals(newest, _newestShown)) return;
            _countShown = notes.Count;
            _newestShown = newest;
            _items.Clear();
            for (int i = notes.Count - 1; i >= 0; i--) _items.Add(notes[i]);
            Rebuilds++;
            _empty.style.display = _items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.style.display = _items.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _list.RefreshItems();
        }

        /// <summary>The texts of the rows that exist right now (only those in view), top to bottom.</summary>
        public List<string> VisibleRowTexts()
        {
            var texts = new List<string>();
            _list.Query(className: "notification-row").ForEach(r =>
            {
                if (r.userData is Notification) texts.Add(r.Q<Label>("date").text + " " + r.Q<Label>("text").text);
            });
            return texts;
        }

        /// <summary>How many row elements exist. With a full list this stays near the number that fit on screen.</summary>
        public int RowElementCount => _list.Query(className: "notification-row").ToList().Count;
    }
}
