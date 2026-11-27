using System.Collections.Generic;

namespace SpaceTrade.Game.Audio
{
    /// <summary>Every sound in the game (docs/audio-checklist.md): 20 short effects and 4 loops.</summary>
    public enum SoundId
    {
        Click,
        PanelOpen,
        PanelClose,
        Select,
        Error,
        ToggleOn,
        ToggleOff,
        Pause,
        Resume,
        SpeedChange,
        Dispatch,
        Arrival,
        Payment,
        Blocked,
        Waiting,
        Provision,
        PurchaseShip,
        DriveInstall,
        ReturnSummary,
        InSystemIncome,
        ShipCloseUpDrive,
        ShipCloseUpDocked,
        MusicMap,
        MusicTitle,
    }

    /// <summary>Which volume slider a sound follows. The master slider and mute apply to both.</summary>
    public enum SoundGroup
    {
        Effects,
        Music,
    }

    public sealed class SoundInfo
    {
        public SoundId Id { get; }

        /// <summary>The file under Assets/Audio without its ending, for example "Events/arrival".</summary>
        public string Key { get; }

        /// <summary>The file ending: "wav" for a short effect, "ogg" for a loop.</summary>
        public string Extension { get; }

        public SoundGroup Group { get; }
        public bool Loops { get; }

        /// <summary>The path under Assets/Audio, for example "Events/arrival.wav".</summary>
        public string File => Key + "." + Extension;

        internal SoundInfo(SoundId id, string key, SoundGroup group, bool loops)
        {
            Id = id;
            Key = key;
            Extension = loops ? "ogg" : "wav";
            Group = group;
            Loops = loops;
        }
    }

    /// <summary>The fixed file for every sound (spec 9.6). The close-up loops follow the effects slider; the two music loops follow the music slider.</summary>
    public static class SoundManifest
    {
        public static readonly IReadOnlyList<SoundInfo> All = new[]
        {
            new SoundInfo(SoundId.Click, "UI/click", SoundGroup.Effects, false),
            new SoundInfo(SoundId.PanelOpen, "UI/panel_open", SoundGroup.Effects, false),
            new SoundInfo(SoundId.PanelClose, "UI/panel_close", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Select, "UI/select", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Error, "UI/error", SoundGroup.Effects, false),
            new SoundInfo(SoundId.ToggleOn, "UI/toggle_on", SoundGroup.Effects, false),
            new SoundInfo(SoundId.ToggleOff, "UI/toggle_off", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Pause, "Clock/pause", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Resume, "Clock/resume", SoundGroup.Effects, false),
            new SoundInfo(SoundId.SpeedChange, "Clock/speed_change", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Dispatch, "Events/dispatch", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Arrival, "Events/arrival", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Payment, "Events/payment", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Blocked, "Events/blocked", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Waiting, "Events/waiting", SoundGroup.Effects, false),
            new SoundInfo(SoundId.Provision, "Events/provision", SoundGroup.Effects, false),
            new SoundInfo(SoundId.PurchaseShip, "Events/purchase_ship", SoundGroup.Effects, false),
            new SoundInfo(SoundId.DriveInstall, "Events/drive_install", SoundGroup.Effects, false),
            new SoundInfo(SoundId.ReturnSummary, "Events/return_summary", SoundGroup.Effects, false),
            new SoundInfo(SoundId.InSystemIncome, "Events/insystem_income", SoundGroup.Effects, false),
            new SoundInfo(SoundId.ShipCloseUpDrive, "Loops/ship_closeup_drive", SoundGroup.Effects, true),
            new SoundInfo(SoundId.ShipCloseUpDocked, "Loops/ship_closeup_docked", SoundGroup.Effects, true),
            new SoundInfo(SoundId.MusicMap, "Loops/music_map", SoundGroup.Music, true),
            new SoundInfo(SoundId.MusicTitle, "Loops/music_title", SoundGroup.Music, true),
        };

        public static SoundInfo Of(SoundId id) => All[(int)id];
    }
}
