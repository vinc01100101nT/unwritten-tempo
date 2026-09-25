# Handoff — World map redesign, ground tiles, and the stacked-props bug

**Task:** redesign the whole world into three regions around the fourteen shadows (only `lastline` survives), make the ground
tiles for every biome, and fix a bug found on the way: every prop in field_11 is stacked at the map centre.

**Status:** IN PROGRESS. Everything up to the end of the second 2026-09-26 session (~05:05) is committed and pushed
to main. The three-region world is GENERATED (59 new scenes + the hand-made `lastline` as Lastline, all doors wired),
every map is named by its id, and normals + elites are placed in every field and dungeon floor — but **nobody has
walked it in play yet (the user only opened the world map and stood in `test_forest_a`) — don't trust any "it works"
claim about it.** **Before anyone can play it: the user must run the database update below (item 10) and rebuild the
game server.** The Sacred cloud look is built and compiled but unseen; the full-screen world map (N) is built and
compiled but unseen. The 14 biome test maps are still
unwalked. The stacked-props bug is NOT FIXED (40 of the 42 older scenes; the new 59 are freshly generated and clean).

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
- **World map proposal:** `docs/3-world/world-map.md` (committed; rewritten 2026-09-25 to the landmass layout, uncommitted).
- **Plan and roadmap:** all built V4 tasks marked done. The wiki Roadmap's Now is "Killing things pays you"
  (only its drop-rate play-test is open). Stale tasks were deleted or rewritten.
- **Files in `~/Downloads`:**
  - `world-map-base.png`: the previous ChatGPT world map, clean, 1536×1024 (replaced by `wm1.png`, now in the repo at
    `tools/world-creator/worlds/wm1.png`).
  - `world-map-homelands.png`: the same map marked with the old twelve homelands (superseded).
  - `world-map-regions.png`: that previous map with the three regions, cities and towns drawn on (superseded by
    `world-regions-layout.png` on `wm1.png`).
  - `world-layout.png`: 88 zones and 92 warps drawn on the map.
  - `world-homelands.json`: that old layout as an importable world-creator file (superseded). The background aligns at 0,0,
    1536×1024 world units.
  - `world-layout-script.py`: the script that generated both layout files. It runs under Pillow and needs its
    paths edited.

## State left in the project (committed 2026-09-25)

- `unity/Assets/Scenes/field_11.unity`: the user's approved **grass sample**, a root `Grass_Sample` of 70 plant
  props placed around (8, 0, 0) near the lastline arrival point. Saving the scene also made Unity drop 43 leftover
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

1. **The three-region world — designed 2026-09-25, generated 2026-09-26.** The design is `docs/3-world/world-map.md`
   (single home): Might 1–33 / Finesse 34–66 / Arcane 67–99 as level tiers, a city per one-family weapon, a border
   town per hybrid, 5 dungeons (15 floors), elites everywhere, the 14 shadows' elements, places and ways in. Weapons
   are 14 (Fist and Kick added; the groups and the Firearm-only magic crit are in the weapon roster doc).
   - **Layout:** `tools/world-creator/worlds/world-regions.json` (74 maps incl. the 14 shadow places, 77 doors,
     each map with a `name`). Built from a throwaway Pillow script in the session scratchpad; the draft preview is
     `~/Downloads/world-regions-layout.png`. The picture: `unity/Assets/3_Data/Resources/WorldMap/world-map.png`.
   - **Generated** with `WorldCreator.BuildCore(...)`, keep list `lastline`, shadow places left out: 59 scenes
     (all named by id — see the next bullet), 122 portals, all registered in Build Settings;
     59 Map Painter sidecars in `tools/map-painter/exports/`. The window's Build World must NOT be clicked (it would
     wipe `lastline` and drop unsaved scene changes) — see the World Creator spec.
   - **Creatures (2026-09-26):** normals and elites placed for all 30 fields and 15 dungeon floors in the world spawn
     list (85 rows; the rules are in the world-map doc's Built section). Shadows not placed. Unwalked.
   - **Map ids (2026-09-26):** every map's id is its name in snake_case (`Lv 1–3` → `lv_1_3`, `town_0` → `lastline`,
     the two repeated names → `lv_67_71_2`, `lv_72_76_2`). The layout json is the ONE source; the browser tool sets the
     id on every rename and import; scene files, doors, arrival points, Build Settings, the world map, Map Painter
     files, the starting map and the music table were all renamed to match, and `tools/check-map-ids.py` (pre-commit
     and end of turn) fails on any drift. Door colours now come from the destination map's type, not its name; 46
     pads were recoloured (dungeon doors purple, city/town doors yellow). NOT done: six characters in the database
     still name `town_0` / `m_f10` as where they stand — the update was refused by the permission guard and is the
     user's to run. A map without a pinned `seed` rolls it from its id, so rebuilding one now gives a new layout.
   - **Lastline (`lastline`)**: nothing removed; its west/east gates retargeted to `lv_1_3` / `lv_21_24`, new doors at its
     south gate (→ `lv_26_30`) and north opening (→ `lv_9_12`). Byte-diff proved only the 8 retarget lines changed.
   - **In-game world map (N)**: shows the world creator's PNG export (default settings: painting + every map's box,
     its name and every door's line) as it is, nothing drawn on top but the "you are here" marker (`WorldAtlasBaker.BakePainted`,
     `WorldCreator.BakeWorldMapCore`). The export sits at `Resources/WorldMap/world-map.png`; the clean painting is
     `tools/world-creator/worlds/wm1.png`. To refresh it: load wm1.png as the tool's background, import the json,
     🖼️ PNG → Download PNG, save over that file (the save window remembers its folder), then re-bake.
     Known: the side-by-side dungeon floors' names overlap in the picture (Lava Tubes, Canyon Temple, Haunted Deep).
   - **Not checked:** whether the map servers are set up to serve the new maps; the user's game SERVER BUILD
     predates all of this and must be rebuilt before the new world can be played online (ask first).
2. **Names are NOT settled.** Decided 2026-09-25: the game uses NO real-country culture, so real languages are
   out; a made-up language per homeland was tried and rejected the same day. Every name ties to its shadow AND
   its weapon, in Ragnarok-city style (rule saved in memory `world-names-tie-to-shadows-and-weapons`). Order of
   work: shadow names first (the current Varro, Brigid… may be replaced), then weapon names and place names.
   The naming theme is not chosen. The world map shows the json's current placeholder names (Lastline, weapon names, "Lv 1–3", dungeon names), and the world-map doc names shadows and cities by
   weapon until the names lock.
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
4. **All-biome map generator — steps 1, 2, 4, 5 DONE (2026-09-25); step 3, the in-game walk, is left.** The steps
   and state live in `docs/3-world/biomes.md` (single home). All 14 biomes are Biome Table rows on their own tiles;
   `tools/check-biome-table.py` guards them (pre-commit). A 13-map test chain (one per biome but Grassland) `test_forest_a` → `test_highland` → … → `test_corrupted`
   is built and linked (24 doors, all checked). `docs/3-world/biome-references.md` puts a real photo beside each
   biome's test map with a match rate (Snow and Peaks 80%, Haunted and Corrupted 20%) and what is still missing;
   the next gains need new art (standing stones, dead trees, gravestones, a heather tile, reeds, a sea edge) or
   ground with height (slopes, dunes). Grass bunches up to ~4.4k plants/map (Sacred) — **frame cost unmeasured,
   watch it first**. **Next:** rebuild the game server (the user's build predates this session's last round: it
   still has the deleted `test_forest_b` and the old doors) (`tools/build-game.sh server`, ask the user first — their map server runs from that build), the
   user restarts their map server, then walks the chain from `/tp test_forest_a`. Never move the user's Scene view
   camera or switch the open scene while building test maps (banned 2026-09-25); build with `leaveOpen: false`.
4b. **Weather a biome always has — BUILT 2026-09-25, not walked in game:** Swamp always has puddles, Snow always
   has the snow blanket, Mist always has fog (the "Weather a biome always has" section of `docs/3-world/biomes.md`, which also holds the
   parked weather-frequency idea).
5. **Fix the stacked-props bug** (below), then check every map.
6. **Assign elements** to all 96 creatures (by the biome majority table) and the shadows (the 14 decided in the
   world-map doc; the game data still gives the 12 existing shadows four converter elements). Then update
   `docs/3-world/mob-roster.md` and `docs/2-systems/gameplay-system-v4/elements-and-converters.md`, which still
   say the shadows use four elements.
7. After the world is agreed, rewrite plan Weeks 5 and 6 (`docs/1-planning/action-plan/not-started.md`), which
   still describe connecting today's 41 maps.
8. Decide the field_11 test state (leaf base or patches; keep or remove the grass sample).
9. **Found 2026-09-25, not fixed:**
   - **Errors in the user's play session (19:23):** 11 × `MissingReferenceException` on a destroyed `UI.Image` in
     `HoverPlate.RebuildIcons` (HoverPlate.cs:264, from LateUpdate :137). Unrelated to the biome work; not looked at.
   - **The field template's map name is "StarterTown"** (`3_Data/Recipes/FieldRecipe.asset`), so ⚒ Generate Map on
     it saves to `Scenes/StarterTown.unity` every press. No such scene exists today. Offered to the user, not changed.
   - **Grassland's road:** the biome doc's table says dirt road; Grassland field maps lay the template's soil road.
     The user's decision, still open.
   - **Systems scene showed "unsaved"** after an editor script ran, with a saved copy byte-identical to the file on
     disk — nothing to save. Not traced.
   - **A freshly generated map's ground renders white** in an editor picture for about a minute, then shows its
     tiles. A picture problem only; the map is fine.

10. **Found 2026-09-26, open:**
   - **Six characters still stand in maps that were renamed** (`town_0` ×5, `m_f10` = Alpha). The update was refused
     by the permission guard; it is the user's to run (Alpha already failed to load `m_f10` at 04:50):
     `psql "$(grep '^DATABASE_URL=' accounts/.env | cut -d= -f2-)" -c "update character set progress = jsonb_set(progress,'{sceneName}', to_jsonb(case progress->>'sceneName' when 'town_0' then 'lastline' when 'm_f10' then 'lv_29_33' end)) where progress->>'sceneName' in ('town_0','m_f10');"`
   - **The game server build predates every change of this session** (map renames, mob list, world map). Rebuild it
     (`tools/build-game.sh server`, ask first), then the user restarts the map server.
   - **Two old maps' doors into Lastline arrive nowhere**: `field_11` → `w0_from_field_11` and `field_15` →
     `w12_from_field_15` have no arrival point in `lastline` since its gates were retargeted to the new world.
     Pre-existing; the old maps are set aside.
   - **The world map picture is 1536×1024 and compressed**; full screen on a large display upscales it. A sharper
     export needs the baker to take the world span from the layout's background, not the picture's pixel size.
   - **Sacred clouds** — rebuilt as a cloud layer at treetop height (`CloudLayerOverlay` + `Unwritten/CloudLayer`,
     setting `alwaysClouds` in `Resources/BiomeWeather.asset`). Compiled, never seen. The first version showed
     nothing; the rebuilding agent measured the old wisps at ≤6.5% opacity on 90% of the ground (too faint) and ruled
     out the biome marker and the shader import. NOT measured: whether the overlay object is actually created on the
     player's machine in a networked session (`WeatherDirector` → `CloudLayerOverlay.Ensure()`). If it still shows
     nothing, measure that first.
   - **Every door uses the plain cylinder pad**: 122 warnings "No Hovl portal prefab found in Assets/0_Raw/Hovl
     Studio/…/Portals/" during the build. Pre-existing; the portal effect asset is missing.
   - **The shadow places** (14 special maps) are in the layout, not generated; the Fist and Kick places are unnamed.
   - **The player wiki's World & Maps page** still lists five biomes and says nothing of the three regions. Left
     alone while the design is a proposal.
   - **Fist and Kick** exist only in the docs: the code's weapon types have `Knuckle` (fist) but no kick, and no
     shadow, mastery or skill exists for either. The Firearm's magic crit is decided, not built (the game blocks
     every magic crit today).

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
- **Measured across every scene (2026-09-25):** 42 scenes save prop positions; all except lastline (0 of 401 broken)
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
Read handoffs/world-map-redesign.md and continue. First: confirm the user ran the database update (item 10), rebuild
the game server (ask first), then walk the new world from Lastline: doors (purple = dungeon, yellow = town), mobs and
elites per field, the full-screen world map (N), /tp by map id (e.g. /tp mines_1), the Sacred clouds. Then the 14
biome test maps (item 4), the stacked-props bug, and the world-map doc's Still open list (names first).
```
