# spacetrade2: build plan

Version 1.0, 2026-11-24. Built in Cursor with Opus 5.5 (high), using the Unity MCP server to drive the Unity 6.6 Editor. `spec.md` says what to build; this file says in what order.

## How to run a session

One session = one fresh Cursor agent chat = one numbered task below. Do not carry a chat across tasks. A fresh chat keeps the agent's context small, which keeps it accurate and uses fewer credits.

Paste this to start each session, changing only the number:

```
Read AGENTS.md, docs/spec.md, docs/plan.md, and docs/progress.md.
Do session N from docs/plan.md and nothing else.
First show me your plan for this session and wait for my OK.
After my OK: do the work, run the checks listed under "Done when", and show me the real results (test counts, console output), not a summary of what you expect.
Then update docs/progress.md, commit, push, and give me the one-line status plus anything I need to decide before session N+1.
```

Rules that apply to every session (also in `AGENTS.md`):

- If a check fails, fix it in the same session or stop and say so. Never mark a session done with failing tests.
- If the spec is unclear or seems wrong, stop and ask. Do not quietly pick a different design.
- Simulation sessions (2 to 10) need no art and no scene. They are finished when their tests pass in the Unity Test Runner (EditMode).
- Interface sessions (11 to 17) finish with the agent pressing Play through MCP, exercising the feature, and reporting a clean console.

## Phase A: simulation (no visuals)

### Session 1: scaffold

- Confirm the Unity project at `game/` opens and MCP responds. Record the exact Editor version string from `game/ProjectSettings/ProjectVersion.txt` in `README.md`.
- Fill the empty root `.gitignore` with Unity ignore rules that work for a project in the `game/` subfolder (Library, Temp, Obj, Logs, UserSettings, build output), plus `builds/`.
- Create assemblies: `SpaceTrade.Sim` (no engine references), `SpaceTrade.Sim.Tests` (EditMode), `SpaceTrade.Game`, `SpaceTrade.Game.Tests` (PlayMode). Add the Test Framework and Input System packages.
- Create folders: `game/Assets/Art/`, `game/Assets/Audio/`, `game/Assets/StreamingAssets/`.
- Set company and product name to `spacetrade2`, `runInBackground` on.
- **Done when:** one trivial test passes in each test assembly, console is clean, first commit is pushed.

### Session 2: time and events

- Tick units and calendar (spec 3.1), the clock with pause and the three speeds, the event queue, `Advance(toTick)`, the real-time driver logic with an injected time source, and the same-tick ordering in spec 8.1.
- Transit time function (spec 3.3).
- **Done when:** tests pass for the timing table, speed changes not moving arrival ticks, pause freezing time, and frame-rate independence (the same elapsed time fed in 1 chunk or 10,000 chunks gives the same tick).

### Session 3: tuning data and galaxy

- Tuning data model, loader, validation with field-named errors, built-in defaults, and the test fixture copy (spec 12). Write `game/Assets/StreamingAssets/tuning.json` and `docs/tuning-guide.md`.
- Seeded galaxy generation with all guarantees in spec 4.
- **Done when:** tests pass for same seed giving the same galaxy, 200 seeds all meeting the guarantees (system count, closed circuit, reachability, shipyards), and a deliberately broken tuning file producing a named error and falling back to defaults.

### Session 4: markets and reservations

- Freight offers, supply, demand, passenger pools, replenishment events, seeded rate re-rolls, provision prices (spec 5).
- Reservation and release functions used later by dispatch.
- **Done when:** tests pass for replenishment up to caps, rate rolls staying in range and repeating for the same seed, and the 80-tonne case (two claims never total more than 80).

### Session 5: ships, provisioning, quote

- Ship classes, drives, on-board provisions with acquisition cost, the provisioning formulas, and the single quote function with quantitative failure reasons (spec 6.1 to 6.5).
- **Done when:** tests pass for hold, tank, and berth limits; supplies' own mass counted against the hold; the "unload N tonnes" message giving the correct N; one-click provisioning buying exactly the shortfall; quote figures matching hand-calculated examples.

### Session 6: dispatch, voyage, arrival

- The dispatch transaction, locked voyage terms, arrival settlement, the receipt, and expense-once accounting (spec 6.6, 6.7).
- **Done when:** tests pass for failed dispatch leaving state byte-identical, payout exactly once, receipt totals matching the quote, supplies bought two stops earlier expensed once on the voyage that uses them, and two ships travelling independently.

### Session 7: new-game start and in-system transport

- New-game creation with the opening-offer guarantee (spec 6.4), using the real quote function.
- In-system transport (spec 10).
- **Done when:** tests pass for 200 seeds each offering two or more feasible affordable jobs with at least one profitable; the live tuning file meeting the same guarantee; and the in-system recovery cases in spec 14.2.

### Session 8: shipyards

- Yard stock and prices, ship purchase delivered docked at the yard, drive upgrades with the docked-at-stocking-yard rule, the upgrade preview on a known route (spec 7).
- **Done when:** tests pass for purchase and delivery, local upgrade success, remote upgrade rejection with reason, stock decrement and restock, and preview time-saved matching the transit formula.

### Session 9: automated routes

- Route orders, per-stop settings, minimum load, waiting with recheck on replenishment, blocked state, empty legs, whole-circuit estimate with estimate labels (spec 8).
- **Done when:** tests pass for a three-stop circuit running 20 game years with correct money, minimum-load waiting and release when supply arrives, the "Waiting for N more tonnes" text, an infeasible leg flagging blocked, empty-leg costs in the circuit estimate, and simultaneous arrivals resolving in ship-id order.

### Session 10: saves, offline time, debug skip

- Versioned save model, atomic write with backup, load with migration hook, autosave triggers (spec 11.1, 11.2).
- Offline catch-up, return summary data, backward-clock handling, sleep reconcile (spec 11.3).
- Debug skip command with the capability gate (spec 13).
- **Done when:** tests pass for save and load round trip equality, a mid-transit save, paused saves not advancing, the three-way equivalence (ordinary, catch-up, debug stepping) on balances, fleet positions, stock, and route state, reloading a settled save paying nothing more, backward clock giving zero elapsed, and debug skip rejected when the capability is absent.

**Checkpoint A.** The whole game now exists as rules with no screen. The agent runs the full EditMode suite and writes a short status in `docs/progress.md`. Eli reviews the default `tuning.json` with `docs/tuning-guide.md` before moving on.

### Session 10b: scaling, receipts, and balance targets

- Simulation and docs only; Assets/Art and Assets/Audio are not touched.
- Receipts: keep the newest N in full (new tuning field `receipts.keepNewest`, default 500, at least 1) and running delivery totals for everything, so the return summary covers the whole absence exactly however many deliveries there were. Spec, tuning guide, live file, and fixture change together. Replaces the session 6 rule of keeping every receipt.
- Long absences: no cap on offline time. Catch-up runs in slices through the same `Advance`, with a progress figure the screen can show, and ends in exactly the same state as one big step.
- Bigger maps: measure galaxies of 25, 50, and 100 systems without changing any rule: generation guarantees across 200 seeds, freight offers and passenger pools, jobs a typical port lists, save size and time, and catch-up time for 30 real days at 5x with 10 ships on routes. Report options with a recommendation for anything that does not scale; build none of them.
- Balance targets: record the owner's second-courier, recovery, in-system ceiling, and freight targets in spec sections 10 and 12 and as notes for session 18. Retune nothing.
- **Done when:** tests pass for keeping only the newest receipts with exact totals, a return summary covering more deliveries than are kept, loading with a lower limit, converting a format 1 save, and slice-by-slice catch-up ending byte-identical to one big step (state, summary, and saved file); the full EditMode and PlayMode suites pass with a clean console; and the scaling measurements are recorded in `docs/progress.md` with options and a recommendation.

## Phase B: the game you can see

### Session 11: map shell

- Startup scene, the bootstrap that creates the simulation and drives it from unscaled time, galaxy map with star systems, names, pan and zoom, system selection.
- Art loading by fixed filename with a generated placeholder for every image in `eli-tasks.md` section 3.
- **Done when:** Play mode shows the generated galaxy, pan, zoom, and select work, console is clean, and every placeholder file exists at its listed path.

### Session 10c: market speed and a job distance limit (after session 11, before session 12)

- A fresh chat, simulation and docs only. Assets/Art, Assets/Audio, and the interface are not touched. Show the plan and wait for the owner's OK before changing anything, because the distance limit changes a rule; list every gap as a question with options and a recommendation.
- **Speed (option 1 from the 10b study):** index freight offers and passenger pools by origin system, so a market event no longer scans the whole galaxy. A pure speed change: every result stays byte-identical, proven by the existing tests.
- **Job distance limit (option 2, a rule change):** a new tuning field, a maximum job distance, so a port offers freight and passenger jobs only to destinations within that distance. Spec section 5, the tuning guide, the live file, and the fixture change together. Session 18 sets the final value. Every galaxy guarantee must still hold within the limit: reachability, an export and an import for every system, the closed three-stop circuit, and the opening offers. A limit that makes any of these impossible is a tuning error naming the field.
- **Done when:** the full EditMode and PlayMode suites pass with a clean console; the speed change is shown byte-identical by the existing tests; tests cover the distance limit (no job beyond it, every guarantee within it over 200 seeds, a field-named error when it is too small); and the 25 and 50 system measurements from session 10b are re-run and reported before and after in `docs/progress.md`.

### Session 12: ships on the map

- Ships drawn at true positions along their legs, active routes drawn, ship selection, follow mode, and the zoom-in close-up with the vessel panel (spec 9.1).
- **Done when:** in Play mode with a test voyage, the ship's map position matches its simulated progress, follow keeps it centred, and the close-up shows live values.

### Session 13: top bar, notifications, sound

- Date, pause/play, the three speed buttons, credits, both ETA displays, notification list with click-to-jump (spec 9.4, 3.4).
- Audio system: one named event per row in `eli-tasks.md` section 4, a silent placeholder file for each, volume sliders and mute in settings.
- **Done when:** speed changes show on the real-time ETA without changing the arrival date, the countdown freezes when paused, and a debug log line confirms each sound event fires at the right moment.

### Session 14: system view, planner, receipt

- System view (spec 9.2), the voyage planner with the quote, one-click provisioning, failure messages, dispatch, and the arrival receipt (spec 6.5 to 6.7).
- The job list can be sorted (for example nearest or most profitable first) and filtered (for example hide jobs the selected ship cannot fly). Presentation only (option 3 from the session 10b study).
- **Done when:** the agent completes a full manual voyage in Play mode through the interface only (using debug skip to reach arrival), and the receipt matches the quote.

### Session 15: fleet, ship panel, shipyard

- Fleet list with statuses (spec 9.3), ship panel with the in-system transport toggle, shipyard screens for buying ships and drives with the upgrade preview.
- **Done when:** in Play mode the agent buys a second ship, runs both at once, installs a drive at a yard, and sees a remote upgrade rejected with its reason.

### Session 16: route editor

- Route creation and editing, per-stop settings, the circuit estimate, waiting and blocked displays, changing orders on a blocked ship (spec 8).
- **Done when:** the agent builds a three-stop route in Play mode and watches it complete two loops unattended, and a deliberately infeasible route shows blocked with a reason and a notification.

### Session 17: menus, saves, return summary

- Title screen, new game, load, save slots, load demo, settings, quit. Offline return summary panel. Debug skip button shown only under the rules in spec 13.
- Saving, autosave, and offline catch-up are wired to the screen here (session 11 runs an unsaved game in memory). The screen must not freeze: open saves with `GameSession.BeginOpen`, run the catch-up in slices with a progress percentage on screen, and keep autosaves from stalling a frame. If an autosave takes longer than the limit below, writing the file moves off the drawing step.
- **Done when:** the agent saves mid-transit, stops Play, restarts, and sees a correct return summary; a paused save returns paused; and at 12 and 50 systems no frame during a catch-up or an autosave takes longer than 50 ms, measured and reported.

## Phase C: balance, proof, delivery

### Session 18: balance and demo save

- A headless balance harness (runs the simulation with a simple scripted player over many seeds, no screen) reporting against the balance targets in spec 12 (and the recovery target in spec 10). Adjust default `tuning.json` values to meet them.
- Build the demo save and the `timing-test` save (spec 11.4, 14.4).
- **Done when:** the harness report is saved to `docs/balance-report.md` with each target marked met or not met with numbers, and both bundled saves load.

### Session 19: full playthrough and fixes

- The agent runs acceptance 14.1 end to end in the Editor through the normal interface and fixes what it finds. Re-run the full test suite.
- **Done when:** playthrough steps are logged with results in `docs/verification-report.md`, all tests pass.

### Session 20: Windows builds

- A normal (release) Windows x64 build and a separate Development Build, both made from the Editor by script so they can be repeated. Zip the normal build into `builds/`.
- Confirm the debug skip code is absent from the normal build (the capability is compiled out; search the build log or use a build-time assertion).
- Finish `README.md` (spec 14.5).
- **Done when:** both builds launch, the zip exists, and the README has the exact Editor version, launch and build steps, controls, and save and tuning file locations.

### Session 21: Eli's checks and the final report

- Eli runs the real-time timing test (spec 14.4) and the playthrough in the normal build, following `eli-tasks.md` section 6, and gives the agent the observed numbers.
- The agent fixes anything found, rebuilds if needed, and completes `docs/verification-report.md`: tests actually run, which build each ran in, results, and anything not built or not verified with the reason.
- **Done when:** the report is complete and honest, the final zip is rebuilt from the final commit, and everything is pushed.

## If something goes wrong

- **A session runs long or the agent starts repeating itself:** stop it, have it write what is done and not done in `docs/progress.md`, commit, and start a fresh chat on the same session number.
- **A session breaks earlier work:** the full test suite catches it. Each session ends with a commit, so the previous good state is always one `git` step back.
- **MCP stops responding:** restart the Unity Editor, check the MCP server is listed as connected in Cursor's settings, then start a fresh chat.
- **Credits running low:** Phase A is the part that must be right. If credits are tight, finish Phase A and Checkpoint A before starting any interface session.
