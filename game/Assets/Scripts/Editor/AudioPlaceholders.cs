using System.IO;
using SpaceTrade.Game.Audio;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Writes a short silent WAV for every sound in docs/audio-checklist.md whose file does not exist. It never touches a
    /// file that is already there, so the owner's sounds are never overwritten. A missing loop gets a silent .wav beside
    /// where the .ogg belongs, which the game finds under the same name.
    /// </summary>
    public static class AudioPlaceholders
    {
        private const int SampleRate = 48000;
        private const int SilentSamples = 4800;

        [MenuItem("spacetrade2/Create missing audio placeholders")]
        public static void CreateMissing()
        {
            int created = 0, present = 0;
            string audioRoot = Path.Combine(Application.dataPath, "Audio");
            foreach (var sound in SoundManifest.All)
            {
                string wanted = Path.Combine(audioRoot, sound.File.Replace('/', Path.DirectorySeparatorChar));
                string asWav = Path.Combine(audioRoot, (sound.Key + ".wav").Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(wanted) || File.Exists(asWav)) { present++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(asWav));
                File.WriteAllBytes(asWav, SilentWav());
                created++;
                Debug.Log("Silent placeholder created: Assets/Audio/" + sound.Key + ".wav");
            }
            if (created > 0) AssetDatabase.Refresh();
            Debug.Log($"Audio placeholders: {created} created, {present} already present.");
        }

        /// <summary>A tenth of a second of silence: 48 kHz, 16-bit, mono.</summary>
        private static byte[] SilentWav()
        {
            int dataBytes = SilentSamples * 2;
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(SampleRate);
                w.Write(SampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                w.Write(new byte[dataBytes]);
                return stream.ToArray();
            }
        }
    }
}
