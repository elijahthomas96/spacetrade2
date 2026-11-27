using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceTrade.Game.Audio;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Keeps Assets/Resources/AudioCatalog.asset listing every sound under Assets/Audio. It rebuilds whenever a file there
    /// is added, replaced, moved, or deleted, so the owner's new or replaced sounds need no code change. It only reads the
    /// sound files; it never writes, renames, or deletes one.
    /// </summary>
    public sealed class AudioCatalogBuilder : AssetPostprocessor
    {
        public const string CatalogPath = "Assets/Resources/AudioCatalog.asset";
        private const string AudioRoot = "Assets/Audio/";
        private static bool _queued;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touchesAudio = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p => p.StartsWith(AudioRoot));
            if (!touchesAudio || _queued) return;
            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Rebuild();
            };
        }

        [MenuItem("spacetrade2/Rebuild audio catalog")]
        public static void Rebuild()
        {
            var entries = new List<AudioCatalog.Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                string key = path.Substring(AudioRoot.Length, path.Length - AudioRoot.Length - Path.GetExtension(path).Length);
                entries.Add(new AudioCatalog.Entry { key = key, clip = clip });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));

            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                catalog.SetEntries(entries);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else
            {
                bool same = catalog.Entries.Count == entries.Count
                            && catalog.Entries.Zip(entries, (a, b) => a.key == b.key && a.clip == b.clip).All(x => x);
                if (same) return;
                catalog.SetEntries(entries);
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Audio catalog rebuilt: {entries.Count} sounds.");
        }
    }
}
