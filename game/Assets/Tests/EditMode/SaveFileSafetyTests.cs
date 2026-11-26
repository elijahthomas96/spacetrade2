using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceTrade.Sim.Saves;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 11.2: atomic writes with a backup, damaged files, version checks, and failed writes.</summary>
    public class SaveFileSafetyTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        /// <summary>Two saves of different states in slot "a": the first becomes the backup.</summary>
        private (Simulation older, byte[] olderBytes, Simulation newer, byte[] newerBytes) TwoSaves()
        {
            var older = RouteFleet();
            byte[] olderBytes = SaveAt(_folder, "a", older, T0);
            var newer = RouteFleet(runTicks: 9 * Year);
            byte[] newerBytes = SaveAt(_folder, "a", newer, T0 + Hour);
            return (older, olderBytes, newer, newerBytes);
        }

        private string Text(string name) => File.ReadAllText(_folder.File(name));
        private void WriteText(string name, string text) => File.WriteAllBytes(_folder.File(name), new UTF8Encoding(false).GetBytes(text));

        [Test]
        public void Write_KeepsThePreviousSaveAsTheBackup_AndLeavesNoTemporaryFile()
        {
            var (_, olderBytes, _, newerBytes) = TwoSaves();
            CollectionAssert.AreEqual(new[] { "a.json", "a.json.bak" }, _folder.FileNames());
            CollectionAssert.AreEqual(newerBytes, Bytes(_folder, "a.json"));
            CollectionAssert.AreEqual(olderBytes, Bytes(_folder, "a.json.bak"));
        }

        [Test]
        public void TruncatedSave_IsRefusedClearly_TheBackupLoads_AndTheDamagedFileIsKept()
        {
            var (older, _, _, newerBytes) = TwoSaves();
            File.WriteAllBytes(_folder.File("a.json"), newerBytes.Take(newerBytes.Length / 2).ToArray());

            var r = _folder.Store.Load("a", FlatTuning(), T0 + 2 * Hour);
            Assert.IsTrue(r.Succeeded);
            Assert.IsTrue(r.UsedBackup);
            Assert.AreEqual(T0, r.SavedUtcMs);
            Assert.AreEqual(State(older), State(r.Simulation), "The backup's game must load whole.");
            StringAssert.Contains("The save \"a\" is damaged: it is not complete, readable save data", r.Messages[0]);
            StringAssert.Contains("Loaded the backup saved 2026-11-25 12:00 UTC instead. The damaged file was kept as a.json.damaged-20261125-140000.", r.Messages[1]);
            CollectionAssert.AreEqual(new[] { "a.json.bak", "a.json.damaged-20261125-140000" }, _folder.FileNames());
            CollectionAssert.AreEqual(newerBytes.Take(newerBytes.Length / 2).ToArray(), Bytes(_folder, "a.json.damaged-20261125-140000"), "The damaged file is kept as it was.");

            byte[] backupBefore = Bytes(_folder, "a.json.bak");
            Ok(_folder.Store.Write("a", r.Simulation, T0 + 2 * Hour));
            CollectionAssert.AreEqual(backupBefore, Bytes(_folder, "a.json.bak"), "The next save starts a fresh main file and the good backup stays.");
        }

        [Test]
        public void OneChangedDigit_StillValidText_IsCaughtByTheChecksum_AndTheBackupLoads()
        {
            var (older, _, newer, _) = TwoSaves();
            string text = Text("a.json");
            string credits = $"\"credits\": {newer.Credits}";
            StringAssert.Contains(credits, text);
            WriteText("a.json", text.Replace(credits, $"\"credits\": {newer.Credits + 1}"));

            var r = _folder.Store.Load("a", FlatTuning(), T0);
            Assert.IsTrue(r.UsedBackup);
            StringAssert.Contains("its checksum does not match its contents", r.Messages[0]);
            Assert.AreEqual(State(older), State(r.Simulation));
        }

        [Test]
        public void ValidChecksumButInconsistentContents_IsDamaged_NothingHalfLoaded_TheBackupLoads()
        {
            var (older, _, newer, _) = TwoSaves();
            var inFlight = newer.Ships.First(s => s.IsInTransit);
            string text = Text("a.json");
            string arrival = $"\"tick\": {inFlight.Voyage.ArrivalTick},\n          \"type\": \"arrival\"";
            StringAssert.Contains(arrival, text);
            WriteText("a.json", SaveFormat.Seal(text.Replace(arrival, $"\"tick\": {inFlight.Voyage.ArrivalTick + 1},\n          \"type\": \"arrival\"")));

            var r = _folder.Store.Load("a", FlatTuning(), T0);
            Assert.IsTrue(r.UsedBackup);
            StringAssert.Contains($"does not match any voyage in flight", r.Messages[0]);
            Assert.AreEqual(State(older), State(r.Simulation));
        }

        [Test]
        public void MainAndBackupBothDamaged_NothingLoads_AndNoFileChanges()
        {
            TwoSaves();
            File.WriteAllText(_folder.File("a.json"), "{ \"format\": \"spacetrade2-save\", ");
            File.WriteAllText(_folder.File("a.json.bak"), "");
            byte[] main = Bytes(_folder, "a.json"), backup = Bytes(_folder, "a.json.bak");

            var r = _folder.Store.Load("a", FlatTuning(), T0);
            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(r.Simulation);
            StringAssert.Contains("The save \"a\" is damaged", r.Messages[0]);
            StringAssert.Contains("The backup could not be loaded either", r.Messages[0]);
            StringAssert.Contains("Nothing was loaded and no file was changed.", r.Messages[0]);
            CollectionAssert.AreEqual(new[] { "a.json", "a.json.bak" }, _folder.FileNames());
            CollectionAssert.AreEqual(main, Bytes(_folder, "a.json"));
            CollectionAssert.AreEqual(backup, Bytes(_folder, "a.json.bak"));
        }

        [Test]
        public void MissingMainFile_LoadsTheBackup()
        {
            var (older, _, _, _) = TwoSaves();
            File.Delete(_folder.File("a.json"));
            var r = _folder.Store.Load("a", FlatTuning(), T0);
            Assert.IsTrue(r.UsedBackup);
            StringAssert.Contains("The save \"a\" is missing.", r.Messages[0]);
            Assert.AreEqual(State(older), State(r.Simulation));
            CollectionAssert.AreEqual(new[] { "a.json.bak" }, _folder.FileNames());
        }

        [Test]
        public void NoSuchSave_IsRefused()
        {
            var r = _folder.Store.Load("nothing", FlatTuning(), T0);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("There is no save named \"nothing\".", r.Messages.Single());
        }

        [TestCase(3, "it was made by a newer version of spacetrade2 (save format 3; this game reads formats 1 to 2)")]
        [TestCase(99, "it was made by a newer version of spacetrade2 (save format 99; this game reads formats 1 to 2)")]
        [TestCase(0, "its save format 0 is not one spacetrade2 has ever written (this game reads formats 1 to 2)")]
        public void NewerOrUnknownVersion_IsRefusedClearly_TheBackupIsNotUsed_AndNothingChanges(int version, string reason)
        {
            TwoSaves();
            WriteText("a.json", SaveFormat.Seal(Text("a.json").Replace("\"version\": 2,", $"\"version\": {version},")));
            var names = _folder.FileNames();
            byte[] main = Bytes(_folder, "a.json"), backup = Bytes(_folder, "a.json.bak");

            var r = _folder.Store.Load("a", FlatTuning(), T0);
            Assert.IsFalse(r.Succeeded);
            Assert.IsFalse(r.UsedBackup);
            Assert.AreEqual($"The save \"a\" was not loaded: {reason}. Nothing was changed.", r.Messages.Single());
            CollectionAssert.AreEqual(names, _folder.FileNames());
            CollectionAssert.AreEqual(main, Bytes(_folder, "a.json"));
            CollectionAssert.AreEqual(backup, Bytes(_folder, "a.json.bak"));
        }

        [Test]
        public void NotASaveFile_IsDamaged()
        {
            File.WriteAllText(_folder.File("tuning.json"), TestTuning.Text());
            var r = _folder.Store.Load("tuning", FlatTuning(), T0);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("The save \"tuning\" is damaged: it is not a spacetrade2 save file. There is no backup. Nothing was loaded and no file was changed.", r.Messages.Single());
        }

        [Test]
        public void FailedWrite_TemporaryFileBlocked_LeavesThePreviousSaveIntact()
        {
            var (_, olderBytes, _, newerBytes) = TwoSaves();
            Directory.CreateDirectory(_folder.File("a.json.tmp"));

            var r = _folder.Store.Write("a", NewSim(1), T0 + 2 * Hour);
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("a folder named a.json.tmp is in the way. The previous save is unchanged.", r.Problem);
            CollectionAssert.AreEqual(newerBytes, Bytes(_folder, "a.json"));
            CollectionAssert.AreEqual(olderBytes, Bytes(_folder, "a.json.bak"));
            Assert.IsTrue(Directory.Exists(_folder.File("a.json.tmp")), "The store only removes what it created.");

            Directory.Delete(_folder.File("a.json.tmp"));
            Ok(_folder.Store.Write("a", NewSim(1), T0 + 3 * Hour));
            CollectionAssert.AreEqual(newerBytes, Bytes(_folder, "a.json.bak"));
        }

        [Test]
        public void FailedWrite_SaveCannotBeSwappedIn_LeavesThePreviousSaveIntact_AndRemovesItsTemporaryFile()
        {
            var (_, olderBytes, _, newerBytes) = TwoSaves();
            SaveWriteResult r;
            using (new FileStream(_folder.File("a.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                r = _folder.Store.Write("a", NewSim(1), T0 + 2 * Hour);
                Assert.IsTrue(_folder.Store.Load("a", FlatTuning(), T0).Succeeded, "The locked save can still be read.");
            }
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("The game could not be saved to \"a\":", r.Problem);
            StringAssert.EndsWith("The previous save is unchanged.", r.Problem);
            CollectionAssert.AreEqual(new[] { "a.json", "a.json.bak" }, _folder.FileNames());
            CollectionAssert.AreEqual(newerBytes, Bytes(_folder, "a.json"));
            CollectionAssert.AreEqual(olderBytes, Bytes(_folder, "a.json.bak"));
        }

        [TestCase("", "A save needs a name.")]
        [TestCase("../escape", "A save name can use letters, digits, spaces, '-' and '_'; \"../escape\" has '.'.")]
        [TestCase(" padded", "A save name cannot start or end with a space.")]
        public void BadSlotName_IsRefused_AndNothingIsWritten(string slot, string reason)
        {
            var r = _folder.Store.Write(slot, NewSim(1), T0);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(reason + " Nothing was saved.", r.Problem);
            CollectionAssert.IsEmpty(_folder.FileNames());
        }

        [Test]
        public void ListSlots_ShowsSavesOnly()
        {
            SaveAt(_folder, "Second", NewSim(1));
            SaveAt(_folder, "First game", NewSim(1));
            SaveAt(_folder, "First game", NewSim(1));
            File.WriteAllText(_folder.File("notes.txt"), "x");
            CollectionAssert.AreEqual(new[] { "First game", "Second" }, _folder.Store.ListSlots());
        }
    }
}
