# spacetrade2: audio checklist

24 sounds: 20 short effects, 4 loops. All 24 files are in place and are your real audio; since session 13 the game plays them as they are. A file with the same name in the same folder replaces the one there. A silent stand-in is made only if a file goes missing.

How they play (session 13): once per event, never once per frame; the same sound asked for twice in one frame plays once; only the more specific sound plays (pause without `click`, dispatch without `provision`), except that a paid arrival plays `arrival` and `payment` together. During an offline catch-up or a debug skip no event sound plays; `return_summary` plays once at the end.

## How to make them

- **Short effects:** WAV, 48 kHz, 16-bit, mono.
- **Loops:** OGG, 48 kHz, stereo, cut so the end joins the start with no click.
- Leave no silence at the start of an effect, or it will feel late.
- Keep loudness consistent across effects. The game has separate effects and music volume sliders, so do not try to balance music against effects in the files.
- Save into `D:\dev\spacetrade2\game\Assets\Audio\` in the folder shown for each section, using the exact filename. Overwrite the file that is there. Do not rename files.
- Lengths are targets, not hard limits.

**If you only make three:** `arrival.wav`, `blocked.wav`, and `click.wav`.

## 1. Interface

Folder: `game\Assets\Audio\UI\` (7 files)

- [ ] `click.wav`
- [ ] `panel_open.wav`
- [ ] `panel_close.wav`
- [ ] `select.wav`
- [ ] `error.wav`
- [ ] `toggle_on.wav`
- [ ] `toggle_off.wav`

| Name | Plays when | Type | Length |
|---|---|---|---|
| `click.wav` | Any button is pressed | WAV, mono | Under 0.3 s |
| `panel_open.wav` | A panel or menu opens | WAV, mono | Under 0.5 s |
| `panel_close.wav` | A panel or menu closes | WAV, mono | Under 0.5 s |
| `select.wav` | A system or ship is selected on the map | WAV, mono | Under 0.5 s |
| `error.wav` | An action is rejected (infeasible load, remote upgrade, not enough credits) | WAV, mono | Under 1 s |
| `toggle_on.wav` | A toggle switches on (in-system transport, auto-provision, follow) | WAV, mono | Under 0.5 s |
| `toggle_off.wav` | A toggle switches off | WAV, mono | Under 0.5 s |

## 2. Clock

Folder: `game\Assets\Audio\Clock\` (3 files)

- [ ] `pause.wav`
- [ ] `resume.wav`
- [ ] `speed_change.wav`

| Name | Plays when | Type | Length |
|---|---|---|---|
| `pause.wav` | The clock is paused | WAV, mono | Under 1 s |
| `resume.wav` | The clock resumes | WAV, mono | Under 1 s |
| `speed_change.wav` | Speed switches between 1x, 3x, 5x | WAV, mono | Under 1 s |

## 3. Events

Folder: `game\Assets\Audio\Events\` (10 files)

- [ ] `dispatch.wav`
- [ ] `arrival.wav`
- [ ] `payment.wav`
- [ ] `blocked.wav`
- [ ] `waiting.wav`
- [ ] `provision.wav`
- [ ] `purchase_ship.wav`
- [ ] `drive_install.wav`
- [ ] `return_summary.wav`
- [ ] `insystem_income.wav`

| Name | Plays when | Type | Length |
|---|---|---|---|
| `dispatch.wav` | A ship departs | WAV, mono | Up to 3 s |
| `arrival.wav` | A ship arrives. The most important sound: it should be noticeable from another window | WAV, mono | Under 2 s |
| `payment.wav` | A delivery receipt pays out | WAV, mono | Under 2 s |
| `blocked.wav` | A route becomes blocked. Should sound different from arrival and more urgent | WAV, mono | Under 2 s |
| `waiting.wav` | A ship starts waiting for a minimum load (subtle) | WAV, mono | Under 1 s |
| `provision.wav` | Supplies are bought | WAV, mono | Under 1 s |
| `purchase_ship.wav` | A ship is bought | WAV, mono | Under 2 s |
| `drive_install.wav` | A drive upgrade is installed | WAV, mono | Under 2 s |
| `return_summary.wav` | The "while you were away" summary opens. Also plays once, in place of every arrival and other event sound, at the end of an offline catch-up or a debug skip | WAV, mono | Under 2 s |
| `insystem_income.wav` | In-system transport pays its daily income (very subtle; may stay silent) | WAV, mono | Under 0.5 s |

## 4. Loops

Folder: `game\Assets\Audio\Loops\` (4 files)

- [ ] `ship_closeup_drive.ogg`
- [ ] `ship_closeup_docked.ogg`
- [ ] `music_map.ogg`
- [ ] `music_title.ogg`

| Name | Plays when | Type | Length |
|---|---|---|---|
| `ship_closeup_drive.ogg` | Drive hum while zoomed into a ship in transit | OGG, stereo, seamless loop | 10 to 20 s |
| `ship_closeup_docked.ogg` | Quiet station ambience while zoomed into a docked ship | OGG, stereo, seamless loop | 10 to 20 s |
| `music_map.ogg` | Background music on the galaxy map | OGG, stereo, seamless loop | 2 to 4 min |
| `music_title.ogg` | Title screen music | OGG, stereo, seamless loop | 1 to 2 min |
