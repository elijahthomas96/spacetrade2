using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceTrade.Game.Art;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Keeps Assets/Resources/ArtCatalog.asset listing every sprite under Assets/Art. It rebuilds whenever a file there
    /// is added, replaced, moved, or deleted, so the owner's new or replaced images need no code change.
    /// </summary>
    public sealed class ArtCatalogBuilder : AssetPostprocessor
    {
        public const string CatalogPath = "Assets/Resources/ArtCatalog.asset";
        private const string ArtRoot = "Assets/Art/";
        private static bool _queued;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touchesArt = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p => p.StartsWith(ArtRoot));
            if (!touchesArt || _queued) return;
            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Rebuild();
            };
        }

        [MenuItem("spacetrade2/Rebuild art catalog")]
        public static void Rebuild()
        {
            var entries = new List<ArtCatalog.Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png")) continue;
                var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null) continue;
                string key = path.Substring(ArtRoot.Length, path.Length - ArtRoot.Length - ".png".Length);
                entries.Add(new ArtCatalog.Entry { key = key, sprite = sprite });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));

            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<ArtCatalog>();
                catalog.SetEntries(entries);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else
            {
                bool same = catalog.Entries.Count == entries.Count
                            && catalog.Entries.Zip(entries, (a, b) => a.key == b.key && a.sprite == b.sprite).All(x => x);
                if (same) return;
                catalog.SetEntries(entries);
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            ArtLibrary.Load(catalog);
            Debug.Log($"Art catalog rebuilt: {entries.Count} images.");
        }
    }
}
