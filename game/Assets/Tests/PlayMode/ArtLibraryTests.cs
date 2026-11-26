using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    public class ArtLibraryTests
    {
        private static string ProjectFolder => Path.GetDirectoryName(Application.dataPath);
        private static string ChecklistPath => Path.Combine(ProjectFolder, "..", "docs", "graphics-checklist.md");

        [SetUp]
        public void LoadCatalog() => ArtLibrary.Load(Resources.Load<ArtCatalog>(ArtCatalog.ResourceName));

        /// <summary>Every image row in docs/graphics-checklist.md: folder/name and size, as listed.</summary>
        private static List<(string Key, int Width, int Height)> ChecklistRows()
        {
            var rows = new List<(string, int, int)>();
            string folder = null;
            foreach (var line in File.ReadAllLines(ChecklistPath))
            {
                var f = Regex.Match(line, @"^Folder: `game\\Assets\\Art\\(\w+)\\`");
                if (f.Success) folder = f.Groups[1].Value;
                var m = Regex.Match(line, @"^\| `([^`]+)\.png` \| [^|]+ \| [^|]+ \| (\d+) x (\d+) \|");
                if (m.Success) rows.Add(($"{folder}/{m.Groups[1].Value}", int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
            }
            return rows;
        }

        [Test]
        public void Manifest_MatchesTheGraphicsChecklist()
        {
            var rows = ChecklistRows();
            Assert.AreEqual(61, rows.Count, "images listed in the checklist");
            var listed = rows.Select(r => $"{r.Key} {r.Width}x{r.Height}").OrderBy(s => s).ToList();
            var manifest = ArtManifest.All.Select(a => $"{a.Key} {a.Width}x{a.Height}").OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(listed, manifest);
        }

        [Test]
        public void EveryListedImage_ExistsAtItsPath()
        {
            var missing = ArtManifest.All.Where(a => !File.Exists(Path.Combine(ProjectFolder, a.AssetPath))).Select(a => a.AssetPath).ToList();
            CollectionAssert.IsEmpty(missing);
            TestContext.WriteLine($"{ArtManifest.All.Count} of {ArtManifest.All.Count} listed images exist at their paths.");
        }

        [Test]
        public void EveryListedImage_LoadsFromItsFile_AtItsListedSize()
        {
            var problems = new List<string>();
            foreach (var a in ArtManifest.All)
            {
                var sprite = ArtLibrary.Get(a.Key);
                if (!ArtLibrary.HasFile(a.Key) || ArtLibrary.IsStandIn(sprite)) problems.Add(a.Key + ": stand-in, not the file");
                else if ((int)sprite.rect.width != a.Width || (int)sprite.rect.height != a.Height)
                    problems.Add($"{a.Key}: {sprite.rect.width}x{sprite.rect.height}, listed {a.Width}x{a.Height}");
            }
            CollectionAssert.IsEmpty(problems);
        }

        [Test]
        public void EveryIndustryAndCommodityInTheFixture_FindsItsIconFile()
        {
            var t = GameTestTuning.Load();
            var industryKeys = t.Industries.Select(i => ArtLibrary.IndustryIconKey(i.Id)).ToList();
            var commodityKeys = t.Commodities.Select(c => ArtLibrary.CommodityIconKey(c.Id)).ToList();
            Assert.AreEqual(6, industryKeys.Count);
            Assert.AreEqual(6, commodityKeys.Count);
            CollectionAssert.IsEmpty(industryKeys.Concat(commodityKeys).Where(k => !ArtLibrary.HasFile(k)).ToList());
            CollectionAssert.AreEqual(
                new[] { "Icons/industry_mining", "Icons/industry_refinery", "Icons/industry_factory", "Icons/industry_agriculture", "Icons/industry_population", "Icons/industry_shipyard" },
                industryKeys);
            CollectionAssert.AreEqual(
                new[] { "Icons/cargo_ore", "Icons/cargo_metals", "Icons/cargo_chemicals", "Icons/cargo_machinery", "Icons/cargo_consumer_goods", "Icons/cargo_food" },
                commodityKeys);
        }

        [Test]
        public void NewIds_LookUpByTheirId_AndGetAStandIn_WithNoConsoleMessage()
        {
            Assert.AreEqual("Icons/industry_ice_harvester", ArtLibrary.IndustryIconKey("ice_harvester"));
            Assert.AreEqual("Icons/cargo_luxury_goods", ArtLibrary.CommodityIconKey("luxury_goods"));
            var industry = ArtLibrary.IndustryIcon("ice_harvester");
            var commodity = ArtLibrary.CommodityIcon("luxury_goods");
            Assert.IsTrue(ArtLibrary.IsStandIn(industry));
            Assert.IsTrue(ArtLibrary.IsStandIn(commodity));
            Assert.Greater(industry.rect.width, 0f);
            Assert.AreSame(industry, ArtLibrary.IndustryIcon("ice_harvester"), "a stand-in is made once per name");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WithNoCatalogAtAll_EverythingIsAStandIn()
        {
            ArtLibrary.Load(null);
            Assert.IsTrue(ArtManifest.All.All(a => ArtLibrary.IsStandIn(ArtLibrary.Get(a.Key))));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
