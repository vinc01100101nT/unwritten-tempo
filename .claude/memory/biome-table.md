---
name: biome-table
description: What a biome LOOKS like (ground tiles, tint, patch amount, road, props per category) lives only in the BiomeTable asset; adding a biome = one row + append to Biome enum + the web World Creator list
metadata:
  type: project
---

**Built 2026-09-25.** `Assets/3_Data/Recipes/BiomeTable.asset` (class `Scripts/Editor/BiomeTable.cs`) is the one
place the map generator reads a biome's look from: `baseTile`, `patchTile`, `roadTile` (empty = the recipe's own
`pathTile`; a road set on a map in the World Creator still wins), `tint`, `patchAmount` (<0 = recipe's `dirtAmount`),
and `props` = which prefabs a `PropCategory` means in that biome (category absent = whole pack list; listed with no
prefabs = places nothing). It replaced the coded `RecipeGenerator.BiomeOf` switch; a missing table/row/tile fails the
bake with a dialog, never a silent fallback.

- **Adding a biome:** one table row + append its name to the END of `Biome` (inserting mid-list remaps every saved
  creature's biome — see [[biome-none-agnostic]]) + add it to `BIOMES` in `tools/world-creator/index.html` (that page
  can't read the table). `WorldCreator.TryParseBiome` accepts any enum name.
- **Proof it moved values unchanged:** Grassland and Snow maps generated with seed 4242 before/after were identical
  (735 scene objects, same ground mesh/material/sidecar).
- **Forest (2026-09-25):** mud base, leaf-litter patches (patchAmount 0.9 ≈ 33–44% leaves), soil road; Flowers =
  ferns/mushrooms/bush, Landmarks = tree stumps. No creature has biome Forest yet.
- **Field template (FieldRecipe.asset) changed the same day, affecting every field map generated after:** trees
  Scatter over the whole map (were an EdgeRing), grass uses the new `PlacementRule.Clumps` (bunches of touching
  2.5 m cells; 50 bunches ≤24 cells, 18 tries/cell, spacing 0.2, scale 1.3 → ~75% cover, ~11 plants/cell, ~5k
  plants on a 60 m map — frame cost NOT measured).
- **All 14 biomes built 2026-09-25:** Highland, Peaks, Steppe, Coast, Swamp, Sacred, Haunted, Mist appended after
  Forest; Snow/Desert/Volcanic/Corrupted moved onto their own tiles, tint white. Every non-Grassland/Forest row lists
  all five field-template categories (Landmarks, Trees, Rocks, Flowers, Grass), empty = places nothing. Grassland +
  Forest proven identical before/after (seed 4242, scene lines equal once random fileIDs are masked).
- **Test maps:** a 13-map chain `test_forest_a` ↔ `test_highland` … ↔ `test_corrupted` (test_forest_b deleted 2026-09-25 at the user's word), 60 m each,
  W/E doors (`Portal_w{k}_to_*`), each with a copy of test_forest_a's WorldSpawns row. Not walked in game yet.
- **Building without touching the user's editor:** `RecipeGenerator.Generate(r, exits, mapPicksRoad, leaveOpen:false)`
  saves and CLOSES the new map and restores the active scene (refuses if that map is open). Wire doors by opening
  additively + SetActiveScene first (else MakePortal's objects land in the user's scene), save, close. `unity cmd eval`
  answers "timed out after 5000ms" on long builds but the build keeps running — write progress to a file and poll.
- **Every row patchAmount 0.9 except Volcanic 0.5** (thin lava glow) (user, 2026-09-25: the recipe's own 0.18 painted "one cell in a zone"). Each
  `BiomeProps` entry has `countMultiplier` (× the recipe's count for that category; 1 = as-is; applies only when the
  pool came from the biome's list). Values + reasons live in docs/3-world/biomes.md "How many of each". Snow now uses
  frozen_dirt patches + gravel_scree road (ice at 0.9 read as a frozen lake); Steppe grass = Rye only; Haunted and
  Corrupted Trees = the two stumps. Forest re-proven identical after the multiplier code.
- **Weather a biome always has (runtime, 2026-09-25):** `Resources/BiomeWeather.asset` (class `BiomeWeather`) — per
  biome `alwaysPuddles` / `alwaysSnowCover` 0..1; Swamp puddles 1, Snow blanket 1, Mist fog 0.6 (`alwaysFog`, read by FogOverlay). `WetGround`/`SnowGround` show
  `max(weather, held)`; held puddles draw on EVERY tier (rain puddles stay High-only). The map's biome comes from a
  `MapBiome` marker the generator stamps into each map (`MapBiome.Current`); old maps have none → nothing held.
  SnowGround snaps its blanket on a map change. The editor-only BiomeTable can't hold these (runtime can't read it).
- **Guard:** `tools/check-biome-table.py` (+ `prove-biome-table-check.py`), in the pre-commit check.
- Design + per-biome tile/prop tables: `docs/3-world/biomes.md`. See [[recipe-generator]].
