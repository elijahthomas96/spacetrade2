# spacetrade2: graphics checklist

61 images: 54 vector, 7 raster. The agent builds with placeholders, so none of this blocks the build. Your file replaces the placeholder when it has the same name in the same folder.

## How to make them

- **Vector** means: draw it in Illustrator, keep the `.ai` file as your master, and export a PNG at the listed size with a transparent background. Unity uses the PNG.
- **Vector, white** means the same, but the artwork must be pure white on transparent. The game tints these, so one icon works in every colour.
- **Raster** means: paint or generate it as pixels (GIMP or similar) at the listed size. These are soft, textured images that gain nothing from being vector.
- Save masters in `D:\dev\spacetrade2\art-source\`.
- Save exports into `D:\dev\spacetrade2\game\Assets\Art\` in the folder shown for each section, using the exact filename. Overwrite the placeholder. Do not rename files.
- Sizes are in pixels, width x height.

**Suggested order:** the three map ships, `star.png`, and `starfield.png` first (they change how the game looks most), then status icons, then cargo and industry icons, then the rest.

**Not needed:** speed buttons (1x, 3x, 5x) and panel frames are drawn by the interface code.

**Adding industries, commodities, or ship classes later.** Icons for these are found by their id in `tuning.json`, so a new one needs no code change:

- A new industry with id `x` loads `Icons\industry_x.png`.
- A new commodity with id `x` loads `Icons\cargo_x.png`.
- Ship icons follow the same pattern (since session 12): a new ship class with id `x` loads `Ships\x_map.png` and `Ships\x_closeup.png`.
- Any of these with no file yet gets an automatic stand-in (a plain white ring with a cross), with no error, until you add the file.
- The defaults keep the names below. Four industries were named before their ids existed and are mapped in the game code: `mining_colony` to `industry_mining.png`, `agricultural_system` to `industry_agriculture.png`, `population_hub` to `industry_population.png`, `shipyard_works` to `industry_shipyard.png`. The default ship classes are mapped the same way (done in session 12): `bulk_freighter` to `freighter_*` and `passenger_liner` to `liner_*`; `courier` already matches.
- Drop the file in, with the exact name, and Unity picks it up with the right import settings automatically.

## 1. Ships

Folder: `game\Assets\Art\Ships\` (8 files)

- [ ] `courier_map.png`
- [ ] `freighter_map.png`
- [ ] `liner_map.png`
- [ ] `courier_closeup.png`
- [ ] `freighter_closeup.png`
- [ ] `liner_closeup.png`
- [ ] `warp_glow.png`
- [ ] `engine_trail.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `courier_map.png` | Courier, top-down, pointing right, for the map | Vector | 128 x 128 |
| `freighter_map.png` | Bulk freighter, top-down, pointing right, for the map | Vector | 128 x 128 |
| `liner_map.png` | Passenger liner, top-down, pointing right, for the map | Vector | 128 x 128 |
| `courier_closeup.png` | Courier detailed view for the zoomed-in close-up, same silhouette as the map icon | Vector | 1024 x 512 |
| `freighter_closeup.png` | Bulk freighter close-up, same silhouette as the map icon | Vector | 1024 x 512 |
| `liner_closeup.png` | Passenger liner close-up, same silhouette as the map icon | Vector | 1024 x 512 |
| `warp_glow.png` | Soft glow drawn around a ship in transit | Raster | 512 x 512 |
| `engine_trail.png` | Soft streak drawn behind a moving ship, fades right to left | Raster | 512 x 128 |

## 2. Map

Folder: `game\Assets\Art\Map\` (8 files)

- [ ] `star.png`
- [ ] `star_glow.png`
- [ ] `selection_ring.png`
- [ ] `shipyard_badge.png`
- [ ] `route_arrow.png`
- [ ] `starfield.png`
- [ ] `nebula_overlay.png`
- [ ] `closeup_backdrop.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `star.png` | Star system marker | Vector, white | 128 x 128 |
| `star_glow.png` | Soft halo behind a star | Raster | 256 x 256 |
| `selection_ring.png` | Ring around the selected system or ship | Vector, white | 256 x 256 |
| `shipyard_badge.png` | Small badge marking a system with a shipyard | Vector, white | 64 x 64 |
| `route_arrow.png` | Small arrowhead showing direction on a route line | Vector, white | 64 x 64 |
| `starfield.png` | Galaxy map background, dark, must tile seamlessly | Raster | 2048 x 2048 |
| `nebula_overlay.png` | Faint coloured clouds layered over the starfield (optional) | Raster | 2048 x 2048 |
| `closeup_backdrop.png` | Background behind the ship close-up, streaked stars | Raster | 1920 x 1080 |

## 3. Industry icons

Folder: `game\Assets\Art\Icons\` (6 files)

- [ ] `industry_mining.png`
- [ ] `industry_refinery.png`
- [ ] `industry_factory.png`
- [ ] `industry_agriculture.png`
- [ ] `industry_population.png`
- [ ] `industry_shipyard.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `industry_mining.png` | Mining colony | Vector, white | 128 x 128 |
| `industry_refinery.png` | Refinery | Vector, white | 128 x 128 |
| `industry_factory.png` | Factory | Vector, white | 128 x 128 |
| `industry_agriculture.png` | Agricultural system | Vector, white | 128 x 128 |
| `industry_population.png` | Population hub | Vector, white | 128 x 128 |
| `industry_shipyard.png` | Shipyard works | Vector, white | 128 x 128 |

## 4. Commodity and supply icons

Folder: `game\Assets\Art\Icons\` (12 files)

- [ ] `cargo_ore.png`
- [ ] `cargo_metals.png`
- [ ] `cargo_chemicals.png`
- [ ] `cargo_machinery.png`
- [ ] `cargo_consumer_goods.png`
- [ ] `cargo_food.png`
- [ ] `supply_fuel.png`
- [ ] `supply_food.png`
- [ ] `supply_water.png`
- [ ] `passenger.png`
- [ ] `crew.png`
- [ ] `credits.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `cargo_ore.png` | Ore | Vector, white | 128 x 128 |
| `cargo_metals.png` | Metals | Vector, white | 128 x 128 |
| `cargo_chemicals.png` | Chemicals | Vector, white | 128 x 128 |
| `cargo_machinery.png` | Machinery | Vector, white | 128 x 128 |
| `cargo_consumer_goods.png` | Consumer goods | Vector, white | 128 x 128 |
| `cargo_food.png` | Food (freight) | Vector, white | 128 x 128 |
| `supply_fuel.png` | Ship fuel | Vector, white | 128 x 128 |
| `supply_food.png` | Shipboard food rations (must look different from freight food) | Vector, white | 128 x 128 |
| `supply_water.png` | Drinking water | Vector, white | 128 x 128 |
| `passenger.png` | Passenger | Vector, white | 128 x 128 |
| `crew.png` | Crew member | Vector, white | 128 x 128 |
| `credits.png` | Money symbol for the game's currency | Vector, white | 128 x 128 |

## 5. Status and drive icons

Folder: `game\Assets\Art\Icons\` (8 files)

- [ ] `status_transit.png`
- [ ] `status_waiting.png`
- [ ] `status_idle.png`
- [ ] `status_insystem.png`
- [ ] `status_blocked.png`
- [ ] `drive_tier1.png`
- [ ] `drive_tier2.png`
- [ ] `drive_tier3.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `status_transit.png` | Ship in transit | Vector, white | 128 x 128 |
| `status_waiting.png` | Ship waiting for a load | Vector, white | 128 x 128 |
| `status_idle.png` | Ship idle and docked | Vector, white | 128 x 128 |
| `status_insystem.png` | Ship doing in-system transport | Vector, white | 128 x 128 |
| `status_blocked.png` | Ship blocked by a problem | Vector, white | 128 x 128 |
| `drive_tier1.png` | 1.5c drive badge | Vector, white | 128 x 128 |
| `drive_tier2.png` | 2.25c drive badge | Vector, white | 128 x 128 |
| `drive_tier3.png` | 3.75c drive badge | Vector, white | 128 x 128 |

## 6. Interface icons

Folder: `game\Assets\Art\UI\` (16 files)

- [ ] `pause.png`
- [ ] `play.png`
- [ ] `follow.png`
- [ ] `fleet.png`
- [ ] `notification.png`
- [ ] `warning.png`
- [ ] `route.png`
- [ ] `empty_leg.png`
- [ ] `provision.png`
- [ ] `dispatch.png`
- [ ] `receipt.png`
- [ ] `buy.png`
- [ ] `save.png`
- [ ] `settings.png`
- [ ] `close.png`
- [ ] `debug_skip.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `pause.png` | Pause | Vector, white | 64 x 64 |
| `play.png` | Play | Vector, white | 64 x 64 |
| `follow.png` | Follow selected ship | Vector, white | 64 x 64 |
| `fleet.png` | Open fleet list | Vector, white | 64 x 64 |
| `notification.png` | Notifications | Vector, white | 64 x 64 |
| `warning.png` | Warning marker for shortfalls and failures | Vector, white | 64 x 64 |
| `route.png` | Route / loop | Vector, white | 64 x 64 |
| `empty_leg.png` | Empty repositioning leg | Vector, white | 64 x 64 |
| `provision.png` | One-click provision | Vector, white | 64 x 64 |
| `dispatch.png` | Launch / dispatch | Vector, white | 64 x 64 |
| `receipt.png` | Delivery receipt | Vector, white | 64 x 64 |
| `buy.png` | Buy ship or drive | Vector, white | 64 x 64 |
| `save.png` | Save | Vector, white | 64 x 64 |
| `settings.png` | Settings | Vector, white | 64 x 64 |
| `close.png` | Close panel | Vector, white | 64 x 64 |
| `debug_skip.png` | Debug skip to next arrival (Editor and dev builds only) | Vector, white | 64 x 64 |

## 7. Branding

Folder: `game\Assets\Art\Branding\` (3 files)

- [ ] `logo.png`
- [ ] `app_icon.png`
- [ ] `title_backdrop.png`

| Name | What it is | Type | Size |
|---|---|---|---|
| `logo.png` | "spacetrade2" title logo for the title screen | Vector | 1600 x 600 |
| `app_icon.png` | Window and taskbar icon, must read clearly when tiny | Vector | 1024 x 1024 |
| `title_backdrop.png` | Title screen background | Raster | 1920 x 1080 |
