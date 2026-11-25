# spacetrade2: progress log

The agent updates this file at the end of every session.

## Current state

- Last completed session: 2 (time and events)
- Next session: 3 (tuning data and galaxy)
- Unity Editor version: `6000.6.10f1 (40e81493c67d)` (Unity 6.6)

## Session log

| Session | Date | Result | Tests (passed / total) | Notes |
|---|---|---|---|---|
| 1 | 2026-11-24 | Done | EditMode 2/2, PlayMode 1/1 | Root `.gitignore`, four assemblies, Art/Audio/StreamingAssets folders, company and product name `spacetrade2`, `runInBackground` on. Console: 0 errors, 0 warnings after both test runs. |
| 2 | 2026-11-24 | Done | EditMode 37/37, PlayMode 1/1 | Tick units and calendar, clock with pause and 1x/3x/5x, event queue with spec 8.1 ordering, `Simulation.Advance(toTick)`, real-time driver with injected time source, transit time. Tests cover the spec 3.3 timing table, speed changes not moving arrival ticks, pause freezing time, and 1 chunk vs 10,000 chunks at all three speeds. Tutorial leftovers removed. Console: 0 errors, 0 warnings after both test runs. |

## Decisions made during the build

- **Folder layout.** `SpaceTrade.Sim` in `game/Assets/Scripts/Sim/`, `SpaceTrade.Game` in `game/Assets/Scripts/Game/`, `SpaceTrade.Sim.Tests` (EditMode) in `game/Assets/Tests/EditMode/`, `SpaceTrade.Game.Tests` (PlayMode) in `game/Assets/Tests/PlayMode/`.
- **Marker classes.** Unity does not build an assembly with no code, so `SimAssemblyMarker` and `GameAssemblyMarker` (empty classes, no logic) exist so both assemblies compile and tests can find them.
- **Guard tests.** `ScaffoldTests.SimAssembly_DoesNotReferenceUnityEngine` fails if `SpaceTrade.Sim` ever references a `UnityEngine` or `UnityEditor` assembly. `IntegerStateGuardTests.SimAssembly_StoresNoFloatingPoint` fails if any class in `SpaceTrade.Sim` has a `float`, `double`, or `decimal` field.
- **Empty folders.** `Art`, `Audio`, and `StreamingAssets` each hold a `.gitkeep` so git keeps them. Unity ignores files starting with a dot.
- **Packages.** Test Framework 1.8.0 and Input System 1.20.1 were already installed by the Universal 2D template; nothing was added.
- **Tutorial leftovers removed (session 2, owner's decision).** Deleted `Assets/Welcome/`, `Assets/Scenes/` (with `SampleScene`), the package `com.unity.learn.iet-framework`, and its settings folder. The build scene list is empty until session 11 adds the startup scene. The URP render settings and scene template in `Assets/Settings/` are kept.
- **Time code layout (session 2).** Namespace `SpaceTrade.Sim.Timing` (named "Timing", not "Time", so it never clashes with Unity's `Time` class): `GameTime` (unit constants, real-time-at-speed helper), `GameDate` (calendar), `ClockSpeed` (only `X1`, `X3`, `X5`), `SimClock` (tick, speed, paused), `TransitMath`, `IRealTimeSource`, `RealTimeDriver`. Namespace `SpaceTrade.Sim.Events`: `EventPhase`, `SimEvent`, `EventQueue`. `Simulation` and `CommandResult` sit in `SpaceTrade.Sim`.
- **Clock commands.** `Pause`, `Play`, and `SetSpeed` are commands on `Simulation` returning a `CommandResult` (success, or failure with a reason). Setting any speed other than 1x, 3x, or 5x is refused and changes nothing. Setting a speed while paused selects it but stays paused; Play resumes at the selected speed. Pausing when already paused (or playing when already playing) succeeds and changes nothing. A new `Simulation` defaults to tick 0, 1x, not paused; session 7 decides the new-game start state.
- **Event ordering.** Same tick: phase (arrival, market replenishment, in-system transport, automated departure), then entity id (ship id, or system id for market events), then the order the events were scheduled. Events scheduled while `Advance` is running are processed in the same call if they fall in range. `Advance` refuses to go backward, an event may not call `Advance`, and nothing may be scheduled in the past.
- **Real-time driver.** Reads a monotonic clock in whole microseconds. Elapsed real microseconds x speed go into a carry; every whole 1,000 becomes one tick passed to `Advance`, the remainder is kept. Real time that passes while paused is discarded. If the time source ever reads earlier than before, nothing advances. The driver's `Pause`, `Play`, and `SetSpeed` first count the time already elapsed at the old setting, then apply the command; the game should send clock commands through the driver. The driver's carry (less than one tick) is not part of saved state.
- **Rounding.** Transit ticks and real-time-remaining both round up, so a countdown never shows zero before the arrival.

## Notes for future sessions

- PlayMode tests run through MCP sat at 0 completed for about 90 seconds while the Unity window was unfocused, then finished as soon as the window got focus. If a PlayMode run stalls, click the Unity window. (In session 2 both runs finished without focus in about 5 seconds.)
- MCP's `execute_code` blocks `AssetDatabase.DeleteAsset`; use `manage_asset` with `action: delete` instead.
- After a script recompile the MCP bridge logs one warning, "[WebSocket] Unexpected receive error: WebSocket is not initialised", while Unity reloads. It comes from the MCP plugin reconnecting, not from project code. Clear the console after compiling and before the test runs that count.
- Session 10 (sleep reconcile): the driver already advances through all real time elapsed since its last reading via `Advance`, so a long sleep currently plays out as one large step. Session 10 should decide whether waking from sleep goes through the offline catch-up path explicitly (return summary, atomic save) as spec 3.2 says.
- Session 11: the Unity component that feeds the driver should use a monotonic clock in microseconds (for example `System.Diagnostics.Stopwatch`), not `Time.unscaledDeltaTime` floats.

## Open questions for the owner

- None.
