using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Game.Map;
using SpaceTrade.Game.Theming;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The theme rule (AGENTS.md): every colour, font, text size, spacing value, glow size, label rule, display limit, and
    /// control value comes from the theme file. One test reads the game's source and fails on a hard-coded value outside
    /// the Theme folder; the other runs the game with a theme in which every value is different and checks that what is
    /// on screen follows it.
    /// </summary>
    public class ThemeGuardTests
    {
        private static string GameScripts => Path.Combine(Application.dataPath, "Scripts", "Game");

        /// <summary>Lines allowed to break a rule, each with its reason. Kept short on purpose.</summary>
        private static readonly (string File, string Contains, string Why)[] Allowed =
        {
            ("ArtLibrary.cs", "new Color32(255, 255, 255,", "the pixels of the generated stand-in image, which is white so the theme can tint it"),
            ("MapInput.cs", "WheelUnitsPerNotch = 120f", "how Windows reports one notch of a mouse wheel: a fact about the hardware, not a feel value"),
        };

        private static readonly (string Name, Regex Pattern)[] Rules =
        {
            ("a colour made in code", new Regex(@"new\s+Color(32)?\s*\(")),
            ("a named colour other than white or clear", new Regex(@"\bColor\.(red|green|blue|black|grey|gray|yellow|cyan|magenta)\b")),
            ("a colour written as text", new Regex("\"#[0-9A-Fa-f]{6,8}\"|ColorUtility")),
            ("a style set to a number written in the code", new Regex(@"\.style\.\w+\s*=\s*-?(?!0f?\s*;|1f?\s*;)\d+(\.\d+)?f?\s*;")),
            ("a percentage written in the code", new Regex(@"Length\.Percent\(\s*-?\d")),
            ("a font loaded outside the theme", new Regex(@"Resources\.Load<Font>|CreateDynamicFontFromOSFont|unityFont(Definition)?\s*=")),
            ("a look or control value kept as a constant", new Regex(@"\b(const|static readonly)\s+(float|int|Color|Color32)\s+\w*(Pixels|Colour|Color|Tint|Alpha|Width|Height|Gap|Margin|Padding|Radius|Degrees|Ly|PerSecond|PerNotch|FontSize)\w*\s*=")),
        };

        private static List<string> Scan(string file, IEnumerable<string> lines)
        {
            var problems = new List<string>();
            int number = 0;
            foreach (string raw in lines)
            {
                number++;
                string line = raw.Trim();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")) continue;
                foreach (var rule in Rules)
                {
                    if (!rule.Pattern.IsMatch(line)) continue;
                    if (Allowed.Any(a => a.File == Path.GetFileName(file) && line.Contains(a.Contains))) continue;
                    problems.Add($"{Path.GetFileName(file)} line {number}: {rule.Name}: {line}");
                }
            }
            return problems;
        }

        [Test]
        public void NoScreen_HardCodesAColourFontSizeSpacingLimitOrControlValue()
        {
            var files = Directory.GetFiles(GameScripts, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Scripts/Game/Theme/")).ToList();
            Assert.Greater(files.Count, 20, "the game's source was found");
            var problems = new List<string>();
            foreach (var f in files) problems.AddRange(Scan(f, File.ReadAllLines(f)));
            TestContext.WriteLine($"Scanned {files.Count} files outside Scripts/Game/Theme. Allowed exceptions: " + string.Join("; ", Allowed.Select(a => $"{a.File} ({a.Why})")));
            CollectionAssert.IsEmpty(problems, "values that must come from the theme file");
        }

        [Test]
        public void TheScan_CatchesEachKindOfHardCodedValue()
        {
            var planted = new[]
            {
                "var c = new Color(0.1f, 0.2f, 0.3f, 1f);",
                "label.style.color = Color.red;",
                "var hex = \"#FFAA00\";",
                "label.style.fontSize = 13;",
                "panel.style.paddingLeft = 12f;",
                "box.style.left = Length.Percent(6);",
                "var f = Resources.Load<Font>(\"Fonts/Mine\");",
                "public const float StarPixels = 26f;",
            };
            foreach (string line in planted) Assert.AreEqual(1, Scan("Planted.cs", new[] { line }).Count, line);
            CollectionAssert.IsEmpty(Scan("Planted.cs", new[] { "row.style.flexGrow = 1;", "e.style.left = 0;", "// label.style.fontSize = 13;", "image.style.width = Theme.Panels.IconSize;" }), "values from the theme, and 0 and 1, are fine");
        }

        private static bool Close(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        /// <summary>A theme in which every colour is different from every other and from the defaults, and every size is changed.</summary>
        private static GameTheme DifferentTheme()
        {
            var theme = new GameTheme();
            int n = 0;
            foreach (var f in ThemeReader.FieldsOf(typeof(ColourTheme)))
            {
                n++;
                f.SetValue(theme.Colours, (Color)new Color32((byte)(20 + n * 5), (byte)(250 - n * 6), (byte)(90 + (n * 37) % 150), 255));
            }
            theme.Text.Body = 17; theme.Text.Small = 11; theme.Text.MapLabel = 16; theme.Text.Title = 23; theme.Text.CloseUpTitle = 37; theme.Text.CloseUpSubtitle = 19;
            theme.Text.TitlesBold = false;
            theme.TopBar.Height = 52;
            theme.Panels.Margin = 22; theme.Panels.InfoWidth = 301; theme.Panels.Padding = 17;
            theme.Map.StarPixels = 40; theme.Ships.IconPixels = 38; theme.Ships.MaxDockedIconsPerStar = 3;
            theme.CloseUp.PanelWidth = 377;
            theme.Notifications.Width = 333; theme.Settings.Width = 299;
            theme.Routes.SelectedAlpha = 1f; theme.Routes.CurrentLegAlpha = 1f; theme.Routes.RouteAlpha = 1f; theme.Routes.PausedRouteAlpha = 1f;
            return theme;
        }

        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Close();
            Theme.Use(null);
        }

        [UnityTest]
        public IEnumerator WithADifferentTheme_EveryColourAndTextSizeOnScreen_IsTheThemes()
        {
            var theme = DifferentTheme();
            var themeColours = ThemeReader.FieldsOf(typeof(ColourTheme)).Select(f => (Color)f.GetValue(theme.Colours)).ToList();
            var defaultColours = ThemeReader.FieldsOf(typeof(ColourTheme)).Select(f => (Color)f.GetValue(new GameTheme().Colours)).ToList();
            var sizes = new[] { theme.Text.Body, theme.Text.Small, theme.Text.MapLabel, theme.Text.Title, theme.Text.CloseUpTitle, theme.Text.CloseUpSubtitle };
            Theme.Use(theme);
            _rig.KeepTheme = true;
            var sim = MapTestRig.FleetGame(12, 6, 5, true);
            DebugFleetNotifications(sim);
            yield return _rig.Open(sim);
            var map = _rig.Map;
            var problems = new List<string>();

            // Sprites on the map: stars, glows, badges, rings, ships, trails, route lines and arrows.
            map.Select(sim.Market.Shipyards[0].SystemId);
            yield return MapTestRig.Frames();
            int sprites = 0;
            foreach (var r in map.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r.transform.IsChildOf(map.transform.Find("Background"))) continue;
                sprites++;
                if (!themeColours.Any(c => Close(c, r.color))) problems.Add($"sprite {r.transform.parent.name}/{r.name} has colour {r.color}");
            }
            Assert.Greater(sprites, 40);
            Assert.AreEqual(3, map.Ships.DrawnDockedAt(sim.Market.Shipyards[0].SystemId), "the docked-icon limit follows the theme");

            // Every text element and panel: the map's text, the bar with its list and settings open, and the close-up.
            map.SelectShip(sim.Ships[0].Id);
            map.Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            int checkedText = Check(map.Hud.Root, "map", themeColours, defaultColours, sizes, problems);
            checkedText += Check(map.Bar.Root, "bar + notifications", themeColours, defaultColours, sizes, problems);
            map.Bar.ToggleSettings();
            yield return MapTestRig.Frames(3);
            checkedText += Check(map.Bar.Root, "bar + settings", themeColours, defaultColours, sizes, problems);
            map.Bar.ToggleSettings();
            map.OpenCloseUp();
            yield return MapTestRig.Frames(3);
            checkedText += Check(map.CloseUp.Root, "close-up", themeColours, defaultColours, sizes, problems);
            Assert.Greater(checkedText, 60, "text elements checked");

            Assert.AreEqual(52f, map.Bar.BarRect.height, 0.5f, "the bar's height follows the theme");
            Assert.AreEqual(301f, map.Hud.InfoPanelRect.width, 0.5f, "the info panel's width follows the theme");
            TestContext.WriteLine($"{sprites} sprites and {checkedText} text elements checked against a theme with every colour and size changed.");
            CollectionAssert.IsEmpty(problems, "things on screen that do not follow the theme");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Session 14: the system view, the voyage planner with its launch question, and the receipt follow the theme too.</summary>
        [UnityTest]
        public IEnumerator WithADifferentTheme_TheSystemViewThePlannerAndTheReceipt_AreTheThemes()
        {
            var theme = DifferentTheme();
            theme.SystemView.Width = 471; theme.Planner.Width = 563; theme.Receipt.Width = 409; theme.JobList.RowHeight = 51; theme.JobList.Height = 207;
            var themeColours = ThemeReader.FieldsOf(typeof(ColourTheme)).Select(f => (Color)f.GetValue(theme.Colours)).ToList();
            var defaultColours = ThemeReader.FieldsOf(typeof(ColourTheme)).Select(f => (Color)f.GetValue(new GameTheme().Colours)).ToList();
            var sizes = new[] { theme.Text.Body, theme.Text.Small, theme.Text.MapLabel, theme.Text.Title, theme.Text.CloseUpTitle, theme.Text.CloseUpSubtitle };
            Theme.Use(theme);
            _rig.KeepTheme = true;

            // One finished voyage, so there is a receipt, and the starter docked at a port with jobs.
            var sim = GameTestTuning.NewGameWithSystems(12);
            var first = SpaceTrade.Sim.OpeningOffers.FindJobs(sim, sim.Ships[0].Id).OrderBy(j => j.TransitTicks).First();
            Assert.IsTrue(sim.Dispatch(first.Quote.Plan, true).Succeeded);
            sim.Advance(sim.Ships[0].Voyage.ArrivalTick);
            yield return _rig.Open(sim);
            var map = _rig.Map;
            var problems = new List<string>();

            map.Select(sim.Market.Shipyards[0].SystemId);
            yield return MapTestRig.Frames(3);
            int checkedText = Check(map.Hud.Root, "system view", themeColours, defaultColours, sizes, problems);
            Assert.AreEqual(471f, map.Hud.InfoPanelRect.width, 0.5f, "the system view's width follows the theme");
            Assert.Greater(map.Hud.SystemJobs.RowElementCount, 0);

            map.SelectShip(sim.Ships[0].Id);
            Assert.IsTrue(map.OpenPlanner());
            yield return MapTestRig.Frames(3);
            var job = map.Planner.Jobs.Shown.First(r => r.CanFly && !r.IsPassengers);
            map.Planner.ChooseJob(job);
            map.Planner.SetTonnes(job.CommodityId, 500); // a voyage that cannot be flown: the warning lines show
            yield return MapTestRig.Frames(3);
            checkedText += Check(map.Bar.Root, "planner with problems", themeColours, defaultColours, sizes, problems);
            map.Planner.ChooseJob(job);
            map.Planner.PressLaunch();
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(map.Planner.IsConfirming, "the launch question is on screen");
            checkedText += Check(map.Bar.Root, "planner asking", themeColours, defaultColours, sizes, problems);
            Assert.AreEqual(563f, map.Planner.Panel.layout.width, 0.5f, "the planner's width follows the theme");
            map.Planner.PressNo();
            map.Planner.ToggleDestinationList();
            yield return MapTestRig.Frames(3);
            checkedText += Check(map.Bar.Root, "planner destinations", themeColours, defaultColours, sizes, problems);

            Assert.IsTrue(map.OpenLastReceipt());
            yield return MapTestRig.Frames(3);
            checkedText += Check(map.Bar.Root, "receipt", themeColours, defaultColours, sizes, problems);
            Assert.AreEqual(409f, map.Receipt.Panel.layout.width, 0.5f, "the receipt's width follows the theme");

            Assert.Greater(checkedText, 120, "text elements checked");
            TestContext.WriteLine($"{checkedText} text elements checked on the system view, planner, and receipt against a theme with every colour and size changed.");
            CollectionAssert.IsEmpty(problems, "things on screen that do not follow the theme");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MovingShipNames_ShowOnlyForTheSelectedShipAndTheOneUnderTheMouse_AndFollowTheThemesRule()
        {
            var sim = MapTestRig.FleetGame(12, 6);
            yield return _rig.Open(sim);
            var map = _rig.Map;
            Assert.AreEqual(ShipNameRule.SelectedOrHovered, Theme.Labels.MovingShipNames, "the rule the owner chose is the default");
            yield return MapTestRig.Frames(3);
            int starter = sim.Ships[0].Id;
            Assert.AreEqual(1, map.Hud.ShipNames.Count, "the selected ship in flight shows its name");
            Assert.AreEqual(starter, map.Hud.ShipNames[0].ShipId);
            Assert.AreEqual(sim.Ships[0].Name, map.Hud.ShipNames[0].Text);

            map.Select(-1);
            yield return _rig.MouseTo(new Vector2(3f, 3f));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(-1, map.Hud.HoveredShipId);
            Assert.AreEqual(0, map.Hud.ShipNames.Count, "nothing selected, nothing under the mouse: no moving ship shows a name");

            var other = sim.Ships.First(s => s.Id != starter && s.Voyage != null);
            yield return _rig.MouseTo(_rig.ScreenOf(ShipMapView.WorldPosition(sim.PositionOf(other.Id))));
            yield return MapTestRig.Frames(3);
            int hovered = map.Hud.HoveredShipId;
            Assert.GreaterOrEqual(hovered, 0, "the mouse is over a ship");
            Assert.AreEqual(1, map.Hud.ShipNames.Count, "the ship under the mouse shows its name");
            Assert.AreEqual(hovered, map.Hud.ShipNames[0].ShipId);
            Assert.AreEqual(sim.FindShip(hovered).Name, map.Hud.ShipNames[0].Text);

            int selected = sim.Ships.First(s => s.Id != hovered && s.Voyage != null).Id;
            map.SelectShip(selected);
            yield return MapTestRig.Frames(3);
            CollectionAssert.AreEquivalent(new[] { selected, hovered }, map.Hud.ShipNames.Select(n => n.ShipId).ToList(), "the selected ship and the one under the mouse, and no others");

            yield return _rig.MouseTo(new Vector2(3f, 3f));
            yield return MapTestRig.Frames(3);
            CollectionAssert.AreEquivalent(new[] { selected }, map.Hud.ShipNames.Select(n => n.ShipId).ToList(), "the mouse moved away: only the selected ship");

            // The rule is a theme value.
            Theme.Labels.MovingShipNames = ShipNameRule.Always;
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(6, map.Hud.ShipNames.Count, "\"always\": every ship in flight shows its name");
            Theme.Labels.MovingShipNames = ShipNameRule.Never;
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(0, map.Hud.ShipNames.Count, "\"never\": none do");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheDockedIconLimit_AndTheClickRadius_FollowTheTheme()
        {
            var sim = MapTestRig.FleetGame(12, 0, 8);
            yield return _rig.Open(sim);
            var map = _rig.Map;
            int yard = sim.Market.Shipyards[0].SystemId;
            int docked = sim.Ships.Count(s => s.DockedSystemId == yard);
            map.Select(-1);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(5, Theme.Ships.MaxDockedIconsPerStar, "the limit the game had before is the default");
            Assert.AreEqual(5, map.Ships.DrawnDockedAt(yard), $"{docked} ships docked, five drawn");
            Assert.AreEqual("+" + (docked - 5), map.Hud.DockedCounts.Single(c => c.SystemId == yard).Text);

            Theme.Ships.MaxDockedIconsPerStar = 2;
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(2, map.Ships.DrawnDockedAt(yard), "the limit is a theme value");
            Assert.AreEqual("+" + (docked - 2), map.Hud.DockedCounts.Single(c => c.SystemId == yard).Text);

            // The click radius: 24 pixels by default (spec 9.1), from the theme's controls section.
            Assert.AreEqual(24f, Theme.Controls.ClickRadiusPixels);
            var empty = sim.Market.Galaxy.Systems.Where(s => sim.Ships.All(ship => ship.DockedSystemId != s.Id)).OrderBy(s => s.YMilliLy).First();
            Vector2 below = _rig.ScreenOf(GalaxyMapView.WorldPosition(empty)) + new Vector2(0f, -20f);
            yield return _rig.Click(below);
            Assert.AreEqual(empty.Id, map.SelectedSystemId, "20 pixels away is inside the 24 pixel reach");
            map.Select(-1);
            Theme.Controls.ClickRadiusPixels = 10f;
            yield return _rig.Click(below);
            Assert.AreEqual(-1, map.SelectedSystemId, "with the reach set to 10 pixels the same click selects nothing");
            LogAssert.NoUnexpectedReceived();
        }

        private static void DebugFleetNotifications(SpaceTrade.Sim.Simulation sim)
        {
            long latest = sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
            sim.Advance(latest);
            Assert.Greater(sim.Notifications.Count, 0);
        }

        /// <summary>Checks every shown label's colour and size, and every background and border colour, under a root.</summary>
        private static int Check(VisualElement root, string where, List<Color> themeColours, List<Color> defaultColours, float[] sizes, List<string> problems)
        {
            int n = 0;
            foreach (var e in root.Query<VisualElement>().ToList())
            {
                if (e.resolvedStyle.display == DisplayStyle.None || !Shown(e)) continue;
                // Unity's own scroll bar and slider internals are restyled by UiKit; their unstyled inner parts draw nothing.
                var background = e.resolvedStyle.backgroundColor;
                if (background.a > 0f && !themeColours.Any(c => Close(c, background)))
                    problems.Add($"{where}: {Describe(e)} has background {background}");
                if (e is TextElement t && !string.IsNullOrEmpty(t.text))
                {
                    n++;
                    var colour = t.resolvedStyle.color;
                    if (!themeColours.Any(c => Close(c, colour))) problems.Add($"{where}: \"{t.text}\" has colour {colour}");
                    if (!sizes.Any(s => Mathf.Abs(s - t.resolvedStyle.fontSize) < 0.01f)) problems.Add($"{where}: \"{t.text}\" has size {t.resolvedStyle.fontSize}");
                }
                // The close-up's backdrop pictures and engine trail are drawn in the art's own colours: they have no tint at all.
                bool untintedArt = e is Image art && (art.name == "backdrop" || art.name == "engine-trail") && Close(art.tintColor, Color.white);
                if (e is Image image && image.sprite != null && !untintedArt && !themeColours.Any(c => Close(c, image.tintColor)))
                    problems.Add($"{where}: image {Describe(e)} has tint {image.tintColor}");
            }
            return n;
        }

        private static bool Shown(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        private static string Describe(VisualElement e) => $"{e.GetType().Name} '{e.name}' [{string.Join(" ", e.GetClasses())}]";
    }
}
