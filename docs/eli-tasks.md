# spacetrade2: Eli's task list

Things for you to do while the agent builds. None of the art or sound work blocks the agent: it builds with placeholders, and your files replace them by filename.

## 1. Before session 1

Sessions 1 to 13 were built in Cursor, so the Cursor steps below are kept as the record of what was done. From session 14 on the agent is Claude Code: open the folder `D:\dev\spacetrade2` (the top folder, not `game`) in Claude Code, and check the Unity MCP server is connected by typing `/mcp`.

- [x] In Unity Hub, create a new project with Unity 6.6 using the **Universal 2D** template. Location: `D:\dev\spacetrade2`. Project name: `game`. Result: `D:\dev\spacetrade2\game`.
- [x] Add the Unity MCP package to that new project (the same way you connected it before; the connection is per project) and confirm Cursor shows the MCP server as connected.
- [x] In Cursor, open the folder `D:\dev\spacetrade2` (the top folder, not `game`), so the agent sees `AGENTS.md` and `docs`.
- [x] In Cursor's model picker, select Opus 5.5 for the agent.
- [x] Add Unity 6.6 and Cursor to `tech-inventory.md`, and add spacetrade2 to the coding projects table.

## 2. During the build

- [ ] Start each session with the prompt in `plan.md`, changing the session number. One fresh Claude Code session per plan session.
- [ ] Read the agent's plan before saying OK. Push back if it wants to do more than the one session.
- [ ] After session 3: open `game\Assets\StreamingAssets\tuning.json` next to `docs\tuning-guide.md` and get familiar with it. Do not change values yet.
- [ ] At Checkpoint A (after session 10): review the default tuning values. This is the cheapest moment to change the feel of the economy.
- [ ] After session 14: play one voyage yourself in the Editor. Note anything confusing in the planner and give it to the agent at the start of session 15.
- [ ] After session 18: read `docs\balance-report.md` and decide whether the pace of growth feels right.

## 2a. Star system names

- [ ] Write at least 50 star system names for `galaxy.names` in `game\Assets\StreamingAssets\tuning.json` (there are 24 today). No repeats, no blank names. The default galaxy moves to 25 systems once these exist (spec section 4), and galaxies up to 50 systems need a name for every system. Give the list to the agent, or add it yourself and tell the agent at the start of the next session.

## 3. Images to create

The full list, with a checkbox per image, is in `docs\graphics-checklist.md`. 61 images: 54 vector, 7 raster.

## 4. Sounds to create

The full list, with a checkbox per sound, is in `docs\audio-checklist.md`. 24 sounds: 20 short effects, 4 loops.

## 5. Tuning the economy

- [ ] Edit `game\Assets\StreamingAssets\tuning.json` in VS Code or Cursor. `docs\tuning-guide.md` explains every field.
- [ ] Change one group of values at a time, then have the agent re-run the balance harness (session 18) and compare the report.
- [ ] If the game reports a tuning error on start, the message names the bad field. The game falls back to defaults until it is fixed.

## 5a. Editing the look and the controls

- [ ] Edit `game\Assets\StreamingAssets\theme.json` in VS Code or Cursor. `docs\theme-guide.md` explains every field in plain language with its unit. Colours, text sizes, spacing, glow sizes, label rules, and display limits are in the look sections; click radius, zoom, and keyboard pan speed are in the `controls` section.
- [ ] If the game reports a theme error on start, the message names the bad field and the line. The game uses the built-in look and controls until it is fixed.
- [ ] After session 13: do the listening check in section 5b (the agent cannot hear).

## 5b. Listening check (after session 13)

The agent can prove the right file started at the right moment and volume, but not how it sounds. In Unity, open the `Main` scene, turn your speakers on, press Play, and click once inside the Game view so the keys work. Settings start at master 80, effects 80, music 60.

**Sounds you can trigger today (12 of the 24):**

| Sound | How to hear it |
|---|---|
| `music_map.ogg` | Starts by itself when you press Play. Leave it running past its end (2 to 4 minutes) and listen for a click or gap where it loops |
| `resume.wav` | Press Space, or the play button in the top bar (the game starts paused) |
| `pause.wav` | Press Space again, or the pause button |
| `speed_change.wav` | Press 2 or 3, or click a speed button that is not lit |
| `click.wav` | Click the speed button that is already lit (1x at the start) |
| `select.wav` | Click a star, or click the ship |
| `toggle_on.wav` | With the ship selected, press F or the Follow button |
| `toggle_off.wav` | Press F again. It also plays when you unmute |
| `panel_open.wav` | Click the bell (notification list) or the gear (settings) in the top bar |
| `panel_close.wav` | Click the bell or gear again, or press Esc |
| `ship_closeup_docked.ogg` | Select the ship (it is selected at the start) and press the Close-up button. The quiet station loop should start, and stop when you leave with Esc |
| `ship_closeup_drive.ogg` | Needs a ship in flight. Before pressing Play, select the `Game` object in the Hierarchy and set "Debug Ships In Flight" to 1 in the Inspector (under Game Bootstrap). Press Play, open the close-up, and listen for the drive hum. Afterwards stop Play and set the field back to 0; if Unity asks to save the scene, choose Don't Save |

**Sounds with no button yet (12).** They are wired and tested, but the screens that trigger them come later. Check each one after the session shown, or ask the agent to fire them one at a time while you listen.

| Sound | First hearable |
|---|---|
| `dispatch.wav`, `provision.wav`, `error.wav` | Session 14 (planner: launch a voyage, buy supplies, try a load that does not fit) |
| `arrival.wav`, `payment.wav` | Session 14 (a voyage arrives and pays) |
| `purchase_ship.wav`, `drive_install.wav`, `insystem_income.wav` | Session 15 (shipyard, and the in-system transport toggle, which also plays the two toggle sounds) |
| `waiting.wav`, `blocked.wav` | Session 16 (routes) |
| `return_summary.wav`, `music_title.ogg` | Session 17 (return summary and title screen) |

**Mute and sliders.** Open settings with the gear. Keep the map music playing as the reference, and use the bell (open and close) as the test effect.

- [ ] **Mute:** click "Mute all sound". The music stops at once, and the bell, Space, and clicking a star make no sound. Click again to unmute: the music returns and you hear `toggle_off.wav`.
- [ ] **Master at 0:** drag Master fully left. Music and effects are both silent. Put it back to 80.
- [ ] **Effects at 0:** the music keeps playing, the bell and Space are silent, and the close-up loop is silent (the two close-up loops belong to effects). Put it back to 80.
- [ ] **Music at 0:** the music is silent, the bell and Space still sound. Put it back to 60.
- [ ] **Remembered:** set Music to 30, stop Play, press Play again, and open settings. It should still read 30. Put it back to 60, or leave whatever you prefer.
- [ ] **Loudness:** note any sound that is too loud, too quiet, late, or cut off, and whether 80 / 80 / 60 are the right starting volumes. Give the list to the agent.

## 6. Final checks only you can do (session 21)

Run these in the normal Windows build from the zip, not in the Editor. Use a phone stopwatch.

- [ ] **Playthrough.** New game, quote, provision, launch, follow the ship, read the receipt after arrival. (Arrival takes real time. Leave it running, or check this step after an overnight offline catch-up.)
- [ ] **Timing test.** Load the `timing-test` save. Launch the 4.5 ly trip at 1.5c at 1x and start the stopwatch.
  - [ ] At 6:00, write down the progress shown. Expected: about 3.33 percent, 0.15 ly.
  - [ ] Pause for 1:00. Write down progress again. Expected: unchanged.
  - [ ] Write down the remaining real ETA at 1x, then switch to 5x. Expected: one fifth of it. Arrival date unchanged.
  - [ ] Switch to 3x. Expected: one third of the 1x ETA. Arrival date unchanged.
- [ ] **No debug skip.** Confirm there is no "Skip to next arrival" button anywhere in this build.
- [ ] **Offline.** Save while a ship is in transit, quit, wait at least 10 minutes, reopen. Confirm the return summary and that progress moved by the right amount. Quit and reopen again straight away: no second payout.
- [ ] Give the agent your written numbers for `docs\verification-report.md`.
