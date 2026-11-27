using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Settings;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Saves;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Volume and mute (spec 9.6): each slider at zero silences its own group, mute silences everything at once, and the
    /// settings are remembered between runs in their own file, never in the game save.
    /// </summary>
    public class VolumeAndSettingsTests
    {
        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        private GameAudio Audio => _rig.Audio;
        private SettingsPanel Panel => _rig.Map.Bar.Settings;

        /// <summary>Opens the game with the close-up open, so the music loop and an effects-group ambience loop are both running.</summary>
        private IEnumerator OpenWithLoops()
        {
            var sim = MapTestRig.FleetGame(12, 3);
            Assert.IsTrue(sim.Play().Succeeded, "a new game starts paused; these tests run the clock");
            yield return _rig.Open(sim);
            _rig.Map.OpenCloseUp();
            _rig.Map.Bar.ToggleSettings();
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(SoundId.MusicMap, Audio.Music);
            Assert.AreEqual(SoundId.ShipCloseUpDrive, Audio.Ambience);
            Assert.IsTrue(Audio.IsMusicAudible, "setup: music is playing");
            Assert.IsTrue(Audio.IsAmbienceAudible, "setup: the close-up loop is playing");
        }

        /// <summary>Asks for every short effect over several frames, and lets an arrival happen. Returns how many sounds started.</summary>
        private IEnumerator TryEveryEffect(System.Action<int> started)
        {
            int total = Audio.StartedTotal;
            foreach (var info in SoundManifest.All.Where(i => !i.Loops))
            {
                GameAudio.Play(info.Id, "test");
                yield return null;
            }
            var sim = _rig.Sim;
            long next = sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
            yield return _rig.RealMs(next - sim.CurrentTick);
            started(Audio.StartedTotal - total);
        }

        [UnityTest]
        public IEnumerator EffectsSliderAtZero_SilencesEffectsAndTheCloseUpLoops_ButNotTheMusic()
        {
            yield return OpenWithLoops();
            Panel.SetVolume(SoundGroupSlider.Effects, 0);
            Assert.AreEqual(0f, Audio.VolumeOf(SoundGroup.Effects));
            Assert.IsFalse(Audio.IsAmbienceAudible, "the close-up loop falls silent at once");
            Assert.IsTrue(Audio.IsMusicAudible, "the music plays on");
            Assert.AreEqual(1f, Audio.VolumeOf(SoundGroup.Music), 0.0001f);

            int started = -1;
            int silenced = Audio.SilencedByVolume;
            yield return TryEveryEffect(n => started = n);
            Assert.AreEqual(0, started, "no effect started while the effects slider was at zero");
            Assert.GreaterOrEqual(Audio.SilencedByVolume - silenced, 21, "all 20 effects and the arrival were asked for and silenced");
            Assert.IsFalse(Audio.IsAnyEffectAudible);
            Assert.IsTrue(Audio.IsMusicAudible);

            Panel.SetVolume(SoundGroupSlider.Effects, 50);
            Assert.IsTrue(Audio.IsAmbienceAudible, "raising the slider brings the loop back");
            Assert.AreEqual(0.5f, Audio.VolumeOf(SoundGroup.Effects), 0.0001f);
            yield return null;
            Assert.IsTrue(Audio.PlayNow(SoundId.Click, "test"), "and effects play again");
            Assert.AreEqual(0.5f, Audio.History.Last().Volume, 0.0001f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MusicSliderAtZero_SilencesTheMusic_ButNotTheEffects()
        {
            yield return OpenWithLoops();
            Panel.SetVolume(SoundGroupSlider.Music, 0);
            Assert.IsFalse(Audio.IsMusicAudible, "the music falls silent at once");
            Assert.IsTrue(Audio.IsAmbienceAudible, "the close-up loop follows the effects slider, so it plays on");

            int started = -1;
            yield return TryEveryEffect(n => started = n);
            Assert.GreaterOrEqual(started, 21, "every effect still plays");
            Assert.IsFalse(Audio.IsMusicAudible);

            Panel.SetVolume(SoundGroupSlider.Music, 30);
            Assert.IsTrue(Audio.IsMusicAudible);
            Assert.AreEqual(0.3f, Audio.VolumeOf(SoundGroup.Music), 0.0001f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MasterSliderAtZero_SilencesBothGroups()
        {
            yield return OpenWithLoops();
            Panel.SetVolume(SoundGroupSlider.Master, 0);
            Assert.IsFalse(Audio.IsMusicAudible);
            Assert.IsFalse(Audio.IsAmbienceAudible);
            int started = -1;
            yield return TryEveryEffect(n => started = n);
            Assert.AreEqual(0, started, "no effect started while the master slider was at zero");
            Assert.IsFalse(Audio.IsAnyEffectAudible);

            Panel.SetVolume(SoundGroupSlider.Master, 40);
            Assert.IsTrue(Audio.IsMusicAudible);
            Assert.IsTrue(Audio.IsAmbienceAudible);
            Assert.AreEqual(0.4f, Audio.VolumeOf(SoundGroup.Effects), 0.0001f, "master times the group's own slider");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Mute_SilencesEverythingAtOnce_IncludingSoundsAlreadyPlaying_AndUnmuteBringsItBack()
        {
            yield return OpenWithLoops();
            Assert.IsTrue(Audio.PlayNow(SoundId.Arrival, "test"), "an effect is playing when mute is clicked");
            Assert.IsTrue(Audio.IsAnyEffectAudible);

            TopBarTests.Press(Panel.Panel.Q<Button>("mute"));
            yield return null;
            Assert.IsTrue(Audio.Settings.Muted, "the mute button mutes");
            Assert.IsFalse(Audio.IsAnyEffectAudible, "the effect already playing is silenced");
            Assert.IsFalse(Audio.IsMusicAudible, "so is the music");
            Assert.IsFalse(Audio.IsAmbienceAudible, "and the close-up loop");
            Assert.AreEqual(100, Audio.Settings.MasterVolume, "the sliders keep their places");

            yield return null;
            int started = -1;
            yield return TryEveryEffect(n => started = n);
            Assert.AreEqual(0, started, "nothing starts while muted");

            TopBarTests.Press(Panel.Panel.Q<Button>("mute"));
            yield return null;
            Assert.IsFalse(Audio.Settings.Muted);
            Assert.IsTrue(Audio.IsMusicAudible, "unmuting brings the music back");
            Assert.IsTrue(Audio.IsAmbienceAudible);
            Assert.IsTrue(Audio.PlayNow(SoundId.Select, "test"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheSliders_SetTheVolumes_AndShowTheirValues()
        {
            yield return OpenWithLoops();
            var master = Panel.Panel.Q<SliderInt>("master-volume");
            var effects = Panel.Panel.Q<SliderInt>("effects-volume");
            var music = Panel.Panel.Q<SliderInt>("music-volume");
            Assert.AreEqual(100, master.value);
            Assert.AreEqual(0, master.lowValue);
            Assert.AreEqual(100, master.highValue);

            master.value = 50;
            effects.value = 20;
            music.value = 0;
            Assert.AreEqual("master=50 effects=20 music=0 muted=false", Audio.Settings.Describe(), "dragging a slider changes the setting");
            Assert.AreEqual(0.1f, Audio.VolumeOf(SoundGroup.Effects), 0.0001f);
            Assert.AreEqual(0f, Audio.VolumeOf(SoundGroup.Music));
            Assert.IsFalse(Audio.IsMusicAudible);
            Assert.IsTrue(Audio.IsAmbienceAudible);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Settings_AreRememberedBetweenRuns_InTheirOwnFile_AndAreNotInTheGameSave()
        {
            yield return OpenWithLoops();
            string path = _rig.SettingsStore.Path;
            Assert.IsFalse(File.Exists(path), "nothing is written until a setting changes");

            Panel.SetVolume(SoundGroupSlider.Master, 70);
            Panel.SetVolume(SoundGroupSlider.Effects, 35);
            Panel.SetVolume(SoundGroupSlider.Music, 5);
            Assert.AreEqual(0, Panel.Saves, "dragging does not write the file on every step");
            Panel.Commit();
            Assert.AreEqual(1, Panel.Saves, "letting go writes it once");
            Panel.Commit();
            Assert.AreEqual(1, Panel.Saves, "and not again while nothing changed");
            Panel.SetMuted(true);
            Assert.AreEqual(2, Panel.Saves, "mute is written at once");
            Assert.IsNull(Panel.LastSaveProblem);
            string text = File.ReadAllText(path);
            TestContext.WriteLine(text);

            // The next run: a new store reading the same file, and a new sound player using what it read.
            var store = new SettingsStore(path);
            var loaded = store.Load();
            Assert.IsNull(store.LastProblem);
            Assert.AreEqual("master=70 effects=35 music=5 muted=true", loaded.Describe());
            var second = GameAudio.Create(null, loaded);
            try
            {
                Assert.AreEqual(0f, second.VolumeOf(SoundGroup.Effects), "muted in the next run too");
                loaded.Muted = false;
                second.ApplyVolumes();
                Assert.AreEqual(0.245f, second.VolumeOf(SoundGroup.Effects), 0.0001f);
                Assert.AreEqual(0.035f, second.VolumeOf(SoundGroup.Music), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(second.gameObject);
            }

            // The game save holds none of it, and saving the game does not touch the settings file.
            string saveFolder = Path.Combine(Path.GetDirectoryName(path), "saves");
            var saves = new SaveStore(saveFolder);
            var written = saves.Write("test", _rig.Sim, 1_700_000_000_000);
            Assert.IsTrue(written.Succeeded, written.Problem);
            string save = File.ReadAllText(saves.PathFor("test"));
            Assert.Greater(save.Length, 1000, "the game was saved");
            CollectionAssert.AreEquivalent(new[] { "test.json" }, Directory.GetFiles(saveFolder).Select(Path.GetFileName).ToList(), "the save folder holds the save and nothing else");
            foreach (string word in new[] { "volume", "muted", "mute", "settings" })
                StringAssert.DoesNotContain(word, save.ToLowerInvariant(), "the game save holds no settings");
            Assert.AreEqual(text, File.ReadAllText(path));

            // Closing the panel writes a change that was not yet written.
            Panel.SetVolume(SoundGroupSlider.Music, 44);
            _rig.Map.Bar.ToggleSettings();
            Assert.AreEqual(44, new SettingsStore(path).Load().MusicVolume, "closing the panel keeps the last change");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AMissingOrDamagedSettingsFile_GivesTheDefaults_AndIsReplacedAtTheNextChange()
        {
            string path = GameTestSettings.NewPath();
            try
            {
                var store = new SettingsStore(path);
                var defaults = store.Load();
                Assert.IsNull(store.LastProblem, "a missing file is not a problem");
                Assert.AreEqual("master=80 effects=80 music=60 muted=false", defaults.Describe());

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                foreach (string bad in new[] { "{ not json", "[1, 2]", "{ \"masterVolume\": 150, \"effectsVolume\": 80, \"musicVolume\": 60, \"muted\": false }", "{ \"masterVolume\": 50 }", "" })
                {
                    File.WriteAllText(path, bad);
                    var s = store.Load();
                    Assert.AreEqual(defaults.Describe(), s.Describe(), "defaults for: " + bad);
                    StringAssert.Contains("so the default settings are used", store.LastProblem);
                    StringAssert.Contains(path, store.LastProblem);
                }

                store.Save(new GameSettings { MasterVolume = 1, EffectsVolume = 2, MusicVolume = 3, Muted = true });
                Assert.AreEqual("master=1 effects=2 music=3 muted=true", store.Load().Describe());
                Assert.IsNull(store.LastProblem);
                Assert.IsFalse(File.Exists(path + ".tmp"), "no temporary file is left behind");
            }
            finally
            {
                GameTestSettings.Remove(path);
            }
        }

        [Test]
        public void TheRealSettingsFile_IsBesideTheSaves_AndTestsNeverUseIt()
        {
            Assert.AreEqual(Path.Combine(Application.persistentDataPath, "settings.json"), GameBootstrap.SettingsPath);
            string testPath = GameTestSettings.NewPath();
            StringAssert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(testPath));
            StringAssert.DoesNotStartWith(Path.GetFullPath(Application.persistentDataPath), Path.GetFullPath(testPath));
        }

        [Test]
        public void EverySound_InTheAudioChecklist_HasItsFile_AndARealClip()
        {
            string checklist = File.ReadAllText(Path.Combine(Application.dataPath, "..", "..", "docs", "audio-checklist.md"));
            var listed = System.Text.RegularExpressions.Regex.Matches(checklist, @"- \[.\] `([a-z_]+\.(wav|ogg))`").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(24, listed.Count, "the checklist lists 24 files");
            CollectionAssert.AreEquivalent(listed, SoundManifest.All.Select(i => Path.GetFileName(i.File)).ToList(), "the game's list is the checklist's list");
            Assert.AreEqual(20, SoundManifest.All.Count(i => !i.Loops));
            Assert.AreEqual(4, SoundManifest.All.Count(i => i.Loops));
            for (int i = 0; i < SoundManifest.All.Count; i++) Assert.AreEqual(i, (int)SoundManifest.All[i].Id, "the list is in the order of the sound ids");

            var catalog = Resources.Load<AudioCatalog>(AudioCatalog.ResourceName);
            Assert.IsNotNull(catalog, "the audio catalog exists");
            foreach (var info in SoundManifest.All)
            {
                string file = Path.Combine(Application.dataPath, "Audio", info.File);
                Assert.IsTrue(File.Exists(file), "missing file: " + file);
                var clip = catalog.Find(info.Key);
                Assert.IsNotNull(clip, "no clip in the catalog for " + info.File);
                Assert.Greater(clip.length, 0.01f, info.File + " has a length");
                TestContext.WriteLine($"{info.File}: {clip.length:0.00} s, {clip.channels} channel(s), {clip.frequency} Hz, group {info.Group}{(info.Loops ? ", loops" : "")}");
            }
        }
    }
}
