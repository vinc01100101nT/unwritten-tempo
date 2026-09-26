---
status: "Ground tiles DONE for all 14 biomes (33 tiles in the project). The biome → tile and biome → prop matching below was agreed by the user on 2026-09-25. Generator steps 1, 2, 4 and 5 DONE (2026-09-25): all 14 biomes are rows in the Biome Table, each on its own tiles, with a test map each and a check that guards the table; step 3, walking the test maps in the game, is the one step left."
owner: vince
last-updated: 2026-09-25
tags:
  - world-content
---

# Biomes — Ground Tiles and Props

What every biome looks like: which ground tile it lays as its main ground, which it sprinkles in patches, which it
uses for roads, and which props stand on it. The fourteen biomes and the element each one favours come from
[The World Map](world-map.md); this page owns the LOOK.

## Where things stand

- **Ground tiles: done.** 33 tiles live in `unity/Assets/1_Authored/Art/GroundTiles/`. Every biome has at least
  one tile of its own. Each tile covers 2.5 m of ground and repeats edge to edge.
- **In the map generator: all 14 biomes**, each laying its own tiles with no colour wash, and each with its own
  props from the table below. No world map uses the nine new ones yet; each has a test map (see step 3).
- **Props: temperate only.** 15 trees (birch, oak, pine, willow, deciduous), 19 plants (grass, rye, ferns, flowers,
  sunflower, one bush), and outdoor props (3 rocks, 3 stones, 5 mushrooms, tree and pine stumps; the rest are
  buildings, walls, carts and storage). Five biomes have nothing that fits them — see the table.
- **Snow and Peaks need no snowy props:** snow weather covers the ground and every prop while it snows.

## The fourteen biomes

Tile names are shortened to their lowercase part; the full name carries its uses (see Tile names below).
✅ = in the map generator. **Needed** = props to make later, as a separate art task.

| Biome | Today | Main ground | Patches | Road | Props that fit now | Needed |
| --- | --- | --- | --- | --- | --- | --- |
| Grassland | ✅ | meadow_grass | soil | dirt_road (field maps lay the field template's own road, soil — Grassland's row sets none) | oak, deciduous and birch trees, grass, rye, flowers, sunflower, bush, rocks, stones | — |
| Forest | ✅ | mud | leaf_litter | soil | all trees, ferns, bush, mushrooms, stumps, grass, rocks | — |
| Highland | ✅ | alpine_grass | gravel_scree | dirt_road | pines, birch, grass, rocks, stones | — |
| Peaks | ✅ | gravel_scree | snow | frozen_dirt | pine stumps, rocks, stones | — |
| Snow | ✅ | snow | frozen_dirt | gravel_scree | pines, pine stumps, rocks, stones | — |
| Steppe | ✅ | steppe_grass | cracked_earth | dirt_road | rye, rocks, stones | — |
| Coast | ✅ | beach_sand | wet_sand | pebbles | rocks, stones | **palm trees, driftwood, dune grass** |
| Desert | ✅ | dune_sand | cracked_earth | sandstone | rocks, stones | **cacti, dry shrubs, dead bushes** |
| Volcanic | ✅ | black_ash | lava_cracks | cooled_lava | rocks, stones | **lava rocks, charred trees** |
| Swamp | ✅ | marsh_grass | mud | soil | willows, stumps, ferns, mushrooms, grass | **reeds, cattails** |
| Sacred | ✅ | clover | meadow_grass | flagstone | birch, flowers, sunflower, grass | — |
| Haunted | ✅ | dead_grass | grave_dirt | dark_cobble | stumps, mushrooms, rocks, stones | **dead trees, gravestones** |
| Mist | ✅ | mist_moss | grey_silt | soil | willows, ferns, mushrooms | — |
| Corrupted | ✅ | blighted_grass | blighted_soil | dark_cobble | stumps, rocks, stones | **twisted dead trees** |

**How many of each** — a biome row can place more or fewer of a category than the field template does (its count
multiplier; 1 = as the template says). Set 2026-09-25 by looking at every test map against how the real landscape
looks:

- **Grassland** — trees ×0.4, grass ×0.35, flowers ×1.5, no landmarks: an open meadow with a few trees, and no
  buildings standing in the middle of the field.
- **Forest** — grass ×0.2, ferns, mushrooms and the bush ×2: leaves and ferns on the floor instead of tall grass.
- **Highland** — trees ×0.3, grass ×0.25, rocks ×3: a near-treeless moor of short turf and boulders.
- **Peaks** — no trees, rocks ×5: above the treeline, a bare boulder field.
- **Snow** — rocks ×1.5: a snowy pine forest with frozen-earth patches and a gravel road.
- **Steppe** — grass is rye alone, ×0.3: sparse golden tussocks with bare ground between them.
- **Coast** — rocks ×3. **Desert** — rocks ×0.5: open sand with a few stones. **Volcanic** — rocks ×4: a boulder
  field over black ash.
- **Swamp** — trees are willows and dead stumps together, ×0.8; grass ×0.6; ferns and mushrooms ×1.5.
- **Mist** — trees ×0.8, ferns and mushrooms ×1.5.
- **Sacred** — trees ×0.5, flowers ×4, grass ×0.7: an open birch meadow in flower.
- **Haunted** — trees are the two stumps, mushrooms ×2. **Corrupted** — trees are the two stumps, rocks ×1.5.

Until the needed props exist, each biome places only what its "Props that fit now" column lists; Coast, Desert and
Volcanic get rocks and stones alone. Concept-art prompts already exist for four of
them in [GPT Concept Prompts](../4-art/gpt-concept-prompts.md): dead / bare tree (26), palm tree (27), cattails /
reeds (32) and cactus (33).

## Weather a biome always has

Some biomes keep a weather look whatever the sky is doing (the user's call, 2026-09-25). It only ever adds: the day's
weather can still bring more on top, and rain still brings its falling drops and rings.

- **Swamp** — standing puddles over the ground at full coverage, on every graphics quality setting (rain puddles
  elsewhere show on High only). Walking through them plays the splash footsteps.
- **Snow** — the snow blanket over the ground and every prop, at full, on every quality setting.
- **Mist** — fog at 0.6 of the Fog weather's thickness: near things stay clear, the far edge of the map fades.
- **Sacred** — patchy, soft white clouds floating over the whole map at treetop height (between about 2 and 4.5
  metres above the ground), drifting slowly. Tree tops rise out of them and the ground shows through the gaps
  between patches; within about 6 metres of the point the camera looks at (you) the clouds are gone, and they fade
  back to full by about 10 metres, so you are never hidden while the far side of the map dissolves into cloud. Fog at
  0.35 of the Fog weather's thickness hazes the distance on top. The clouds fade in over about 2 seconds on arrival.
- **Every other biome** — nothing held; puddles, snow and fog come only with the weather.

The levels live in `unity/Assets/3_Data/Resources/BiomeWeather.asset` (one row per biome that holds something). A map
knows its biome because the generator stamps a biome marker into every map it builds; maps built before that carry
none and hold nothing. Arriving on a map starts straight at its snow blanket rather than building up or melting. Fog shows at the held level
the moment the map loads.
**Parked (the user, 2026-09-25):** how often each kind of weather comes per biome — snow maps often snowing,
grassland often sunny.

## Tile names

A tile's file name is an UPPERCASE list of every way it can be laid — BASE (main ground), PATCH (sprinkled
patches), PATH (roads), SIDEWALK, CURB, CORNER — joined by `_OR_`, then its lowercase name. The rule lives in
`unity/Assets/ASSETS.md` (golden rule 11).

| Tile | What it is | Biomes |
| --- | --- | --- |
| BASE_OR_PATCH_meadow_grass | short green grass, a few tiny flowers | Grassland, Sacred |
| BASE_OR_PATCH_OR_PATH_soil | brown soil with grass bits | Grassland, Forest, Swamp, Mist |
| PATH_dirt_road | packed yellow-brown dirt road | Grassland, Highland, Steppe |
| BASE_OR_PATCH_leaf_litter | fallen autumn leaves (older, semi-realistic style) | Forest |
| BASE_OR_PATCH_OR_PATH_mud | wet brown mud | Forest, Swamp |
| BASE_OR_PATCH_alpine_grass | short cropped mountain turf | Highland |
| BASE_OR_PATCH_OR_PATH_gravel_scree | loose grey stones and grit | Highland, Peaks, Snow |
| BASE_OR_PATCH_snow | wind-packed snow | Peaks, Snow |
| BASE_OR_PATCH_ice | frozen ice sheet with cracks | none — laid over a third of a snow map it reads as a bright frozen lake everywhere |
| BASE_OR_PATCH_OR_PATH_frozen_dirt | frozen earth with frost and thin snow | Peaks, Snow |
| BASE_OR_PATCH_steppe_grass | short dry golden-green turf | Steppe |
| BASE_OR_PATCH_OR_PATH_cracked_earth | sun-baked clay plates | Steppe, Desert |
| BASE_OR_PATCH_OR_PATH_beach_sand | pale sand with a few shells | Coast |
| BASE_OR_PATCH_wet_sand | damp tide sand | Coast |
| BASE_OR_PATCH_OR_PATH_pebbles | rounded shore pebbles | Coast |
| BASE_OR_PATCH_dune_sand | orange desert sand with ripples | Desert |
| PATH_OR_SIDEWALK_sandstone | sandstone flagstones | Desert |
| BASE_OR_PATCH_OR_PATH_black_ash | volcanic ash with a few embers | Volcanic |
| PATCH_lava_cracks | black crust with glowing cracks | Volcanic |
| BASE_OR_PATCH_OR_PATH_cooled_lava | smooth black basalt | Volcanic |
| BASE_OR_PATCH_marsh_grass | flattened reedy grass over mud | Swamp |
| BASE_OR_PATCH_clover | clover carpet | Sacred |
| BASE_OR_PATCH_dead_grass | withered flattened grass | Haunted |
| BASE_OR_PATCH_OR_PATH_grave_dirt | churned dark soil, a few bones and leaves | Haunted |
| PATH_OR_SIDEWALK_dark_cobble | old dark cobblestones | Haunted, Corrupted |
| BASE_OR_PATCH_mist_moss | flat pale grey-green moss | Mist |
| BASE_OR_PATCH_OR_PATH_grey_silt | damp blue-grey earth | Mist |
| BASE_OR_PATCH_blighted_grass | withered violet-grey grass | Corrupted |
| BASE_OR_PATCH_OR_PATH_blighted_soil | purple-black soil with dark cracks | Corrupted |
| PATH_OR_SIDEWALK_flagstone | grey stone flagstones | towns; Sacred roads |
| BASE_OR_SIDEWALK_plaza | pale cobbles in fan circles | towns |
| CURB_blocks | curb strip | towns |
| CORNER_curb_slab | curb corner stone | towns |

### Seam fixes still to do

Every tile was tested by laying four copies 2×2 and looking where they meet. Still to fix:

- **Edge blend** (soft textures — fade each edge into the opposite one): frozen_dirt, wet_sand, dune_sand, cooled_lava.
- **Redo in ChatGPT** (the blend would leave see-through ghost shapes): pebbles (also too big — about 22 cm against
  an 8 cm limit), lava_cracks (glowing cracks cut at the join). For a seam, a short "still not repeating properly"
  reply on the image has worked better than a long prompt.

## The all-biome map generator — everything but the in-game test done

The map generator lays the ground, winds roads to each door and scatters props, from the existing **⚒ Generate Map**
button (on a map's recipe) and the World Creator's **⚒ Build World** button. It was extended to every biome in the
user's five steps:

1. **Planning** — done (2026-09-25).
2. **One biome first: Forest** — done (2026-09-25).
   - **The Biome Table** (`unity/Assets/3_Data/Recipes/BiomeTable.asset`) is the one place a biome's look lives: one
     row per biome with its main ground, patch and road tiles, its colour wash, and which props each prop category
     means in that biome (a category left out uses the whole pack list; a category listed with no props places
     nothing). A map's own road choice in the World Creator still wins over the biome's road.
   - Forest's row: mud main ground, leaf-litter patches, soil road; "Flowers" means ferns, mushrooms and the bush;
     "Landmarks" means the two tree stumps.
   - Trees in the field template are scattered over the whole map (they used to stand only in a ring on the map's
     edge) — changed 2026-09-25 at the user's instruction, so every field map generated from now on has them.
   - Grass in the field template grows in **bunches** (2026-09-25, the user's instruction): each bunch is one or more
     touching 2.5 m ground-tile cells in any shape, filled densely, never on a road, growing under trees and around
     rocks. 50 bunches of up to 24 cells each cover about three quarters of a 60 m map (measured 73% and 77% on
     two Forest test maps), with about 11 plants in every grassy cell — 4,700 and 5,200 grass plants per map, each at
     full size instead of 30%. The frame-rate cost of that many plants has not been measured yet.
   - **Every biome uses the same patch amount, 0.9** (the user's call, 2026-09-25): patches cover about a third of
     the ground (33–44% measured on the Forest maps). The map's own amount laid only a few small spots. **Volcanic is
     the one exception, at 0.5**, because its patches are the glowing lava cracks and real lava glows only in thin
     lines (approved 2026-09-25 as one of the reference-sheet wins).
3. **Testing in the game** — NOT DONE. Thirteen linked test maps, one per biome except Grassland, wait to be
   walked: `test_forest_a` ↔ `test_highland` ↔ `test_peaks` ↔ `test_snow` ↔ `test_steppe` ↔ `test_coast` ↔ `test_desert` ↔ `test_volcanic` ↔
   `test_swamp` ↔ `test_sacred` ↔ `test_haunted` ↔ `test_mist` ↔ `test_corrupted`, each 60 m, joined west to east by
   one door each way. Each has the same creatures as `test_forest_a` (32 Grassland normals and commons, levels 1–99,
   within 25 m of the centre), because no creature belongs to any of the nine new biomes yet.
4. **All biomes** — done (2026-09-25). The nine new biomes (Highland, Peaks, Steppe, Coast, Swamp, Sacred, Haunted,
   Mist, and Forest before them) were appended to the END of the biome list — inserting one in the middle would
   silently change every creature's biome — and Snow, Desert, Volcanic and Corrupted moved off the general tiles and
   colour wash onto their own tiles. Each row places exactly the props in the table above; every category the field
   template uses that a biome has nothing for is listed empty, so it places nothing (a Desert map gets no trees and
   no grass).
5. **Per-biome checks** — done (2026-09-25). `tools/check-biome-table.py` fails when a biome has no row, a tile is
   used for a job its name does not allow, a row keeps a colour wash, a prop points at no file, a biome places no
   props at all, a prop entry has no saved count multiplier, the Grassland row changes, the browser World Creator is
   missing a biome, a test map's saved prop positions point at a part the prop no longer has, a test map is missing
   its biome stamp or carries the wrong one, or the held-weather list holds a level outside 0..1. It runs before every commit; `tools/prove-biome-table-check.py`
   plants each of those faults in a throwaway copy and proves the check catches it.

**Adding a biome** takes a table row, its name appended to the biome list, and its name in the browser World
Creator's biome list (that page cannot read the table). The check above fails until all three are done.

**Known problem the generator must not repeat:** in every existing map except lastline, all saved prop positions point
at a part of the prop that no longer exists, so every prop sits at the map centre (measured 2026-09-25: 40 maps).
A freshly generated map does not have this problem.

**Open decisions:** whether props get a per-biome colour tint. A tint can shift a colour (green → olive or grey-green)
but cannot turn green into yellow or red, and all 19 plants share one image, so tinting grass also tints the flowers.

## Related

- [Biome References — Real Places Beside Our Maps](biome-references.md) — a real photo of each biome beside its
  test map.
- [The World Map — Twelve Homelands](world-map.md) — the fourteen biomes, the element each favours, and which
  homeland map uses which biome.
- [Creatures and Where They Live](biome-mob-families.md) — which creatures live in each map, and each creature's biome, element and level.
- [World Creator — Spec](world-creator-spec.md) — the tool that builds a world of linked maps.
