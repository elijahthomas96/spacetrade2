using System;
using System.Collections.Generic;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Map;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The voyage planner (spec 6.5, 6.6): pick a job or a destination for a docked ship, set the load, read the quote,
    /// provision, and launch. Every figure and every failure message is the simulation's own, shown as it gives them:
    /// the job list is <see cref="Simulation.JobBoardFor"/>, the quote is <see cref="Simulation.Quote"/>, and a ship that
    /// cannot launch at all shows <see cref="Simulation.ManualDispatchRefusal"/>. The planner sends Provision and Dispatch
    /// and nothing else.
    ///
    /// Launching never goes through with figures other than the ones on screen. When supplies are short the planner asks
    /// first, showing the top-up cost; while it asks, the quote on screen is held still. On the click it asks the
    /// simulation for the quote again, and if any term has changed it does not launch: it shows the new quote and asks
    /// again.
    /// </summary>
    public sealed class VoyagePlanner : IWorkPanel
    {
        private sealed class LoadRow
        {
            public JobBoardEntry Entry;
            public IntegerField Field;
            public Label Info;
        }

        private readonly VisualElement _panel, _body, _loadBox, _quoteBox, _confirmBox, _actions;
        private readonly Label _title, _where, _reason, _hint, _voyageTitle, _topUp, _revenue, _cost, _net, _trip, _arrival, _problems, _confirmText, _message;
        private readonly Label[] _supplies = new Label[3];
        private readonly Button _modeButton, _provision, _launch, _yes, _no;
        private readonly Dictionary<string, LoadRow> _loadRows = new Dictionary<string, LoadRow>();
        private readonly Dictionary<string, int> _tonnes = new Dictionary<string, int>();
        private const string PassengerKey = "";

        private Simulation _sim;
        private Func<Func<Simulation, CommandResult>, CommandResult> _command;
        private bool _pickingDestination;
        private GameDate _boardDate;
        private long _boardCredits = long.MinValue;
        private string _boardShipState;
        private string _shownTerms, _confirmTerms;
        private bool _visibleLastFrame;

        public VisualElement Panel => _panel;
        public string Title => "voyage planner";

        /// <summary>The ship being planned for, or -1.</summary>
        public int ShipId { get; private set; } = -1;

        /// <summary>The chosen destination, or -1 before one is chosen.</summary>
        public int DestinationId { get; private set; } = -1;

        public int Passengers { get; private set; }

        /// <summary>The ship's job board the lists were last built from.</summary>
        public JobBoard Board { get; private set; }

        /// <summary>The quote on screen now (held still while the planner is asking about the top-up), or null before a destination is chosen.</summary>
        public VoyageQuote Quote { get; private set; }

        /// <summary>The quote that was on screen when the last launch went through.</summary>
        public VoyageQuote LastLaunchedQuote { get; private set; }

        public JobListView Jobs { get; }
        public JobListView Destinations { get; }

        /// <summary>True while the planner is asking "buy supplies and launch?".</summary>
        public bool IsConfirming => _confirmTerms != null;

        public string ConfirmText => IsConfirming ? _confirmText.text : "";

        /// <summary>The planner's last word to the player: a refused command's reason, or that the quote changed.</summary>
        public string Message => _message.text;

        /// <summary>Why this ship cannot launch whatever the voyage, in the simulation's words, or "".</summary>
        public string LaunchReason => _reason.text;

        public bool CanLaunch => _launch.enabledSelf;
        public bool CanProvision => _provision.enabledSelf;

        /// <summary>How many times the job board was asked for.</summary>
        public int BoardBuilds { get; private set; }

        /// <summary>Raised when a launch goes through, with the quote that was on screen.</summary>
        public event Action<VoyageQuote> Launched;

        /// <summary>Raised when the player closes the planner with its own button.</summary>
        public event Action CloseRequested;

        public VoyagePlanner(VisualElement root)
        {
            var t = Theme.Planner;
            var colours = Theme.Colours;
            _panel = new VisualElement { name = "voyage-planner" };
            _panel.style.position = Position.Absolute;
            _panel.style.top = Theme.TopBar.Height + Theme.Panels.Margin;
            _panel.style.right = Theme.Panels.Margin;
            _panel.style.width = t.Width;
            UiKit.Panel(_panel, colours.Dropdown, Theme.Panels.Padding);
            _panel.style.display = DisplayStyle.None;

            var header = UiKit.Row();
            _title = UiKit.Title("", Theme.Text.Title);
            _title.name = "planner-title";
            _title.style.flexGrow = 1;
            header.Add(_title);
            var close = UiKit.TextButton("Close", () => CloseRequested?.Invoke(), null);
            close.name = "planner-close";
            header.Add(close);
            _panel.Add(header);

            _body = UiKit.Scroll("planner-body");
            _body.style.flexShrink = 1;
            _panel.Add(_body);

            _where = Line("planner-where", colours.TextDim);
            _reason = Line("launch-reason", colours.Warning);
            _body.Add(_where);
            _body.Add(_reason);

            _modeButton = UiKit.TextButton("", ToggleDestinationList);
            _modeButton.name = "planner-mode";
            _modeButton.style.alignSelf = Align.FlexStart;
            _modeButton.style.marginTop = t.SectionGap;
            _body.Add(_modeButton);

            Jobs = new JobListView("planner-jobs", new[] { JobSort.Nearest, JobSort.MostProfitable, JobSort.Quickest }, true, r => ChooseJob(r));
            Jobs.IsSelected = r => r.DestinationId == DestinationId && LoadOf(r.IsPassengers ? PassengerKey : r.CommodityId) > 0;
            _body.Add(Jobs.Root);
            Destinations = new JobListView("planner-destinations", new[] { JobSort.Nearest }, false, r => ChooseDestination(r.DestinationId));
            Destinations.IsSelected = r => r.DestinationId == DestinationId;
            _body.Add(Destinations.Root);

            _hint = Line("planner-hint", colours.TextDim);
            _hint.style.marginTop = t.SectionGap;
            _body.Add(_hint);

            _voyageTitle = UiKit.Title("", Theme.Text.Body);
            _voyageTitle.name = "planner-voyage";
            _voyageTitle.style.marginTop = t.SectionGap;
            _body.Add(_voyageTitle);
            _loadBox = new VisualElement { name = "planner-load" };
            _loadBox.style.flexShrink = 0;
            _body.Add(_loadBox);

            _quoteBox = new VisualElement { name = "planner-quote" };
            _quoteBox.style.flexShrink = 0;
            _quoteBox.style.marginTop = t.SectionGap;
            _trip = QuoteLine("quote-trip", colours.Text);
            _arrival = QuoteLine("quote-arrival", colours.Text);
            for (int i = 0; i < _supplies.Length; i++)
            {
                _supplies[i] = QuoteLine("quote-supply-" + i, colours.TextDim);
                _supplies[i].style.fontSize = Theme.Text.Small;
            }
            _topUp = QuoteLine("quote-topup", colours.Text);
            _revenue = QuoteLine("quote-revenue", colours.Text);
            _cost = QuoteLine("quote-cost", colours.Text);
            _net = QuoteLine("quote-net", colours.Text);
            _problems = QuoteLine("quote-problems", colours.Warning);
            _body.Add(_quoteBox);

            _actions = UiKit.Row();
            _actions.name = "planner-actions";
            _actions.style.marginTop = t.SectionGap;
            _actions.style.flexShrink = 0;
            _provision = UiKit.TextButton("Provision", PressProvision, null);
            _provision.name = "provision";
            UiKit.Margin(_provision, 0);
            _provision.style.marginRight = t.ControlGap;
            _actions.Add(_provision);
            _launch = UiKit.TextButton("Launch", PressLaunch, null);
            _launch.name = "launch";
            UiKit.Margin(_launch, 0);
            _actions.Add(_launch);
            _body.Add(_actions);

            _confirmBox = new VisualElement { name = "planner-confirm" };
            _confirmBox.style.flexShrink = 0;
            _confirmBox.style.marginTop = t.SectionGap;
            UiKit.Panel(_confirmBox, colours.ConfirmBox, t.ConfirmPadding);
            _confirmText = UiKit.Body("", colours.Text);
            _confirmText.name = "confirm-text";
            _confirmText.style.whiteSpace = WhiteSpace.Normal;
            _confirmBox.Add(_confirmText);
            var answers = UiKit.Row();
            answers.style.marginTop = t.ControlGap;
            _yes = UiKit.TextButton("Yes, buy and launch", PressYes, null);
            _yes.name = "confirm-yes";
            UiKit.Margin(_yes, 0);
            _yes.style.marginRight = t.ControlGap;
            answers.Add(_yes);
            _no = UiKit.TextButton("No", PressNo);
            _no.name = "confirm-no";
            UiKit.Margin(_no, 0);
            answers.Add(_no);
            _confirmBox.Add(answers);
            _confirmBox.style.display = DisplayStyle.None;
            _body.Add(_confirmBox);

            _message = Line("planner-message", colours.Warning);
            _message.style.marginTop = t.ControlGap;
            _body.Add(_message);

            root.Add(_panel);
        }

        private static Label Line(string name, Color colour)
        {
            var l = UiKit.Body("", colour);
            l.name = name;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = Theme.Planner.LineGap;
            l.style.flexShrink = 0;
            UiKit.Padding(l, 0);
            return l;
        }

        private Label QuoteLine(string name, Color colour)
        {
            var l = Line(name, colour);
            _quoteBox.Add(l);
            return l;
        }

        /// <summary>How commands reach the simulation: through the game's runner, so a refusal plays the error sound.</summary>
        public void Bind(Simulation sim, Func<Func<Simulation, CommandResult>, CommandResult> command)
        {
            _sim = sim;
            _command = command;
            ShipId = -1;
            Reset();
        }

        /// <summary>Starts planning for a ship. The panel itself is shown by the work-panel manager.</summary>
        public void Open(int shipId)
        {
            ShipId = shipId;
            Reset();
            if (_sim == null || _sim.FindShip(shipId) == null) { ShipId = -1; return; }
            RebuildBoard();
            _visibleLastFrame = true; // the board is fresh: the next frame need not read it again
            ShowAll();
        }

        private void Reset()
        {
            DestinationId = -1;
            Passengers = 0;
            _tonnes.Clear();
            _loadRows.Clear();
            _loadBox.Clear();
            _pickingDestination = false;
            _confirmTerms = null;
            _shownTerms = null;
            _boardShipState = null;
            Quote = null;
            _message.text = "";
        }

        /// <summary>True while the player is typing a number into the planner, so the map's keys leave it alone.</summary>
        public bool IsTyping
        {
            get
            {
                var focused = _panel.panel?.focusController?.focusedElement as VisualElement;
                for (var e = focused; e != null; e = e.parent)
                    if (e is IntegerField) return _panel.Contains(e);
                return false;
            }
        }

        private int LoadOf(string key) => key == PassengerKey ? Passengers : _tonnes.TryGetValue(key, out int t) ? t : 0;

        /// <summary>The tonnes of a commodity in the plan.</summary>
        public int Tonnes(string commodityId) => LoadOf(commodityId);

        /// <summary>The plan as it stands, or null before a destination is chosen.</summary>
        public VoyagePlan Plan()
        {
            if (ShipId < 0 || DestinationId < 0) return null;
            var freight = new List<FreightLine>();
            if (Board != null)
                foreach (var e in Board.Entries)
                    if (!e.IsPassengerJob && e.DestinationId == DestinationId && LoadOf(e.CommodityId) > 0) freight.Add(new FreightLine(e.CommodityId, LoadOf(e.CommodityId)));
            return new VoyagePlan(ShipId, DestinationId, freight, Passengers);
        }

        // ---- What the player does ----

        /// <summary>Clicking a job: go there with that job's largest feasible load (or 1, so the quote says why it cannot fly).</summary>
        public void ChooseJob(JobRow row)
        {
            if (row == null || IsConfirming) return;
            if (row.DestinationId != DestinationId) SetDestination(row.DestinationId);
            int load = row.Entry != null && row.Entry.CanFly ? (int)row.Entry.LargestLoad : 1;
            if (row.IsPassengers) Passengers = load; else _tonnes[row.CommodityId] = load;
            _message.text = "";
            ShowAll();
        }

        /// <summary>Clicking a destination in the empty-voyage list: go there with nothing aboard.</summary>
        public void ChooseDestination(int systemId)
        {
            if (IsConfirming) return;
            SetDestination(systemId);
            _pickingDestination = false;
            _message.text = "";
            ShowAll();
        }

        private void SetDestination(int systemId)
        {
            DestinationId = systemId;
            _tonnes.Clear();
            Passengers = 0;
            BuildLoadRows();
        }

        public void SetTonnes(string commodityId, int tonnes)
        {
            if (IsConfirming || commodityId == null) return;
            _tonnes[commodityId] = Mathf.Max(0, tonnes);
            _message.text = "";
            ShowAll();
        }

        public void SetPassengers(int passengers)
        {
            if (IsConfirming) return;
            Passengers = Mathf.Max(0, passengers);
            _message.text = "";
            ShowAll();
        }

        public void ToggleDestinationList()
        {
            if (IsConfirming) return;
            _pickingDestination = !_pickingDestination;
            ShowAll();
        }

        /// <summary>The Provision button: buys exactly the quoted shortfall (spec 6.5).</summary>
        public void PressProvision()
        {
            var plan = Plan();
            if (plan == null || IsConfirming || _sim == null) return;
            var result = Send(s => s.Provision(plan));
            _message.text = result.Succeeded ? "" : result.Reason;
            RebuildBoard();
            ShowAll();
        }

        /// <summary>
        /// The Launch button. With supplies short it asks first, showing the top-up cost. With nothing to buy it launches
        /// at once, but only if the quote is still the one on screen.
        /// </summary>
        public void PressLaunch()
        {
            var plan = Plan();
            if (plan == null || IsConfirming || _sim == null) return;
            var fresh = _sim.Quote(plan);
            if (JobText.Terms(fresh) != _shownTerms)
            {
                ShowQuote(fresh);
                _message.text = "The quote changed just as you pressed Launch. These are the new figures; press Launch again if you still want to go.";
                ShowButtons();
                return;
            }
            if (!fresh.IsFeasible || _sim.ManualDispatchRefusal(ShipId) != null) return;
            if (fresh.TopUpCost > 0)
            {
                GameAudio.Play(SoundId.Click, "launch: asking about the top-up");
                Ask(fresh, "");
            }
            else Launch(plan, fresh, false);
        }

        private void Ask(VoyageQuote quote, string message)
        {
            ShowQuote(quote);
            _confirmTerms = JobText.Terms(quote);
            _confirmText.text = JobText.ConfirmQuestion(quote);
            _message.text = message;
            ShowAll(requote: false);
        }

        /// <summary>
        /// "Yes, buy and launch." The simulation is asked for the quote once more. If any term differs from the one the
        /// question showed, nothing is launched: the new quote is shown and the question is asked again.
        /// </summary>
        public void PressYes()
        {
            var plan = Plan();
            if (!IsConfirming || plan == null || _sim == null) return;
            var fresh = _sim.Quote(plan);
            if (JobText.Terms(fresh) != _confirmTerms)
            {
                const string changed = "The quote changed while you were deciding, so nothing was launched. These are the new figures.";
                _confirmTerms = null;
                if (fresh.IsFeasible && fresh.TopUpCost > 0 && _sim.ManualDispatchRefusal(ShipId) == null) Ask(fresh, changed);
                else
                {
                    ShowQuote(fresh);
                    _message.text = changed;
                    ShowAll(requote: false);
                }
                return;
            }
            _confirmTerms = null;
            Launch(plan, fresh, true);
        }

        /// <summary>"No": back to the planner. Nothing is sent to the simulation.</summary>
        public void PressNo()
        {
            if (!IsConfirming) return;
            _confirmTerms = null;
            _message.text = "";
            ShowAll();
        }

        private void Launch(VoyagePlan plan, VoyageQuote shown, bool buyTopUp)
        {
            var result = Send(s => s.Dispatch(plan, buyTopUp));
            if (!result.Succeeded)
            {
                _message.text = result.Reason;
                RebuildBoard();
                ShowAll();
                return;
            }
            LastLaunchedQuote = shown;
            _message.text = "";
            Launched?.Invoke(shown);
        }

        private CommandResult Send(Func<Simulation, CommandResult> command) => _command != null ? _command(command) : command(_sim);

        // ---- What the planner shows ----

        /// <summary>Once a frame: keeps the job list and the quote in step with the game.</summary>
        public void Refresh(bool visible)
        {
            if (_sim == null || ShipId < 0) return;
            if (!visible) { _visibleLastFrame = false; return; }
            var ship = _sim.FindShip(ShipId);
            if (ship == null) return;
            string shipState = $"{ship.DockedSystemId}|{ship.Fuel.Kg}|{ship.Food.Kg}|{ship.Water.Kg}|{ship.IsOnInSystemTransport}|{ship.Route != null && ship.Route.IsActive}|{ship.DriveId}";
            bool stale = !_visibleLastFrame || !_sim.CurrentDate.Equals(_boardDate) || _sim.Credits != _boardCredits || shipState != _boardShipState;
            _visibleLastFrame = true;
            if (stale && !IsConfirming)
            {
                RebuildBoard();
                ShowAll();
            }
            else if (!IsConfirming) ShowLiveQuote();
        }

        private void RebuildBoard()
        {
            if (_sim == null || ShipId < 0) return;
            var ship = _sim.FindShip(ShipId);
            if (ship == null) return;
            Board = _sim.JobBoardFor(ShipId);
            BoardBuilds++;
            _boardDate = _sim.CurrentDate;
            _boardCredits = _sim.Credits;
            _boardShipState = $"{ship.DockedSystemId}|{ship.Fuel.Kg}|{ship.Food.Kg}|{ship.Water.Kg}|{ship.IsOnInSystemTransport}|{ship.Route != null && ship.Route.IsActive}|{ship.DriveId}";
            Jobs.SetFilterCaption($"Hide jobs {ship.Name} cannot fly");
            Jobs.SetEmptyText(Board.Problem ?? (Jobs.HideUnflyable && Board.Entries.Count > 0
                ? $"{ship.Name} cannot fly any of the {Board.Entries.Count} jobs here. Turn the filter off to see why."
                : "This port has no jobs right now."));
            Jobs.SetRows(JobText.BoardRows(_sim, Board));
            Destinations.SetRows(Board.OriginId.HasValue ? JobText.DestinationRows(_sim, Board.OriginId.Value) : new List<JobRow>());
            Destinations.SetEmptyText("There is nowhere else to go.");
            if (_loadRows.Count == 0 && DestinationId >= 0) BuildLoadRows();
            foreach (var pair in _loadRows) pair.Value.Entry = FindEntry(pair.Key == PassengerKey ? null : pair.Key);
        }

        private JobBoardEntry FindEntry(string commodityId)
        {
            if (Board == null) return null;
            foreach (var e in Board.Entries)
                if (e.DestinationId == DestinationId && e.CommodityId == commodityId) return e;
            return null;
        }

        private void BuildLoadRows()
        {
            _loadRows.Clear();
            _loadBox.Clear();
            if (Board == null || DestinationId < 0) return;
            foreach (var e in Board.Entries)
            {
                if (e.DestinationId != DestinationId) continue;
                var entry = e;
                string key = e.IsPassengerJob ? PassengerKey : e.CommodityId;
                var t = Theme.Planner;
                var row = UiKit.Row();
                row.name = "load-" + (e.IsPassengerJob ? "passengers" : e.CommodityId);
                row.style.marginTop = t.LineGap;
                var name = UiKit.Body(JobText.Cargo(_sim, e.CommodityId), Theme.Colours.Text);
                name.style.width = t.NameWidth;
                name.style.flexShrink = 0;
                row.Add(name);
                row.Add(Step("-", key, -1));
                var field = UiKit.NumberField(LoadOf(key), t.AmountWidth, v => SetLoad(key, v));
                field.name = "amount";
                row.Add(field);
                row.Add(Step("+", key, 1));
                var max = UiKit.TextButton("Max", () => SetLoad(key, _loadRows.TryGetValue(key, out var r) && r.Entry != null ? (int)r.Entry.LargestLoad : 0));
                max.name = "max";
                UiKit.Margin(max, 0);
                max.style.marginLeft = t.ControlGap;
                row.Add(max);
                var info = UiKit.Label("", Theme.Colours.TextDim, Theme.Text.Small);
                info.name = "info";
                info.style.marginLeft = t.ControlGap;
                info.style.flexShrink = 1;
                info.style.whiteSpace = WhiteSpace.Normal;
                row.Add(info);
                _loadBox.Add(row);
                _loadRows[key] = new LoadRow { Entry = entry, Field = field, Info = info };
            }
        }

        private Button Step(string text, string key, int by)
        {
            var b = UiKit.TextButton(text, () => SetLoad(key, LoadOf(key) + by));
            b.name = by < 0 ? "less" : "more";
            b.style.width = Theme.Planner.StepButtonWidth;
            UiKit.Margin(b, 0);
            UiKit.Padding(b, 0);
            return b;
        }

        private void SetLoad(string key, int value)
        {
            if (key == PassengerKey) SetPassengers(value); else SetTonnes(key, value);
        }

        private void ShowAll(bool requote = true)
        {
            if (_sim == null || ShipId < 0) return;
            var ship = _sim.FindShip(ShipId);
            if (ship == null) return;
            var shipClass = _sim.Market.Tuning.FindShip(ship.ClassId);
            Set(_title, "Plan a voyage: " + ship.Name);
            Set(_where, ShipText.Status(_sim, ship, _sim.PositionOf(ship.Id)) + (shipClass == null ? "" : $" · {shipClass.Name} · hold {JobText.N(shipClass.HoldTonnes)} t · tank {JobText.N(shipClass.TankTonnes)} t · {JobText.N(shipClass.Berths)} berths"));
            Set(_reason, _sim.ManualDispatchRefusal(ShipId) ?? "");
            Show(_reason, _reason.text.Length > 0);

            Set(_modeButton, _pickingDestination ? "Back to the job list" : "Empty voyage: choose a destination");
            Show(Jobs.Root, !_pickingDestination);
            Show(Destinations.Root, _pickingDestination);
            Jobs.Redraw();
            Destinations.Redraw();

            bool chosen = DestinationId >= 0;
            Show(_hint, !chosen);
            Set(_hint, Board != null && Board.Problem != null ? Board.Problem : "Choose a job from the list, or an empty voyage to any system.");
            Show(_voyageTitle, chosen);
            Show(_loadBox, chosen);
            Show(_quoteBox, chosen);
            Show(_actions, chosen);
            if (chosen)
            {
                Set(_voyageTitle, "Voyage to " + ShipText.SystemName(_sim, DestinationId) + (_loadRows.Count == 0 ? " (this port has no jobs to there: an empty voyage)" : ""));
                foreach (var pair in _loadRows)
                {
                    var row = pair.Value;
                    row.Field.SetValueWithoutNotify(LoadOf(pair.Key));
                    var e = row.Entry;
                    if (e == null) continue;
                    Set(row.Info, e.IsPassengerJob
                        ? $"of {JobText.N(e.Available)} waiting · {Format.Credits(e.Rate)} each"
                        : $"of {JobText.N(e.Available)} t · {Format.Credits(e.Rate)} per tonne");
                }
                if (requote) ShowLiveQuote();
            }
            else
            {
                Quote = null;
                _shownTerms = null;
            }
            Show(_confirmBox, IsConfirming);
            Show(_message, _message.text.Length > 0);
            ShowButtons();
        }

        private void ShowLiveQuote()
        {
            var plan = Plan();
            if (plan == null) return;
            ShowQuote(_sim.Quote(plan));
            ShowButtons();
        }

        /// <summary>Writes a quote on screen exactly as the simulation gave it.</summary>
        private void ShowQuote(VoyageQuote q)
        {
            Quote = q;
            _shownTerms = JobText.Terms(q);
            bool valid = q.IsPlanValid;
            Show(_trip, valid); Show(_arrival, valid); Show(_topUp, valid); Show(_revenue, valid); Show(_cost, valid); Show(_net, valid);
            for (int i = 0; i < _supplies.Length; i++) Show(_supplies[i], valid);
            if (valid)
            {
                Set(_trip, JobText.TripLine(_sim, q));
                Set(_arrival, q.DescribeArrival() + " if launched now");
                for (int i = 0; i < _supplies.Length; i++) Set(_supplies[i], JobText.SupplyLine(q.Supplies[i]));
                Set(_topUp, JobText.TopUpLine(q));
                Set(_revenue, JobText.RevenueLine(q.FreightRevenue, q.FareRevenue, q.GrossRevenue));
                Set(_cost, JobText.SuppliesCostLine(q.OperatingCost));
                Set(_net, JobText.NetLine(q));
                var net = q.NetProfit < 0 ? Theme.Colours.Loss : Theme.Colours.Text;
                if (_net.style.color != net) _net.style.color = net;
            }
            Set(_problems, string.Join("\n", q.Problems));
            Show(_problems, q.Problems.Count > 0);
        }

        private void ShowButtons()
        {
            bool feasible = Quote != null && Quote.IsFeasible;
            bool blocked = _reason.text.Length > 0;
            UiKit.SetEnabled(_provision, feasible && Quote.TopUpCost > 0 && !IsConfirming);
            UiKit.SetEnabled(_launch, feasible && !blocked && !IsConfirming);
            UiKit.SetEnabled(_modeButton, !IsConfirming);
        }

        /// <summary>Every line of the quote on screen, top to bottom.</summary>
        public List<string> QuoteLines()
        {
            var lines = new List<string>();
            foreach (var l in _quoteBox.Query<Label>().ToList())
                if (l.style.display != DisplayStyle.None && l.text.Length > 0) lines.Add(l.text);
            return lines;
        }

        /// <summary>The text of the line with this name, or "" when it is not showing.</summary>
        public string LineText(string name)
        {
            var l = _panel.Q<Label>(name);
            return l == null || l.style.display == DisplayStyle.None ? "" : l.text;
        }

        private static void Set(TextElement e, string text)
        {
            if (e.text != text) e.text = text;
        }

        private static void Show(VisualElement e, bool show)
        {
            var want = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != want) e.style.display = want;
        }
    }
}
