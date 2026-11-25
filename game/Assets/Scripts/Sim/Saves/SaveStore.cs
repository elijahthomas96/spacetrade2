using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Saves
{
    public sealed class SaveWriteResult
    {
        public bool Succeeded { get; }

        /// <summary>Why the save failed, in plain words; null when it succeeded. The previous save is always left as it was.</summary>
        public string Problem { get; }

        public long SavedUtcMs { get; }

        internal SaveWriteResult(bool succeeded, string problem, long savedUtcMs)
        {
            Succeeded = succeeded;
            Problem = problem;
            SavedUtcMs = savedUtcMs;
        }
    }

    public sealed class SaveLoadResult
    {
        public bool Succeeded => Simulation != null;

        /// <summary>The loaded game, or null when nothing was loaded.</summary>
        public Simulation Simulation { get; }

        /// <summary>The real-world UTC time the loaded file was written, in milliseconds since 1970.</summary>
        public long SavedUtcMs { get; }

        /// <summary>True when the main file was damaged and the backup was loaded instead.</summary>
        public bool UsedBackup { get; }

        /// <summary>What went wrong or what was done instead, in plain words, for the player. Empty for a clean load.</summary>
        public IReadOnlyList<string> Messages { get; }

        internal SaveLoadResult(Simulation simulation, long savedUtcMs, bool usedBackup, IReadOnlyList<string> messages)
        {
            Simulation = simulation;
            SavedUtcMs = savedUtcMs;
            UsedBackup = usedBackup;
            Messages = messages;
        }
    }

    /// <summary>
    /// Save files in one folder (spec 11.2). A slot "name" is the file name.json, with name.json.bak as its backup.
    /// Writing is atomic: the new save goes to name.json.tmp, is flushed to disk, and is then swapped into place, keeping
    /// the previous file as the backup. A failed write leaves the previous save as it was. Loading a damaged main file
    /// falls back to the backup and keeps the damaged file under a new name; nothing is ever deleted.
    /// </summary>
    public sealed class SaveStore
    {
        public const string Extension = ".json";
        public const string BackupSuffix = ".bak";
        public const string TempSuffix = ".tmp";
        public const string DamagedSuffix = ".damaged-";
        private const int MaxSlotLength = 64;

        public string Folder { get; }

        public SaveStore(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A save folder is needed.", nameof(folder));
            Folder = folder;
        }

        public string PathFor(string slot) => Path.Combine(Folder, slot + Extension);
        public string BackupPathFor(string slot) => PathFor(slot) + BackupSuffix;
        public string TempPathFor(string slot) => PathFor(slot) + TempSuffix;

        /// <summary>Null when <paramref name="slot"/> can be a save name, otherwise why not.</summary>
        public static string CheckSlotName(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return "A save needs a name.";
            if (slot.Length > MaxSlotLength) return $"A save name can be at most {MaxSlotLength} characters; \"{slot}\" has {slot.Length}.";
            if (slot != slot.Trim()) return "A save name cannot start or end with a space.";
            foreach (char c in slot)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_'))
                    return $"A save name can use letters, digits, spaces, '-' and '_'; \"{slot}\" has '{c}'.";
            return null;
        }

        /// <summary>The names of the saves in the folder, sorted.</summary>
        public IReadOnlyList<string> ListSlots()
        {
            if (!Directory.Exists(Folder)) return new List<string>();
            return Directory.GetFiles(Folder, "*" + Extension)
                .Where(p => p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Where(n => CheckSlotName(n) == null)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Saves the game to this slot, atomically, stamped with <paramref name="savedUtcMs"/>. On failure the previous
        /// save and its backup are unchanged and only the temporary file this write made is removed.
        /// </summary>
        public SaveWriteResult Write(string slot, Simulation sim, long savedUtcMs)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            string bad = CheckSlotName(slot);
            if (bad != null) return new SaveWriteResult(false, bad + " Nothing was saved.", 0);

            byte[] bytes;
            try
            {
                bytes = SaveFormat.Utf8.GetBytes(SaveWriter.Write(sim, savedUtcMs));
            }
            catch (InvalidOperationException e)
            {
                return new SaveWriteResult(false, $"The game could not be saved: {e.Message}", 0);
            }

            string target = PathFor(slot), temp = TempPathFor(slot), backup = BackupPathFor(slot);
            bool wroteTemp = false;
            try
            {
                Directory.CreateDirectory(Folder);
                if (Directory.Exists(temp))
                    return new SaveWriteResult(false, $"The game could not be saved to \"{slot}\": a folder named {Path.GetFileName(temp)} is in the way. The previous save is unchanged.", 0);
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    wroteTemp = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(target)) File.Replace(temp, target, backup, ignoreMetadataErrors: true);
                else File.Move(temp, target);
                return new SaveWriteResult(true, null, savedUtcMs);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                if (wroteTemp)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException) { }
                }
                return new SaveWriteResult(false, $"The game could not be saved to \"{slot}\": {e.Message} The previous save is unchanged.", 0);
            }
        }

        /// <summary>
        /// Loads a slot with the current tuning. A damaged main file falls back to the backup; the damaged file is then kept as
        /// name.json.damaged-(date and time) so the next save starts a fresh main file and the good backup stays. A file from
        /// a newer or unknown format, or one the tuning cannot support, is refused without trying the backup. When nothing
        /// loads, no file is changed.
        /// </summary>
        /// <param name="nowUtcMs">The real-world time now, used only to name a damaged file kept aside.</param>
        public SaveLoadResult Load(string slot, TuningData tuning, long nowUtcMs, DebugCapability debug = null)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            string bad = CheckSlotName(slot);
            if (bad != null) return Refuse(bad + " Nothing was loaded.");

            string target = PathFor(slot), backup = BackupPathFor(slot);
            bool mainExists = File.Exists(target), backupExists = File.Exists(backup);
            if (!mainExists && !backupExists) return Refuse($"There is no save named \"{slot}\".");

            string mainProblem;
            if (mainExists)
            {
                var main = ReadFile(target, tuning, debug, out string ioProblem);
                if (ioProblem != null) return Refuse($"The save \"{slot}\" could not be read: {ioProblem} Nothing was loaded and no file was changed.");
                if (main.Kind == SaveReadKind.Loaded) return new SaveLoadResult(main.Simulation, main.SavedUtcMs, false, new List<string>());
                if (main.Kind == SaveReadKind.Refused) return Refuse($"The save \"{slot}\" was not loaded: {main.Problem}. Nothing was changed.");
                mainProblem = $"The save \"{slot}\" is damaged: {main.Problem}.";
            }
            else mainProblem = $"The save \"{slot}\" is missing.";

            if (!backupExists) return Refuse($"{mainProblem} There is no backup. Nothing was loaded and no file was changed.");
            var fromBackup = ReadFile(backup, tuning, debug, out string backupIo);
            if (backupIo != null || fromBackup.Kind != SaveReadKind.Loaded)
                return Refuse($"{mainProblem} The backup could not be loaded either: {backupIo ?? fromBackup.Problem + "."} Nothing was loaded and no file was changed.");

            var messages = new List<string> { mainProblem };
            string savedAt = DateTimeOffset.FromUnixTimeMilliseconds(fromBackup.SavedUtcMs).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
            if (mainExists)
            {
                string kept = KeepDamaged(target, nowUtcMs, out string keepProblem);
                messages.Add(kept != null
                    ? $"Loaded the backup saved {savedAt} instead. The damaged file was kept as {Path.GetFileName(kept)}."
                    : $"Loaded the backup saved {savedAt} instead. The damaged file could not be set aside ({keepProblem}); it is still in place.");
            }
            else messages.Add($"Loaded the backup saved {savedAt} instead.");
            return new SaveLoadResult(fromBackup.Simulation, fromBackup.SavedUtcMs, true, messages);
        }

        private static SaveLoadResult Refuse(string message) => new SaveLoadResult(null, 0, false, new List<string> { message });

        private static SaveReadOutcome ReadFile(string path, TuningData tuning, DebugCapability debug, out string ioProblem)
        {
            ioProblem = null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                ioProblem = e.Message;
                return null;
            }
            return SaveReader.Read(bytes, tuning, debug);
        }

        /// <summary>Renames a damaged save aside, never over an existing file. Returns the new path, or null with the reason.</summary>
        private static string KeepDamaged(string path, long nowUtcMs, out string problem)
        {
            problem = null;
            string stamp = DateTimeOffset.FromUnixTimeMilliseconds(nowUtcMs).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                for (int n = 1; n < 1000; n++)
                {
                    string kept = path + DamagedSuffix + stamp + (n == 1 ? "" : "-" + n);
                    if (File.Exists(kept) || Directory.Exists(kept)) continue;
                    File.Move(path, kept);
                    return kept;
                }
                problem = "too many damaged copies with the same time";
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                problem = e.Message;
                return null;
            }
        }
    }
}
