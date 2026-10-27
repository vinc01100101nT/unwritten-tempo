# Handoff — the seamless world (`world`): the block around Lastline, its look, and the old world gone

**Task:** one seamless world (32-unit squares streamed around each player), laid out by the world layout tool and built
square by square with the Idyllic Fantasy Nature pack. The first block — squares 17..23 × 17..23 around Lastline — is
built and is now the whole game: the old zone maps are deleted. No Tempo issue/doc id.

## Status

**IN PROGRESS.** The world plays (start map, townsfolk, creatures, quest, minimap, map window), but three pieces were
built after the game server was last rebuilt and have **not been seen with a rebuilt server**: townsfolk waiting for
their ground, the deleted old-world data, the start map. **Don't trust prior "it works" claims** for townsfolk or
creatures until the game server is rebuilt and they are seen in Play.

## What's done (2026-10-27 / 28)

**Measuring** — the built game has no stutter: editor frame spikes (11–13% of frames over 25 ms, 100 ms on a square
load) do not happen in the build (0–2 per 600 frames; 25 square loads, none over 36 ms). Built game 1920×1080:
12–14 ms a frame in the fields, 19 ms at (70, 60). Control-link command `perf [nocap|cap]` (`AgentLink.cs`,
`FrameStats`); skill `.claude/skills/measure-built-game/`.

**Old world deleted** — 15 test maps, `seamless_test`, then all 77 zone maps, their ground, minimaps, painter files;
`WorldAtlas.asset` and `tools/world-creator/worlds/world-regions.json` emptied; build list = Systems + world. Lastline's
source moved to `1_Authored/Prefabs/Towns/Lastline.prefab` (+ its meshes/materials); `SeamlessWorldBuilder` reads it.
The builder keeps the world scene's own settings blocks (no template map). Kept: 18 Summoners (parked on old map names),
all 230 creatures. Deleted: 15 other-town Keepers, 315 old spawn areas. Music row `lastline` → `world`.
Checkers adjusted: `check-map-ids` (seamless world, start map, music), `check-quest-data` (towns inside a world, shipped
town maps only), `check-biome-table` (no test maps) — every prover passes.

**Migrated into the world** — Lastline's 16 townsfolk (same coordinates); 42 creature spawn areas of Lastline's fields
and dungeons as 7 hunting grounds (fild01 (-62,18) W, fild02 (-18,74) N, fild05 (14,-78) S, fild04 (58,-74) SE,
fild03 (94,-98), dun01 (82,-38), dun02 (86,6)); the Plopling quest; new characters start in `world`
(`SavePoint.DefaultMap`, the one constant). Townsfolk wait for their ground (`SeamlessWorld.HoldGround`,
`WorldTownsfolk.Build`).

**Minimap / map** — `MinimapBaker` draws a seamless world as square pictures (`Minimaps/world/<gx>_<gy>`, 256 px per
32 units) + one 2048 overview; `Minimap` shows the squares around the player (≤25 held); the world builder redraws
them. `SeamlessWorldPreview` builds a whole world into a hidden scene for editor work. `MapWindow` works without the
old atlas (opens the map you are in; no world view).

**Look** — post-processing turned on (`Spike3DRenderer.asset` had none); `IdyllicLook_PostProcessing` = light look (no
warm white balance / channel mixer / split toning, neutral tone, contrast 5, saturation +15, white bloom, vignette 0.15).
Settings ▸ Graphics sliders: Brightness, Contrast, Saturation (added to the look, `LookAdjustments`), Fog thickness.
`DayNightLight`: the sun, the three-direction fill (`dayAmbient*`), sky and billboards keep midday values at every
hour; time of day is ONE screen tint (a runtime volume, `TimeOfDayTint.At`): white by day, warm in a narrow band at
sunrise/sunset, (0.34, 0.42, 0.60) at night. Systems scene re-saved without the removed fields.

**Meadows** — grass no longer grows inside meadow patches. Meadow cards turn about their upright axis to face the
camera: `Shaders/MeadowFacing.shadergraph` (a copy of the pack's Vegetation graph) + `Shaders/FaceCamera.hlsl`,
material `FlowerMeadowFacing.mat`, used by the builder for meadows. Cost unchanged (5.79 ms GPU at the pink meadow).

## Uncommitted working-tree changes

Everything above is committed together with this file (2026-10-28). Re-run `git status` before trusting this line.
The font `DejaVuSansCondensed SDF.asset` was committed in a cleared state (glyph list emptied by
`ClearFontAssetData`; it is a dynamic font and refills while playing) — if text looks wrong, restore it from the commit
before this one.

## What's left

1. **Rebuild + restart the game server**, then see in Play: townsfolk standing in Lastline, creatures in the 7
   hunting grounds, a new character starting in the world.
2. **Top-down meadows still read peach**: the facing cards are upright, so from 65° above the pink tops are a sliver.
   Next idea, offered to the user: let each card also lean back toward the camera (same shader, one more step).
3. **Keepers per map**: `WorldTownsfolk.KeeperIn(map)` returns the first Keeper of a map — breaks when a second town
   is in the world; teleport prices count doors the world does not have. Needs a design call (price by distance?).
4. **Fog not starting** in my test session (`FogOverlay` had no material/quad) — cause not found.
5. **Trees missing from the minimap pictures** (the pack's leaf shader draws nothing in the bake camera) — cause not
   measured.
6. **Wiki creature list** shows the hunting grounds as "world" (no named areas for fields yet).
7. **Character stuck** between field trees and rocks at (-56, 50) — the route-finder may not know the field fill.
8. Next blocks of the world; the 18 Summoners' places; the other 14 towns.

## Dead ends (proven)

- Single 3D flowers for meadows: face up and stay pink, but 60 triangles vs a card's 8 — at equal cost 13% as many,
  sparse; at the cards' count ~7× the cost (estimated 35–50 ms). Taken out.
- Multiplying the plant shader by the time-of-day tint: dimmed plants at night but the user wanted one screen tint —
  undone.
- HDR colour grading: pinker flowers but more orange grass; not the cause.
- Sky-based ambient read right after one `UpdateEnvironment()` was unsettled; settled sky colours turned rocks blue —
  the saved fill is 60% toward grey.

## Next single step

Rebuild and restart the game server, sign in with the test account (alpha@test.local / testpass123), and check that
Lastline's townsfolk stand in the world and creatures appear in the hunting grounds.

## How to resume

```text
Resume the seamless world work: read handoffs/seamless-world.md first, check editor_status (stop Play before ANY script edit), then continue from "Next single step".
```
