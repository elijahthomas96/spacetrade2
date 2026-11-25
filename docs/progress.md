# spacetrade2: progress log

The agent updates this file at the end of every session.

## Current state

- Last completed session: 1 (scaffold)
- Next session: 2 (time and events)
- Unity Editor version: `6000.6.10f1 (40e81493c67d)` (Unity 6.6)

## Session log

| Session | Date | Result | Tests (passed / total) | Notes |
|---|---|---|---|---|
| 1 | 2026-11-24 | Done | EditMode 2/2, PlayMode 1/1 | Root `.gitignore`, four assemblies, Art/Audio/StreamingAssets folders, company and product name `spacetrade2`, `runInBackground` on. Console: 0 errors, 0 warnings after both test runs. |

## Decisions made during the build

- **Folder layout.** `SpaceTrade.Sim` in `game/Assets/Scripts/Sim/`, `SpaceTrade.Game` in `game/Assets/Scripts/Game/`, `SpaceTrade.Sim.Tests` (EditMode) in `game/Assets/Tests/EditMode/`, `SpaceTrade.Game.Tests` (PlayMode) in `game/Assets/Tests/PlayMode/`.
- **Marker classes.** Unity does not build an assembly with no code, so `SimAssemblyMarker` and `GameAssemblyMarker` (empty classes, no logic) exist so both assemblies compile and tests can find them.
- **Guard test.** `ScaffoldTests.SimAssembly_DoesNotReferenceUnityEngine` fails if `SpaceTrade.Sim` ever references a `UnityEngine` or `UnityEditor` assembly. This backs up the `noEngineReferences` setting.
- **Empty folders.** `Art`, `Audio`, and `StreamingAssets` each hold a `.gitkeep` so git keeps them. Unity ignores files starting with a dot.
- **Packages.** Test Framework 1.8.0 and Input System 1.20.1 were already installed by the Universal 2D template; nothing was added. Active input handling was already set to the Input System only.

## Notes for future sessions

- PlayMode tests run through MCP sat at 0 completed for about 90 seconds while the Unity window was unfocused, then finished as soon as the window got focus. If a PlayMode run stalls, click the Unity window.

## Open questions for the owner

- The Universal 2D template left tutorial content in the project: the `game/Assets/Welcome/` folder, `game/Assets/Scenes/SampleScene.unity`, and the tutorial package `com.unity.learn.iet-framework`. They are harmless. Remove them now, or leave them until session 11 replaces the startup scene?
