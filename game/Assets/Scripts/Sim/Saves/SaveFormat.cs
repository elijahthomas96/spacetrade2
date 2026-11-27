using System;
using System.Security.Cryptography;
using System.Text;

namespace SpaceTrade.Sim.Saves
{
    /// <summary>
    /// The save file's fixed parts (spec 11.1): the format marker, the version this game writes and reads, and the
    /// checksum. The checksum is a SHA-256 fingerprint of the whole file with the checksum's own 64 characters
    /// written as zeros, so any change to the file, accidental or by hand, is detected and the file counts as damaged.
    /// </summary>
    public static class SaveFormat
    {
        public const string Marker = "spacetrade2-save";

        /// <summary>The save format version this game writes, and the newest it reads.</summary>
        public const int CurrentVersion = 3;

        /// <summary>The oldest save format version that exists.</summary>
        public const int OldestVersion = 1;

        internal const string ChecksumKey = "\"checksum\": \"";
        internal const int ChecksumLength = 64;
        internal static readonly string ChecksumPlaceholder = new string('0', ChecksumLength);

        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>The fingerprint of <paramref name="text"/>, whose checksum field starts at <paramref name="valueStart"/>, with that value taken as zeros.</summary>
        internal static string Checksum(string text, int valueStart)
        {
            string blanked = text.Substring(0, valueStart) + ChecksumPlaceholder + text.Substring(valueStart + ChecksumLength);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Utf8.GetBytes(blanked));
                var sb = new StringBuilder(ChecksumLength);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Where the checksum's value starts, or -1 when the file has no checksum field.</summary>
        internal static int ChecksumValueStart(string text)
        {
            int at = text.IndexOf(ChecksumKey, StringComparison.Ordinal);
            if (at < 0) return -1;
            int start = at + ChecksumKey.Length;
            return start + ChecksumLength <= text.Length ? start : -1;
        }

        /// <summary>Writes the correct checksum into text that has one. Used by the writer, and by tests that build files by hand.</summary>
        internal static string Seal(string text)
        {
            int start = ChecksumValueStart(text);
            if (start < 0) throw new ArgumentException("The text has no checksum field.", nameof(text));
            return text.Substring(0, start) + Checksum(text, start) + text.Substring(start + ChecksumLength);
        }
    }
}
