# spacetrade2: theme guide

The theme file is `D:\dev\spacetrade2\game\Assets\StreamingAssets\theme.json`. It holds every colour, font, text size, spacing value, glow size, label rule, and display limit the screens use, and, in its own `controls` section, how the camera and mouse feel. Every screen reads from it; nothing on screen has these values written into the code.

You can edit it in Notepad. Save, then start the game again (in the Editor: stop Play and press Play again). It changes how the game looks and handles. It never changes game rules, prices, or times; those are in `tuning.json`.

## How the file is read

It is read the same way as `tuning.json`:

- Every field must be there, spelled exactly as in this guide (small first letter, capitals inside the word).
- A wrong value, a missing field, or a field the game does not know (including a misspelled one) is an error.
- If the file has any error, the game does not break. It shows a message on the map naming each problem field and its line, for example `text.body: must be a number, but is the text "big" (line 39)`, and uses the built-in look and controls for the whole file until the file is fixed. This is the same rule as `tuning.json`: one error sets the whole file aside, so a half-applied look never happens.
- The built-in values are the "Default" column below. A fresh copy of the file with every default is easy to get back: ask the agent to "rewrite theme.json from the defaults".

## Kinds of value

| Kind | How to write it | Example |
|---|---|---|
| Colour | Text in quotes: `#` then red, green, blue, and opacity, each two characters from `00` to `FF`. Opacity `FF` is solid, `00` is invisible. | `"#FFCC59FF"` |
| Pixels | A number. One pixel is one dot of the game window. | `14` |
| Percent | A number of hundredths of something named in the field's description. | `52` |
| Light-years (ly) | A number of light-years on the map. | `1.5` |
| Share | A number from 0 to 1. 0 is none, 1 is all. | `0.35` |
| Yes/no | `true` or `false`, no quotes. | `true` |
| Whole number | A number with no decimal point. | `5` |
| Choice | One of the listed words, in quotes. | `"selectedOrHovered"` |

Unless a row says otherwise, a number must be between 0 and 100,000.

## colours

All are colours. Art files are drawn in their own colours multiplied by the colour given here, so `#FFFFFFFF` (white) means "draw the file as it is".

| Field | Default | What it colours |
|---|---|---|
| `colours.text` | `#DBE6F5FF` | Ordinary text on every panel and the bar. |
| `colours.textDim` | `#9EADC2FF` | Quieter text: captions, dates in the notification list, hints. |
| `colours.warning` | `#FFBF59FF` | Warnings: a blocked route's text and its icon, startup problem messages. |
| `colours.highlight` | `#FFCC59FF` | Small attention marks: the "+3" under a crowded star, the count beside the bell. |
| `colours.panel` | `#0D121CE0` | The background of panels on the map (system, ship, messages). |
| `colours.topBar` | `#0D121CF2` | The background of the bar across the top. |
| `colours.dropdown` | `#0D121CFF` | The background of the notification list and the settings panel, which open over whatever is under them. Solid by default so the panel underneath does not show through. |
| `colours.rowHover` | `#FFFFFF1F` | The wash over a list row the mouse is on. |
| `colours.icon` | `#FFFFFFFF` | The tint of icons on panels and the bar. |
| `colours.buttonBackground` | `#BCBCBCFF` | A text button at rest. |
| `colours.buttonHover` | `#CFCFCFFF` | A text button with the mouse on it. |
| `colours.buttonPressed` | `#9A9A9AFF` | A text button while held down. |
| `colours.buttonSelected` | `#73D9FFFF` | A button that is switched on: the chosen speed, Mute while muted. |
| `colours.buttonText` | `#1B1B1BFF` | The text on a text button. |
| `colours.buttonBorder` | `#959595FF` | The outline of a text button. |
| `colours.iconButtonHover` | `#FFFFFF2E` | The wash behind an icon button with the mouse on it. |
| `colours.iconButtonPressed` | `#FFFFFF4D` | The wash behind an icon button while held down. |
| `colours.scrollTrack` | `#FFFFFF1F` | The groove of a scroll bar. |
| `colours.scrollThumb` | `#9EADC2FF` | The part of a scroll bar you drag. |
| `colours.sliderTrack` | `#FFFFFF1F` | The groove of a volume slider. |
| `colours.sliderFill` | `#73D9FFFF` | The handle of a volume slider. |
| `colours.star` | `#FFF5E0FF` | The tint of every star on the map. |
| `colours.starGlow` | `#FFEBCC8C` | The glow around every star. |
| `colours.shipyardBadge` | `#FFCC59FF` | The small shipyard mark beside a star that has a yard. |
| `colours.systemRing` | `#73D9FFFF` | The ring around the selected system. |
| `colours.shipIcon` | `#FFFFFFFF` | The tint of ship icons on the map. |
| `colours.shipGlow` | `#FFFFFFBF` | The glow behind a ship in flight. |
| `colours.shipTrail` | `#FFFFFFE6` | The engine trail behind a ship in flight. |
| `colours.shipRing` | `#FFD966FF` | The ring around the selected ship. |
| `colours.routeLine` | `#73D9FFFF` | Route lines and their arrows. How strong each kind of line is comes from the `routes` section. |
| `colours.closeUpPanel` | `#0D121CE6` | The background of the panel in the ship close-up. |
| `colours.closeUpBackground` | `#000000FF` | The colour behind the close-up's backdrop pictures. |
| `colours.progressTrack` | `#FFFFFF1F` | The empty part of the close-up's voyage progress bar. |
| `colours.progressFill` | `#73D9FFFF` | The filled part of the progress bar. |
| `colours.loss` | `#FF8A73FF` | A figure that is bad news: a net loss in the job list, the planner's quote, or a receipt, and "Cannot fly" beside a job. |
| `colours.rowSelected` | `#73D9FF40` | The wash over the job or destination chosen in the voyage planner. |
| `colours.fieldBackground` | `#1A2433FF` | The inside of a box you type a number into (the load boxes in the planner). |
| `colours.fieldBorder` | `#9EADC2FF` | The outline of a box you type a number into. |
| `colours.confirmBox` | `#26364DFF` | The background of the planner's "Buy supplies and launch?" question. |

## text

| Field | Default | Unit | What it does |
|---|---|---|---|
| `text.font` | `""` | Text | The font for all text: the name of a font file in `game\Assets\Resources\Fonts`, without its ending. `""` uses Unity's built-in font. A name with no file is an error. |
| `text.body` | `14` | Pixels (6 to 100) | The size of ordinary text. |
| `text.small` | `12` | Pixels (6 to 100) | The size of small text: notification rows, counts. |
| `text.mapLabel` | `13` | Pixels (6 to 100) | The size of star names and ship names on the map. |
| `text.title` | `18` | Pixels (6 to 100) | The size of panel titles. |
| `text.closeUpTitle` | `30` | Pixels (6 to 200) | The size of the ship's name in the close-up. |
| `text.closeUpSubtitle` | `15` | Pixels (6 to 100) | The size of the line under the ship's name in the close-up. |
| `text.titlesBold` | `true` | Yes/no | Whether titles are bold. |

## panels

Spacing shared by the panels on the map. All in pixels.

**One side panel at a time, in two tiers (owner's rule, 2026-11-27, reworded in session 14).** This is a fixed rule of how the screens behave, not a value in the theme file, so there is no field for it.

- **Work panels** (the system view, the ship panel, the voyage planner, a receipt): only one shows at a time. Opening another replaces it, and closing it brings back the one it replaced.
- **Utility panels** (the notification list, settings): only one is open at a time, and it sits over the work panel. They replace each other and never bring each other back. Closing one returns to the work panel underneath. While a utility panel is open the work panel underneath is hidden, so nothing of it shows beside or below the utility panel.
- **Esc** closes a utility panel first if one is open, then the top work panel (the planner or a receipt), then the close-up, then clears the selection.

| Field | Default | What it does |
|---|---|---|
| `panels.margin` | `16` | The gap between a panel and the edge of the screen. |
| `panels.padding` | `12` | The space inside a panel, between its edge and its contents. |
| `panels.infoWidth` | `260` | The width of the system and ship panel. |
| `panels.messagesWidth` | `640` | The widest the startup messages box may be. |
| `panels.messagesPadding` | `10` | The space inside the messages box. |
| `panels.rowGap` | `4` | The gap between one line of text and the next. |
| `panels.iconRowGap` | `6` | The gap between rows that start with an icon. |
| `panels.iconSize` | `20` | The size of icons in panel rows. |
| `panels.iconGap` | `8` | The gap between an icon and the text after it. |
| `panels.buttonRowGap` | `10` | The gap above a row of buttons. |
| `panels.iconButtonSize` | `30` | The size of an icon button, such as Follow. |
| `panels.inlineGap` | `12` | The gap between things side by side on one row. |
| `panels.listMinHeight` | `40` | The least height of a scrolling list, such as the ships docked at a system. |
| `panels.debugLineLeft` | `12` | How far the debug line (seed and system count) is from the left edge. |
| `panels.debugLineBottom` | `8` | How far the debug line is from the bottom edge. |
| `panels.buttonBorderWidth` | `1` | The thickness of a text button's outline. |
| `panels.scrollBarWidth` | `10` | The width of scroll bars. |

## topBar

The bar across the top of the screen. All in pixels.

| Field | Default | What it does |
|---|---|---|
| `topBar.height` | `36` | The height of the bar (20 to 200). Panels and the close-up start below it. |
| `topBar.padding` | `8` | The space at the left and right ends of the bar. |
| `topBar.gap` | `8` | The gap between groups on the bar (date, clock buttons, credits). |
| `topBar.buttonSize` | `26` | The size of the bar's buttons. |
| `topBar.speedButtonWidth` | `34` | The width of each of the 1x, 3x, 5x buttons. |
| `topBar.dateWidth` | `104` | The room kept for the date, so the buttons do not shift as the date changes. |
| `topBar.iconSize` | `18` | The size of the credits icon. |
| `topBar.iconGap` | `4` | The small gap between neighbours inside a group. |

## notifications

The list under the bell. Pixels unless noted.

| Field | Default | What it does |
|---|---|---|
| `notifications.width` | `380` | The width of the list. |
| `notifications.maxHeight` | `260` | The height of the scrolling part of the list. |
| `notifications.rowHeight` | `44` | The height of one notification (10 to 1,000). Every row is the same height. |
| `notifications.dateWidth` | `88` | The room kept for the date at the start of a row. |
| `notifications.rowPadding` | `6` | The space at the left and right of a row. |
| `notifications.badgeMaxCount` | `99` | Whole number (1 to 9,999). The largest count shown beside the bell; above it the bell shows this number and a plus, for example "99+". |
| `notifications.iconSize` | `20` | The size of the icon at the start of a row. |
| `notifications.iconGap` | `8` | The gap between that icon and the date. |

## settings

The settings panel under the gear. All in pixels.

| Field | Default | What it does |
|---|---|---|
| `settings.width` | `280` | The width of the panel. |
| `settings.rowGap` | `8` | The gap between one slider row and the next. |
| `settings.captionWidth` | `64` | The room kept for the words "Master", "Effects", "Music". |
| `settings.valueWidth` | `36` | The room kept for the number after each slider. |
| `settings.sliderHeight` | `8` | The thickness of a slider's groove. |
| `settings.sliderHandleWidth` | `10` | The width of the part of a slider you drag. |
| `settings.sliderHandleHeight` | `18` | The height of the part of a slider you drag. |

## labels

Names and counts drawn on the map. Pixels unless noted.

| Field | Default | What it does |
|---|---|---|
| `labels.gap` | `16` | How far a star's name starts from the centre of its star. |
| `labels.height` | `18` | The height allowed for one name when deciding whether two names would overlap. |
| `labels.clearance` | `3` | The least space kept between a name and anything else (another name, a star, a panel). A name that cannot keep it is hidden. |
| `labels.fallbackLetterWidth` | `7.5` | The width assumed for one letter in the first frame, before the real width of a name is known. |
| `labels.countWidth` | `40` | The room kept for the "+3" count under a crowded star. |
| `labels.movingShipNames` | `"selectedOrHovered"` | Choice. Which ships in flight show their name on the map: `"never"` (none), `"selectedOrHovered"` (only the selected ship and the one under the mouse), or `"always"` (all of them). |
| `labels.shipNameGap` | `18` | How far a ship's name starts from the centre of its icon. |

## map

Stars and their marks. Pixels unless noted. These sizes stay the same on screen at every zoom.

| Field | Default | What it does |
|---|---|---|
| `map.starPixels` | `26` | The size of a star (1 to 1,000). |
| `map.starGlowPixels` | `84` | The size of the glow around a star. |
| `map.shipyardBadgePixels` | `15` | The size of the shipyard mark. |
| `map.shipyardBadgeOffset` | `0.55` | Where the shipyard mark sits, as a share of the star's size up and to the right of its centre (0 to 10). |
| `map.selectionRingPixels` | `58` | The size of the ring around the selected system. |

## ships

Ships on the map. Pixels unless noted.

| Field | Default | What it does |
|---|---|---|
| `ships.iconPixels` | `26` | The size of a ship in flight (1 to 1,000). |
| `ships.dockedIconPixels` | `22` | The size of a docked ship beside its star (1 to 1,000). |
| `ships.glowPixels` | `56` | The size of the glow behind a ship in flight. |
| `ships.trailPixels` | `52` | The length of the engine trail. |
| `ships.trailOffset` | `0.35` | How far behind the ship's centre the trail starts, as a share of the ship's size (0 to 10). |
| `ships.selectionRingPixels` | `44` | The size of the ring around the selected ship. |
| `ships.maxDockedIconsPerStar` | `5` | Whole number (1 to 100). The display limit for docked ships: at most this many icons are drawn beside one star. The rest are counted in the "+N" under it. The selected ship is always one of those drawn. |
| `ships.fanRadiusPixels` | `36` | How far from the star's centre the docked ships sit. |
| `ships.fanFirstAngleDegrees` | `150` | Degrees (0 to 360). Where the first docked ship sits around the star: 0 is to the right, 90 is straight up. |
| `ships.fanStepDegrees` | `40` | Degrees (0 to 360). The angle between one docked ship and the next. |
| `ships.overflowBelowPixels` | `62` | How far below the star's centre the "+N" count sits. |

## routes

Route lines. The colour is `colours.routeLine`; these set how strong and thick each kind of line is.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `routes.arrowPixels` | `13` | Pixels | The size of the arrow on a route line. |
| `routes.arrowAlong` | `0.6` | Share (0 to 1) | Where the arrow sits along its line: 0 is the start, 1 is the end. |
| `routes.pausedRouteAlpha` | `0.2` | Share (0 to 1) | How solid the lines of a paused route are. |
| `routes.routeAlpha` | `0.35` | Share (0 to 1) | How solid the lines of a running route are. |
| `routes.currentLegAlpha` | `0.55` | Share (0 to 1) | How solid the line a ship is flying right now is. |
| `routes.selectedAlpha` | `0.95` | Share (0 to 1) | How solid the selected ship's lines are. |
| `routes.routeWidth` | `1.5` | Pixels | The thickness of a route's lines. |
| `routes.currentLegWidth` | `2` | Pixels | The thickness of the line a ship is flying right now. |
| `routes.selectedWidth` | `2.5` | Pixels | The thickness of the selected ship's lines. |

## background

The starfield and nebula pictures behind the map.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `background.starfieldTileScreens` | `1` | Screens (0.1 to 100) | How large one copy of the starfield picture is drawn: 1 means one copy is one screen high. |
| `background.starfieldDriftTilesPerLy` | `0.012` | Picture-widths per light-year (0 to 10) | How much the starfield slides when the map is panned one light-year. Smaller looks further away. |
| `background.nebulaTileScreens` | `1.6` | Screens (0.1 to 100) | How large one copy of the nebula picture is drawn. |
| `background.nebulaDriftTilesPerLy` | `0.006` | Picture-widths per light-year (0 to 10) | How much the nebula slides when the map is panned one light-year. |

## closeUp

The ship close-up screen. Pixels unless noted; percentages are of the screen's width or height.

| Field | Default | What it does |
|---|---|---|
| `closeUp.panelWidth` | `340` | The width of the panel on the right. |
| `closeUp.panelPaddingSide` | `14` | The space at the left and right inside that panel. |
| `closeUp.panelPaddingTop` | `12` | The space at the top and bottom inside that panel. |
| `closeUp.headerLeft` | `24` | How far the ship's name is from the left edge. |
| `closeUp.headerTop` | `20` | How far the ship's name is below the bar. |
| `closeUp.headerGap` | `32` | The gap between the header and what is beside it. |
| `closeUp.suppliesTop` | `8` | The gap above the row of fuel, food, and water. |
| `closeUp.supplyGap` | `14` | The gap between one supply and the next. |
| `closeUp.supplyIconGap` | `5` | The gap between a supply's icon and its number. |
| `closeUp.iconSize` | `18` | The size of icons in the close-up. |
| `closeUp.iconGap` | `6` | The gap between an icon and its text. |
| `closeUp.rowGap` | `5` | The gap between one row of the panel and the next. |
| `closeUp.lineGap` | `4` | The gap between lines of text in one block. |
| `closeUp.headingGap` | `6` | The gap above and below a heading in the panel. |
| `closeUp.captionWidth` | `100` | The room kept for a row's caption, so the values line up. |
| `closeUp.iconCaptionWidth` | `76` | The room kept for a caption that follows an icon. |
| `closeUp.barHeight` | `6` | The thickness of the voyage progress bar. |
| `closeUp.barGap` | `4` | The gap above the progress bar. |
| `closeUp.closeButtonSize` | `34` | The size of the close button. |
| `closeUp.footerLeft` | `24` | How far the close button and hint are from the left edge. |
| `closeUp.footerBottom` | `20` | How far they are from the bottom edge. |
| `closeUp.hintGap` | `10` | The gap between the close button and the hint text. |
| `closeUp.shipLeftPercent` | `6` | Percent of the screen's width (0 to 100). Where the ship picture's left edge is. |
| `closeUp.shipWidthPercent` | `52` | Percent of the screen's width (1 to 100). How wide the ship picture is. |
| `closeUp.shipTopPercent` | `30` | Percent of the screen's height (0 to 100). Where the ship picture's top edge is. |
| `closeUp.shipHeightPercent` | `48` | Percent of the screen's height (1 to 100). How tall the ship picture is. |
| `closeUp.glowOutsetPixels` | `20` | How far the drive glow reaches beyond the ship picture on every side. |
| `closeUp.trailRightPercent` | `88` | Percent of the ship picture's width (0 to 200). How far from the picture's right edge the engine trail ends; it sits behind the ship. |
| `closeUp.trailWidthPercent` | `60` | Percent of the ship picture's width (0 to 200). How long the engine trail is. |
| `closeUp.trailTopPercent` | `40` | Percent of the ship picture's height (0 to 100). Where the top of the trail is. |
| `closeUp.trailHeightPercent` | `20` | Percent of the ship picture's height (0 to 100). How tall the trail is. |
| `closeUp.backdropPixelsPerTick` | `0.05` | Pixels per tick (0 to 10). How fast the stars slide past a ship in flight. A tick is the game's smallest step of time: one real millisecond at 1x. The slide follows game time, so it is faster at 3x and 5x and stops while paused. |

## systemView

The panel for a selected star: its industries, the prices of supplies, its jobs, the ships docked there, and its shipyard's stock.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `systemView.width` | `440` | Pixels (200 to 2,000) | The width of the system view. It is wider than the ship panel (`panels.infoWidth`) because a job needs a whole line. |
| `systemView.sectionGap` | `10` | Pixels | The gap above each heading in the system view ("Supplies for sale", "Jobs from...", "Docked here", "Shipyard stock"). |
| `systemView.priceWidth` | `130` | Pixels | The width given to each of the three supply prices (fuel, food, water), which sit side by side. |

## jobList

The list of jobs, in the system view and in the voyage planner. Each job is one row of two lines of text with a figure on the right.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `jobList.rowHeight` | `44` | Pixels (10 to 1,000) | The height of one job row. Two lines of text must fit. |
| `jobList.height` | `180` | Pixels (40 to 2,000) | The height of the list itself. More jobs than fit are reached by scrolling. |
| `jobList.rowPadding` | `6` | Pixels | The space between a row's text and its left and right edges. |
| `jobList.figureWidth` | `150` | Pixels | The width of the figure on the right of a row ("Net 96,267", "3,300 per tonne"). |
| `jobList.controlGap` | `6` | Pixels | The gap between the sort buttons above the list, and between them and the "hide jobs this ship cannot fly" button. |

## planner

The voyage planner: the job list, the load, the quote, and the Provision and Launch buttons.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `planner.width` | `520` | Pixels (200 to 2,000) | The width of the planner. |
| `planner.sectionGap` | `10` | Pixels | The gap between the planner's parts (job list, load, quote, buttons, the launch question). |
| `planner.lineGap` | `3` | Pixels | The gap between one line of the quote and the next, and between one load row and the next. |
| `planner.nameWidth` | `110` | Pixels | The width given to the name at the start of a load row ("Ore", "Passengers"). |
| `planner.stepButtonWidth` | `30` | Pixels | The width of the minus and plus buttons beside a load. |
| `planner.amountWidth` | `64` | Pixels | The width of the box a load is typed into. |
| `planner.controlGap` | `6` | Pixels | The gap between buttons that sit side by side in the planner (Provision and Launch, Yes and No, Max). |
| `planner.confirmPadding` | `8` | Pixels | The space inside the "Buy supplies and launch?" question box. |

## receipt

The delivery receipt shown for a finished voyage.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `receipt.width` | `440` | Pixels (200 to 2,000) | The width of the receipt. |
| `receipt.sectionGap` | `10` | Pixels | The gap above the receipt's total lines (revenue, supplies used, net profit). |
| `receipt.lineGap` | `3` | Pixels | The gap between the receipt's other lines. |

## controls

How the camera and the mouse feel. This section is separate from the look: nothing here changes what anything looks like, and nothing here changes game rules.

| Field | Default | Unit | What it does |
|---|---|---|---|
| `controls.clickRadiusPixels` | `24` | Pixels (1 to 500) | How close to a star or ship a click must land to select it. The same reach decides what counts as "under the mouse". Larger is more forgiving; too large makes neighbours hard to tell apart. |
| `controls.zoomPerWheelNotch` | `0.85` | Share (0.1 to 0.99) | How much of the view is left after one notch of the mouse wheel inward. 0.85 means each notch shows 85% of what was shown before. Closer to 1 zooms more gently. |
| `controls.maxWheelNotchesPerFrame` | `5` | Notches (1 to 100) | The most wheel notches counted in one frame (one redraw of the screen), so a fast spin does not jump the zoom. |
| `controls.keyPanScreensPerSecond` | `0.8` | Screens per second (0.01 to 100) | How fast WASD and the arrow keys move the map: 0.8 means four-fifths of the screen's height each second, at any zoom. |
| `controls.closestZoomHalfHeightLy` | `1.5` | Light-years (0.1 to 1,000) | The closest zoom: at it, the screen shows this many light-years above the centre and the same below. Wheeling in again from here opens the selected ship's close-up. |
| `controls.fitMarginPercent` | `12` | Percent of the galaxy's size (0 to 1,000) | Extra room left around the whole galaxy at the widest zoom. |
| `controls.fitMarginLy` | `1.5` | Light-years (0 to 1,000) | A fixed extra room added to that, so small galaxies are not edge to edge either. |
| `controls.panMarginLy` | `3` | Light-years (0 to 1,000) | How far past the outermost stars the centre of the view may be moved. |
