# spacetrade2: Eli's task list

Things for you to do while the agent builds. None of the art or sound work blocks the agent: it builds with placeholders, and your files replace them by filename.

## 1. Before session 1

- [x] In Unity Hub, create a new project with Unity 6.6 using the **Universal 2D** template. Location: `D:\dev\spacetrade2`. Project name: `game`. Result: `D:\dev\spacetrade2\game`.
- [x] Add the Unity MCP package to that new project (the same way you connected it before; the connection is per project) and confirm Cursor shows the MCP server as connected.
- [x] In Cursor, open the folder `D:\dev\spacetrade2` (the top folder, not `game`), so the agent sees `AGENTS.md` and `docs`.
- [x] In Cursor's model picker, select Opus 5.5 for the agent.
- [x] Add Unity 6.6 and Cursor to `tech-inventory.md`, and add spacetrade2 to the coding projects table.

## 2. During the build

- [ ] Start each session with the prompt in `plan.md`, changing the session number. One fresh chat per session.
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
- [ ] After session 13: play in the Editor with the sound on and listen to every sound (the agent cannot hear). The agent's session 13 notes in `docs\progress.md` list what to listen for.

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
