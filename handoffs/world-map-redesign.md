# Handoff — World map redesign and ground tiles

**Task:** redesign the whole world into three regions around the fourteen shadows (only `lastline` survives), make the ground
tiles for every biome.

**Status:** IN PROGRESS. Everything up to the mob-size work is committed and pushed (2026-09-26 ~13:05). Since then,
UNCOMMITTED: the sandbox creature layout, the fourteen shadow styles and the sword's 22 forms with the called-out form
names, every place named (regions, cities, towns, the Fist and Kick shadow places), every field and dungeon renamed to
its town's code (`lst_fild01`, `olk_dun01`), and 16 new maps filling the empty land — 75 generated maps + the
hand-made `lastline`, all doors wired. **None of it is walked in play, and the game server build predates all of it.**
The 14 biome test maps are still unwalked. The stacked-props bug went with the 40 old maps it lived in; Lastline never
had it (0 of 401 broken).

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

- `unity/Assets/1_Authored/Prefabs/VFX/Textures/Circle2.png.meta`: the editor keeps flipping this texture's import
  settings (sprite↔default, compression). The cause is not investigated.
- **The grass sample and the tile test squares were in `field_11` and were deleted with it (2026-09-26).** Their
  materials `2_Generated/Materials/Ground_tile_N_test.mat` and `Ground_pebble_test.mat` are still on disk and now
  used by nothing.
- **The 40 map-ground materials and the brush palette that pointed at the removed grass/rock/stones tiles were
  repointed on 2026-09-25** (the commit check refuses broken references): grass → meadow_grass, rock → gravel_scree,
  stones → dark_cobble — the same mapping the code's name lookups use (the old maps are deleted since). The cleanup on
  2026-09-25 removed 11 materials nothing used (5 old prop ground materials, plus 6 map-ground materials no scene
  uses: Field, Prontera, SampleElevation, StarterTown, Systems, Town) and the props model's 5 remap entries to them. The map-painter files' grass brush id was renamed to `base_or_patch_meadow_grass`
  to match.

## What's left

1. **The three-region world — designed 2026-09-25, generated 2026-09-26.** The design is `docs/3-world/world-map.md`
   (single home): a sandbox in three regions — Midlaine (Might), Lirenne (Finesse), The Far Shores (Arcane) — whose
   creatures mostly sit at 1–33 / 34–66 / 67–99, a city per one-family weapon, a border town per hybrid, 8 dungeons
   (22 floors), elites everywhere, the 14 shadows' elements, places and ways in. Weapons are 14 (Fist and Kick added;
   the groups and the Firearm-only magic crit are in the weapon roster doc).
   - **Layout:** `tools/world-creator/worlds/world-regions.json` (90 maps incl. the 14 shadow places, 100 doors, each
     map with a `name`; every generated map carries a pinned `seed`, so a rebuild reproduces it). The picture:
     `unity/Assets/3_Data/Resources/WorldMap/world-map.png`.
   - **Generated** with `WorldCreator.BuildCore(...)`, keep list `lastline`, shadow places left out: 75 scenes, 172
     door objects, all registered in Build Settings, each with its Map Painter file in `tools/map-painter/exports/`.
     BuildCore also places doors to the shadow places it does not generate; those were deleted by script afterwards
     (28 objects in 14 scenes) — do it again after any rebuild. The window's Build World must NOT be clicked (it would
     wipe `lastline` and drop unsaved scene changes) — see the World Creator spec.
   - **Creatures (2026-09-26):** a sandbox — every field and dungeon floor names its own creatures in the world spawn
     list (394 rows); the whole mapping is `docs/3-world/biome-mob-families.md`. The three dungeons added on 2026-09-26
     (Lastline, Kharnel 5–6, Kharnel 7–9) have no elite of their own on the bottom floor. Shadows not placed. Unwalked.
   - **Map ids (2026-09-26):** a city, town or special place's id is its name in snake_case (`old_oak`,
     `aurora_steps`); a field is `<town code>_fildNN` and a dungeon floor `<town code>_dunNN`, set by hand in the
     browser tool, which asks for the id when one is renamed. The layout json is the ONE source; scene files, doors,
     arrival points, Build Settings, the world map, Map Painter files, the starting map and the music table follow it,
     and `tools/check-map-ids.py` (pre-commit and end of turn) fails on any drift or a malformed field/dungeon id. Door
     colours come from the destination map's type (dungeon doors purple, city/town doors yellow).
   - **Lastline (`lastline`)**: nothing removed; its west/east gates lead to `lst_fild01` / `lst_fild03`, its south
     gate to `hrw_fild01` and its north opening to `lst_fild02`.
   - **In-game world map (N)**: shows the world creator's PNG export (default settings: painting + every map's box,
     its name and every door's line) as it is, nothing drawn on top but the "you are here" marker (`WorldAtlasBaker.BakePainted`,
     `WorldCreator.BakeWorldMapCore`). The export sits at `Resources/WorldMap/world-map.png`; the clean painting is
     `tools/world-creator/worlds/wm1.png`. To refresh it: load wm1.png as the tool's background, import the json,
     🖼️ PNG → Download PNG, save over that file (the save window remembers its folder), then re-bake.
   - **Served:** nothing yet — the server build must be remade for the renamed and new maps.
2. **Names are set (2026-09-26):** the fourteen shadow styles (locked, in the story canon doc; every skill is a
   numbered FORM of its style, the sword's 22 built) and every place name (the world-map doc's cities table and naming
   rule; memory `world-names-tie-to-shadows-and-weapons`). Open: the Thousand Gates (the Sword shadow's place) is to be
   reworked away from a shrine of gates.
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
   watch it first**. **Next:** the user walks the chain from `/tp test_forest_a` (the 05:03 server build already
   has the current chain, without the deleted `test_forest_b`). Never move the user's Scene view
   camera or switch the open scene while building test maps (banned 2026-09-25); build with `leaveOpen: false`.
4b. **Weather a biome always has — BUILT 2026-09-25, not walked in game:** Swamp always has puddles, Snow always
   has the snow blanket, Mist always has fog (the "Weather a biome always has" section of `docs/3-world/biomes.md`, which also holds the
   parked weather-frequency idea).
5. **Old maps deleted (2026-09-26, uncommitted):** the 40 scenes `field_0–16`, `dungeon_0–13`, `town_1–9`, their
   ground materials and meshes, their Map Painter files, 33 spawn rows, 40 music rows and 43 Build Settings entries
   (three were listed twice). Lastline's generated files and its ground object were renamed from `town_0` to
   `lastline` (asset IDs kept).
6. **The creatures are a sandbox (2026-09-26, uncommitted):** every creature has one home biome, element and level,
   each map names its own creatures, and elites summon their own biome's creatures first. The whole mapping is
   `docs/3-world/biome-mob-families.md` (Creatures and Where They Live). Not walked in play; needs the server rebuilt.
7. After the world is agreed, rewrite plan Weeks 5 and 6 (`docs/1-planning/action-plan/not-started.md`), which
   still describe connecting the 41 old maps. What needs agreeing first was put to the user on 2026-09-26.
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
   - **A character saved in a map the game no longer has goes to Lastline (built 2026-09-26, not run yet):** the map
     server answers "no such map" with its starting map, and the character server forgets that character's saved
     map and position and sends them there. Test characters in maps that no longer exist (Tester in `lv_1_3`, Alpha in
     `lv_83_90`, Dex and Claude in `town_0`, Bravo and Tessa in `field_11`) will be moved the first time each signs in,
     once the server build is remade and the character server restarted.
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
   - **The shadow places** (14 special maps) are in the layout, not generated.
   - **The player wiki's World & Maps page** still lists five biomes and says nothing of the three regions. Left
     alone while the design is a proposal.
   - **Fist and Kick** exist only in the docs: the code's weapon types have `Knuckle` (fist) but no kick, and no
     shadow, mastery or skill exists for either. The Firearm's magic crit is decided, not built (the game blocks
     every magic crit today).

## How to resume

```text
Read handoffs/world-map-redesign.md and continue. First: commit if the user asks, then walk the new world from Lastline:
doors (purple = dungeon, yellow = town), creatures and elites per field, the full-screen world map (N), /tp by map id
(e.g. /tp hlt_dun01), the Sacred clouds. Then the 14 biome test maps (item 4), and the world-map doc's Still open list.
```
