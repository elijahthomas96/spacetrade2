using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Settings;
using SpaceTrade.Game.Theming;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The settings panel under the gear (spec 9.6): master, effects, and music volume from 0 to 100, and mute. A change is
    /// heard at once. The settings are written to their own file when a slider is let go, when mute is clicked, and when
    /// the panel closes; they are never part of a game save.
    /// </summary>
    public sealed class SettingsPanel
    {
        private readonly VisualElement _panel;
        private readonly SliderInt _master, _effects, _music;
        private readonly Label _masterValue, _effectsValue, _musicValue;
        private readonly Button _mute;
        private GameAudio _audio;
        private GameSettings _settings = new GameSettings();
        private SettingsStore _store;
        private bool _dirty, _binding;

        public bool IsOpen { get; private set; }
        public Rect Rect => _panel.layout;
        public VisualElement Panel => _panel;
        public GameSettings Settings => _settings;

        /// <summary>How many times the settings file has been written by this panel.</summary>
        public int Saves { get; private set; }

        /// <summary>What went wrong the last time the settings could not be written, or null.</summary>
        public string LastSaveProblem { get; private set; }

        public SettingsPanel(VisualElement root)
        {
            var t = Theme.Settings;
            _panel = new VisualElement { name = "settings-panel" };
            _panel.style.position = Position.Absolute;
            _panel.style.top = Theme.TopBar.Height;
            _panel.style.right = Theme.Panels.Margin;
            _panel.style.width = t.Width;
            UiKit.Panel(_panel, Theme.Colours.Dropdown, Theme.Panels.Padding);
            _panel.style.display = DisplayStyle.None;
            _panel.Add(UiKit.Title("Settings", Theme.Text.Title));

            _master = AddSlider("Master", "master-volume", out _masterValue);
            _effects = AddSlider("Effects", "effects-volume", out _effectsValue);
            _music = AddSlider("Music", "music-volume", out _musicValue);

            _mute = UiKit.TextButton("", ToggleMute, null);
            _mute.name = "mute";
            _mute.style.marginTop = t.RowGap;
            _mute.style.alignSelf = Align.FlexStart;
            _panel.Add(_mute);
            _panel.RegisterCallback<PointerCaptureOutEvent>(_ => Commit());
            _panel.RegisterCallback<FocusOutEvent>(_ => Commit());
            root.Add(_panel);
            ShowValues();
        }

        private SliderInt AddSlider(string caption, string name, out Label value)
        {
            var t = Theme.Settings;
            var row = UiKit.Row();
            row.style.marginTop = t.RowGap;
            var cap = UiKit.Body(caption, Theme.Colours.TextDim);
            cap.style.width = t.CaptionWidth;
            row.Add(cap);
            var slider = UiKit.Slider(GameSettings.MaxVolume, 0, _ => OnSliders());
            slider.name = name;
            row.Add(slider);
            value = UiKit.Body("", Theme.Colours.Text);
            value.style.width = t.ValueWidth;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(value);
            _panel.Add(row);
            return slider;
        }

        /// <summary>The sound player whose settings this panel edits, and where they are kept (null: nowhere).</summary>
        public void Bind(GameAudio audio, SettingsStore store)
        {
            Commit();
            _audio = audio;
            _settings = audio != null ? audio.Settings : new GameSettings();
            _store = store;
            _dirty = false;
            ShowValues();
        }

        public void Open()
        {
            IsOpen = true;
            ShowValues();
            _panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            IsOpen = false;
            _panel.style.display = DisplayStyle.None;
            Commit();
        }

        private void OnSliders()
        {
            if (_binding) return;
            Set(_master.value, _effects.value, _music.value, _settings.Muted, false);
        }

        private void ToggleMute()
        {
            Set(_settings.MasterVolume, _settings.EffectsVolume, _settings.MusicVolume, !_settings.Muted, true);
            GameAudio.Play(_settings.Muted ? SoundId.ToggleOn : SoundId.ToggleOff, "mute " + (_settings.Muted ? "on" : "off"));
        }

        /// <summary>Sets one volume the way dragging its slider does. Call <see cref="Commit"/> to write the file, as letting go does.</summary>
        public void SetVolume(SoundGroupSlider which, int value)
        {
            value = Mathf.Clamp(value, 0, GameSettings.MaxVolume);
            Set(which == SoundGroupSlider.Master ? value : _settings.MasterVolume,
                which == SoundGroupSlider.Effects ? value : _settings.EffectsVolume,
                which == SoundGroupSlider.Music ? value : _settings.MusicVolume, _settings.Muted, false);
        }

        /// <summary>Sets mute the way clicking the mute button does, and writes the file.</summary>
        public void SetMuted(bool muted) => Set(_settings.MasterVolume, _settings.EffectsVolume, _settings.MusicVolume, muted, true);

        private void Set(int master, int effects, int music, bool muted, bool commit)
        {
            if (master != _settings.MasterVolume || effects != _settings.EffectsVolume || music != _settings.MusicVolume || muted != _settings.Muted) _dirty = true;
            _settings.MasterVolume = master;
            _settings.EffectsVolume = effects;
            _settings.MusicVolume = music;
            _settings.Muted = muted;
            if (_audio != null) _audio.ApplyVolumes();
            ShowValues();
            if (commit) Commit();
        }

        /// <summary>Writes the settings file if anything changed since it was last written.</summary>
        public void Commit()
        {
            if (!_dirty || _store == null) { _dirty = false; return; }
            _dirty = false;
            try
            {
                _store.Save(_settings);
                Saves++;
                LastSaveProblem = null;
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                LastSaveProblem = $"The settings could not be saved to {_store.Path}: {e.Message}";
                Debug.LogWarning(LastSaveProblem);
            }
        }

        private void ShowValues()
        {
            _binding = true;
            _master.SetValueWithoutNotify(_settings.MasterVolume);
            _effects.SetValueWithoutNotify(_settings.EffectsVolume);
            _music.SetValueWithoutNotify(_settings.MusicVolume);
            _binding = false;
            _masterValue.text = _settings.MasterVolume.ToString();
            _effectsValue.text = _settings.EffectsVolume.ToString();
            _musicValue.text = _settings.MusicVolume.ToString();
            _mute.text = _settings.Muted ? "Muted: click to unmute" : "Mute all sound";
            UiKit.SetSelected(_mute, _settings.Muted);
        }
    }

    public enum SoundGroupSlider
    {
        Master,
        Effects,
        Music,
    }
}
