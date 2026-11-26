using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Unity import settings for the owner's art and audio. A file new to Assets/Art or Assets/Audio gets them
    /// automatically; files already imported keep whatever settings they have unless the menu item is run.
    /// </summary>
    public sealed class AssetImportRules : AssetPostprocessor
    {
        private const string ArtFolder = "Assets/Art/";
        private const string AudioFolder = "Assets/Audio/";

        /// <summary>Images drawn at many sizes on the map get mipmaps; icons and interface images are shown at a fixed size.</summary>
        private static readonly string[] MipmapFolders = { "Assets/Art/Map/", "Assets/Art/Ships/" };

        /// <summary>Images that repeat across the screen.</summary>
        private static readonly HashSet<string> Tiling = new HashSet<string> { "Assets/Art/Map/starfield.png", "Assets/Art/Map/nebula_overlay.png" };

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(ArtFolder) && assetImporter.importSettingsMissing) ApplyTexture((TextureImporter)assetImporter);
        }

        private void OnPreprocessAudio()
        {
            if (assetPath.StartsWith(AudioFolder) && assetImporter.importSettingsMissing) ApplyAudio((AudioImporter)assetImporter);
        }

        public static void ApplyTexture(TextureImporter ti)
        {
            ti.GetSourceTextureWidthAndHeight(out int width, out int height);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteExtrude = 1;
            ti.SetTextureSettings(settings);
            ti.spritePixelsPerUnit = Mathf.Max(1, width);
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            bool mips = System.Array.Exists(MipmapFolders, f => ti.assetPath.StartsWith(f));
            ti.mipmapEnabled = mips;
            ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.wrapMode = Tiling.Contains(ti.assetPath) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.maxTextureSize = 2048;
            ti.textureCompression = (long)width * height >= 512L * 512L
                ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Uncompressed;
        }

        public static void ApplyAudio(AudioImporter ai)
        {
            bool loop = ai.assetPath.StartsWith("Assets/Audio/Loops/");
            var s = ai.defaultSampleSettings;
            if (loop)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.preloadAudioData = false;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
                s.preloadAudioData = true;
                ai.loadInBackground = false;
            }
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.forceToMono = false;
        }

        [MenuItem("spacetrade2/Apply import settings to all art and audio")]
        public static void ApplyToAll()
        {
            int textures = 0, sounds = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" }))
                {
                    var ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                    if (ti == null) continue;
                    ApplyTexture(ti);
                    ti.SaveAndReimport();
                    textures++;
                }
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
                {
                    var ai = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
                    if (ai == null) continue;
                    ApplyAudio(ai);
                    ai.SaveAndReimport();
                    sounds++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"Import settings applied to {textures} images and {sounds} sounds.");
        }
    }
}
