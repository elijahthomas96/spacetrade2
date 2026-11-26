using System.Collections.Generic;

namespace SpaceTrade.Game.Art
{
    public enum ArtKind { Vector, VectorWhite, Raster }

    /// <summary>One image from docs/graphics-checklist.md: its key (folder/name without ".png"), pixel size, and kind.</summary>
    public sealed class ArtFile
    {
        public string Key { get; }
        public int Width { get; }
        public int Height { get; }
        public ArtKind Kind { get; }

        public ArtFile(string key, int width, int height, ArtKind kind)
        {
            Key = key;
            Width = width;
            Height = height;
            Kind = kind;
        }

        /// <summary>Path relative to the project folder, for example "Assets/Art/Map/star.png".</summary>
        public string AssetPath => "Assets/Art/" + Key + ".png";
    }

    /// <summary>
    /// The fixed list of images in docs/graphics-checklist.md. Keys are the path under Assets/Art without ".png".
    /// A PlayMode test checks this list against the checklist itself.
    /// </summary>
    public static class ArtManifest
    {
        public static readonly IReadOnlyList<ArtFile> All = new List<ArtFile>
        {
            new ArtFile("Ships/courier_map", 128, 128, ArtKind.Vector),
            new ArtFile("Ships/freighter_map", 128, 128, ArtKind.Vector),
            new ArtFile("Ships/liner_map", 128, 128, ArtKind.Vector),
            new ArtFile("Ships/courier_closeup", 1024, 512, ArtKind.Vector),
            new ArtFile("Ships/freighter_closeup", 1024, 512, ArtKind.Vector),
            new ArtFile("Ships/liner_closeup", 1024, 512, ArtKind.Vector),
            new ArtFile("Ships/warp_glow", 512, 512, ArtKind.Raster),
            new ArtFile("Ships/engine_trail", 512, 128, ArtKind.Raster),

            new ArtFile("Map/star", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Map/star_glow", 256, 256, ArtKind.Raster),
            new ArtFile("Map/selection_ring", 256, 256, ArtKind.VectorWhite),
            new ArtFile("Map/shipyard_badge", 64, 64, ArtKind.VectorWhite),
            new ArtFile("Map/route_arrow", 64, 64, ArtKind.VectorWhite),
            new ArtFile("Map/starfield", 2048, 2048, ArtKind.Raster),
            new ArtFile("Map/nebula_overlay", 2048, 2048, ArtKind.Raster),
            new ArtFile("Map/closeup_backdrop", 1920, 1080, ArtKind.Raster),

            new ArtFile("Icons/industry_mining", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/industry_refinery", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/industry_factory", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/industry_agriculture", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/industry_population", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/industry_shipyard", 128, 128, ArtKind.VectorWhite),

            new ArtFile("Icons/cargo_ore", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/cargo_metals", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/cargo_chemicals", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/cargo_machinery", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/cargo_consumer_goods", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/cargo_food", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/supply_fuel", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/supply_food", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/supply_water", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/passenger", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/crew", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/credits", 128, 128, ArtKind.VectorWhite),

            new ArtFile("Icons/status_transit", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/status_waiting", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/status_idle", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/status_insystem", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/status_blocked", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/drive_tier1", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/drive_tier2", 128, 128, ArtKind.VectorWhite),
            new ArtFile("Icons/drive_tier3", 128, 128, ArtKind.VectorWhite),

            new ArtFile("UI/pause", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/play", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/follow", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/fleet", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/notification", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/warning", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/route", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/empty_leg", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/provision", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/dispatch", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/receipt", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/buy", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/save", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/settings", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/close", 64, 64, ArtKind.VectorWhite),
            new ArtFile("UI/debug_skip", 64, 64, ArtKind.VectorWhite),

            new ArtFile("Branding/logo", 1600, 600, ArtKind.Vector),
            new ArtFile("Branding/app_icon", 1024, 1024, ArtKind.Vector),
            new ArtFile("Branding/title_backdrop", 1920, 1080, ArtKind.Raster),
        };
    }
}
