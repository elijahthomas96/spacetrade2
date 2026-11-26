using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTrade.Game.Art
{
    /// <summary>
    /// Every sprite under Assets/Art, keyed by its path there without ".png" (for example "Icons/cargo_ore").
    /// The Editor rebuilds it whenever a file under Assets/Art changes, so a new file is picked up with no code change.
    /// It lives in Resources so the build includes every sprite it lists.
    /// </summary>
    public sealed class ArtCatalog : ScriptableObject
    {
        public const string ResourceName = "ArtCatalog";

        [Serializable]
        public struct Entry
        {
            public string key;
            public Sprite sprite;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(List<Entry> newEntries) => entries = newEntries;
    }
}
