using System;
using UnityEngine;

namespace SpaceTrade.Game.Theming
{
    /// <summary>Limits for a theme number. Without it a number must be between 0 and 100,000.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ThemeRangeAttribute : Attribute
    {
        public float Min { get; }
        public float Max { get; }

        public ThemeRangeAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>When a ship's name is drawn beside it on the map.</summary>
    public enum ShipNameRule
    {
        Never,
        SelectedOrHovered,
        Always,
    }

    /// <summary>
    /// Every colour, font, text size, spacing value, glow size, label rule, display limit, and control value the screens use
    /// (AGENTS.md). The values written here are the built-in defaults; theme.json holds the same fields and replaces them
    /// when it loads cleanly. A field added here is read from and written to the file automatically, under the same name
    /// with a small first letter, and must be explained in docs/theme-guide.md. Sizes are screen pixels unless the name
    /// says otherwise.
    /// </summary>
    public sealed class GameTheme
    {
        public ColourTheme Colours = new ColourTheme();
        public TextTheme Text = new TextTheme();
        public PanelTheme Panels = new PanelTheme();
        public TopBarTheme TopBar = new TopBarTheme();
        public NotificationTheme Notifications = new NotificationTheme();
        public SettingsTheme Settings = new SettingsTheme();
        public LabelTheme Labels = new LabelTheme();
        public MapTheme Map = new MapTheme();
        public ShipTheme Ships = new ShipTheme();
        public RouteTheme Routes = new RouteTheme();
        public BackgroundTheme Background = new BackgroundTheme();
        public CloseUpTheme CloseUp = new CloseUpTheme();
        public SystemViewTheme SystemView = new SystemViewTheme();
        public JobListTheme JobList = new JobListTheme();
        public PlannerTheme Planner = new PlannerTheme();
        public ReceiptTheme Receipt = new ReceiptTheme();
        public ControlTheme Controls = new ControlTheme();

        internal static Color Hex(string text)
        {
            if (!ThemeReader.TryParseColour(text, out var c)) throw new ArgumentException("Not a colour: " + text, nameof(text));
            return c;
        }
    }

    public sealed class ColourTheme
    {
        public Color Text = GameTheme.Hex("#DBE6F5FF");
        public Color TextDim = GameTheme.Hex("#9EADC2FF");
        public Color Warning = GameTheme.Hex("#FFBF59FF");
        public Color Highlight = GameTheme.Hex("#FFCC59FF");
        public Color Panel = GameTheme.Hex("#0D121CE0");
        public Color TopBar = GameTheme.Hex("#0D121CF2");
        public Color Dropdown = GameTheme.Hex("#0D121CFF");
        public Color RowHover = GameTheme.Hex("#FFFFFF1F");
        public Color Icon = GameTheme.Hex("#FFFFFFFF");
        public Color ButtonBackground = GameTheme.Hex("#BCBCBCFF");
        public Color ButtonHover = GameTheme.Hex("#CFCFCFFF");
        public Color ButtonPressed = GameTheme.Hex("#9A9A9AFF");
        public Color ButtonSelected = GameTheme.Hex("#73D9FFFF");
        public Color ButtonText = GameTheme.Hex("#1B1B1BFF");
        public Color ButtonBorder = GameTheme.Hex("#959595FF");
        public Color IconButtonHover = GameTheme.Hex("#FFFFFF2E");
        public Color IconButtonPressed = GameTheme.Hex("#FFFFFF4D");
        public Color ScrollTrack = GameTheme.Hex("#FFFFFF1F");
        public Color ScrollThumb = GameTheme.Hex("#9EADC2FF");
        public Color SliderTrack = GameTheme.Hex("#FFFFFF1F");
        public Color SliderFill = GameTheme.Hex("#73D9FFFF");
        public Color Star = GameTheme.Hex("#FFF5E0FF");
        public Color StarGlow = GameTheme.Hex("#FFEBCC8C");
        public Color ShipyardBadge = GameTheme.Hex("#FFCC59FF");
        public Color SystemRing = GameTheme.Hex("#73D9FFFF");
        public Color ShipIcon = GameTheme.Hex("#FFFFFFFF");
        public Color ShipGlow = GameTheme.Hex("#FFFFFFBF");
        public Color ShipTrail = GameTheme.Hex("#FFFFFFE6");
        public Color ShipRing = GameTheme.Hex("#FFD966FF");
        public Color RouteLine = GameTheme.Hex("#73D9FFFF");
        public Color CloseUpPanel = GameTheme.Hex("#0D121CE6");
        public Color CloseUpBackground = GameTheme.Hex("#000000FF");
        public Color ProgressTrack = GameTheme.Hex("#FFFFFF1F");
        public Color ProgressFill = GameTheme.Hex("#73D9FFFF");
        public Color Loss = GameTheme.Hex("#FF8A73FF");
        public Color RowSelected = GameTheme.Hex("#73D9FF40");
        public Color FieldBackground = GameTheme.Hex("#1A2433FF");
        public Color FieldBorder = GameTheme.Hex("#9EADC2FF");
        public Color ConfirmBox = GameTheme.Hex("#26364DFF");
    }

    public sealed class TextTheme
    {
        /// <summary>A font file name under Assets/Resources/Fonts, without its ending; blank uses Unity's built-in font.</summary>
        public string Font = "";
        [ThemeRange(6, 100)] public float Body = 14;
        [ThemeRange(6, 100)] public float Small = 12;
        [ThemeRange(6, 100)] public float MapLabel = 13;
        [ThemeRange(6, 100)] public float Title = 18;
        [ThemeRange(6, 200)] public float CloseUpTitle = 30;
        [ThemeRange(6, 100)] public float CloseUpSubtitle = 15;
        public bool TitlesBold = true;
    }

    public sealed class PanelTheme
    {
        public float Margin = 16;
        public float Padding = 12;
        public float InfoWidth = 260;
        public float MessagesWidth = 640;
        public float MessagesPadding = 10;
        public float RowGap = 4;
        public float IconRowGap = 6;
        public float IconSize = 20;
        public float IconGap = 8;
        public float ButtonRowGap = 10;
        public float IconButtonSize = 30;
        public float InlineGap = 12;
        public float ListMinHeight = 40;
        public float DebugLineLeft = 12;
        public float DebugLineBottom = 8;
        public float ButtonBorderWidth = 1;
        public float ScrollBarWidth = 10;
    }

    public sealed class TopBarTheme
    {
        [ThemeRange(20, 200)] public float Height = 36;
        public float Padding = 8;
        public float Gap = 8;
        public float ButtonSize = 26;
        public float SpeedButtonWidth = 34;
        public float DateWidth = 104;
        public float IconSize = 18;
        public float IconGap = 4;
    }

    public sealed class NotificationTheme
    {
        public float Width = 380;
        public float MaxHeight = 260;
        [ThemeRange(10, 1000)] public float RowHeight = 44;
        public float DateWidth = 88;
        public float RowPadding = 6;
        [ThemeRange(1, 9999)] public int BadgeMaxCount = 99;
        public float IconSize = 20;
        public float IconGap = 8;
    }

    public sealed class SettingsTheme
    {
        public float Width = 280;
        public float RowGap = 8;
        public float CaptionWidth = 64;
        public float ValueWidth = 36;
        public float SliderHeight = 8;
        public float SliderHandleWidth = 10;
        public float SliderHandleHeight = 18;
    }

    public sealed class LabelTheme
    {
        public float Gap = 16;
        public float Height = 18;
        public float Clearance = 3;
        public float FallbackLetterWidth = 7.5f;
        public float CountWidth = 40;
        public ShipNameRule MovingShipNames = ShipNameRule.SelectedOrHovered;
        public float ShipNameGap = 18;
    }

    public sealed class MapTheme
    {
        [ThemeRange(1, 1000)] public float StarPixels = 26;
        public float StarGlowPixels = 84;
        public float ShipyardBadgePixels = 15;
        [ThemeRange(0, 10)] public float ShipyardBadgeOffset = 0.55f;
        public float SelectionRingPixels = 58;
    }

    public sealed class ShipTheme
    {
        [ThemeRange(1, 1000)] public float IconPixels = 26;
        [ThemeRange(1, 1000)] public float DockedIconPixels = 22;
        public float GlowPixels = 56;
        public float TrailPixels = 52;
        [ThemeRange(0, 10)] public float TrailOffset = 0.35f;
        public float SelectionRingPixels = 44;
        [ThemeRange(1, 100)] public int MaxDockedIconsPerStar = 5;
        public float FanRadiusPixels = 36;
        [ThemeRange(0, 360)] public float FanFirstAngleDegrees = 150;
        [ThemeRange(0, 360)] public float FanStepDegrees = 40;
        public float OverflowBelowPixels = 62;
    }

    public sealed class RouteTheme
    {
        public float ArrowPixels = 13;
        [ThemeRange(0, 1)] public float ArrowAlong = 0.6f;
        [ThemeRange(0, 1)] public float PausedRouteAlpha = 0.2f;
        [ThemeRange(0, 1)] public float RouteAlpha = 0.35f;
        [ThemeRange(0, 1)] public float CurrentLegAlpha = 0.55f;
        [ThemeRange(0, 1)] public float SelectedAlpha = 0.95f;
        public float RouteWidth = 1.5f;
        public float CurrentLegWidth = 2;
        public float SelectedWidth = 2.5f;
    }

    public sealed class BackgroundTheme
    {
        [ThemeRange(0.1f, 100)] public float StarfieldTileScreens = 1;
        [ThemeRange(0, 10)] public float StarfieldDriftTilesPerLy = 0.012f;
        [ThemeRange(0.1f, 100)] public float NebulaTileScreens = 1.6f;
        [ThemeRange(0, 10)] public float NebulaDriftTilesPerLy = 0.006f;
    }

    public sealed class CloseUpTheme
    {
        public float PanelWidth = 340;
        public float PanelPaddingSide = 14;
        public float PanelPaddingTop = 12;
        public float HeaderLeft = 24;
        public float HeaderTop = 20;
        public float HeaderGap = 32;
        public float SuppliesTop = 8;
        public float SupplyGap = 14;
        public float SupplyIconGap = 5;
        public float IconSize = 18;
        public float IconGap = 6;
        public float RowGap = 5;
        public float LineGap = 4;
        public float HeadingGap = 6;
        public float CaptionWidth = 100;
        public float IconCaptionWidth = 76;
        public float BarHeight = 6;
        public float BarGap = 4;
        public float CloseButtonSize = 34;
        public float FooterLeft = 24;
        public float FooterBottom = 20;
        public float HintGap = 10;
        [ThemeRange(0, 100)] public float ShipLeftPercent = 6;
        [ThemeRange(1, 100)] public float ShipWidthPercent = 52;
        [ThemeRange(0, 100)] public float ShipTopPercent = 30;
        [ThemeRange(1, 100)] public float ShipHeightPercent = 48;
        public float GlowOutsetPixels = 20;
        [ThemeRange(0, 200)] public float TrailRightPercent = 88;
        [ThemeRange(0, 200)] public float TrailWidthPercent = 60;
        [ThemeRange(0, 100)] public float TrailTopPercent = 40;
        [ThemeRange(0, 100)] public float TrailHeightPercent = 20;
        [ThemeRange(0, 10)] public float BackdropPixelsPerTick = 0.05f;
    }

    /// <summary>The system view: the panel for a selected star (spec 9.2).</summary>
    public sealed class SystemViewTheme
    {
        [ThemeRange(200, 2000)] public float Width = 440;
        public float SectionGap = 10;
        public float PriceWidth = 130;
    }

    /// <summary>The job list, in the system view and in the voyage planner.</summary>
    public sealed class JobListTheme
    {
        [ThemeRange(10, 1000)] public float RowHeight = 44;
        [ThemeRange(40, 2000)] public float Height = 180;
        public float RowPadding = 6;
        public float FigureWidth = 150;
        public float ControlGap = 6;
    }

    /// <summary>The voyage planner (spec 6.5).</summary>
    public sealed class PlannerTheme
    {
        [ThemeRange(200, 2000)] public float Width = 520;
        public float SectionGap = 10;
        public float LineGap = 3;
        public float NameWidth = 110;
        public float StepButtonWidth = 30;
        public float AmountWidth = 64;
        public float ControlGap = 6;
        public float ConfirmPadding = 8;
    }

    /// <summary>The delivery receipt (spec 6.7).</summary>
    public sealed class ReceiptTheme
    {
        [ThemeRange(200, 2000)] public float Width = 440;
        public float SectionGap = 10;
        public float LineGap = 3;
    }

    /// <summary>Camera and control feel, kept apart from the look.</summary>
    public sealed class ControlTheme
    {
        [ThemeRange(1, 500)] public float ClickRadiusPixels = 24;
        [ThemeRange(0.1f, 0.99f)] public float ZoomPerWheelNotch = 0.85f;
        [ThemeRange(1, 100)] public float MaxWheelNotchesPerFrame = 5;
        [ThemeRange(0.01f, 100)] public float KeyPanScreensPerSecond = 0.8f;
        [ThemeRange(0.1f, 1000)] public float ClosestZoomHalfHeightLy = 1.5f;
        [ThemeRange(0, 1000)] public float FitMarginPercent = 12;
        [ThemeRange(0, 1000)] public float FitMarginLy = 1.5f;
        [ThemeRange(0, 1000)] public float PanMarginLy = 3;
    }
}
