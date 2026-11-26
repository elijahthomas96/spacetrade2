using System.IO;
using SpaceTrade.Game.Art;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Writes a simple placeholder image for every file in docs/graphics-checklist.md that does not exist yet. It never
    /// touches a file that is already there, so the owner's images are never overwritten.
    /// </summary>
    public static class ArtPlaceholders
    {
        [MenuItem("spacetrade2/Create missing art placeholders")]
        public static void CreateMissing()
        {
            int created = 0, present = 0;
            foreach (var file in ArtManifest.All)
            {
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), file.AssetPath);
                if (File.Exists(fullPath)) { present++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, Draw(file).EncodeToPNG());
                created++;
                Debug.Log("Placeholder created: " + file.AssetPath);
            }
            if (created > 0) AssetDatabase.Refresh();
            Debug.Log($"Art placeholders: {created} created, {present} already present.");
        }

        /// <summary>A white outline with a diagonal on transparent (or a dark fill for raster backdrops), at the listed size.</summary>
        private static Texture2D Draw(ArtFile file)
        {
            int w = file.Width, h = file.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            var line = new Color32(255, 255, 255, 255);
            var fill = file.Kind == ArtKind.Raster ? new Color32(10, 14, 24, 255) : new Color32(255, 255, 255, 0);
            int border = Mathf.Max(2, Mathf.Min(w, h) / 24);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < border || y < border || x >= w - border || y >= h - border;
                bool diagonal = Mathf.Abs(x * h - y * w) < border * Mathf.Max(w, h);
                pixels[y * w + x] = edge || diagonal ? line : fill;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
