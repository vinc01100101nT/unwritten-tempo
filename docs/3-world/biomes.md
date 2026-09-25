---
status: "Ground tiles DONE for all 14 biomes (33 tiles in the project). The biome → tile and biome → prop matching below is the assistant's proposal, not yet agreed. Generator step 2 DONE (2026-09-25): biome looks live in one Biome Table, and Forest is built; step 3 (testing in the game) is next. The other eight new biomes are not in the game yet."
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
- **In the game today: 6 biomes** — Grassland, Snow, Desert, Volcanic, Corrupted and Forest. Snow, Desert, Volcanic
  and Corrupted still paint a few general tiles with a colour wash; **only Forest uses its own new tiles so far**.
- **Props: temperate only.** 15 trees (birch, oak, pine, willow, deciduous), 19 plants (grass, rye, ferns, flowers,
  sunflower, one bush), and outdoor props (3 rocks, 3 stones, 5 mushrooms, tree and pine stumps; the rest are
  buildings, walls, carts and storage). Five biomes have nothing that fits them — see the table.
- **Snow and Peaks need no snowy props:** snow weather covers the ground and every prop while it snows.

## The fourteen biomes

Tile names are shortened to their lowercase part; the full name carries its uses (see Tile names below).
✅ = in the game today. **Needed** = props to make later, as a separate art task.

| Biome | Today | Main ground | Patches | Road | Props that fit now | Needed |
| --- | --- | --- | --- | --- | --- | --- |
| Grassland | ✅ | meadow_grass | soil | dirt_road | oak, deciduous and birch trees, grass, rye, flowers, sunflower, bush, rocks, stones | — |
| Forest | ✅ | mud | leaf_litter | soil | all trees, ferns, bush, mushrooms, stumps, grass, rocks | — |
| Highland | | alpine_grass | gravel_scree | dirt_road | pines, birch, grass, rocks, stones | — |
| Peaks | | gravel_scree | snow | frozen_dirt | pines, pine stumps, rocks, stones | — |
| Snow | ✅ | snow | ice | frozen_dirt | pines, pine stumps, rocks, stones | — |
| Steppe | | steppe_grass | cracked_earth | dirt_road | grass, rye, rocks, stones | — |
| Coast | | beach_sand | wet_sand | pebbles | rocks, stones | **palm trees, driftwood, dune grass** |
| Desert | ✅ | dune_sand | cracked_earth | sandstone | rocks, stones | **cacti, dry shrubs, dead bushes** |
| Volcanic | ✅ | black_ash | lava_cracks | cooled_lava | rocks, stones | **lava rocks, charred trees** |
| Swamp | | marsh_grass | mud | soil | willows, ferns, mushrooms, grass | **reeds, cattails** |
| Sacred | | clover | meadow_grass | flagstone | birch, flowers, sunflower, grass | — |
| Haunted | | dead_grass | grave_dirt | dark_cobble | stumps, mushrooms, rocks, stones | **dead trees, gravestones** |
| Mist | | mist_moss | grey_silt | soil | willows, ferns, mushrooms | — |
| Corrupted | ✅ | blighted_grass | blighted_soil | dark_cobble | stumps, rocks, stones | **twisted dead trees** |

Until the needed props exist, those biomes get rocks and stones only. Concept-art prompts already exist for four of
them in [GPT Concept Prompts](../4-art/gpt-concept-prompts.md): dead / bare tree (26), palm tree (27), cattails /
reeds (32) and cactus (33).

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
| BASE_OR_PATCH_OR_PATH_gravel_scree | loose grey stones and grit | Highland, Peaks |
| BASE_OR_PATCH_snow | wind-packed snow | Peaks, Snow |
| BASE_OR_PATCH_ice | frozen ice sheet with cracks | Snow |
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

## The all-biome map generator — step 2 of 5 done

The map generator lays the ground, winds roads to each door and scatters props, from the existing **⚒ Generate Map**
button (on a map's recipe) and the World Creator's **⚒ Build World** button. It is being extended to every biome, in
the user's five steps:

1. **Planning** — done (2026-09-25).
2. **One biome first: Forest** — done (2026-09-25).
   - **The Biome Table** (`unity/Assets/3_Data/Recipes/BiomeTable.asset`) is the one place a biome's look lives: one
     row per biome with its main ground, patch and road tiles, its colour wash, and which props each prop category
     means in that biome (a category left out uses the whole pack list; a category listed with no props places
     nothing). A map's own road choice in the World Creator still wins over the biome's road.
   - The five old biomes were moved into it unchanged: a Grassland and a Snow map generated with the same seed
     before and after came out identical (same props, same ground, same painter file).
   - Forest was added at the END of the biome list — inserting it in the middle would silently change every
     creature's biome. Its row: mud main ground, leaf-litter patches, soil road; "Flowers" means ferns, mushrooms and the bush;
     "Landmarks" means the two tree stumps. No creature belongs to Forest yet, so a Forest spawn area draws only the
     creatures that fit every biome.
   - `test_forest_a` has creatures: field_11's spawn row copied into the world spawn list (32 Grassland normals and
     commons, levels 1–99, within 25 m of the centre), since no creature belongs to Forest yet.
   - Adding a biome now takes a table row, its name appended to the biome list, and its name in the browser World
     Creator's biome list (that page cannot read the table).
   - Trees in the field template are scattered over the whole map (they used to stand only in a ring on the map's
     edge) — changed 2026-09-25 at the user's instruction, so every field map generated from now on has them.
   - Grass in the field template grows in **bunches** (2026-09-25, the user's instruction): each bunch is one or more
     touching 2.5 m ground-tile cells in any shape, filled densely, never on a road, growing under trees and around
     rocks. 50 bunches of up to 24 cells each cover about three quarters of a 60 m map (measured 73% and 77% on the
     two Forest test maps), with about 11 plants in every grassy cell — 4,700 and 5,200 grass plants per map, each at
     full size instead of 30%. The frame-rate cost of that many plants has not been measured yet.
   - A biome row can set its own **patch amount**. Forest uses 0.9, which lays leaf-litter patches over about
     33–44% of its mud ground; the other biomes keep their map's own amount.
3. **Testing** — two linked Forest test maps, `test_forest_a` and `test_forest_b`, are built with a door between
   them; checked from the editor so far, not yet in the game.
4. **All biomes** — the four old biomes switch to their own tiles; the nine new biomes are added as table rows.
5. **Per-biome checks** — every tile is used only for a job its name allows, no leftover colour wash on dedicated
   tiles, no empty prop list, no props stacked at the map centre, Grassland unchanged.

**Known problem the generator must not repeat:** in every existing map except town_0, all saved prop positions point
at a part of the prop that no longer exists, so every prop sits at the map centre (measured 2026-09-25: 40 maps).
A freshly generated map does not have this problem.

**Open decisions:** whether props get a per-biome colour tint. A tint can shift a colour (green → olive or grey-green)
but cannot turn green into yellow or red, and all 19 plants share one image, so tinting grass also tints the flowers.

## Related

- [The World Map — Twelve Homelands](world-map.md) — the fourteen biomes, the element each favours, and which
  homeland map uses which biome.
- [Biome Mob Families](biome-mob-families.md) — how a spawn area picks its creatures by biome and level.
- [World Creator — Spec](world-creator-spec.md) — the tool that builds a world of linked maps.
