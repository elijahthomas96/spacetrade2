using System;
using System.Collections.Generic;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>The orders a job list can be put in.</summary>
    public enum JobSort
    {
        Nearest,
        MostProfitable,
        Quickest,
        HighestRate,
    }

    /// <summary>
    /// One row of a job list. Every figure in it is the simulation's own; the words are only put around them.
    /// </summary>
    public sealed class JobRow
    {
        public string Title;
        public string Detail;
        public string Figure;

        /// <summary>The figure on the right is a loss or a "cannot fly": drawn in the loss colour.</summary>
        public bool FigureIsBad;

        public bool CanFly = true;
        public int DestinationId;

        /// <summary>The commodity id, or null for passengers or a bare destination.</summary>
        public string CommodityId;
        public bool IsPassengers;
        public long DistanceMilliLy, NetProfit, TransitTicks, Rate;

        /// <summary>The job board entry this row shows, when it comes from a ship's job board.</summary>
        public JobBoardEntry Entry;

        /// <summary>Where the row came in the simulation's own order; equal rows keep that order.</summary>
        public int Order;
    }

    /// <summary>
    /// A sortable, filterable list of jobs (spec 9.2), used by the system view and the voyage planner. It only orders and
    /// hides rows; it works out no figure. Only the rows on screen exist, so a long list costs no more than a short one.
    /// </summary>
    public sealed class JobListView
    {
        private readonly VisualElement _root;
        private readonly ListView _list;
        private readonly Label _empty;
        private readonly List<JobRow> _all = new List<JobRow>();
        private readonly List<JobRow> _shown = new List<JobRow>();
        private readonly Dictionary<JobSort, Button> _sortButtons = new Dictionary<JobSort, Button>();
        private readonly Button _filter;
        private readonly Action<JobRow> _onClick;
        private string _filterCaption = "";

        public VisualElement Root => _root;
        public JobSort Sort { get; private set; }
        public bool HideUnflyable { get; private set; }

        /// <summary>The rows in the order shown, after sorting and filtering.</summary>
        public IReadOnlyList<JobRow> Shown => _shown;

        /// <summary>Every row given to the list, before filtering.</summary>
        public IReadOnlyList<JobRow> All => _all;

        /// <summary>The row drawn as chosen, or null.</summary>
        public Func<JobRow, bool> IsSelected { get; set; }

        /// <summary>How many row elements exist. With a long list this stays near the number that fit on screen.</summary>
        public int RowElementCount => _list.Query(className: "job-row").ToList().Count;

        public JobListView(string name, JobSort[] sorts, bool withFilter, Action<JobRow> onClick)
        {
            _onClick = onClick;
            var t = Theme.JobList;
            _root = new VisualElement { name = name };
            _root.style.flexShrink = 0;

            var controls = UiKit.Row();
            controls.name = name + "-controls";
            controls.style.flexWrap = Wrap.Wrap;
            foreach (var sort in sorts)
            {
                var chosen = sort;
                var b = UiKit.TextButton(Caption(sort), () => SetSort(chosen));
                b.name = name + "-sort-" + sort;
                UiKit.Margin(b, 0);
                b.style.marginRight = t.ControlGap;
                b.style.marginTop = t.ControlGap;
                _sortButtons[sort] = b;
                controls.Add(b);
            }
            if (withFilter)
            {
                _filter = UiKit.TextButton("", () => SetHideUnflyable(!HideUnflyable));
                _filter.name = name + "-hide-unflyable";
                UiKit.Margin(_filter, 0);
                _filter.style.marginTop = t.ControlGap;
                controls.Add(_filter);
            }
            _root.Add(controls);

            _empty = UiKit.Body("", Theme.Colours.TextDim);
            _empty.name = name + "-empty";
            _empty.style.marginTop = Theme.Panels.RowGap;
            _empty.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_empty);

            _list = new ListView(_shown, t.RowHeight, MakeRow, BindRow)
            {
                name = name + "-rows",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _list.style.marginTop = Theme.Panels.RowGap;
            _list.style.height = t.Height;
            _list.style.flexGrow = 0;
            _list.style.flexShrink = 0;
            _list.RegisterCallback<GeometryChangedEvent>(_ => UiKit.ScrollBars(_list));
            _root.Add(_list);

            Sort = sorts.Length > 0 ? sorts[0] : JobSort.Nearest;
            ShowControls();
        }

        private static string Caption(JobSort sort)
        {
            switch (sort)
            {
                case JobSort.Nearest: return "Nearest";
                case JobSort.MostProfitable: return "Most profitable";
                case JobSort.Quickest: return "Quickest";
                default: return "Highest rate";
            }
        }

        /// <summary>The words on the filter button, for example "Hide jobs Tern cannot fly".</summary>
        public void SetFilterCaption(string caption)
        {
            _filterCaption = caption ?? "";
            ShowControls();
        }

        /// <summary>What the list says when it has no rows.</summary>
        public void SetEmptyText(string text) => _empty.text = text ?? "";

        public void SetSort(JobSort sort)
        {
            if (!_sortButtons.ContainsKey(sort)) return;
            Sort = sort;
            ShowControls();
            Apply();
        }

        public void SetHideUnflyable(bool hide)
        {
            HideUnflyable = hide;
            ShowControls();
            Apply();
        }

        private void ShowControls()
        {
            foreach (var pair in _sortButtons) UiKit.SetSelected(pair.Value, pair.Key == Sort);
            if (_filter != null)
            {
                _filter.text = _filterCaption;
                UiKit.SetSelected(_filter, HideUnflyable);
            }
        }

        /// <summary>Gives the list its rows, in the simulation's own order.</summary>
        public void SetRows(List<JobRow> rows)
        {
            _all.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Order = i;
                _all.Add(rows[i]);
            }
            Apply();
        }

        private void Apply()
        {
            _shown.Clear();
            foreach (var row in _all)
                if (!HideUnflyable || row.CanFly) _shown.Add(row);
            _shown.Sort(Compare);
            _empty.style.display = _shown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.style.display = _shown.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _list.RefreshItems();
        }

        /// <summary>Redraws the rows on screen, for example after the chosen row changes.</summary>
        public void Redraw() => _list.RefreshItems();

        private int Compare(JobRow a, JobRow b)
        {
            int c = 0;
            switch (Sort)
            {
                case JobSort.Nearest: c = a.DistanceMilliLy.CompareTo(b.DistanceMilliLy); break;
                case JobSort.Quickest: c = a.TransitTicks.CompareTo(b.TransitTicks); break;
                case JobSort.HighestRate: c = b.Rate.CompareTo(a.Rate); break;
                case JobSort.MostProfitable:
                    // Jobs the ship cannot fly have no profit to compare: they go last.
                    c = b.CanFly.CompareTo(a.CanFly);
                    if (c == 0) c = b.NetProfit.CompareTo(a.NetProfit);
                    break;
            }
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        }

        private VisualElement MakeRow()
        {
            var t = Theme.JobList;
            var row = UiKit.Row();
            row.AddToClassList("job-row");
            row.style.height = t.RowHeight;
            row.style.paddingLeft = t.RowPadding; row.style.paddingRight = t.RowPadding;
            row.style.overflow = Overflow.Hidden;
            var words = new VisualElement { name = "words", pickingMode = PickingMode.Ignore };
            words.style.flexGrow = 1;
            words.style.flexShrink = 1;
            words.style.overflow = Overflow.Hidden;
            var title = UiKit.Body("", Theme.Colours.Text);
            title.name = "title";
            title.pickingMode = PickingMode.Ignore;
            UiKit.Margin(title, 0);
            UiKit.Padding(title, 0);
            words.Add(title);
            var detail = UiKit.Label("", Theme.Colours.TextDim, Theme.Text.Small);
            detail.name = "detail";
            detail.pickingMode = PickingMode.Ignore;
            UiKit.Margin(detail, 0);
            UiKit.Padding(detail, 0);
            words.Add(detail);
            row.Add(words);
            var figure = UiKit.Body("", Theme.Colours.Text);
            figure.name = "figure";
            figure.pickingMode = PickingMode.Ignore;
            figure.style.width = t.FigureWidth;
            figure.style.flexShrink = 0;
            figure.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(figure);
            bool over = false;
            row.RegisterCallback<PointerEnterEvent>(_ => { over = true; Paint(row, true); });
            row.RegisterCallback<PointerLeaveEvent>(_ => { over = false; Paint(row, false); });
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is JobRow r && _onClick != null)
                {
                    GameAudio.Play(SoundId.Select, "job list: " + r.Title);
                    Click(r);
                }
                Paint(row, over);
            });
            return row;
        }

        private void Paint(VisualElement row, bool over)
        {
            bool selected = row.userData is JobRow r && IsSelected != null && IsSelected(r);
            row.style.backgroundColor = selected ? Theme.Colours.RowSelected : over ? Theme.Colours.RowHover : Color.clear;
        }

        private void BindRow(VisualElement row, int index)
        {
            var r = _shown[index];
            row.userData = r;
            row.Q<Label>("title").text = r.Title;
            var detail = row.Q<Label>("detail");
            detail.text = r.Detail;
            detail.style.color = r.CanFly ? Theme.Colours.TextDim : Theme.Colours.Warning;
            var figure = row.Q<Label>("figure");
            figure.text = r.Figure ?? "";
            figure.style.color = r.FigureIsBad ? Theme.Colours.Loss : Theme.Colours.Text;
            Paint(row, false);
        }

        /// <summary>What clicking a row does.</summary>
        public void Click(JobRow row)
        {
            if (row == null) return;
            _onClick?.Invoke(row);
            _list.RefreshItems();
        }

        /// <summary>Clicks the row at this place in the list as shown (0 is the top).</summary>
        public bool ClickAt(int index)
        {
            if (index < 0 || index >= _shown.Count) return false;
            Click(_shown[index]);
            return true;
        }
    }
}
