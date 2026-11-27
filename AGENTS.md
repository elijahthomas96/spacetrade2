# spacetrade2: agent rules

Read this file, `docs/spec.md`, `docs/plan.md`, and `docs/progress.md` at the start of every session.

## Project

A single-player Windows space trading simulator. Unity 6.6, C#. The Unity project is in `game/`. You drive the Unity Editor through the Unity MCP server.

- `docs/spec.md`: what the game is. Source of truth.
- `docs/plan.md`: numbered sessions. Do exactly one per chat.
- `docs/progress.md`: what is done, what is next, decisions made. You keep this current.
- `docs/eli-tasks.md`: the owner's list, including the exact filenames for every art and audio file.

## Start of every session

Before any work:

1. Confirm the git tree is clean and `main` matches `origin/main`.
2. Report Unity's memory use.
3. Run both full test suites (EditMode and PlayMode) twice each, with the console cleared first and every message type read.
4. Compare the counts with those recorded in `docs/progress.md` for the last session. If anything differs, stop and tell the owner.

## Working rules

1. Do one session from `docs/plan.md` per chat. Show a plan and wait for the owner's OK before changing anything.
2. Prove work with real results: run the tests in the Unity Test Runner, read the console, and report actual counts and output. Never report a result you did not observe.
3. If a check fails, fix it or stop and say so. Never mark a session done with failing tests or console errors.
4. If the spec is unclear, contradictory, or looks wrong, stop and ask. Do not substitute a different design.
5. End every session by updating `docs/progress.md`, committing, and pushing.
6. The owner is not a programmer. Explain decisions and problems in plain language, and define jargon the first time you use it.
7. Questions for the owner come with options and a recommendation.
8. No test is weakened, skipped, or deleted to make it pass.
9. Any frame over 250 ms stops the session, whatever the cause. Stop and report it to the owner. Ordinary frames stay under 50 ms.
10. The owner's art, audio, and art-source files (`game/Assets/Art`, `game/Assets/Audio`, `art-source/`) are never overwritten, renamed, moved, or deleted. Every final report confirms they are unchanged.

## Final report

Every final report gives:

- Real test counts from both double runs (EditMode twice, PlayMode twice).
- Console output.
- Screenshots for screen work.
- Unity's memory use at the start and at the end.
- Confirmation that the owner's art, audio, and art-source files are unchanged.
- Anything the owner must decide before the next session.

## Hard technical rules

- `SpaceTrade.Sim` never references `UnityEngine`. All game rules live there.
- No floating point in simulation state. Integers only: ticks, credits, kilograms, milli-light-years, milli-c.
- Ordinary play, offline catch-up, and debug skip all go through the same `Advance(toTick)` path.
- The voyage preview and the real dispatch call the same quote function.
- Commands are all-or-nothing transactions.
- Never shorten transit times, add speeds beyond 1x, 3x, 5x, or add any time jump to the normal build.
- Every economy value comes from the tuning data. No economy numbers hard-coded in logic.
- Art and audio load from the fixed filenames in `docs/eli-tasks.md`. Create a placeholder for each. Never rename or delete a file the owner has replaced.
- Tests use the fixed test tuning fixture, not the live `tuning.json`.
- Every colour, font, text size, spacing value, glow size, label rule, display limit, and camera or control feel value comes from the theme file, `game/Assets/StreamingAssets/theme.json`, read through `Theme`. No screen hard-codes these values, and `ThemeGuardTests` must pass. Every field is explained in `docs/theme-guide.md`. Tests use the fixed test theme fixture, not the live `theme.json`.
- Only one side panel is open at a time, in two tiers. Work panels (system view, ship panel, voyage planner, receipt): only one shows at a time; opening another replaces it, and closing it brings back the one it replaced. Utility panels (notification list, settings): only one is open at a time, and it sits over the work panel; they replace each other and never bring each other back, and closing one returns to the work panel underneath. Esc closes a utility panel first if one is open, then the top work panel, then the close-up, then clears the selection.
- PlayMode tests, screenshots, and frame-time measurements always run with the Game view at 1920 x 1080. The shared PlayMode test setup (`TestGameView`) sets this itself and restores the previous size afterwards; for screenshots and live measurements, set it before entering Play.
- Frame-time measurements are run hands-off: start a case, send nothing to Unity until it has finished, then read the report. A status check during a measurement stalls the Editor and spoils the frame times. Confirm the Unity window is focused before and after each case.
- The project enters Play mode without reloading code. Anything in SpaceTrade.Game kept in static fields or caches must be reset at the start of every Play session. PlayMode results count only from the full suite run twice in a row, with the console cleared first and every message type read.

## Do not

- Do not delete saves, reset the git history, or force-push.
- Do not add packages, services, or paid assets without asking.
- Do not ask the owner to paste passwords, keys, or tokens.
