# spacetrade2: agent rules

Read this file, `docs/spec.md`, `docs/plan.md`, and `docs/progress.md` at the start of every session.

## Project

A single-player Windows space trading simulator. Unity 6.6, C#. The Unity project is in `game/`. You drive the Unity Editor through the Unity MCP server.

- `docs/spec.md`: what the game is. Source of truth.
- `docs/plan.md`: numbered sessions. Do exactly one per chat.
- `docs/progress.md`: what is done, what is next, decisions made. You keep this current.
- `docs/eli-tasks.md`: the owner's list, including the exact filenames for every art and audio file.

## Working rules

1. Do one session from `docs/plan.md` per chat. Show a plan and wait for the owner's OK before changing anything.
2. Prove work with real results: run the tests in the Unity Test Runner, read the console, and report actual counts and output. Never report a result you did not observe.
3. If a check fails, fix it or stop and say so. Never mark a session done with failing tests or console errors.
4. If the spec is unclear, contradictory, or looks wrong, stop and ask. Do not substitute a different design.
5. End every session by updating `docs/progress.md`, committing, and pushing.
6. The owner is not a programmer. Explain decisions and problems in plain language, and define jargon the first time you use it.

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

## Do not

- Do not delete saves, reset the git history, or force-push.
- Do not add packages, services, or paid assets without asking.
- Do not ask the owner to paste passwords, keys, or tokens.
