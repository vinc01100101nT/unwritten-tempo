---
status: "Ground tiles DONE for all 14 biomes; every biome is a row in the Biome Table with its own tiles, props, ground shape and cliffs switch, guarded by tools/check-biome-table.py. The per-map generator bakes a map from a recipe on that table; no generated map is in the game. The seamless world reads the table for its props and its four kept-biome towns."
owner: vince
last-updated: 2026-10-31
tags:
  - world-content
---

# Biomes — Ground Tiles and Props

What every biome looks like: which ground tile it lays as its main ground, which it sprinkles in patches, which it
uses for roads, and which props stand on it. The fourteen biomes and the element each one favours come from
the `world-authoring` skill; this page owns the LOOK.

## Where things stand

- **Ground tiles: done.** They live in `unity/Assets/1_Authored/Art/GroundTiles/`. Every biome has at least one tile
  of its own; each of the Biome Table's tiles covers 2.5 m of ground and repeats edge to edge.
- **The Biome Table** (`unity/Assets/3_Data/Recipes/BiomeTable.asset`) is the one place a biome's look lives: one row
  per biome with its main ground, patch and road tiles, its colour wash, how high its ground rises, how wide its hills
  are and how many levels it settles into, whether its maps have cliffs and what colour a cliff face is, and which
  props each prop category means in that biome (a category left out uses the whole pack list; a category listed with
  no props places nothing).
- **The seamless world `world` reads the table in two places** (`SeamlessWorldBuilder`): the **Grassland** row decides
  which prop categories the world places at all, at the sizes the field recipe's prop groups give; and the four towns
  that keep their biome's own look — Hazemere (Desert), Hilt (Coast), Cindrel (Volcanic) and Kharnel (Haunted) — take
  their trees and flowers from their own row. Every other town's props are the nature pack's, by its theme. The
  world's ground is the nature pack's painted ground, not the table's tiles: grass, dirt, stone, rock on the cliff
  faces, and sand on Desert and Coast land. Ground shape and cliffs in the world come from the world layout, not from
  the table (the `world-authoring` skill).
- **The per-map generator** bakes one map from a recipe (below). No map it made is in the game today.
- **The table's props are the temperate pack's:** trees (birch, oak, pine, willow, deciduous), plants (grass, rye,
  ferns, flowers, sunflower, one bush) and outdoor props (rocks, stones, mushrooms, tree and pine stumps). Five biomes
  have nothing that fits them — see the table.
- **Snow and Peaks need no snowy props:** snow weather covers the ground and every prop while it snows.

## The fourteen biomes

Tile names are shortened to their lowercase part; the full name carries its uses (see Tile names below).
✅ = a row in the Biome Table. **Needed** = props to make later, as a separate art task.

| Biome | Today | Main ground | Patches | Road | Props that fit now | Needed |
| --- | --- | --- | --- | --- | --- | --- |
| Grassland | ✅ | meadow_grass | soil | the recipe's own road (the field template's is soil — Grassland's row sets none) | oak, deciduous and birch trees, grass, rye, flowers, sunflower, bush, rocks, stones | — |
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
multiplier; 1 = as the template says). Set 2026-09-25 against how the real landscape looks:

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

- **Swamp** — standing puddles over the ground at full coverage, and water standing in its hollows as if the first
  30 seconds of heavy rain had just fallen, on every graphics quality setting (rain puddles elsewhere show on High
  only). Rain on a Swamp fills its hollows further like anywhere else. Walking through the water plays the splash
  footsteps.
- **Snow** — the snow blanket over the ground and every prop, at full, on every quality setting.
- **Sacred** — patchy, soft white clouds floating over the whole map at treetop height (between about 2 and 4.5
  metres above the ground), drifting slowly. Tree tops rise out of them and the ground shows through the gaps
  between patches; within about 6 metres of the point the camera looks at (you) the clouds are gone, and they fade
  back to full by about 10 metres, so you are never hidden while the far side of the map dissolves into cloud. The clouds fade in over about 2 seconds on arrival.
- **Every other biome** — nothing held; puddles and snow come only with the weather.

**A map shows these by the biome marker it carries.** The seamless world carries Grassland's, so none of the three
shows in the world today.

**Fog is not a weather and not a biome's: every map, in every weather, shows it at full** — near things stay clear
and the far edge of the map fades.

The levels live in `unity/Assets/3_Data/Resources/BiomeWeather.asset` (one row per biome). A map knows its biome by
its biome marker: the per-map generator stamps one into every map it bakes, and the world builder stamps Grassland's
into the world. Arriving on a map starts straight at its snow blanket rather than building up or melting.
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
| BASE_OR_PATCH_painted_grass | the nature pack's painted grass | the seamless world |
| BASE_OR_PATCH_OR_PATH_painted_dirt | the nature pack's painted dirt, also its roads | the seamless world |
| BASE_OR_PATCH_OR_PATH_painted_dirt_stone | painted dirt with stones | the seamless world |
| BASE_OR_PATCH_painted_sand | painted sand | the seamless world (Desert and Coast land) |
| BASE_OR_PATCH_OR_PATH_painted_rock | painted rock | the seamless world (cliff faces, cave rock) |

### Seam fixes still to do

Every tile was tested by laying four copies 2×2 and looking where they meet. Still to fix:

- **Edge blend** (soft textures — fade each edge into the opposite one): frozen_dirt, wet_sand, dune_sand, cooled_lava.
- **Redo in ChatGPT** (the blend would leave see-through ghost shapes): pebbles (also too big — about 22 cm against
  an 8 cm limit), lava_cracks (glowing cracks cut at the join). For a seam, a short "still not repeating properly"
  reply on the image has worked better than a long prompt.

## The per-map generator

The per-map generator bakes one map from a **map recipe** (`MapRecipe`): select the recipe and press **⚒ Generate
Map** in its inspector, and it writes `Assets/Scenes/<map name>.unity` (overwriting it), laying the ground, winding
the roads and scattering props from the recipe's biome row. The templates live in `unity/Assets/3_Data/Recipes/`
(`FieldRecipe`, `TownRecipe`, `DungeonRecipe`); **Tools ▸ unwritten ▸ World ▸ Create Biome Starter Recipes** stamps a
starter recipe per biome into `Recipes/Biomes/` without touching one already there. No map it bakes is in the game
today: the game ships only `Systems` and the seamless world.

- **Ground levels:** a ground height above 0 raises the ground into hills, with the collision on the same raised
  surface, so feet, props, doors and creatures all stand on them. With a level count of 2 or more the hills settle
  into that many flat levels joined by slopes, each level covering about the same share of the map; with 0 they are
  smooth rolling mounds. No slope is steeper than the biome's steepest slope: where the ground would be, the higher
  side's edge is cut back into a ramp of that slope, so the levels keep their heights. Shaped ground is drawn with its
  points 0.25 m apart (flat ground keeps 0.8 m), the shape is worked out on a 0.2 m grid, the ramps curve around each
  level, and the crease where a ramp meets a level is rounded over about half a metre, so a level's edge follows a
  smooth curve instead of a zigzag of the ground's triangles. Each biome's shape (a character is 0.5 m tall; levels
  0 = smooth mounds):

  | Biome | Height | Hills across | Levels | Steepest slope | Cliffs |
  | --- | --- | --- | --- | --- | --- |
  | Grassland | 1.2 m | 16 m | 3 | 20° | off |
  | Forest | 0.8 m | 12 m | 0 | 15° | off |
  | Highland | 1.8 m | 18 m | 4 | 25° | on |
  | Peaks | 2.4 m | 20 m | 4 | 30° | on |
  | Snow | 1.2 m | 20 m | 0 | 15° | off |
  | Steppe | 0.4 m | 30 m | 0 | 8° | off |
  | Coast | 1.0 m | 18 m | 2 | 20° | on |
  | Desert | 1.5 m | 14 m | 0 | 20° | off |
  | Volcanic | 1.8 m | 14 m | 3 | 25° | on |
  | Swamp | 0.3 m | 10 m | 0 | 8° | off |
  | Sacred | 1.2 m | 16 m | 3 | 20° | off |
  | Haunted | 0.8 m | 12 m | 2 | 20° | on |
  | Mist | 0.8 m | 14 m | 0 | 12° | off |
  | Corrupted | 1.5 m | 12 m | 3 | 30° | on |

  Players and creatures walk every one of those slopes by the same rule (up to 45°, see
  [Walking and Routing](../2-systems/walking-and-routing.md)). On a cliff map the levels go on the cliff tops (see
  Cliffs). The Map Painter (**Import Painted Map…**) does not carry levels, so a map imported through it is flat.
- **Cliffs:** a map with cliffs raises all the ground past a wall line about half the pass width from every road, and
  the cliff is that same ground, shaped — one surface and one collision for the pass floor, the rock face and the top,
  with the rock painted on it (nothing is laid over the ground, and the ground has no holes; see RULE ONEGROUND in the
  project rules). Going across a cliff from the road: a rounded foot where the pass floor curves up, starting 0.5 m in
  front of the wall line, gentle at the bottom and upright at the top, fading from the ground's colours into the rock;
  a carved face standing 0.3 m behind the wall line, cut back up to 0.7 m further into columns and blocks about 1–2 m
  wide and half a metre tall each at its own depth, with level ledges about 0.45 m tall, one every metre and broken off
  here and there, jutting forward almost to the face's line (going up, the face never leans back shallower than 50°,
  so no part of it is a slope anyone can walk up — only the meeting points are); and a rounded shoulder at the top
  that rolls back into the grass. The pass floor is flat, so everything in it can walk the whole pass, and each cliff
  stands its height plus the level of the ground behind it, so its edge rises and falls. No stretch is straight: the
  cliff's edge wanders up to 0.8 m in toward the road and out again (an 8 m pass is 6.4–9.6 m wide), every corner —
  where passes meet, where a road bends sharply — is rounded, and the rim swells up to 0.35 m. Rock also lines the
  map's own edge 1.5 m in, so a pass running to the edge ends in a curved cliff instead of opening onto nothing.
  **Whether a map has cliffs:** each biome's row has a cliffs switch (on for Highland, Peaks, Coast, Volcanic, Haunted
  and Corrupted), and a recipe can override it — follow the biome, on or off. How tall they stand, the pass width and
  the meeting points come from the recipe: the field template says 1.5 m, 8 m and 3. **Meeting points** (chosen at
  random along the pass, at least 15 m apart and 6 m in from the map's edge): there a cliff shrinks to nothing within
  1.5 m of the spot and back to full height by 10 m, and its high ground slopes down to meet the pass, so a player can
  walk up onto the top. The rock and the grass fade into each other over the shoulder and at the foot. Feet stand on
  the top, bodies stop against the carved face, and creatures chase up and down the meeting points like a player does.
  Nothing is placed in the band the cliff takes up. **The face's colour:** a biome with a cliff face colour in its row
  draws the face in shades of that colour — lighter toward the top, darker and greyer toward the foot, in patches
  about 2.5 m and 0.8 m across, split by thin dark cracks along the borders of rock cells about 0.9 m across and 2 m
  tall, only some lengths of them cracked. Each biome's colour is the average colour of its own earthy or rocky ground
  tile: Grassland brown #604623 (soil), Highland and Peaks grey #7B7C7E (gravel_scree), Coast pale grey-brown #A1958B
  (pebbles), Volcanic dark grey #3D3738 (cooled_lava), Haunted dark brown #41362D (grave_dirt), Corrupted near-black
  violet #2C2731 (blighted_soil). A biome with none draws the face with the gravel ground tile. The Map Painter does not
  carry cliffs.
- **Biome order:** a new biome is appended to the END of the biome list — inserting one in the middle would silently
  change every creature's biome. Every category the field template uses that a biome has nothing for is listed empty,
  so it places nothing (a Desert map gets no trees and no grass).
- **Per-biome checks:** `tools/check-biome-table.py` fails when a biome has no row or appears twice, a tile is used
  for a job its name does not allow, a row keeps a colour wash, a row has no saved cliffs switch, cliff face colour or
  ground shape, a prop points at no file, a prop entry has no saved count multiplier, a biome places no props at all,
  the Grassland row changes, or the held-weather list holds a biome twice or a level outside 0..1.
  `tools/prove-biome-table-check.py` plants each of those faults in a throwaway copy and proves the check catches it.

**Adding a biome** takes a table row and its name appended to the biome list (in `MapRecipe.cs`). The check above fails
until both are done.

**Open decisions:** whether props get a per-biome colour tint. A tint can shift a colour (green → olive or grey-green)
but cannot turn green into yellow or red, and the plants share one image, so tinting grass also tints the flowers.

## Related

- [Biome References — Real Places Beside Our Maps](biome-references.md) — a real photo of each biome beside a map
  the per-map generator made from its row.
