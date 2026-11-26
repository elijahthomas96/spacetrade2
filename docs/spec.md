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
- If the PC sleeps while the game is open, waking goes through the offline catch-up path (section 11.3): the same elapsed-time rule, one atomic save, and a return summary. **How sleep is detected:** every frame the game reads the real-world (UTC) clock; a gap of **30 real seconds or more** since the previous frame counts as a wake. The whole gap is then counted once by the catch-up rule from the UTC clock, the frame-timing clock is reset so the gap is never counted twice, and the result is saved at once. A gap under 30 seconds is an ordinary frame. The summary panel is shown only when the time away is **5 real minutes or more**, the same threshold as opening a closed game. Both numbers are fixed in code, not tuning. Sleep catch-up and closed-game catch-up over the same gap end in byte-identical state.
- If the real-world clock jumps backward while the game is running, that frame is ordinary: time keeps flowing from the frame-timing clock, which never runs backward.

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
- **Map size target (owner's decision, session 11).** A new game is to default to **25 systems**, and galaxies of **up to 50 systems** are supported and tested. 100 systems is not a target for now. The default stays at 8 to 12 until the owner has written at least 50 star system names (`galaxy.names`); the change itself is a tuning edit, checked by the balance work in session 18. The map (section 9.1) and its tests already cover 12 and 50 systems.
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

**Receipts and delivery totals.** The simulation keeps the newest `receipts.keepNewest` delivery receipts in full (tuning, default 500, at least 1), in the order the ships arrived, and running totals of every delivery since the game began: deliveries, freight tonnes, passengers, freight revenue, fare revenue, and the kilograms and cost of the fuel, food, and water used. Older receipts are dropped as new ones arrive, and when a save loads with a lower `keepNewest`, but they stay counted in the totals, so everything older than the newest N is exactly the totals minus the receipts still kept. The save holds both (section 11.1).

**Supplies bought.** The simulation keeps a running total of every credit spent buying supplies, through Provision or a dispatch top-up, so every credit spent on supplies can be accounted for. It is one total rather than a figure on each voyage, so that dispatching with the top-up still ends in exactly the same state as pressing Provision and then dispatching (section 6.6).

**Arrival notification.** Each arrival raises a notification naming the ship, the destination, and the voyage's net profit (section 3.4).

A manually dispatched ship stays docked after arrival until ordered out again.

## 7. Shipyards and growth

- Shipyards exist at selected systems. Each has its own stock of ships and drive upgrades with local prices. Stock is finite and restocks slowly.
- A purchased ship is delivered docked at the yard that sold it.
- A drive upgrade requires that specific ship to be docked at a yard that stocks that drive. The player can open any ship from anywhere on the map, but an upgrade order for a ship that is not docked at a stocking yard is rejected with the reason. This check lives in the simulation, not only the interface.
- Before buying a drive, the player can pick a known route and see the cost and the time saved on it.

### 7.1 Stock

- A yard's stock is created with the markets, when the game is created. Every yard has an entry for every ship class and every drive in the tuning.
- **Stock limits.** For each ship class and drive at each yard, a roll is made once, when the yard is created, and stored as a whole percent from 0 to 100. The limit is worked out from that roll and the current tuning: minimum + (maximum - minimum) x roll, rounded by the one rounding rule (section 5.5), using `shipStockPerClassMin`/`Max` for ships and `driveStockPerTierMin`/`Max` for drives. Edits to the minimum and maximum therefore reach existing saves.
- **Which yards stock what.** A yard stocks an item when its limit is at least 1, or when it still has units of it left. "Out of stock" means it stocks the item but has none left right now. With the default minimum of 1, every yard stocks every class and drive; a minimum of 0 lets some yards not stock some items.
- A new yard starts full: each item's count equals its limit.
- **Restocking.** Every `restockIntervalDays`, counted from when the markets were created, each item a yard stocks gains `restockAmount` (default 1), never above its current limit. Stock above a lowered limit is never removed; it just stops growing. Restocks run in the market replenishment phase for that yard's system (section 8.1).
- **Prices.** Each yard has one price roll, made once when the yard is created and stored as a whole percent in the `priceRoll` range. Every ship and drive at that yard costs its current tuning base price x that roll, rounded by the one rounding rule. Tuning price edits therefore reach existing saves.
- Yard rolls and bought ships' names use the yards' own seeded generator, separate from the markets', so a seed's galaxy and market rolls never depend on its yards.

### 7.2 Buying a ship

- A ship can be bought at any yard from anywhere on the map, whether or not one of the player's ships is there. It is delivered docked at that yard.
- A bought ship always comes with the slowest drive (the lowest `speedMilliC`; if two are equally slow, the first listed). Its price covers that drive, and buying it does not take a drive out of the yard's drive stock. A faster drive is a separate drive purchase, which can be made at once if that yard stocks it.
- A bought ship starts with empty fuel, food, and water and with in-system transport off.
- **Its name** comes from `shipNames`. While any listed name is not already in the fleet, one of those is drawn by the seeded generator. Once every listed name is in use, a name is drawn from the whole list and given the lowest number from 2 up that no ship in the fleet has ("Tern 2", then "Tern 3"). The same seed and commands always give the same names.
- The purchase is refused, changing nothing, when the system has no shipyard, the yard does not sell that class, it has none in stock, or the credits do not cover the price. Credits exactly equal to the price are enough.

### 7.3 Fitting a drive

- The ship must be docked at a yard that stocks the drive (not in transit). A ship on in-system transport can be fitted; the toggle stays on and its daily pay count is not restarted.
- Any stocked drive different from the installed one may be fitted at the full price, including a slower one. The old drive is gone, with no refund or trade-in. Fitting the drive already installed is refused with a reason.
- Refusals give their numbers: the nearest yard that stocks the drive with its distance, stock, and price; the stock limit and next restock date when out of stock; the price, the credits, and the shortfall. Every problem that applies is listed, except for a ship in transit, which gets only the in-transit reason. A refused order changes nothing.
- Buying a ship and fitting a drive take no game time and work while paused.

### 7.4 The upgrade preview

- The player picks a ship, a yard, a drive, and a route: an ordered loop of two or more systems, the last leg returning to the first (A to B to A for a simple run).
- The preview shows the drive's price at that yard (the same figure the fitting charges) and how many are in stock, or a note that the yard cannot sell it. For each leg it shows the transit time with the current drive and with the new one, and the fuel each needs **for an empty ship with crew only** (no freight, no passengers, only the food and water the crew needs), in kilograms, worked out by the same provisioning math the quote uses. It shows the total time saved per loop (negative for a slower drive). Each leg is rounded up separately, as real voyages are.
- The preview works for any ship, docked or in transit, from anywhere, and changes nothing.

## 8. Automated routes

- A route is an ordered loop of two or more stops assigned to a ship.
- Each stop has: which freight is eligible, whether to carry passengers, automatic provisioning on or off (the dispatch top-up yes/no in section 6.6), and a minimum departure load in the relevant unit (tonnes for freight, passengers for berths).
- A leg can be marked as an explicitly empty repositioning leg.
- The route editor estimates revenue, costs, total transit time, and profit per game year for the **whole circuit**, including empty legs. Any figure that depends on future bookings or waiting time is labelled as an estimate.
- Automated departures use exactly the same dispatch rules and code as manual ones.
- A ship waiting for its minimum load is rechecked whenever supply or bookings replenish. It shows the unmet condition, for example "Waiting for 12 more tonnes of ore".
- If the next leg is infeasible (cannot afford provisions, load can never fit, destination removed from route), the ship stays docked, is flagged as blocked with the reason, raises a notification, and the player can change its orders.

### 8.2 Route orders

- **Valid orders.** At least two stops. Two consecutive stops (the last and the first included) must be different systems; a system may appear more than once otherwise. Each eligible commodity must be produced at its stop and bought at the next stop, and none may be listed twice. A freight minimum needs at least one eligible commodity; a passenger minimum needs passengers switched on. Minimums cannot be negative. Invalid orders are refused, listing every problem, and change nothing.
- **Empty legs are explicit.** A stop marked as an empty repositioning leg cannot list freight, carry passengers, or have a minimum. A stop that lists no freight and does not carry passengers must be marked empty; otherwise the orders are refused.
- **Where the ship starts.** The ship starts at the first stop (in list order) whose system is where it is docked, or where it is heading if it is in flight. Orders can be set on a ship in flight; they never change the voyage in progress.
- **Ships on in-system transport.** Setting or resuming a route on a ship with in-system transport on is refused, changing nothing, with a reason telling the player to turn in-system transport off first. A ship on an active route cannot be switched onto in-system transport (section 10).
- **Manual dispatch.** A ship on an active route cannot be dispatched by hand; the order is refused, changing nothing, with a reason telling the player to pause or clear the route first. It can be dispatched by hand while its route is paused. Quote, Provision, and drive fitting stay allowed on a route ship.
- **Drives.** A drive can be fitted to any docked ship, on a route or not (section 7.3). The next departure is quoted with the new drive.

### 8.3 Loading

- **How much a stop loads.** As much as the quote allows. If the stop carries passengers they board first, up to the berths and the bookings waiting, but never so many that the stop's freight minimum could no longer fit in the hold and tank. Then freight for the next stop loads in the order the player listed the commodities, each as many tonnes as fit (hold and tank) and are available (supply and demand). There are no set amounts.
- Credits never shrink the load. If the top-up for that load cannot be paid, the ship is blocked.
- **Minimums.** Each stop has two minimums: tonnes of freight (all eligible commodities together) and number of passengers. Both must be met to leave. Zero means no minimum.
- **Waiting text.** A ship that cannot meet its minimums waits, and its status lists every unmet minimum with its number: "Waiting for 12 more tonnes of ore", "Waiting for 12 more tonnes of freight (ore or metals)" when several commodities are eligible, "Waiting for 12 more tonnes of ore and 3 more passengers", with "1 more tonne" and "1 more passenger" in the singular.

### 8.4 Departure checks

- **When a ship checks.** A route ship's departure check runs in phase 4 (section 8.1). A ship can leave in the same tick it arrives: the arrival is in phase 1, the departure check in phase 4. Setting or resuming a route on a docked ship checks at once (commands run between ticks).
- **The check.** It builds the voyage plan by the loading rules and calls the same dispatch as a manual voyage, with the stop's automatic provisioning setting as the top-up yes/no. A check that does not depart changes nothing except the ship's waiting or blocked status and, when it becomes blocked, its notification.
- **Waiting rechecks.** A waiting ship is rechecked in phase 4 whenever a market event (replenishment or re-roll) runs at its stop or at its next stop, but no more often than once per `automation.recheckMinIntervalDays`. A replenishment that comes too soon schedules the recheck for the earliest allowed tick, so no replenishment is missed.
- **Blocked.** A ship is blocked when: the next leg cannot fit even with no freight or passengers; a minimum can never be met (more passengers than berths or than the pool can ever hold, more freight than supply and demand can ever hold, or the two minimums together do not fit the hold and tank); the top-up cannot be paid; automatic provisioning is off and supplies are short (the dispatch's own reason); or the ship is docked at a system that is not its route's current stop (for example, the stop it was heading to was removed from the route). The route orders are accepted in that last case; the ship is flagged blocked once docked.
- **Blocked for the top-up only.** A ship blocked only because the top-up cannot be paid is rechecked when credits rise (an arrival's payout or in-system pay), in the phase-4 departure check, by ship id, and no more often than once per `automation.recheckMinIntervalDays`. It stays flagged blocked until it leaves. Every other kind of block waits for the player.
- **Notifications.** A ship raises one notification each time it becomes blocked, with the reason, not one per failed retry. Waiting shows only as the ship's status.
- **Retry.** Resuming the route of a blocked ship retries the check at once, whether or not the route was paused, with no change to its orders. After a manual Provision on a ship blocked for short supplies, this retry is how it leaves.

### 8.5 Pause, resume, and clear

- **Pause** keeps the route orders and the ship's place in the loop (the stop it is at or heading to) but makes no automatic departures, and clears any waiting or blocked status. A ship in flight finishes its voyage unchanged and stays docked on arrival. While paused, the ship can be dispatched by hand or put on in-system transport.
- **Resume** continues from the ship's remembered stop if the ship is docked at that stop's system. Otherwise it continues from the next stop after the remembered one, going round the loop, whose system is where the ship is docked. When the docked system appears more than once in the loop, this is how the right one is picked. Resuming a docked ship checks at once; a ship in flight continues when it docks.
- **Clear** deletes the route orders. The ship becomes an ordinary manually flown ship: a ship in flight finishes its voyage unchanged and stays docked.
- Changing, pausing, or clearing a route never alters a voyage already in flight.

### 8.6 The whole-circuit estimate

- The estimate uses today's published rates and fares and today's supply, demand, and bookings, loads each stop by its own loading rules, prices supplies as if bought at each stop at today's prices with nothing aboard at the start, and counts no waiting time.
- **Exact (not labelled):** each leg's distance and transit time, the circuit's total transit time, and the kilograms of fuel, food, and water an empty leg needs.
- **Labelled as an estimate:** every load, revenue, cost, profit, and profit per game year figure, and the supply kilograms of a loaded leg.
- A note names any stop where today's stock is below its minimum (the ship would wait there) and any leg that cannot be flown.

### 8.1 Deterministic ordering

Events at the same tick are processed in this order: (1) arrivals, by ship id; (2) market replenishment, rate re-rolls, and shipyard restocks, by system id; (3) in-system transport income, by ship id; (4) automated departure checks, by ship id. Competing reservations are resolved in that order, so the lower ship id wins a tie.

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
- **Icons looked up by id.** An industry with tuning id `x` uses `Icons/industry_x.png` and a commodity with id `x` uses `Icons/cargo_x.png`, so adding one to the tuning file needs no code change. Any image with no file gets an automatic stand-in drawn by the game, with no error. The four default industries whose files were named before their ids existed are mapped by a fixed list in the presentation code (`mining_colony` to `industry_mining`, `agricultural_system` to `industry_agriculture`, `population_hub` to `industry_population`, `shipyard_works` to `industry_shipyard`). Ship classes follow the same pattern (`Ships/x_map.png`, `Ships/x_closeup.png`), with the defaults `bulk_freighter` and `passenger_liner` mapped to the existing `freighter_*` and `liner_*` files.
- Every sound event in `eli-tasks.md` is wired to a named audio file under `game/Assets/Audio/`, each a silent placeholder. The settings menu has master, effects, and music volume sliders and a mute toggle.

## 10. In-system transport

This is how a broke player recovers. It replaces the brief's emergency contract.

- A toggle on any docked ship. While on, the ship does local hauling inside its current system and earns a small, steady income, credited once per game day.
- **When it pays.** The daily rate is paid after each full game day (10,000 ticks) since the toggle went on. A partial day pays nothing, and switching off then on restarts the count. A day that completes at the same tick the toggle goes off is paid.
- A ship in transit cannot be switched on; the request is refused with its destination and arrival date and changes nothing. Asking for the state a ship is already in (on when on, off when off) succeeds and changes nothing; in particular, it does not restart the count.
- The ship cannot be dispatched while the toggle is on (section 6.6); it can still be quoted and provisioned. Turning it off is instant and has no cost.
- The daily rate is per ship class, in the tuning file, and must be at least 1 credit for every class (a lower value is a tuning error naming the field), so recovery is always possible. It is set low: a full game year of in-system transport may earn no more than about 25 percent of what the same ship earns from typical voyages in that time (this replaces the earlier 10 to 15 percent band).
- **Recovery target.** A courier with zero credits, on in-system transport, can afford the cheapest feasible job in about 450 game days (75 real minutes at 1x). This is reached mainly by making the cheapest job cheaper to supply, not by raising in-system income (section 12, target 4).
- It uses no provisions and needs no cargo.
- It runs through the normal event queue, so it earns nothing while paused and earns correctly during offline catch-up.
- **In-system income total.** The simulation keeps a running total of every credit earned from in-system transport, like the supplies-bought total (section 6.7). It is one total, saved with the game, and the return summary uses it (section 11.3).
- A ship on an automated route can only be toggled after the route is paused or cleared. The request is refused with that reason and changes nothing (section 8.2).
- There is no other rescue mechanic. Because this income is always available to a docked ship, the player can always earn enough to provision the cheapest feasible job.

## 11. Saves and offline time

### 11.1 Contents

Versioned saves under Unity's persistent data path (`%USERPROFILE%\AppData\LocalLow\spacetrade2\spacetrade2\saves\`). Each save is one JSON text file. A save holds:

- **Header:** a marker naming it a spacetrade2 save, the save format version, the real-world UTC timestamp of the save, and a checksum (section 11.2).
- **Clock:** the tick, the selected speed, and the pause state.
- **Money:** credits, the supplies-bought total (section 6.7), and the in-system income total (section 10).
- **Galaxy:** the seed, the start system, and every generated system exactly (id, name, position, population, industries, whether it has a shipyard), so a later change to galaxy generation never changes a saved galaxy.
- **Market:** when the market was created; both random generators' exact states (market and shipyard); whether the start port's opening rates are still held (section 6.4); every supply and demand stock; every freight offer's and passenger pool's stored roll and waiting passengers; every system's provision price rolls; every shipyard's price roll and, per ship class and drive, its stock count and stock-limit roll. Reservations last only inside a command, so none is ever open when a save is written.
- **Ships:** for every ship, its **id, name, class, drive, and docked system (empty while in transit)**; fuel, food, and water aboard, each with its total cost; the tick its in-system transport went on (if on); its voyage in flight with all locked terms (origin, destination, distance, drive, departure and arrival ticks, freight lines with locked rates, passengers and locked fare, and the fuel, food, and water it will use); and its route (stops with every order, current stop, paused or not, status and its reason, whether it is blocked for a top-up, and its last and pending recheck ticks). The next ship id is saved too.
- **Deliveries:** the running delivery totals (section 6.7) and the newest receipts kept in full (section 12, `receipts.keepNewest`), in arrival order.
- **Notifications:** the newest ones kept (section 12, `notifications.keepNewest`).
- **Pending events:** every scheduled event (arrivals, market replenishment and re-rolls, in-system pay days, route rechecks) with its tick and its ordering number, plus the next ordering number, stored exactly. Loading checks that these events match the ships, routes, and systems, and refuses a save where they do not. Same-tick events therefore keep their exact order across a save and load, and a save and load round trip is byte-identical in the full state dump, with nothing ignored.

Debug permission (section 13) is never saved.

### 11.2 Writing and loading

- **Atomic write.** A save is written to a temporary file (`name.json.tmp`), flushed to disk, then swapped into place, keeping the previous file as the backup (`name.json.bak`). If any step fails, the previous save and its backup are unchanged, and only the temporary file that write made is removed.
- **Checksum.** Every save carries a SHA-256 checksum of its own text. A file whose checksum does not match is treated as damaged.
- **Damaged saves.** A save that is cut short, unreadable, fails its checksum, or whose contents do not fit together is damaged. The game then loads the backup instead and tells the player both what was wrong and that the backup (with its date) was loaded. The damaged file is kept under a new name (`name.json.damaged-YYYYMMDD-HHMMSS`) and never deleted, so the next save starts a fresh main file and the good backup stays. If the backup cannot be loaded either, nothing is loaded and no file is changed.
- **Refused saves.** Only damage falls back to the backup. A save from a newer or unknown format version, or one that uses a commodity, ship class, or drive the current tuning file no longer defines, is refused with a clear message and changes nothing; the backup is not tried.
- **Versions.** An older format version is converted step by step on load (a migration hook). Format 2 is current. Format 1 had no delivery totals but kept every receipt, so converting it works the totals out exactly from its receipts. A save is never loaded half-way: the game is rebuilt separately and handed over only when every step succeeds.
- **Tuning on load.** A save loads with the current tuning file. Prices, rates, stock limits, and caps follow it; in-flight voyages keep their locked terms and arrival ticks. If a replenish, re-roll, or restock interval changed, the next market event is rescheduled under the new interval, but nothing is ever granted for time already passed: no extra replenishment, re-roll, or restock from before the load. Lowering `notifications.keepNewest` trims the oldest notifications on load. Lowering `receipts.keepNewest` drops the oldest receipts on load; the delivery totals do not change.
- **Slots.** Each game has a current slot. Autosaves and the quit save write to it. "Save as" writes a new named slot, which becomes the current slot (a failed "Save as" keeps the old one). Slot names use letters, digits, spaces, `-`, and `_`, up to 64 characters. Starting a new game under a name that already exists is refused. How a new game is named is decided in session 17.
- **Autosave.** The game autosaves on every dispatch, every arrival, every 60 real seconds, and on quit. The player can also save at any time.
- **Save warning.** If a save fails, the game keeps running and shows a warning with the reason. The warning stays visible until a save succeeds.

### 11.3 Offline catch-up

- The simulation continues while the game is closed. On opening a save that was running, elapsed game time = real milliseconds since the save timestamp x the saved speed (1 tick per real millisecond at 1x), and the simulation advances by that amount through the normal `Advance` path and event queue. The same rule handles waking from sleep (section 3.2).
- A paused save stays paused and advances nothing.
- Deliveries, replenishment, in-system income, automated departures, and later arrivals are processed in chronological order.
- **Return summary.** The player sees a short return summary: the deliveries made while away (their number and totals, taken from the running delivery totals so they cover the whole absence exactly, with the receipts of the newest ones still kept listed in full and the rest counted but not listed), voyage payouts, in-system income, supplies bought, and the net change in credits, plus the ships needing attention. Payouts + in-system income - supplies bought always equals the net change, which always equals the change in credits exactly. **Ships needing attention** are ships blocked on their route (with the route's reason) and docked ships with no active route and no in-system transport (idle, or with a paused route). The summary panel is shown only when the time away is 5 real minutes or more.
- The catch-up result and the new timestamp are saved together in one atomic write before the player gets control, so loading twice cannot pay twice. If that save fails, the player still gets the caught-up game, with the save warning (section 11.2); the file on disk is unchanged, so reopening it cannot pay twice.
- If the system clock has moved backward, elapsed time is treated as zero and the timestamp is reset. Game time never runs backward.
- **No cap, run in slices.** There is no cap on offline time. A catch-up can run in slices, each through the same `Advance`, so the screen keeps drawing and shows how far it has got: a progress figure from 0 to 1,000 (thousandths), which reaches 1,000 only when the catch-up is done. However it is sliced, it ends in exactly the same state as one big step. Real time that passes while catching up is counted too, so the game is fully up to date when it ends. Until then player and clock commands are refused, with a reason; saving or quitting finishes the catch-up first. It ends with the one atomic save above. (Measured catch-up times are in `docs/progress.md`.)

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
- **shipNames:** the list new ships are named from (sections 6.4 and 7.2). At least one name, no blank names, no repeats.
- **drives:** per tier speed, price, fuel kg per tonne-ly.
- **shipyards:** stock-limit ranges, restock interval, restock amount (default 1), local price variation (section 7.1).
- **automation:** recheck behaviour limits.
- **notifications:** `keepNewest`, how many notifications are kept (default 200, at least 1). When there are more, the oldest are dropped, while playing and on load.
- **receipts:** `keepNewest`, how many delivery receipts are kept in full (default 500, at least 1). When there are more, the oldest are dropped, while playing and on load; every delivery stays counted in the delivery totals (section 6.7).

Rules:

- The file is read when the game starts. Changes apply to existing saves for anything not already locked (in-flight voyages keep their locked terms; the generated galaxy does not change).
- The file is validated on load. A bad value produces a message naming the field, and the game falls back to built-in defaults rather than crashing.
- Validation includes a starter-range check: the starter ship class with the starter drive, a full fuel tank, crew only, and the food and water that crew needs, must be able to fly `maxHopMilliLy`. Failure gives a field-named error like any other.
- A companion `docs/tuning-guide.md` explains each field in plain language with its unit.
- Automated tests use a fixed test copy of the tuning data, so editing the live file never breaks the test suite. One separate test checks that the live file loads and still satisfies the new-game guarantees in section 6.4.

Balance targets for the default values (the agent tunes toward these and reports results):

1. The guaranteed first voyage nets a clear profit.
2. A second courier is affordable after about 10 to 15 game years of reasonable voyages (2 to 3 real hours at 5x), roughly 4 to 5 voyages.
3. A drive upgrade pays for itself on a good circuit within roughly 5 to 10 circuits.
4. Recovery: a courier with zero credits on in-system transport can afford the cheapest feasible job in about 450 game days (75 real minutes at 1x). Reach this mainly by making the cheapest job cheaper to supply. In-system income may rise to no more than about 25 percent of typical voyage income (section 10).
5. About one in four published jobs is a money-loser for a courier, so reading the quote matters.
6. Freight stays in tonnes. A bulk freighter can normally fill its hold on a good route, and freight runs short only when several ships work the same route. Supply and demand caps are probably in the low thousands of tonnes.

## 13. Debug skip

- "Skip to next arrival" exists only in the Unity Editor and in Development Builds, and only when debug mode is explicitly switched on.
- It is enforced in two places: the simulation command handler rejects the command unless debug capability was granted when the simulation was created, and the interface does not create the button otherwise. In a normal build the capability is compiled out and cannot be granted: the only way to make one exists only when Unity's `DEBUG` symbol is defined, which Unity sets in the Editor and in Development Builds only.
- It jumps to the earliest pending arrival tick, processing every event in between and every arrival at that tick, through the normal `Advance` path.
- It is allowed while paused and never unpauses.
- The permission is never saved. A loaded game has debug permission only if the code that loads it grants it.
- The button is disabled when nothing is in flight; the command, if sent anyway, is refused and changes nothing.
- The normal Windows build has no player-triggered time jump of any kind.

## 14. Acceptance

### 14.1 Playthrough (in the Editor through MCP, then repeated by Eli in the build)

Start a fresh game through the normal interface. Quote, provision, launch, follow the ship, and read its delivery receipt. Confirm two ships travel independently, a yard installs a drive on a ship docked there, and an upgrade for a ship elsewhere is rejected.

### 14.2 Deterministic tests

Drive math (the table in 3.3); capacity and provisioning; competing claims on 80 tonnes; failed dispatch rollback; exactly-once payouts; sustained automated circuits over many game years; minimum-load waiting and recheck; empty return leg costs; the new-game guarantee across many seeds; and in-system transport recovery (a docked ship with zero credits earns enough to provision the cheapest feasible job, cannot dispatch while toggled, earns nothing while paused, and repeated toggling cannot create extra money).

### 14.3 Save tests

Running and paused saves, including one made mid-transit. Run the same elapsed game time three ways (ordinary simulation, offline catch-up, debug stepping) and compare balances, fleet positions, port stock, and route state: they must match exactly. Reopen a settled save and confirm no second payment. Backward clock change. Also: a save and load round trip is byte-identical; a damaged or cut-short save is refused clearly and the backup loads; a newer or unknown format is refused and changes nothing; an edited tuning file changes prices and limits but not locked voyages; a changed interval grants nothing for time already passed (shorter and longer); a failed write leaves the previous save intact; sleep catch-up and closed-game catch-up over the same gap end byte-identical; the return summary adds up to the change in credits; debug skip is refused without the capability. Tests write saves only to a temporary folder, never the real save folder.

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
