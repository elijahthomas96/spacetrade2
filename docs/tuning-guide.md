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

Some fields are used by rules that arrive in later build sessions (markets in session 4, ships in 5, shipyards in 8, automated routes in 9). Their meaning is fixed here; later sessions may add fields, and will update this guide when they do.

## start

| Field | Unit | Meaning |
|---|---|---|
| `startingCredits` | credits | Money the player has at the start of a new game. |
| `starterShipClass` | id from `ships` | The class of the one ship the player starts with. |
| `starterDrive` | id from `drives` | The drive fitted to that starting ship. |
| `openingOffers.minFeasibleJobs` | jobs | The start port must offer at least this many jobs that the starting ship can fly and the player can afford. |
| `openingOffers.minProfitableJobs` | jobs | Of those, at least this many must show a profit in the real quote. Cannot be more than `minFeasibleJobs`. |

## galaxy

| Field | Unit | Meaning |
|---|---|---|
| `systemCountMin`, `systemCountMax` | systems | How many star systems a new galaxy has, picked at random in this range. At least 3. |
| `nearestNeighbourMinMilliLy`, `nearestNeighbourMaxMilliLy` | milli-ly | Every system's closest neighbour is at least the minimum and at most the maximum distance away. Controls how spread out the map is and how long the shortest trips take. |
| `maxHopMilliLy` | milli-ly | The longest single hop the galaxy rules count on. Every system must be connected to the start system by a chain of hops no longer than this, and the start system must be within this distance of a shipyard. Must be at least `nearestNeighbourMaxMilliLy`, so a galaxy can always be generated. A later session adds a check that the starter ship can actually fly this far on a full tank. |
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
| `poolCapExtraPerTenMillionPopulation` | passengers | Extra waiting-passenger room for every 10 million people living at the two ends combined. |
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
| `industryModifiers` | list | Systems with a given industry sell some supplies cheaper or dearer. Each entry has `industry` (an industry id) and `fuelPercent`, `foodPercent`, `waterPercent` (100 = no change, 70 = 30 percent cheaper). One entry per industry at most. |
| `foodGramsPerPersonPerDay` | grams | Food each person aboard (crew and passengers) eats per game day. |
| `waterGramsPerPersonPerDay` | grams | Drinking water each person uses per game day. |
| `personMassKg` | kilograms | Mass of one person with luggage, counted in the ship's loaded mass. |

Voyages last game years, so small changes to the per-person food and water amounts have a big effect on how much hold space supplies take.

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
| `price` | credits | Base price at a shipyard. |
| `inSystemDailyIncome` | credits per game day | What the ship earns doing in-system transport. Aim for about 10 to 15 percent of what the ship earns from voyages in the same time. |

## drives

A list. Each drive tier has:

| Field | Unit | Meaning |
|---|---|---|
| `id`, `name` | text | Short id and display name. |
| `speedMilliC` | milli-c | Speed in thousandths of light speed. 1500 = 1.5c. A trip takes (light-years divided by c-multiple) game years. |
| `price` | credits | Base price at a shipyard. |
| `fuelKgPerTonneLy` | kilograms | Fuel burned per tonne of loaded ship per light-year. |

## shipyards

| Field | Unit | Meaning |
|---|---|---|
| `shipStockPerClassMin`, `shipStockPerClassMax` | ships | How many ships of each class a yard has in stock. |
| `driveStockPerTierMin`, `driveStockPerTierMax` | drives | How many drives of each tier a yard has in stock. |
| `restockIntervalDays` | game days | How often a yard restocks. |
| `priceRollMinPercent`, `priceRollMaxPercent` | percent | Each yard's prices vary within this range of the base price. |

## automation

| Field | Unit | Meaning |
|---|---|---|
| `recheckMinIntervalDays` | game days | A ship on an automated route that is waiting for a load is checked again at most once per this many days. 0 means check on every replenishment. |
