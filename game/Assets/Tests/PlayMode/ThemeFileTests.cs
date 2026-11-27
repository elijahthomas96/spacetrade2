using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Theming;
using UnityEngine;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// theme.json is read like tuning.json (spec 9.7): a bad or missing value, or an unknown or misspelled field, gives a
    /// message naming the field and the line, and the whole file is set aside for the built-in defaults.
    /// </summary>
    public class ThemeFileTests
    {
        private static string[] Lines() => GameTestTheme.Text().Replace("\r\n", "\n").Split('\n');

        /// <summary>The 1-based line holding this text in the fixture.</summary>
        private static int LineOf(string[] lines, string text)
        {
            int i = Array.FindIndex(lines, l => l.Contains(text));
            Assert.GreaterOrEqual(i, 0, "the fixture has a line containing " + text);
            return i + 1;
        }

        private static string Join(string[] lines) => string.Join("\n", lines);

        private static void AssertIsDefaults(ThemeLoadResult result)
        {
            Assert.IsTrue(result.UsedDefaults, "the file is set aside");
            Assert.AreEqual(ThemeReader.Write(new GameTheme()), ThemeReader.Write(result.Theme), "every value is the built-in default");
        }

        [Test]
        public void TheFixture_AndTheLiveThemeFile_LoadWithNoProblems()
        {
            var fixture = ThemeReader.LoadFile(GameTestTheme.FixturePath);
            CollectionAssert.IsEmpty(fixture.Errors);
            Assert.IsFalse(fixture.UsedDefaults);
            var live = ThemeReader.LoadFile(GameTestTheme.LivePath);
            CollectionAssert.IsEmpty(live.Errors, "the live theme.json");
            Assert.IsFalse(live.UsedDefaults);
        }

        [Test]
        public void WritingATheme_AndReadingItBack_GivesTheSameTheme()
        {
            var theme = new GameTheme();
            theme.Colours.Text = new Color32(1, 2, 3, 4);
            theme.Ships.MaxDockedIconsPerStar = 9;
            theme.Controls.ZoomPerWheelNotch = 0.5f;
            theme.Labels.MovingShipNames = ShipNameRule.Always;
            theme.Text.TitlesBold = false;
            string text = ThemeReader.Write(theme);
            var back = ThemeReader.LoadText(text);
            CollectionAssert.IsEmpty(back.Errors);
            Assert.AreEqual(text, ThemeReader.Write(back.Theme));
            Assert.AreEqual(9, back.Theme.Ships.MaxDockedIconsPerStar);
            Assert.AreEqual(ShipNameRule.Always, back.Theme.Labels.MovingShipNames);
            Assert.AreEqual((Color)new Color32(1, 2, 3, 4), back.Theme.Colours.Text);
        }

        [Test]
        public void ABadColour_NamesTheFieldAndTheLine_AndTheDefaultsAreUsed()
        {
            var lines = Lines();
            int line = LineOf(lines, "\"panel\":");
            lines[line - 1] = "    \"panel\": \"dark blue\",";
            var result = ThemeReader.LoadText(Join(lines));
            Assert.AreEqual(1, result.Errors.Count, string.Join("\n", result.Errors));
            Assert.AreEqual($"colours.panel: must be a colour written as \"#RRGGBBAA\" (red, green, blue, and opacity, each 00 to FF), but is \"dark blue\" (line {line})", result.Errors[0]);
            AssertIsDefaults(result);
        }

        [Test]
        public void ABadNumber_NamesTheFieldAndTheLine_AndTheDefaultsAreUsed()
        {
            var lines = Lines();
            int line = LineOf(lines, "\"clickRadiusPixels\":");
            lines[line - 1] = "    \"clickRadiusPixels\": \"wide\",";
            var result = ThemeReader.LoadText(Join(lines));
            Assert.AreEqual(1, result.Errors.Count, string.Join("\n", result.Errors));
            Assert.AreEqual($"controls.clickRadiusPixels: must be a number, but is \"wide\" (line {line})", result.Errors[0]);
            AssertIsDefaults(result);
        }

        [Test]
        public void ANumberOutsideItsLimits_NamesTheFieldTheLimitsAndTheLine()
        {
            var lines = Lines();
            int line = LineOf(lines, "\"zoomPerWheelNotch\":");
            lines[line - 1] = "    \"zoomPerWheelNotch\": 1.5,";
            int line2 = LineOf(lines, "\"maxDockedIconsPerStar\":");
            lines[line2 - 1] = "    \"maxDockedIconsPerStar\": 2.5,";
            int line3 = LineOf(lines, "\"body\":");
            lines[line3 - 1] = "    \"body\": -3,";
            var result = ThemeReader.LoadText(Join(lines));
            CollectionAssert.AreEquivalent(new[]
            {
                $"controls.zoomPerWheelNotch: must be between 0.1 and 0.99, but is 1.5 (line {line})",
                $"ships.maxDockedIconsPerStar: must be a whole number, but is 2.5 (line {line2})",
                $"text.body: must be between 6 and 100, but is -3 (line {line3})",
            }, result.Errors);
            AssertIsDefaults(result);
        }

        [Test]
        public void AWrongKindOfValue_ForAYesNoOrAChoice_NamesTheFieldAndTheLine()
        {
            var lines = Lines();
            int line = LineOf(lines, "\"titlesBold\":");
            lines[line - 1] = "    \"titlesBold\": \"yes\"";
            int line2 = LineOf(lines, "\"movingShipNames\":");
            lines[line2 - 1] = "    \"movingShipNames\": \"sometimes\",";
            var result = ThemeReader.LoadText(Join(lines));
            CollectionAssert.AreEquivalent(new[]
            {
                $"text.titlesBold: must be true or false, but is \"yes\" (line {line})",
                $"labels.movingShipNames: must be one of \"never\", \"selectedOrHovered\", \"always\", but is \"sometimes\" (line {line2})",
            }, result.Errors);
            AssertIsDefaults(result);
        }

        [Test]
        public void AMissingValue_NamesTheFieldAndTheLineOfItsGroup_AndTheDefaultsAreUsed()
        {
            var lines = Lines().ToList();
            int group = LineOf(lines.ToArray(), "\"controls\":");
            int line = LineOf(lines.ToArray(), "\"clickRadiusPixels\":");
            lines.RemoveAt(line - 1);
            var result = ThemeReader.LoadText(Join(lines.ToArray()));
            Assert.AreEqual(1, result.Errors.Count, string.Join("\n", result.Errors));
            Assert.AreEqual($"controls.clickRadiusPixels: is missing (line {group})", result.Errors[0]);
            AssertIsDefaults(result);
        }

        [Test]
        public void AMissingGroup_NamesTheGroup_AndTheDefaultsAreUsed()
        {
            var lines = Lines().ToList();
            int from = LineOf(lines.ToArray(), "\"background\":");
            int to = LineOf(lines.ToArray(), "\"closeUp\":");
            lines.RemoveRange(from - 1, to - from);
            var result = ThemeReader.LoadText(Join(lines.ToArray()));
            Assert.AreEqual(1, result.Errors.Count, string.Join("\n", result.Errors));
            Assert.AreEqual("background: is missing (line 1)", result.Errors[0]);
            AssertIsDefaults(result);
        }

        [Test]
        public void AMisspelledField_IsAnError_NamingItsSpellingAndLine_AndTheFieldItShouldHaveBeen()
        {
            var lines = Lines();
            int group = LineOf(lines, "\"map\":");
            int line = LineOf(lines, "\"starPixels\":");
            lines[line - 1] = lines[line - 1].Replace("starPixels", "starPixles");
            var result = ThemeReader.LoadText(Join(lines));
            CollectionAssert.AreEquivalent(new[]
            {
                $"map.starPixles: is not a recognised field (check the spelling) (line {line})",
                $"map.starPixels: is missing (line {group})",
            }, result.Errors);
            AssertIsDefaults(result);
        }

        [Test]
        public void AnUnknownField_OrAnUnknownGroup_IsAnError_NamingItAndItsLine()
        {
            var lines = Lines().ToList();
            int line = LineOf(lines.ToArray(), "\"clickRadiusPixels\":");
            lines.Insert(line, "    \"doubleClickMs\": 300,");
            lines.Insert(1, "  \"sparkles\": { \"amount\": 3 },");
            var result = ThemeReader.LoadText(Join(lines.ToArray()));
            CollectionAssert.AreEquivalent(new[]
            {
                "sparkles: is not a recognised field (check the spelling) (line 2)",
                $"controls.doubleClickMs: is not a recognised field (check the spelling) (line {line + 2})",
            }, result.Errors);
            AssertIsDefaults(result);
        }

        [Test]
        public void OneBadField_SetsTheWholeFileAside_SoGoodEditsInTheSameFileAreNotUsedEither()
        {
            var lines = Lines();
            int good = LineOf(lines, "\"clickRadiusPixels\":");
            lines[good - 1] = "    \"clickRadiusPixels\": 60,";
            var fine = ThemeReader.LoadText(Join(lines));
            Assert.IsFalse(fine.UsedDefaults);
            Assert.AreEqual(60f, fine.Theme.Controls.ClickRadiusPixels, "a good edit alone is used");

            int bad = LineOf(lines, "\"starGlow\":");
            lines[bad - 1] = "    \"starGlow\": 12,";
            var result = ThemeReader.LoadText(Join(lines));
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.StartsWith("colours.starGlow: ", result.Errors[0]);
            StringAssert.EndsWith($"(line {bad})", result.Errors[0]);
            Assert.AreEqual(24f, result.Theme.Controls.ClickRadiusPixels, "as with tuning.json, one error sets the whole file aside");
            AssertIsDefaults(result);
        }

        [Test]
        public void AFontThatIsNotInTheFontsFolder_IsAnError_NamingTheFieldAndTheLine()
        {
            var lines = Lines();
            int line = LineOf(lines, "\"font\":");
            lines[line - 1] = "    \"font\": \"NoSuchFont\",";
            var result = ThemeReader.LoadText(Join(lines));
            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual($"text.font: there is no font file named \"NoSuchFont\" in Assets/Resources/Fonts (leave it as \"\" for the built-in font) (line {line})", result.Errors[0]);
            AssertIsDefaults(result);
        }

        [Test]
        public void AFileThatIsNotJson_OrIsMissing_GivesAMessage_AndTheDefaultsAreUsed()
        {
            string text = GameTestTheme.Text();
            var broken = ThemeReader.LoadText(text.Substring(0, text.Length / 2));
            Assert.AreEqual(1, broken.Errors.Count);
            StringAssert.StartsWith("The theme file is not valid JSON: ", broken.Errors[0]);
            StringAssert.Contains("line ", broken.Errors[0]);
            AssertIsDefaults(broken);

            string nowhere = Path.Combine(GameTestSettings.Folder, "no-such-folder", "theme.json");
            var missing = ThemeReader.LoadFile(nowhere);
            Assert.AreEqual(1, missing.Errors.Count);
            StringAssert.StartsWith("Could not read the theme file at " + nowhere, missing.Errors[0]);
            AssertIsDefaults(missing);
        }

        [Test]
        public void LoadingABadFile_LeavesTheGameOnTheDefaults_WithTheProblemsKept()
        {
            try
            {
                var result = Theme.LoadText("{ \"colours\": 5 }");
                Assert.IsTrue(result.UsedDefaults);
                Assert.Greater(Theme.Errors.Count, 0);
                StringAssert.StartsWith("colours: must be a group of fields in { }, but is 5 (line 1)", Theme.Errors[0]);
                Assert.AreEqual(ThemeReader.Write(new GameTheme()), ThemeReader.Write(Theme.Current));
            }
            finally
            {
                Theme.Use(null);
            }
        }

        [Test]
        public void TheBuiltInDefaults_HoldTheValuesTheGameHadBeforeTheThemeFile()
        {
            var t = new GameTheme();
            Assert.AreEqual(24f, t.Controls.ClickRadiusPixels, "spec 9.1's click radius");
            Assert.AreEqual(0.85f, t.Controls.ZoomPerWheelNotch);
            Assert.AreEqual(5f, t.Controls.MaxWheelNotchesPerFrame);
            Assert.AreEqual(0.8f, t.Controls.KeyPanScreensPerSecond);
            Assert.AreEqual(1.5f, t.Controls.ClosestZoomHalfHeightLy);
            Assert.AreEqual(12f, t.Controls.FitMarginPercent);
            Assert.AreEqual(1.5f, t.Controls.FitMarginLy);
            Assert.AreEqual(3f, t.Controls.PanMarginLy);
            Assert.AreEqual(5, t.Ships.MaxDockedIconsPerStar, "the 5-icon limit for docked ships");
            Assert.AreEqual(ShipNameRule.SelectedOrHovered, t.Labels.MovingShipNames);
            Assert.AreEqual(ThemeReader.Write(t), ThemeReader.Write(GameTestTheme.Load()), "the fixture holds the same values");
        }

        [Test]
        public void EveryThemeField_IsExplainedInTheThemeGuide()
        {
            string guidePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath)), "docs", "theme-guide.md");
            Assert.IsTrue(File.Exists(guidePath), guidePath);
            string guide = File.ReadAllText(guidePath);
            var missing = (from section in ThemeReader.FieldsOf(typeof(GameTheme))
                           from field in ThemeReader.FieldsOf(section.FieldType)
                           let name = "`" + ThemeReader.FileName(section) + "." + ThemeReader.FileName(field) + "`"
                           where !guide.Contains(name)
                           select name).ToList();
            CollectionAssert.IsEmpty(missing, "fields with no entry in docs/theme-guide.md");
        }
    }
}
