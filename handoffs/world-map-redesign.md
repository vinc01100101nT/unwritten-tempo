# Handoff — World map redesign, ground tiles, and the stacked-props bug

**Task:** redesign the whole world around the twelve shadows' homelands (only `town_0` survives), make the ground
tiles for every biome, and fix a bug found on the way: every prop in field_11 is stacked at the map centre.

**Status:** IN PROGRESS (session of 2026-09-24/25 committed and pushed to main). All 26 new ground tiles are in and
renamed by use; the all-biome map generator is at step 2 of 5 (Forest built, not yet walked in the game). The world
design is half-agreed, and its doc is partly STALE (see below). The stacked-props bug is NOT FIXED; it is measured
in 40 of the 42 scenes that save prop positions (only town_0 and Systems are clean).

---

## What's done

- **Ground tiles: one home, one lookup, one import rule** (committed `12e83e70`).
  - Every tile lives only in `unity/Assets/1_Authored/Art/GroundTiles/` (11 .png tiles committed; on disk now 33,
    uncommitted: 4 removed by the user, 26 new ones added, and all renamed by use on 2026-09-25 — see item 3).
  - Code reaches a tile only through `GroundTiles.Load(name)` (`Scripts/Editor/GroundTiles.cs`). Used by
    `GroundBlendBuilder.GroundBaseMap` + curb materials, and `BrushPaletteWindow.FindGroundAsset/FindGroundTexture`.
  - Import rule (`PixelArtImportPostprocessor.cs`, `/GroundTiles/` branch): 1024 max, compressed, no alpha,
    repeat, mipmaps, npot ToNearest.
  - `tools/check-asset-rules.py` fails a tile that isn't .png / 1024 / alpha-free, and any non-tile file in the
    folder. Texture compression is now checked across all of `Assets/` except `0_Raw` and `Mirror`. The checker
    runs in the pre-commit check. Its prover (`tools/prove-asset-rules-check.py`) catches all 19 planted faults.
  - Written as golden rule 11 in `unity/Assets/ASSETS.md`.
  - Removed: the duplicate stones texture, the 5 `Props/Grounds` tile prefabs, the `Fill Ground Under Map` tool
    (`GroundFill.cs`), and the unused `prontera_tile.mat`.
  - World map atlas import cap 4096 → 2048 (`WorldAtlasBaker.cs` now uses `TargetLongSidePx`). No visible change.
- **New tiles:** `ground_leaf_litter.png` (committed). `ground_moss.png` was committed too, then removed by the user.
- **World map proposal:** `docs/3-world/world-map.md` (committed). **See the STALE warning under What's left.**
- **Plan and roadmap:** all built V4 tasks marked done. The wiki Roadmap's Now is "Killing things pays you"
  (only its drop-rate play-test is open). Stale tasks were deleted or rewritten.
- **Files in `~/Downloads`:**
  - `world-map-base.png`: the chosen ChatGPT world map, clean, 1536×1024.
  - `world-map-homelands.png`: the same map marked with which homeland goes where.
  - `world-layout.png`: 88 zones and 92 warps drawn on the map.
  - `world-homelands.json`: that layout as an importable world-creator file. The background aligns at 0,0,
    1536×1024 world units.
  - `world-layout-script.py`: the script that generated both layout files. It runs under Pillow and needs its
    paths edited.

## State left in the project (committed 2026-09-25)

- `unity/Assets/Scenes/field_11.unity`: the user's approved **grass sample**, a root `Grass_Sample` of 70 plant
  props placed around (8, 0, 0) near the town_0 arrival point. Saving the scene also made Unity drop 43 leftover
  scale, material and name override lines. No position data was lost.
- `unity/Assets/1_Authored/Prefabs/VFX/Textures/Circle2.png.meta`: the editor keeps flipping this texture's import
  settings (sprite↔default, compression). The cause is not investigated.
- **Tile test squares in field_11 (scaffolding, delete when the tiles are final):** `Tile_1_Test`…`Tile_12_Test`,
  5 m squares (each shows its tile 2×2) in rows of three at x −20..−5 — tiles 1–3 at z 12.5..17.5, 4–6 below,
  7–9 below that, 10–12 at z −2.5..2.5 — plus `Pebble_Test` (7.5 m) at (10, 0.01, 9). Their images are
  the renamed tiles (the materials follow the rename, they link by asset ID); their materials are
  `2_Generated/Materials/Ground_tile_N_test.mat` and `Ground_pebble_test.mat`. Tiles 13–22 have no square.
- **The 40 map-ground materials and the brush palette that pointed at the removed grass/rock/stones tiles were
  repointed on 2026-09-25** (the commit check refuses broken references): grass → meadow_grass, rock → gravel_scree,
  stones → dark_cobble — the same mapping the code's name lookups use. Old maps now show those tiles. The cleanup on
  2026-09-25 removed 11 materials nothing used (5 old prop ground materials, plus 6 map-ground materials no scene
  uses: Field, Prontera, SampleElevation, StarterTown, Systems, Town) and the props model's 5 remap entries to them. The map-painter files' grass brush id was renamed to `base_or_patch_meadow_grass`
  to match.
- **field_11's ground base is the leaf-litter tile** (a test the user asked for; committed in `Ground_field_11_blend.mat`).
  The user liked it blended with the soil path. Their idea: use leaves only in patches (base → grass, leaves → the
  `_DirtMap` patch slot). **The user hasn't decided.**

## What's left

1. **Rewrite `docs/3-world/world-map.md`: it is STALE on layout.** It still describes the CLOCK-FACE ring
   (hours, spokes, homelands bordering in level order). **The user rejected the clock**: it felt small and
   isolated. The agreed direction is Ragnarok-style: several irregular landmasses, towns at random spots, wild
   fields between homelands, remote islands, and levels as a rough gradient with no fixed walking order. The
   chosen map is `~/Downloads/world-map-base.png`; the homeland placement proposed on it is
   `world-map-homelands.png`:
   - Main continent: Roman (start), Irish, Slavic.
   - East islands: Greek.
   - North island: Norse and German.
   - West continent: Spanish, Persian, Mongol.
   - South: Cornish peninsula and Italian lagoon.
   - Far top-left island: Japanese.
   - The big dark south-east island is unassigned.
   - Flagged: the Mongol steppe (84–91) touches Lastline's heartland.
   - **Still agreed and correct in the doc:** 12 homelands, one per shadow; the shadows ARE the Twelve and keep
     their names; each homeland has its own language, a town (smith/loom/tannery), 3 fields, a dungeon, the
     shadow's iconic place (12 place names agreed) and a castle map (castle gameplay deferred); biome table and
     per-map biomes; all 10 elements spread across creatures with one shadow Neutral (Varro); Holy/Shadow/Poison
     countered via skill buffs and cards (not built); teleports and service NPCs deferred.
2. **Homeland names are NOT settled.** The rule, saved in memory `world-names-tie-to-shadows-and-weapons`: every
   name ties to its shadow AND its weapon, in Ragnarok-city style, 1–4 syllables, with some one-off multi-word
   formats like "Al De Baran". The last proposal was Brand, Kil Brigh, Khar Num, Zweiburg, Hien, Scutaro, Rad
   Molot, Porthwenna, Asazar, Kallidora, Santa Inés, Val di Fiora. The user said "plan names later", and the map
   is kept label-free until then. The doc still carries the older names (Testudo, Tír na mBeo…); replace them
   once names are chosen.
3. **Ground tiles for every biome** — the per-biome tile and prop tables now live in `docs/3-world/biomes.md`
   (the single home; update it, not this list). **22 tiles, made in ChatGPT in PURE ANIME style.** The user removed
   `ground_grass`, `ground_rock`, `ground_moss` and `ground_stones` (they hated the look); `ground_leaf_litter`
   stays but is semi-realistic, so it may be redone. The 22, numbered as the user saved them in `~/Downloads`
   (`tile_N.png`):
   1 gravel scree · 2 short alpine grass · 3 snow · 4 ice · 5 frozen dirt · 6 dry steppe grass · 7 beach sand ·
   8 wet sand · 9 pebbles · 10 dune sand · 11 cracked dry earth · 12 sandstone paving · 13 black ash ·
   14 cooled lava rock · 15 glowing lava cracks · 16 mud · 17 marsh grass · 18 clover grass · 19 dead grass ·
   20 grave dirt · 21 dark cobble · 22 meadow grass (replaces the removed grass; Grassland had nothing else).
   The user keeps the snow tile: snow weather is winter-only and melts, so snow maps need their own ground.

   **Prompt lessons (ChatGPT ignored every one of these at least once):** give every size in cm AND as a fraction
   of the image width or a count across it, stated as a MAXIMUM (metres alone came back 3–5× too big); give an
   exact TOTAL count for every scattered thing and ban everything else ("a light scatter" came back as hundreds of
   shells and stones); ban stripes/bands; spell out what a perfect repeat means and keep big shapes 1/10 of the
   width from the edges; end each tile line with a one-line rule reminder. The rules prompt and tile lines live
   only in the chat of 2026-09-24/25; rebuild them from these lessons if lost. **Fixing a seam:** a long
   "fix only the edges" prompt failed on 9, 10 and 11 (ChatGPT redrew a new picture each time, joins stayed
   3–5×); a short plain reply on the image, "still not repeating properly", fixed tile 11 (join 4.0× → 1.8×).
   Try the short reply first.

   **Naming (the user's rule, 2026-09-25):** an UPPERCASE prefix of every use — BASE, PATCH, PATH, SIDEWALK, CURB,
   CORNER — joined by `_OR_`, then the lowercase name (written into ASSETS.md rule 11). All 29 tiles renamed:
   1 `BASE_OR_PATCH_OR_PATH_gravel_scree` · 2 `BASE_OR_PATCH_alpine_grass` · 3 `BASE_OR_PATCH_snow` ·
   4 `BASE_OR_PATCH_ice` · 5 `BASE_OR_PATCH_OR_PATH_frozen_dirt` · 6 `BASE_OR_PATCH_steppe_grass` ·
   7 `BASE_OR_PATCH_OR_PATH_beach_sand` · 8 `BASE_OR_PATCH_wet_sand` · 9 `BASE_OR_PATCH_OR_PATH_pebbles` ·
   10 `BASE_OR_PATCH_dune_sand` · 11 `BASE_OR_PATCH_OR_PATH_cracked_earth` · 12 `PATH_OR_SIDEWALK_sandstone` ·
   13 `BASE_OR_PATCH_OR_PATH_black_ash` · 14 `BASE_OR_PATCH_OR_PATH_cooled_lava` · 15 `PATCH_lava_cracks` ·
   16 `BASE_OR_PATCH_OR_PATH_mud` · 17 `BASE_OR_PATCH_marsh_grass` · 18 `BASE_OR_PATCH_clover` ·
   19 `BASE_OR_PATCH_dead_grass` · 20 `BASE_OR_PATCH_OR_PATH_grave_dirt` · 21 `PATH_OR_SIDEWALK_dark_cobble` ·
   22 `BASE_OR_PATCH_meadow_grass` · Mist 23 `BASE_OR_PATCH_mist_moss` · 24 `BASE_OR_PATCH_OR_PATH_grey_silt` ·
   Corrupted 25 `BASE_OR_PATCH_OR_PATH_blighted_soil` · 26 `BASE_OR_PATCH_blighted_grass` (all four seam-clean,
   joins 1.5–2.2×); older ones: `BASE_OR_PATCH_leaf_litter`, `BASE_OR_PATCH_OR_PATH_soil`,
   `PATH_dirt_road`, `PATH_OR_SIDEWALK_flagstone` (was sidewalk), `BASE_OR_SIDEWALK_plaza` (was prontera_tile),
   `CURB_blocks`, `CORNER_curb_slab`. The uses per tile are the assistant's judgement — the user may change them.
   Code lookups by name and the map-painter brush ids/thumbnails were updated; the old grass/rock/stones names in
   code now point at meadow grass / gravel scree / dark cobble. The biome table in the recipe generator still
   uses a few general tiles with tints — wiring the dedicated biome tiles there is not done.

   Batch fixes still to do:
   - **Edge-blend fix needed** (smooth textures, seam visible): 5 frozen dirt (join 2.4× the normal pixel step,
     snow patches cut straight), 8 wet sand (2.6×, faint cross of tone steps). The fix, proven on scratch copies:
     cross-fade the last 40 px of the right/bottom edges into the first 40 px of the left/top, then crop to
     1214 px — seam gone on sand and ice, content ~3% larger on the ground, nothing in the game moves.
   - **Regenerate in ChatGPT** (the blend leaves see-through ghost stones on pebbles): 9 pebbles — seam 3.5×
     with cut pebbles, and the largest are ~23 cm against an 8 cm limit. The 06:41 redo was worse (3.3×/4.2×,
     largest ~22 cm).
   - **Edge-blend fix needed, also:** 10 dune sand — the short-reply redo (07:12) improved it from 4.8× to 2.8×/2.9×,
     but a faint cross still shows because the sand is so smooth. The edge blend suits it (soft sand).
   - **Edge-blend fix needed, also:** 14 cooled lava rock (2.4×/2.9×, smooth, faint tone step).
   - **Seam broken, try the short reply:** 15 glowing lava cracks (5.6×/3.8×, glowing cracks cut at the join).
   - Accepted: 1, 2, 3, 4, 6, 7, 11, 12, 13, 16–22 (joins 1.3–2.4×; the approved leaf litter is 1.9× and shows
     no seam).

   **Seam test:** place 4 copies 2×2 and look at the corner where they meet, plus the join-vs-normal pixel step
   ratio. Also check square, no alpha, no dark border.
4. **All-biome map generator — step 2 of 5 DONE (2026-09-25), step 3 (in-game test) next.** The plan, steps and
   state live in `docs/3-world/biomes.md` (single home). Built: `Scripts/Editor/BiomeTable.cs` + the asset
   `3_Data/Recipes/BiomeTable.asset` (the generator reads every biome look from it), `Forest` appended to `Biome`,
   test maps `test_forest_a`/`test_forest_b` (in the build list). Grassland/Snow proven identical before/after with
   seed 4242. Then, at the user's instruction: trees scatter over the whole field map, grass grows in bunches
   (~75% cover, ~5k plants/map — **frame cost unmeasured, watch it first**), Forest = mud base + leaf-litter
   patches, and `test_forest_a` got field_11's spawn row. Rejected and removed: previews of the next zone past a
   door, and a stone wall along the edges. **Next:** rebuild the game server (`tools/build-game.sh server`, ask the
   user first — their map server runs from that build), the user restarts their map server, then walks
   `/tp test_forest_a`. Then step 4 (the other biomes). Open question for the user: while building test maps the
   assistant moves their Scene view camera and switches the open scene — should that be banned too?
5. **Fix the stacked-props bug** (below), then check every map.
6. **Assign elements** to all 96 creatures and 12 shadows per the world-map biome table. Then update
   `docs/3-world/mob-roster.md` and `docs/2-systems/gameplay-system-v4/elements-and-converters.md`: both still
   say the shadows use four elements.
7. After the world is agreed, rewrite plan Weeks 5 and 6 (`docs/1-planning/action-plan/not-started.md`), which
   still describe connecting today's 41 maps.
8. Decide the field_11 test state (leaf base or patches; keep or remove the grass sample).

## Stacked-props bug — proven vs hypothesis

**Proven (measured this session):**
- Loaded field_11: every child of `Props/Grass` (80), `Flowers` (48), `Treeline` (24), `Rocks` (10) and
  `Landmarks` (2) sits at world (0, 0, 0), all 80 grass at one distinct position. The scene view showed trees
  poking out of the centre house.
- On disk, field_11 has 166 prefab instances, and 164 of their `m_LocalPosition.x` overrides are non-zero, so the
  positions ARE saved.
- Those overrides target `fileID: 324027705005997943` on `Grass4.prefab`
  (guid `6f93198897c87f841815509b3add360d`). The prefab's Transform is `6047917465493172141`, and it already was
  before 2026-09-21. The overrides are orphaned.
- `324027705005997943` last appears in git history around `d78abc98` (2026-07-20), `be088eac` (2026-07-15,
  "props shaders for trees and houses, removed old prefabs").
- **Measured across every scene (2026-09-25):** 42 scenes save prop positions; all except town_0 (0 of 401 broken)
  and Systems have them orphaned (field_5 162/162, dungeon_0 144/144, field_11 164/236). Freshly generated maps
  are clean (0 of 352+ broken).

**Hypothesis (unverified):** the mid-July prop prefab rebuild gave every prop new internal IDs, orphaning the
overrides in every generated map.

## Next single step

Most old maps will be replaced by the new world, and regenerating a map fixes it. For a map worth keeping: map each
orphaned override target fileID to the current prefab's root Transform fileID (per source-prefab guid), count how
many instances it would fix read-only, then apply. The counting script used on 2026-09-25 compared each
`m_LocalPosition.x` override's target fileID against the fileIDs present in its prefab.

## How to resume

```text
Read handoffs/world-map-redesign.md and continue. First the Forest in-game test (item 4: ask before rebuilding the
server), then generator step 4 (all biomes, per docs/3-world/biomes.md). The stacked-props bug and the world-map doc
rewrite are still open.
```
