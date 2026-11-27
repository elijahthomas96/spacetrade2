using System.Collections.Generic;
using SpaceTrade.Game.Settings;
using UnityEngine;

namespace SpaceTrade.Game.Audio
{
    /// <summary>One sound that was started, kept so tests and the debug log can see exactly what played and when.</summary>
    public readonly struct PlayedSound
    {
        public readonly SoundId Id;
        public readonly int Frame;
        public readonly float Volume;
        public readonly string Why;

        public PlayedSound(SoundId id, int frame, float volume, string why)
        {
            Id = id;
            Frame = frame;
            Volume = volume;
            Why = why;
        }
    }

    /// <summary>
    /// Plays every sound in the game (spec 9.6). Short effects are asked for by event and each plays at most once per
    /// frame, however many times it is asked for. Two loops can run at once: music, and the close-up's ambience.
    /// Volumes come from the player's settings: master times the group's own slider; mute silences everything at once.
    /// A group whose volume is zero starts no sound at all.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const int EffectVoices = 8;
        private const int HistoryLimit = 256;

        /// <summary>The game's sound player, or null when there is none (sounds asked for are then simply not played).</summary>
        public static GameAudio Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => Current = null;

        private AudioCatalog _catalog;
        private readonly AudioSource[] _effects = new AudioSource[EffectVoices];
        private AudioSource _music, _ambience;
        private SoundId? _musicId, _ambienceId;
        private readonly Dictionary<SoundId, int> _lastFrame = new Dictionary<SoundId, int>();
        private readonly Dictionary<SoundId, int> _started = new Dictionary<SoundId, int>();
        private readonly List<PlayedSound> _history = new List<PlayedSound>();
        private int _nextVoice;

        public GameSettings Settings { get; private set; } = new GameSettings();

        /// <summary>Debug only: writes one console line for every sound event as it fires.</summary>
        public bool LogEvents { get; set; }

        /// <summary>The newest sounds started, oldest first (at most 256 are kept).</summary>
        public IReadOnlyList<PlayedSound> History => _history;

        /// <summary>How many sounds have been started since this player was created.</summary>
        public int StartedTotal { get; private set; }

        /// <summary>How many requests were dropped because the same sound had already been asked for in that frame.</summary>
        public int DroppedSameFrame { get; private set; }

        /// <summary>How many requests started nothing because their group's volume was zero or everything was muted.</summary>
        public int SilencedByVolume { get; private set; }

        public SoundId? Music => _musicId;
        public SoundId? Ambience => _ambienceId;

        public static GameAudio Create(Transform parent, GameSettings settings, AudioCatalog catalog = null)
        {
            var go = new GameObject("Audio");
            if (parent != null) go.transform.SetParent(parent, false);
            var audio = go.AddComponent<GameAudio>();
            audio._catalog = catalog != null ? catalog : Resources.Load<AudioCatalog>(AudioCatalog.ResourceName);
            audio.Settings = settings ?? new GameSettings();
            for (int i = 0; i < EffectVoices; i++) audio._effects[i] = audio.AddSource("Effect " + i, false);
            audio._music = audio.AddSource("Music", true);
            audio._ambience = audio.AddSource("Ambience", true);
            if (FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            audio.ApplyVolumes();
            Current = audio;
            return audio;
        }

        private AudioSource AddSource(string sourceName, bool loop)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>The volume a group plays at, 0 to 1: master times the group's slider, or 0 when muted.</summary>
        public float VolumeOf(SoundGroup group)
        {
            if (Settings.Muted) return 0f;
            int own = group == SoundGroup.Music ? Settings.MusicVolume : Settings.EffectsVolume;
            return Settings.MasterVolume * own / (float)(GameSettings.MaxVolume * GameSettings.MaxVolume);
        }

        /// <summary>Call after changing <see cref="Settings"/>: sounds already playing follow at once.</summary>
        public void ApplyVolumes()
        {
            float effects = VolumeOf(SoundGroup.Effects);
            foreach (var s in _effects) Apply(s, effects);
            Apply(_music, _musicId.HasValue ? VolumeOf(SoundManifest.Of(_musicId.Value).Group) : 0f);
            Apply(_ambience, _ambienceId.HasValue ? VolumeOf(SoundManifest.Of(_ambienceId.Value).Group) : 0f);
        }

        private static void Apply(AudioSource s, float volume)
        {
            if (s == null) return;
            s.volume = volume;
            s.mute = volume <= 0f;
        }

        /// <summary>Asks for a sound on the current player, if there is one.</summary>
        public static void Play(SoundId id, string why = null)
        {
            if (Current != null) Current.PlayNow(id, why);
        }

        /// <summary>
        /// Starts a short effect. The same sound asked for again in the same frame is dropped, and nothing starts when the
        /// group's volume is zero. Returns true when a sound was started.
        /// </summary>
        public bool PlayNow(SoundId id, string why = null)
        {
            var info = SoundManifest.Of(id);
            if (info.Loops) return false;
            int frame = Time.frameCount;
            if (_lastFrame.TryGetValue(id, out int last) && last == frame)
            {
                DroppedSameFrame++;
                return false;
            }
            _lastFrame[id] = frame;
            float volume = VolumeOf(info.Group);
#if DEBUG
            if (LogEvents) Debug.Log($"[sound] {info.File} · frame {frame} · volume {volume.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} · {why ?? "no reason given"}");
#endif
            if (volume <= 0f)
            {
                SilencedByVolume++;
                return false;
            }
            var clip = _catalog != null ? _catalog.Find(info.Key) : null;
            var voice = FreeVoice();
            voice.clip = clip;
            voice.volume = volume;
            voice.mute = false;
            if (clip != null) voice.Play();
            Record(id, frame, volume, why);
            return true;
        }

        private AudioSource FreeVoice()
        {
            for (int i = 0; i < EffectVoices; i++)
            {
                var s = _effects[(_nextVoice + i) % EffectVoices];
                if (!s.isPlaying)
                {
                    _nextVoice = (_nextVoice + i + 1) % EffectVoices;
                    return s;
                }
            }
            var oldest = _effects[_nextVoice];
            _nextVoice = (_nextVoice + 1) % EffectVoices;
            oldest.Stop();
            return oldest;
        }

        private void Record(SoundId id, int frame, float volume, string why)
        {
            StartedTotal++;
            _started.TryGetValue(id, out int n);
            _started[id] = n + 1;
            if (_history.Count == HistoryLimit) _history.RemoveAt(0);
            _history.Add(new PlayedSound(id, frame, volume, why));
        }

        /// <summary>How many times this sound has been started.</summary>
        public int StartedCount(SoundId id) => _started.TryGetValue(id, out int n) ? n : 0;

        /// <summary>The music loop to play, or null for none. Asking for the loop already playing changes nothing.</summary>
        public void SetMusic(SoundId? id) => SetLoop(_music, ref _musicId, id);

        /// <summary>The close-up's ambience loop, or null for none.</summary>
        public void SetAmbience(SoundId? id) => SetLoop(_ambience, ref _ambienceId, id);

        private void SetLoop(AudioSource source, ref SoundId? current, SoundId? id)
        {
            if (current == id) return;
            current = id;
            source.Stop();
            source.clip = null;
            if (!id.HasValue) { Apply(source, 0f); return; }
            var info = SoundManifest.Of(id.Value);
#if DEBUG
            if (LogEvents) Debug.Log($"[sound] {info.File} · frame {Time.frameCount} · loop started");
#endif
            source.clip = _catalog != null ? _catalog.Find(info.Key) : null;
            Apply(source, VolumeOf(info.Group));
            if (source.clip != null) source.Play();
        }

        /// <summary>True when the loop source is playing and can be heard (its group's volume is above zero).</summary>
        public bool IsMusicAudible => _music != null && _music.isPlaying && !_music.mute && _music.volume > 0f;
        public bool IsAmbienceAudible => _ambience != null && _ambience.isPlaying && !_ambience.mute && _ambience.volume > 0f;

        /// <summary>True when any effect voice is playing and can be heard.</summary>
        public bool IsAnyEffectAudible
        {
            get
            {
                foreach (var s in _effects) if (s.isPlaying && !s.mute && s.volume > 0f) return true;
                return false;
            }
        }

        /// <summary>The clip the catalog holds for a sound, or null when its file is missing.</summary>
        public AudioClip ClipOf(SoundId id) => _catalog != null ? _catalog.Find(SoundManifest.Of(id).Key) : null;
    }
}
