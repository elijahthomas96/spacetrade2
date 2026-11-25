# spacetrade2: game specification

Version 1.0, 2026-11-24. This file is the source of truth for what the game is. `plan.md` says how and in what order it gets built. Where this spec and the original brief disagree, this spec wins (it includes decisions made after the brief, listed in section 1).

## 1. Summary and decisions

spacetrade2 is a single-player Windows game about running an interstellar transport company. The player starts with one docked ship, picks cargo and passengers, commits the ship to a long real-time transit, gets paid on arrival, and reinvests in ships and drives. As the fleet grows, the job shifts from flying single voyages to designing and supervising recurring routes.

| Topic | Decision |
|---|---|
| Engine | Unity 6.6 (record the exact Editor version string in `README.md`), C# |
| Builder | Cursor agent running Opus 5.5, driving the Unity Editor through the Unity MCP server |
| Project location | `D:\dev\spacetrade2`, Unity project in the `game` subfolder |
| Goal structure | Pure sandbox. No win state, no lose state, no milestone list |
| Visual style | Clean vector look: simple shapes and icons on a dark background. Agent builds with placeholder art; Eli replaces files later |
| Sound | Full sound system built, every sound slot filled with a silent placeholder file |
| Economy values | Every value in one editable tuning file (section 12) |
| First voyage | Every new save guarantees at least one profitable first voyage (section 6.4) |
| Later voyages | Rates vary randomly within tuning ranges. Profit is not guaranteed |
| Getting unstuck | "In-system transport" toggle (section 10). This replaces the emergency contract from the brief |
| Pacing | 1 real hour = 1 game year at 1x. Long transits are intended. Do not shorten them |

## 2. Architecture rules

1. **Simulation assembly.** All game rules live in a plain C# assembly, `SpaceTrade.Sim`, that does not reference `UnityEngine` (set `noEngineReferences` in its assembly definition). It must run in tests with an injected clock and no scene.
2. **Presentation assembly.** `SpaceTrade.Game` holds everything Unity: map, camera, panels, audio, input. It sends commands to the simulation and draws the state it gets back. It never changes simulation state directly.
3. **One code path.** Ordinary play, offline catch-up, and debug skip all call the same `Advance(toTick)` function, which processes the same event queue. No separate "fast" logic.
4. **No floating point in authoritative state.** Use integers: ticks for time, credits for money, kilograms for mass, milli-light-years for distance, milli-c for drive speed. Floats are allowed for drawing only.
5. **Commands are transactions.** Each player or automation command either fully applies or changes nothing, and returns a result with a reason on failure.
6. **Determinism.** The same seed, tuning, and command sequence at the same ticks always produce the same state. Random rolls come from a seeded generator whose state is saved.
7. **Shared quote math.** The voyage preview and the actual dispatch call the same function. The preview is never computed separately.

Other technical defaults (agent may change with a note in `docs/progress.md`): UI Toolkit for panels, the Input System package for input, 2D rendering with sprites and line renderers for the map, Unity Test Framework for tests.

## 3. Time

### 3.1 Units

- 1 tick = 1 real millisecond at 1x speed.
- 1 game day = 10,000 ticks (10 real seconds at 1x).
- 1 game year = 360 game days = 3,600,000 ticks (exactly 1 real hour at 1x).
- Calendar: 12 months of 30 days. Dates display as `Y3 M04 D12`. A new game starts at `Y1 M01 D01` (tick 0), paused, with 1x selected.

### 3.2 Clock controls

- Pause/play, and exactly three speeds: **1x, 3x, 5x**. No others.
- Play resumes at the last selected speed.
- A speed change takes effect for every ship at once. It never changes a voyage's arrival date, manifest, required supplies, or payout.
- The driver adds unscaled real elapsed time, multiplied by the speed once, to a carry value and advances whole ticks. Event timing does not depend on frame rate.
- The game keeps running when the window is not focused (`runInBackground` on).
- If the PC sleeps while the game is open, the driver reconciles on wake using the same rule as offline catch-up (section 11.3).

### 3.3 Transit time

Game years in transit = distance in light-years divided by the drive rating in multiples of c.

`transitTicks = ceil(distanceMilliLy * 3,600,000 / speedMilliC)`

Required cases (these are tests):

| Distance | Drive | Game years | Real time at 1x | At 3x | At 5x |
|---|---|---:|---:|---:|---:|
| 4.5 ly | 1.5c | 3 | 3 hours | 1 hour | 36 minutes |
| 4.5 ly | 3.75c | 1.2 | 72 minutes | 24 minutes | 14 minutes 24 seconds |

### 3.4 What the player sees

- Every voyage shows its in-game arrival date and the real time remaining at the current speed.
- While paused, the real-time countdown freezes and shows "Paused".
- An arrival or a blocked route raises a notification. Other ships keep moving.

## 4. Galaxy

- 8 to 12 named star systems, generated from the save's seed. Positions are in light-years on a 2D map.
- The generated galaxy (names, positions, industries, yards) is stored in the save. It is never regenerated from the seed after creation, so later tuning edits cannot reshape an existing save.
- Nearest-neighbour distances fall in a tuning range (default 2.5 to 7 ly) so early voyages take one to a few game years with the starter drive.
- Each system has 1 to 3 persistent industries, a population size, and optionally a shipyard.

### 4.1 Commodities and industries

Six freight commodities and six industry types. Counts and names are tuning data; these are the defaults.

| Industry | Consumes | Produces |
|---|---|---|
| Mining colony | Food, Machinery | Ore |
| Refinery | Ore | Metals, Chemicals |
| Factory | Metals, Chemicals | Machinery, Consumer goods |
| Agricultural system | Machinery, Chemicals | Food |
| Population hub | Food, Consumer goods | Passengers (large volume) |
| Shipyard works | Metals, Machinery | (enables a shipyard) |

Generation must guarantee:

- At least one closed three-stop freight circuit exists (for example ore to a refinery, machinery to a farm system, food back to the colony).
- Every system can be reached and has at least one export and one import. A system is reachable if a chain of hops, each no longer than `maxHopMilliLy` (tuning, default 7 ly), connects it to the start system.
- At least two systems have shipyards, and the start system is within `maxHopMilliLy` of one.
- The map and system panels show enough (exports, imports, rates, distances) for the player to find circuits without outside notes.

## 5. Markets

### 5.1 Freight

- A **freight offer** is: origin system, commodity, destination system, tonnes available, contracted rate per tonne. Offers come from a producing industry paired with a consuming industry elsewhere.
- **Supply** at the origin is finite and replenishes over game time up to a cap.
- **Demand** at the destination is finite and replenishes over game time up to a cap.
- When a galaxy's markets are created, supply and demand start at a tuning percent of their caps (`start.marketStartFill`, 0 to 100, default 100).
- The amount a ship can load is limited by both supply and demand.
- The rate per tonne is rolled within a range around `base rate x distance factor`, and re-rolled on a tuning interval (default every 90 game days) using the seeded generator. The save stores the rolled percent for each open offer; the published rate is worked out from it and the current tuning. Published rates are what the player sees. At dispatch the rate is locked as a fixed credit amount per tonne (section 6.6), not as a percent, so later tuning edits never change the payout of a voyage in flight.

### 5.2 Passengers

- A **passenger booking pool** is: origin, destination, passengers waiting, fare per passenger. Pools scale with the populations at both ends and replenish over time. Fares roll the same way freight rates do: the save stores the rolled percent, and at dispatch the fare is locked as a fixed credit amount per passenger. Pools start at a tuning percent of their size (`start.marketStartFill`, default 100).

### 5.3 Reservations

- When a dispatch is confirmed, the game reserves the freight at the origin **and** the matching delivery demand at the destination, and books the passenger seats. All of it happens in one transaction.
- A failed dispatch consumes nothing: no supply, no demand, no seats, no credits, no provisions.
- Reservations can never exceed what exists. If two ships compete for 80 tonnes, their combined reservations are at most 80 tonnes. The second ship gets the remainder or fails with a clear reason.
- Customers' freight is separate from supplies bought for the ship. Freight is never owned by the player and is never sold; the player is paid a rate for delivering it.

### 5.4 Provisions for sale

Every port sells fuel, food, and water. Prices vary by system within tuning ranges (a refinery system sells cheap fuel, a farm system sells cheap food). Provision prices are shown in the system view.

- Each system's price roll for fuel, food, and water is made once, when the markets are created, and never re-rolled.
- The save stores the rolled percent, not the price. The price shown and charged is worked out from the current tuning: base price x combined industry modifier x stored roll. Edits to the tuning file therefore reach existing saves (section 12).
- The combined industry modifier is every modifier for the system's industries multiplied together, then clamped to a tuning floor and ceiling (default 50 and 200 percent).

### 5.5 Rounding

There is one rounding rule. Wherever a figure is rounded to the nearest whole number, it is calculated exactly from whole numbers and rounded once, at the end, with exact halves rounding **away from zero** (2.5 becomes 3, -2.5 becomes -3). This covers market prices, rates, fares, pool sizes, starting amounts, the cost of buying supplies, the share of a supply's cost that a voyage uses, and profit per game year (which can be negative).

Three amounts are rounded up instead, on purpose, because coming out short would be wrong: transit ticks (section 3.3), the real time remaining shown to the player (so a countdown never reaches zero early), and the supplies a voyage needs (section 6.3). These are round-ups, not roundings to the nearest number.

## 6. Ships and voyages

### 6.1 Ship classes

Three classes minimum. Stats are tuning data; the roles are fixed.

| Class | Role | Shape of the stats |
|---|---|---|
| Courier | Starter ship. Small mixed loads, cheap to run | Small hold, few berths, small tank, low price |
| Bulk freighter | Freight circuits | Large hold, almost no berths, large tank, high fuel burn |
| Passenger liner | Passenger services | Many berths, small hold (mostly for food and water), mid price |

Each ship has: name, class, dry mass, hold capacity (tonnes), fuel tank (tonnes), passenger berths, crew count, installed drive, and on-board provisions (fuel, food, water) each tracked with quantity and total acquisition cost.

### 6.2 Drives

Every ship uses an Alcubierre drive with a rated speed. Default tiers: **1.5c, 2.25c, 3.75c**. Each tier has a price and a fuel-burn factor. The starter ship has the 1.5c drive.

### 6.3 Provisioning math

- People aboard = crew + passengers.
- Food required (kg) = people x voyage days x food kg per person per day. Water is the same with its own rate.
- Fuel required (kg) = distance (ly) x loaded mass (tonnes) x fuel kg per tonne-ly for the drive tier.
- Loaded mass = dry mass + freight + food + water + people mass. Fuel's own mass is left out of loaded mass on purpose, to avoid a circular calculation.
- **Hold:** freight + food + water must fit in the hold together. The supplies' own mass counts.
- **Tank:** fuel must fit in the tank.
- **Berths:** passengers must not exceed berths.
- A voyage is feasible only if all three fit and the player can pay for any top-up.
- Supplies needed are rounded up to the whole kilogram, so a ship is never a few grams short.
- Food and water in the hold and in loaded mass are what will be aboard after topping up: the amount the voyage needs, or more if leftovers from earlier voyages already exceed it. A ship cannot leave leftovers behind.
- Supply prices are per tonne and supplies are bought by the kilogram: cost = kilograms x price per tonne / 1,000, rounded once (section 5.5).

### 6.4 New-game start

- The player starts with one courier, docked at the start system with empty fuel, food, and water (the first step is Provision), and starting credits from the tuning file. The clock opens paused at 1x on `Y1 M01 D01`.
- **Starter ship name.** The save's seed picks the name from the `shipNames` list in the tuning file. The name is stored on the ship when it is created, so later edits to the list never rename an existing ship. An empty or missing list, a blank name, or a repeated name is a tuning error naming the field, with fallback to the built-in defaults (section 12).
- The start port must offer at least two jobs that are both feasible and affordable with starting credits, with different tradeoffs (for example a short low-pay freight run versus a longer passenger run).
- **A job**, for this guarantee, is one freight offer or one passenger booking from the start port, loaded with as much as the starter ship can feasibly take (the most tonnes, or the most passengers, for which the quote shows no problem). **Feasible and affordable** means the real quote for it shows no problem, with the starting credits.
- **Different tradeoffs** means two of those jobs go to different destinations, one is faster (strictly fewer transit ticks) and the other earns more (strictly more net profit on its quote), so neither is better on both time and money.
- **At least one of them must show a positive net profit** when run through the real quote function. The generator checks this with the actual quote math and adjusts the opening offers (never the distances or travel rules) until it holds.
- **How the opening offers are adjusted.** Only the stored roll percents of the start port's freight offers and passenger pools change. If the guarantee does not hold, every one of those rolls is raised by 1 percent, never above its tuning maximum, and the check runs again, until it holds.
- **If it cannot hold.** If the guarantee still fails with every start-port roll at its maximum (or the galaxy cannot be generated), tuning the owner edited is set aside for that new game with a message naming the fields to look at, and the new game is built from the built-in defaults, as for a tuning error. A new game never fails on the built-in defaults: if they cannot meet the guarantee on a seed, the next seed is tried, and the seed actually used is the game's seed, stored in the game state.
- **The opening rates are held until the first dispatch.** From new-game creation until the first dispatch, the start port's freight rates and passenger fares are not re-rolled, so the guarantee still holds if the player waits past one or more re-roll intervals before launching. Every other system re-rolls as normal. After the first dispatch the start port rejoins the normal re-roll schedule at its next re-roll.
- The player can inspect destinations and their likely return loads before committing.
- After the first voyage leaves, nothing is guaranteed. Some jobs will lose money, and the quote says so.

### 6.5 Planning and the quote

The player picks a destination, chooses freight and passengers, and reviews a quote before launch. The quote shows:

- What is aboard, what the voyage requires, the cost to top up, and any shortfall, for fuel, food, and water.
- A one-click **Provision** button that buys exactly the shortfall.
- Gross revenue (freight plus fares), expected operating costs (supplies the voyage will consume, at their acquisition cost), net profit, and profit per game year.
- The in-game arrival date and real time at the current speed.

Failures are explained with numbers and a fix, for example "Unload 4 tonnes to make room for drinking water" or "Fuel tank holds 40 t, this voyage needs 46 t". For the hold and the tank the fix is the smallest one: the fewest tonnes of freight to unload, or, if no amount of freight is enough, the fewest passengers to drop and then the fewest tonnes to unload with that many passengers.

The Provision button is refused, changing nothing, unless the voyage is otherwise feasible.

### 6.6 Dispatch

Dispatch is one transaction: validate, reserve freight and demand, book seats, buy any approved top-up, lock the manifest, bookings, drive, destination, and arrival tick, then depart. If any step fails, nothing changes.

The locked terms hold each freight rate (credits per tonne) and the passenger fare (credits per passenger) as fixed credit amounts taken at dispatch, never as a rolled percent. Editing the tuning file never changes the payout of a voyage already in flight.

- **Top-up approval.** Dispatch carries a yes/no "buy the top-up" choice. Yes buys exactly the quoted shortfall inside the same transaction, so the result is exactly the same as pressing Provision and then dispatching. No, with any supply short, refuses the dispatch with the shortfall and its cost and changes nothing. An automated route stop's automatic provisioning setting (section 8) is this same yes/no.
- **Ships in transit.** A ship in transit cannot be quoted, provisioned, or dispatched. Each is refused with the destination and arrival date and changes nothing.
- **Ships on in-system transport.** A ship with the in-system toggle on can still be quoted and provisioned, but dispatch is refused, changing nothing, with a reason telling the player to turn off in-system transport first (section 10).
- **Empty voyages.** A voyage with no freight and no passengers may be dispatched by hand (for example, to reposition a ship). It reserves nothing, is paid nothing, uses its supplies like any voyage, and its quote shows the loss.

### 6.7 Arrival and the receipt

At the arrival tick the ship docks, unloads the contracted freight and passengers, and is paid exactly once at the locked rates. The receipt shows fares, freight revenue, supplies consumed at acquisition cost, elapsed game time, and net profit.

Each expense is counted once. Buying supplies moves credits into on-board inventory at cost; the expense appears on the receipt of the voyage that consumes them, even if they were bought at an earlier stop. Leftover supplies stay aboard with their cost.

Each supply aboard has a quantity and a total cost, so it has one average price. A voyage is charged its share: total cost x kilograms used / kilograms aboard, rounded once. When a voyage uses everything that is left, it is charged everything that is left, so a supply with zero quantity always has zero cost. Credits spent on a supply therefore always equal the amount expensed on receipts plus the cost still aboard.

**When supplies are used.** A voyage's supplies are used up and expensed once, at arrival: the kilograms of fuel, food, and water fixed at dispatch are taken out of the ship's stock at that moment, at their acquisition cost, and that is the only place supplies are used up. (Buying, through Provision or the dispatch top-up, still adds kilograms and cost.) Nothing aboard changes during the flight.

**Remaining supplies in flight.** The supplies a ship in flight shows as remaining are a read-only figure, worked out from the locked voyage terms and the current tick, in whole kilograms. It is never stored, never saved, and changes nothing. For each supply: used so far = the voyage's locked use x ticks elapsed / transit ticks, rounded once by the one rounding rule (section 5.5); remaining = what is aboard - used so far. At the dispatch tick it equals what is aboard; at the arrival tick it equals exactly what settlement leaves aboard. Because the figure uses only locked terms, tuning edits during the flight do not change it.

**Receipts are kept.** The simulation keeps every delivery receipt, in the order the ships arrived, and the save holds them all (section 11.1).

A manually dispatched ship stays docked after arrival until ordered out again.

## 7. Shipyards and growth

- Shipyards exist at selected systems. Each has its own stock of ships and drive upgrades with local prices. Stock is finite and restocks slowly.
- A purchased ship is delivered docked at the yard that sold it.
- A drive upgrade requires that specific ship to be docked at a yard that stocks that drive. The player can open any ship from anywhere on the map, but an upgrade order for a ship that is not docked at a stocking yard is rejected with the reason. This check lives in the simulation, not only the interface.
- Before buying a drive, the player can pick a known route and see the cost and the time saved on it.

## 8. Automated routes

- A route is an ordered loop of two or more stops assigned to a ship.
- Each stop has: which freight is eligible, whether to carry passengers, automatic provisioning on or off (the dispatch top-up yes/no in section 6.6), and a minimum departure load in the relevant unit (tonnes for freight, passengers for berths).
- A leg can be marked as an explicitly empty repositioning leg.
- The route editor estimates revenue, costs, total transit time, and profit per game year for the **whole circuit**, including empty legs. Any figure that depends on future bookings or waiting time is labelled as an estimate.
- Automated departures use exactly the same dispatch rules and code as manual ones.
- A ship waiting for its minimum load is rechecked whenever supply or bookings replenish. It shows the unmet condition, for example "Waiting for 12 more tonnes of ore".
- If the next leg is infeasible (cannot afford provisions, load can never fit, destination removed from route), the ship stays docked, is flagged as blocked with the reason, raises a notification, and the player can change its orders.

### 8.1 Deterministic ordering

Events at the same tick are processed in this order: (1) arrivals, by ship id; (2) market replenishment and rate re-rolls, by system id; (3) in-system transport income, by ship id; (4) automated departure checks, by ship id. Competing reservations are resolved in that order, so the lower ship id wins a tie.

## 9. Interface

### 9.1 Galaxy map (opening screen)

- The game opens on the galaxy map with the starter ship selected.
- Pan, zoom, select a system, select a moving ship, follow a selected ship as it travels.
- Active routes are drawn, and ships appear at their actual positions along each leg.
- Zooming in on a selected ship moves into a close-up of that vessel that keeps its identity and motion, and shows: manifest, crew and passengers, remaining supplies (the read-only in-flight figure in section 6.7, in whole kilograms), installed drive, destination, progress, ETA.
- Map inspection and route planning stay available while ships travel and while the clock is paused.

### 9.2 System view

Industries, local jobs with rates, provision prices, docked ships, and shipyard stock if present.

### 9.3 Fleet list

Every ship with a status the player can tell apart at a glance: in transit, waiting for a load, idle, in-system transport, blocked by a problem. Clicking a ship selects it and centres the map on it.

### 9.4 Always-visible bar

Date, pause/play, speed buttons (1x, 3x, 5x), credits, notification list. Notifications are clickable and jump to the ship or system.

### 9.5 Controls (defaults)

| Action | Control |
|---|---|
| Pan | Hold right or middle mouse and drag, or WASD / arrow keys |
| Zoom | Mouse wheel |
| Select | Left click |
| Follow selected ship | F |
| Pause / play | Space |
| Speed 1x / 3x / 5x | 1 / 2 / 3 |
| Fleet list | Tab |
| Close panel / menu | Esc |

### 9.6 Art and audio hooks

- Every sprite and icon is loaded from a file with a fixed name under `game/Assets/Art/`. The agent creates a simple placeholder for each. Replacing the file with the same name swaps the art with no code change. The full list is in `eli-tasks.md`.
- Icons are white on transparent so the code can tint them.
- Every sound event in `eli-tasks.md` is wired to a named audio file under `game/Assets/Audio/`, each a silent placeholder. The settings menu has master, effects, and music volume sliders and a mute toggle.

## 10. In-system transport

This is how a broke player recovers. It replaces the brief's emergency contract.

- A toggle on any docked ship. While on, the ship does local hauling inside its current system and earns a small, steady income, credited once per game day.
- **When it pays.** The daily rate is paid after each full game day (10,000 ticks) since the toggle went on. A partial day pays nothing, and switching off then on restarts the count. A day that completes at the same tick the toggle goes off is paid.
- A ship in transit cannot be switched on; the request is refused with its destination and arrival date and changes nothing. Asking for the state a ship is already in (on when on, off when off) succeeds and changes nothing; in particular, it does not restart the count.
- The ship cannot be dispatched while the toggle is on (section 6.6); it can still be quoted and provisioned. Turning it off is instant and has no cost.
- The daily rate is per ship class, in the tuning file, and must be at least 1 credit for every class (a lower value is a tuning error naming the field), so recovery is always possible. It is set low: default target is that a full game year of in-system transport earns about 10 to 15 percent of what the same ship earns from typical voyages in that time.
- It uses no provisions and needs no cargo.
- It runs through the normal event queue, so it earns nothing while paused and earns correctly during offline catch-up.
- A ship on an automated route can only be toggled after the route is paused or cleared.
- There is no other rescue mechanic. Because this income is always available to a docked ship, the player can always earn enough to provision the cheapest feasible job.

## 11. Saves and offline time

### 11.1 Contents

Versioned saves under Unity's persistent data path (`%USERPROFILE%\AppData\LocalLow\spacetrade2\spacetrade2\saves\`). A save holds: save format version, galaxy seed and the generated galaxy, random generator state, clock tick, selected speed, pause state, market supply, demand, bookings, rates and reservations, credits, ships with provisions and cost, in-flight voyages with locked terms, every delivery receipt, route orders and waiting or blocked state, in-system toggles with the tick each went on, whether the start port's opening rates are still held (section 6.4), notifications, and the real-world UTC timestamp of the save.

### 11.2 Writing

- Saves are written atomically: write a temporary file, flush it, then swap it into place, keeping the previous file as a backup.
- The game autosaves on dispatch, on arrival, every 60 real seconds, and on quit. The player can also save to named slots.
- An older save version is migrated on load or refused with a clear message. It is never loaded half-way.

### 11.3 Offline catch-up

- The simulation continues while the game is closed. On opening a save that was running, elapsed game time = real time since the save timestamp x the saved speed, and the simulation advances by that amount through the normal event queue.
- A paused save stays paused and advances nothing.
- Deliveries, replenishment, in-system income, automated departures, and later arrivals are processed in chronological order.
- The player sees a short return summary: deliveries, earnings, ships needing attention.
- The catch-up result and the new timestamp are saved together in one atomic write before the player gets control, so loading twice cannot pay twice.
- If the system clock has moved backward, elapsed time is treated as zero and the timestamp is reset. Game time never runs backward.

### 11.4 Fresh and demo saves

- **New game** creates a fresh save with normal pacing.
- **Load demo** copies a bundled demonstration save with several ships, active routes, and ships mid-transit. On first load its timestamp is set to now, so it opens in motion without a giant catch-up. The demo never changes the pacing or values of a fresh game.

## 12. Tuning file

One JSON file (a plain text data format), at `game/Assets/StreamingAssets/tuning.json`. In a built game it sits at `spacetrade2_Data\StreamingAssets\tuning.json` and can be edited with any text editor. Sections:

- **start:** starting credits, starter ship class and drive, opening-offer rules, market starting fill (percent of cap, 0 to 100, separately for freight supply, destination demand, and passenger pools; default 100).
- **galaxy:** system count range, distance ranges, maximum hop distance `maxHopMilliLy` (default 7,000 milli-ly; must be at least the nearest-neighbour maximum), industries per system, shipyard count, name list.
- **commodities:** per commodity base rate per tonne, rate roll range, supply and demand caps, replenish amounts and intervals.
- **passengers:** base fare, fare roll range, pool sizes, replenish rates.
- **rates:** distance factor, re-roll interval.
- **provisions:** base prices for fuel, food, water, and per-industry price modifiers; floor and ceiling for the combined modifier (default 50 and 200 percent); food and water per person per day; people mass.
- **ships:** per class dry mass, hold, tank, berths, crew, price, in-system daily income (at least 1).
- **shipNames:** the list new ships are named from (section 6.4). At least one name, no blank names, no repeats.
- **drives:** per tier speed, price, fuel kg per tonne-ly.
- **shipyards:** stock sizes, restock interval, local price variation.
- **automation:** recheck behaviour limits.

Rules:

- The file is read when the game starts. Changes apply to existing saves for anything not already locked (in-flight voyages keep their locked terms; the generated galaxy does not change).
- The file is validated on load. A bad value produces a message naming the field, and the game falls back to built-in defaults rather than crashing.
- Validation includes a starter-range check: the starter ship class with the starter drive, a full fuel tank, crew only, and the food and water that crew needs, must be able to fly `maxHopMilliLy`. Failure gives a field-named error like any other.
- A companion `docs/tuning-guide.md` explains each field in plain language with its unit.
- Automated tests use a fixed test copy of the tuning data, so editing the live file never breaks the test suite. One separate test checks that the live file loads and still satisfies the new-game guarantees in section 6.4.

Balance targets for the default values (the agent tunes toward these and reports results):

1. The guaranteed first voyage nets a clear profit.
2. A second ship (courier) is affordable after roughly 4 to 8 reasonable voyages.
3. A drive upgrade pays for itself on a good circuit within roughly 5 to 10 circuits.
4. In-system transport sits in the 10 to 15 percent band described in section 10.
5. About one in four published jobs is a money-loser for a courier, so reading the quote matters.

## 13. Debug skip

- "Skip to next arrival" exists only in the Unity Editor and in Development Builds, and only when debug mode is explicitly switched on.
- It is enforced in two places: the simulation command handler rejects the command unless debug capability was granted when the simulation was created, and the interface does not create the button otherwise. In a normal build the capability is compiled out and cannot be granted.
- It jumps to the earliest pending arrival tick, processing every event in between and every arrival at that tick, through the normal `Advance` path.
- The button is disabled when nothing is in flight.
- The normal Windows build has no player-triggered time jump of any kind.

## 14. Acceptance

### 14.1 Playthrough (in the Editor through MCP, then repeated by Eli in the build)

Start a fresh game through the normal interface. Quote, provision, launch, follow the ship, and read its delivery receipt. Confirm two ships travel independently, a yard installs a drive on a ship docked there, and an upgrade for a ship elsewhere is rejected.

### 14.2 Deterministic tests

Drive math (the table in 3.3); capacity and provisioning; competing claims on 80 tonnes; failed dispatch rollback; exactly-once payouts; sustained automated circuits over many game years; minimum-load waiting and recheck; empty return leg costs; the new-game guarantee across many seeds; and in-system transport recovery (a docked ship with zero credits earns enough to provision the cheapest feasible job, cannot dispatch while toggled, earns nothing while paused, and repeated toggling cannot create extra money).

### 14.3 Save tests

Running and paused saves, including one made mid-transit. Run the same elapsed game time three ways (ordinary simulation, offline catch-up, debug stepping) and compare balances, fleet positions, port stock, and route state: they must match exactly. Reopen a settled save and confirm no second payment. Backward clock change.

### 14.4 Real-time timing test (Eli, in the normal Windows build)

Launch a 4.5 ly trip at 1.5c. Run six uninterrupted real minutes at 1x: expect about 3.33 percent complete (0.15 ly). Pause one real minute: no progress. Resume at 5x: the remaining real ETA drops to one fifth of the 1x value and the arrival date is unchanged. Also check 3x, and confirm there is no debug skip in that build. Record observed values, elapsed times, and tolerance (default tolerance: plus or minus 2 real seconds).

To make this test possible on a fresh save, the bundled test save `timing-test` places a 1.5c ship at a port with a job exactly 4.5 ly away. It is a normal save with normal rules.

### 14.5 Delivery

- A zipped standalone Windows x64 build: the executable and all companion files.
- The editable Unity project: assets, metadata, packages, project settings, startup scene.
- `README.md`: exact Editor version, how to launch, how to build, controls, save locations, tuning file location.
- `docs/verification-report.md`: every test actually run, which build it ran in, results, and anything that could not be built or verified and why.

## 15. Out of scope

Multiplayer, combat, crew wages or morale, ship damage or maintenance, story or characters, controller support, non-Windows builds, localization, and Steam or store integration.
