# spacetrade2: tuning guide

Every economy number in the game lives in one file, `game/Assets/StreamingAssets/tuning.json` (in a built game: `spacetrade2_Data\StreamingAssets\tuning.json`). This guide explains every field in plain language, with its unit.

## How the file works

- Open it in any text editor (VS Code or Cursor are easiest). It is JSON: groups of `"field": value` pairs inside `{ }`, and lists inside `[ ]`. Text goes in double quotes; numbers do not.
- **Every number is a whole number.** No decimal points. Where you might want a fraction, the unit is small enough that you don't need one: `2500` milli-light-years is 2.5 light-years, `1500` milli-c is 1.5 times the speed of light, `80` percent means 0.8 times.
- The unit is in each field's name: `...MilliLy` (thousandths of a light-year), `...MilliC` (thousandths of light speed), `...Tonnes`, `...Kg`, `...Grams`, `...Days` (game days), `...Percent`, and plain credits for prices.
- The game reads the file when it starts. Changes apply to existing saves for anything not already locked: voyages in flight keep their agreed terms, and an existing save's galaxy never changes.
- **If anything in the file is wrong, the game ignores the whole file and uses its built-in defaults.** It shows a message for each problem, naming the field and the line, for example:
  - `galaxy.systemCountMin: must be a whole number, got "eight" (line 12)`
  - `ships[courier].tankTonnes: must be a whole number with no decimal point, got 20.5 (line 225)`
  - `galaxy.maxHopMilliLy: must be at least galaxy.nearestNeighbourMaxMilliLy (7000), got 5000`
- Items in a list are named by their id, so `commodities[ore].baseRatePerTonne` means the `baseRatePerTonne` field of the commodity whose id is `ore`.
- Problems are reported in two rounds. First, every field is checked on its own (missing, misspelled, wrong kind of value, out of range). Only when all of those are fixed does the game run the checks that compare fields with each other (for example, a minimum larger than its maximum). Fixing one round can reveal the next.
- Misspelled or extra field names are errors too, so a typo never silently does nothing.
- Ids (`"courier"`, `"ore"`, `"refinery"`) link sections together. If you rename an id, rename every place that uses it.

Some fields are used by rules that arrive in later build sessions (automated routes in 9). Their meaning is fixed here; later sessions may add fields, and will update this guide when they do. Market rules are described in "How markets use these numbers" and ship and voyage rules in "How voyages use these numbers" below.

## start

| Field | Unit | Meaning |
|---|---|---|
| `startingCredits` | credits | Money the player has at the start of a new game. |
| `starterShipClass` | id from `ships` | The class of the one ship the player starts with. |
| `starterDrive` | id from `drives` | The drive fitted to that starting ship. |
| `openingOffers.minFeasibleJobs` | jobs | The start port must offer at least this many jobs that the starting ship can fly and the player can afford. When this is 2 or more, two of them must also go to different destinations with one faster (fewer transit ticks) and the other earning more (more net profit on its quote). See "New games" below. |
| `openingOffers.minProfitableJobs` | jobs | Of those, at least this many must show a profit in the real quote. Cannot be more than `minFeasibleJobs`. |
| `marketStartFill.supplyPercent` | percent, 0 to 100 | How full each system's freight supply is when a new galaxy's markets are created, as a percent of `supplyCapTonnes`. 100 = full. |
| `marketStartFill.demandPercent` | percent, 0 to 100 | The same for each destination's demand, as a percent of `demandCapTonnes`. |
| `marketStartFill.passengerPercent` | percent, 0 to 100 | The same for each passenger pool, as a percent of that route's pool size. |

### New games

- A new game opens paused at 1x on Y1 M01 D01, with the starter ship docked at the start system with empty supplies and `startingCredits`. Its name is picked from `shipNames` by the save's seed.
- **A job** is one freight offer or one passenger booking from the start port, loaded with as much as the starter ship can take. It counts as feasible and affordable when its real quote shows no problem with the starting credits.
- **How the game makes the guarantee hold.** If the start port's jobs do not meet `minFeasibleJobs`, `minProfitableJobs`, and the faster-versus-earns-more rule, the game raises the rate roll of every freight offer and passenger fare at the start port by 1 percent, never above `rateRollMaxPercent` or `fareRollMaxPercent`, and checks again. Distances, stock, and every other system are never touched. With the defaults this happened on 1 seed in 200.
- **The start port's rates stay put until the first launch.** Its rates and fares are not re-rolled until the first ship is dispatched, so waiting before the first voyage never loses the guarantee. Everywhere else re-rolls every `rates.rerollIntervalDays` as usual.
- **If your edited file cannot meet it.** If the guarantee still fails with every start-port roll at its maximum, that new game is built from the built-in defaults instead, and the game shows a message naming the fields to look at, for example `start.openingOffers.minFeasibleJobs: the start port offers 0 job(s) the starter ship can fly and afford, needs 2. Look at start.startingCredits, start.marketStartFill, ...`. The built-in defaults never fail: on a seed they cannot meet, the next seed is used, and that seed becomes the game's seed.

## galaxy

| Field | Unit | Meaning |
|---|---|---|
| `systemCountMin`, `systemCountMax` | systems | How many star systems a new galaxy has, picked at random in this range. At least 3. |
| `nearestNeighbourMinMilliLy`, `nearestNeighbourMaxMilliLy` | milli-ly | Every system's closest neighbour is at least the minimum and at most the maximum distance away. Controls how spread out the map is and how long the shortest trips take. |
| `maxHopMilliLy` | milli-ly | The longest single hop the galaxy rules count on. Every system must be connected to the start system by a chain of hops no longer than this, and the start system must be within this distance of a shipyard. Must be at least `nearestNeighbourMaxMilliLy`, so a galaxy can always be generated. The starter ship must also be able to fly this far: see "Starter-range check" below. |
| `industriesPerSystemMin`, `industriesPerSystemMax` | industries | How many industries each system has. The maximum must be at least 2 (a shipyard system needs the yard plus at least one trading industry) and no more than the number of industries that don't enable a shipyard. |
| `shipyardCountMin`, `shipyardCountMax` | systems | How many systems get a shipyard. At least 2, and the maximum cannot be more than `systemCountMin`. |
| `populationMinMillions`, `populationMaxMillions` | millions of people | Each system's population, picked at random in this range. Larger populations mean more passengers. |
| `names` | list of text | Names for star systems. Needs at least `systemCountMax` names, with no repeats. |

## industries

A list. Each industry type has:

| Field | Unit | Meaning |
|---|---|---|
| `id` | text | Short name used by other sections. Must be unique. |
| `name` | text | Name shown to the player. |
| `consumes` | list of commodity ids | What this industry buys. Freight of these commodities can be delivered here. |
| `produces` | list of commodity ids | What this industry makes. Freight of these commodities ships out from here. |
| `producesPassengers` | true or false | True for population hubs: extra passengers travel from here. |
| `enablesShipyard` | true or false | True for shipyard works: a system with this industry has a shipyard. At least one industry must have this. |

The industries together must allow a closed three-stop freight circuit: industry A makes something B uses, B makes something C uses, and C makes something A uses. The defaults allow two (mining colony, refinery, agricultural system; and mining colony, refinery, factory).

## commodities

A list. Each freight commodity has:

| Field | Unit | Meaning |
|---|---|---|
| `id`, `name` | text | Short id and display name. |
| `baseRatePerTonne` | credits per tonne | Starting point for what a customer pays to move one tonne. The distance factor in `rates` scales it. |
| `rateRollMinPercent`, `rateRollMaxPercent` | percent | Each published rate is rolled at random between these percentages of (base rate times distance factor). For example, 80 to 120 means plus or minus 20 percent. |
| `supplyCapTonnes` | tonnes | Most freight that can wait at a producing system at once. |
| `demandCapTonnes` | tonnes | Most freight a consuming system will accept at once. |
| `supplyReplenishTonnes`, `demandReplenishTonnes` | tonnes | How much supply and demand grow back each interval, up to their caps. |
| `replenishIntervalDays` | game days | How often supply and demand grow back. |

## passengers

| Field | Unit | Meaning |
|---|---|---|
| `baseFare` | credits per passenger | Starting point for a fare. The distance factor in `rates` scales it. |
| `fareRollMinPercent`, `fareRollMaxPercent` | percent | Fares roll in this range, the same way freight rates do. |
| `poolCapBasePassengers` | passengers | Every route between two systems can hold at least this many waiting passengers. |
| `poolCapExtraPerTenMillionPopulation` | passengers | Extra waiting-passenger room for every 10 million people living at the two ends combined, counted proportionally (35 million people gives 3.5 times this). |
| `hubPoolBonusPercent` | percent | Extra room on routes from a population hub. 100 means double. |
| `replenishPassengers` | passengers | How many new passengers arrive per interval, up to the cap. |
| `replenishIntervalDays` | game days | How often new passengers arrive. |

## rates

| Field | Unit | Meaning |
|---|---|---|
| `distanceFactorBasePercent` | percent | The fixed part of the distance factor. |
| `distanceFactorPerLyPercent` | percent per light-year | The part of the distance factor that grows with trip length. Distance factor = base percent + (per-ly percent times light-years). With the defaults (100 and 40), a 5 ly trip pays 300 percent of the base rate. Both cannot be 0. |
| `rerollIntervalDays` | game days | How often published rates and fares are re-rolled. |

## provisions

| Field | Unit | Meaning |
|---|---|---|
| `fuelBasePricePerTonne`, `foodBasePricePerTonne`, `waterBasePricePerTonne` | credits per tonne | Typical prices for ship supplies. |
| `priceRollMinPercent`, `priceRollMaxPercent` | percent | Each system's prices vary at random within this range of the base price. |
| `industryModifiers` | list | Systems with a given industry sell some supplies cheaper or dearer. Each entry has `industry` (an industry id) and `fuelPercent`, `foodPercent`, `waterPercent` (100 = no change, 70 = 30 percent cheaper). One entry per industry at most. When a system has several industries with entries, their percents multiply (70 and 70 give 49). |
| `combinedModifierMinPercent`, `combinedModifierMaxPercent` | percent | Floor and ceiling for the multiplied-together industry modifier. With the defaults (50 and 200), two 70 percent modifiers give 50 percent, not 49. The minimum must be at least 1 and no more than the maximum. |
| `foodGramsPerPersonPerDay` | grams | Food each person aboard (crew and passengers) eats per game day. |
| `waterGramsPerPersonPerDay` | grams | Drinking water each person uses per game day. |
| `personMassKg` | kilograms | Mass of one person with luggage, counted in the ship's loaded mass. |

Voyages last game years, so small changes to the per-person food and water amounts have a big effect on how much hold space supplies take.

## How markets use these numbers

- **Prices are worked out, not stored.** For open freight offers, passenger pools, and provision prices, a save keeps only the random roll (a percent). The number the player sees is worked out each time from that roll and the current tuning file. Edit a base price, the distance factor, or a modifier, and existing saves show the new prices straight away.
- **Voyages in flight keep their pay.** When a ship is dispatched, the freight rate and passenger fare it was quoted are written into the voyage as fixed credit amounts, not as a percent. Editing this file never changes what a voyage already in flight will be paid. Its arrival time and the kilograms of fuel, food, and water it uses are fixed at dispatch too, so changing a drive speed or a supply rate only affects voyages dispatched afterwards.
- **Freight rate** = `baseRatePerTonne` x distance factor x roll, where the roll is a whole percent between `rateRollMinPercent` and `rateRollMaxPercent`. **Passenger fare** is the same with `baseFare` and the fare roll range.
- **Provision price** = base price x combined industry modifier (clamped to the floor and ceiling) x the system's roll, a whole percent between `priceRollMinPercent` and `priceRollMaxPercent`. Provision rolls are made once, when a galaxy's markets are created, and never re-rolled, so a cheap-fuel port stays cheap.
- **Rounding.** One rule everywhere a figure is rounded to the nearest whole number: it is calculated exactly and rounded once, at the end, with exact halves rounding away from zero (2.5 becomes 3, -2.5 becomes -3). That covers rates, fares, prices, pool sizes, starting amounts, supply purchase costs, the share of supply cost a voyage uses, and profit per game year. Transit time, the real-time countdown, and supplies needed are rounded up instead, so none of them ever comes out short.
- **Schedules** count from the moment the markets were created. Every `replenishIntervalDays` (per commodity) supply and demand grow by their replenish amounts; every `passengers.replenishIntervalDays` each pool gains `replenishPassengers`; every `rates.rerollIntervalDays` every freight rate and passenger fare is re-rolled. Nothing grows past its cap, and growing back never removes stock that is already above a cap you lowered.
- **Offers.** There is one freight offer for every pair of a system that produces a commodity and a different system that consumes it, and one passenger pool for every ordered pair of systems. Supply at an origin is shared by all its offers for that commodity, and demand at a destination is shared by every origin delivering there.

## How voyages use these numbers

- **People aboard** = the class's `crew` + passengers.
- **Food needed** (kg) = people x voyage days x `foodGramsPerPersonPerDay` / 1,000, rounded up to the whole kilogram. **Water** is the same with `waterGramsPerPersonPerDay`. Voyage days can be a fraction: a 4.5 ly trip at 1.5c lasts exactly 1,080 days.
- **Loaded mass** (tonnes) = `dryMassTonnes` + freight + food + water + people x `personMassKg`. Fuel's own mass is left out.
- **Fuel needed** (kg) = light-years x loaded mass in tonnes x the drive's `fuelKgPerTonneLy`, rounded up to the whole kilogram.
- **Hold**: freight + food + water must fit in `holdTonnes`. Food and water count at what will be aboard after topping up, including leftovers from earlier voyages.
- **Tank**: fuel needed must fit in `tankTonnes`. **Berths**: passengers must not exceed `berths`.
- **Buying supplies**: kilograms x the local price per tonne / 1,000, rounded once.
- **Cost of supplies used**: each supply aboard has one average price. A voyage is charged its share of the total cost; using everything left charges everything left, so an empty tank or larder always carries zero cost.

## Starter-range check

When the file loads, the game checks that the starter ship (`start.starterShipClass` with `start.starterDrive`) can fly `galaxy.maxHopMilliLy` with crew only, a full tank, and the food and water the crew needs. If the fuel needed is more than the tank, the error names `ships[<starter class>].tankTonnes`. If the food and water do not fit, it names `ships[<starter class>].holdTonnes`. Either way the file is set aside and the built-in defaults are used. With the defaults the courier needs 9.789 t of fuel (tank 20 t) and 3.36 t of food and water (hold 30 t) for a 7 ly hop.

## ships

A list. Each ship class has:

| Field | Unit | Meaning |
|---|---|---|
| `id`, `name` | text | Short id and display name. |
| `dryMassTonnes` | tonnes | The empty ship's mass. Heavier ships burn more fuel. |
| `holdTonnes` | tonnes | Room for freight plus food and water together. |
| `tankTonnes` | tonnes | Most fuel the ship can carry. |
| `berths` | passengers | Most passengers the ship can carry. |
| `crew` | people | Crew aboard. They eat and drink like passengers. |
| `price` | credits | Base price at a shipyard, including the slowest drive it comes with. Each yard charges this times its price roll. |
| `inSystemDailyIncome` | credits per game day | What the ship earns doing in-system transport. At least 1, so a broke player can always recover. Aim for about 10 to 15 percent of what the ship earns from voyages in the same time. |

**In-system transport** pays `inSystemDailyIncome` after each full game day (10,000 ticks, 10 real seconds at 1x) since the toggle went on. A partial day pays nothing, and switching off then on starts the count again. It earns nothing while the game is paused.

## shipNames

A list of text: the names new ships are given. The starter ship's name is picked from it by the save's seed. A bought ship gets a name from the list that no ship in the fleet already has; once every name is taken, it gets a listed name plus the lowest free number from 2 up ("Tern 2"). A ship keeps its name for good, so editing this list never renames a ship that already exists. The list must have at least one name, with no blank names and no repeats; otherwise the error names `shipNames` (for example `shipNames: "Tern" appears more than once`) and the defaults are used.

## drives

A list. Each drive tier has:

| Field | Unit | Meaning |
|---|---|---|
| `id`, `name` | text | Short id and display name. |
| `speedMilliC` | milli-c | Speed in thousandths of light speed. 1500 = 1.5c. A trip takes (light-years divided by c-multiple) game years. |
| `price` | credits | Base price at a shipyard, for fitting it to a ship. Each yard charges this times its price roll. |
| `fuelKgPerTonneLy` | kilograms | Fuel burned per tonne of loaded ship per light-year. |

## shipyards

| Field | Unit | Meaning |
|---|---|---|
| `shipStockPerClassMin`, `shipStockPerClassMax` | ships | The range for each yard's stock limit for each ship class. A limit of 0 means that yard does not sell that class. |
| `driveStockPerTierMin`, `driveStockPerTierMax` | drives | The same for each drive. |
| `restockIntervalDays` | game days | How often every yard restocks, counted from the start of the game. |
| `restockAmount` | ships or drives | How many of each item a yard stocks are added at each restock, never above that item's limit. At least 1. |
| `priceRollMinPercent`, `priceRollMaxPercent` | percent | Each yard's one price roll falls in this range. Every ship and drive there costs its base price times that roll. |

### How shipyards use these numbers

- **Stock limits move with your edits.** When a yard is created, it makes one roll for each ship class and drive, a whole percent from 0 to 100, and keeps only that roll. The limit is worked out each time as min + (max - min) x roll, rounded by the one rounding rule. A roll of 50 with min 1 and max 2 gives 1.5, which rounds to 2. Edit the min and max and every existing yard's limits follow. A yard starts with each item full at its limit.
- **Which yards sell what.** A yard sells an item when its limit is at least 1, or when it still has some left over. With the default minimum of 1 every yard sells every class and every drive. Set a minimum to 0 to make some yards not carry some items.
- **Restocking.** Every `restockIntervalDays`, each item a yard sells gains `restockAmount`, up to its limit. If you lower a limit, stock already above it is kept; it just stops growing.
- **Prices move with your edits.** Each yard keeps one price roll, made when it is created. The price shown and charged is the current `ships[].price` or `drives[].price` x that roll, rounded once. Edit a base price and every yard's price follows.
- **A bought ship** comes with the slowest drive (lowest `speedMilliC`, the first listed on a tie), included in the ship's price, with empty supplies. It can be bought at any yard from anywhere on the map and is delivered docked there. A faster drive is a separate purchase, which can be made at once if that yard stocks it.
- **Fitting a drive** needs the ship docked at a yard that stocks the drive. It costs the full price, with no refund or trade-in for the old drive. Any stocked drive other than the installed one can be fitted, including a slower one.
- **The upgrade preview** shows, for a route you pick, each leg's time and the fuel needed for an empty ship with crew only, with the current and the new drive, in kilograms.

## automation

| Field | Unit | Meaning |
|---|---|---|
| `recheckMinIntervalDays` | game days | A ship on an automated route that is waiting for a load is checked again when stock at its stop or next stop replenishes, at most once per this many days; a replenishment that comes too soon is checked at the earliest allowed day instead, so it is never missed. A ship blocked only because it cannot pay for its supplies is checked again when credits rise, with the same limit. 0 means check every time. |

## notifications

| Field | Unit | Meaning |
|---|---|---|
| `keepNewest` | notifications | How many notifications (arrivals, blocked routes) the game keeps. When there are more, the oldest are dropped. Default 200, at least 1; 0 or less is an error naming `notifications.keepNewest`. |

- **Lowering it trims old saves too.** When a save loads, any notifications beyond `keepNewest` are dropped, oldest first. Raising it later does not bring them back.
- **Delivery receipts are never trimmed.** Every receipt is kept and saved, whatever this number is.

## Editing tuning with saves you already have

- Saves load with the current tuning file. Prices, rates, stock limits, and caps follow your edits at once; voyages already in flight keep their agreed terms and arrival dates.
- If you change `replenishIntervalDays` (commodities or passengers), `rerollIntervalDays`, or `restockIntervalDays`, the next replenishment, re-roll, or restock in an existing save moves to the new schedule (still counted from the start of that game). Nothing is granted for time already passed: shortening an interval never hands out a catch-up replenishment, re-roll, or restock for the days before you loaded.
- If you remove a commodity, ship class, or drive that a save uses, that save is refused with a message saying what is missing. Put it back to load the save. The save file is not changed.
