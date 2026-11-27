using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTrade.Game.Audio
{
    /// <summary>
    /// Every sound file under Assets/Audio, keyed by its path there without the ending (for example "Events/arrival").
    /// The Editor rebuilds it whenever a file under Assets/Audio changes, so a replaced file needs no code change.
    /// It lives in Resources so the build includes every sound it lists.
    /// </summary>
    public sealed class AudioCatalog : ScriptableObject
    {
        public const string ResourceName = "AudioCatalog";

        [Serializable]
        public struct Entry
        {
            public string key;
            public AudioClip clip;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(List<Entry> newEntries) => entries = newEntries;

        public AudioClip Find(string key)
        {
            foreach (var e in entries) if (e.key == key) return e.clip;
            return null;
        }
    }
}
