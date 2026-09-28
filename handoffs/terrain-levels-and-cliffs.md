# Handoff — terrain: ground levels, cliffs, meeting points, one walking rule

**Task.** Give the world's fields real shape and roll it out to the world map. Action plan: `MT-44` (fields have shape,
on-going), `MT-45` (rock-face tile, not started), `MT-46` (cliffs from the World Creator, built), `MT-47` (tiny
creatures take the full step, done). Docs: [Biomes](../docs/3-world/biomes.md),
[Walking and Routing](../docs/2-systems/walking-and-routing.md).

## Status

**BUILT, MEASURED IN THE EDITOR, NOT PLAYED ON A REBUILT SERVER.** Every number below was measured in the editor;
none of it has been walked in the game since the last changes. The game server build predates the walking rule,
the route areas and the cliff changes, so **don't trust any "it works" until the server is rebuilt and it is walked.**

## What's done

- **Ground levels** — a biome's ground settles into flat levels joined by slopes. `BiomeRow.bumpHeight / bumpWidth /
  bumpLevels / bumpSteepestDegrees` (Biome Table); `GroundBumps.Make / Terrace / LimitSlope`; used by
  `GroundBlendBuilder.LayPlotSampled`. Only **Grassland** has any: 1.2 m, hills 16 m, 3 levels, slopes to 20°.
- **Cliffs** — `GroundCliffs` (the field, the carved face + rounded shoulder + grass top as ONE welded mesh that is
  also the collider), `GroundBlend.shader` `_CLIFF_ROCK` keyword (triplanar rock faded by uv1.x), ground triangles
  joining raised and low vertices left out. On a cliff map the biome's levels go on the cliff TOPS; the pass is flat.
  **Meeting points** (`Spec.Standing`) — a cliff shrinks to nothing and its top slopes down to the pass.
- **Whether a map has cliffs** — `BiomeRow.cliffs` (false for every biome today) unless `MapRecipe.cliffs`
  (`CliffChoice` FollowBiome / On / Off) overrides it. The World Creator layout's per-map `cliffs: "on"|"off"` (browser
  map menu, `tools/world-creator/index.html`) sets it on the build's recipe copy (`WorldCreator.cs`). The numbers come
  from the recipe: the field template (`FieldRecipe.asset`) says 1.5 m tall, 8 m pass, 3 meeting points; town, dungeon
  and Prontera recipes are Off.
- **One walking rule** — `GroundElevation.Walkable / SlopeTan / ApplyWalkRules / StepBound / StepBoundFor /
  HeightForFullStep`: 45° slopes, 0.075 m steps, for the player (`CharacterSpec.ApplyCapsule`), townsfolk, every
  creature (`PathAgent3D.Awake`, `MobBody.Fit` after resizing) and the route-finder (`Pathfinder3D`). A capsule too
  short for the full step is grown just enough (bottom and width kept): all 98 creatures now step 0.075 m.
- **Route areas** — `Pathfinder3D.LabelAreas / KnownApart / ApproachWithin`, called per map by
  `Pathfinder3DConfig.Start`; an unreachable target costs 0.1 ms instead of 52–57 ms.
- **Test maps** — `test_grassland` (levels, no cliffs; seed 1311222968) and `test_grassland_cliffs` (cliffs on, seed
  707456464), doors `w13` / `w14` to `test_forest_a` and each other. Rebuilt 2026-09-27 after the cliffs switch moved
  to the biome: both ground meshes and the cliff mesh came out **byte-identical** to before.
- **Checks** — the walking rule and the full-step growth are pinned in `tools/game-math-tests/Checks.cs`; Grassland's
  levels and cliffs switch are pinned in `tools/check-biome-table.py`.

## Uncommitted working-tree changes (measured 2026-09-27 ~20:20)

Everything from commit `a02aecd3` (pushed) onward is committed EXCEPT this last round — 32 changed files:
`GroundElevation.cs`, `MobSize.cs`, `MapRecipe.cs`, `BiomeTable.cs`, `RecipeGenerator.cs`, `WorldCreator.cs`,
`tools/world-creator/index.html`, `tools/check-biome-table.py`, `tools/game-math-tests/Checks.cs`, the Biome Table and
the nine recipe assets (explicit cliffs values), the two test scenes and their minimaps, docs (biomes,
walking-and-routing, world-creator-spec, action plan) and two memories. Plus `Circle2.png.meta`, which the editor
keeps rewriting on its own (not from this work), and `tools/hook-analytics/hook-analytics.jsonl`. This file itself is
new. None of it is committed; the user has not asked for a commit of this round.

## What's left

1. **Rebuild the game server** (`tools/build-game.sh server`, with the running one stopped) and walk both test maps:
   every level, a meeting point up and down, a creature chasing up and down, and the tiny creatures crossing a curb.
2. **Decide the biome plan below** (which biomes get levels, which get cliffs), then set the rows in the Biome Table
   and rebuild the world's fields through the World Creator (`BuildCore` with a keep list — never "Build World" with
   hand-made maps open or unsaved).
3. `MT-45` — a repeating rock-face tile for cliff faces (they use `gravel_scree` now); any tile drops into
   `GroundBlendBuilder.MakeCliffMaterial`.
4. Walk a field with "Cliffs: on" set in the browser World Creator through a real build (`MT-46`'s done-when).

## The biome plan — PROPOSAL, for the user to decide

What exists to tune per biome: **levels** (height, hill width, level count, steepest slope — every body walks up to
45°) and **cliffs** (on/off; height, pass width and meeting points come from the field template: 1.5 m, 8 m, 3). The
world has **39 fields**: Forest 6, Grassland 5, Volcanic 4, Coast 3, Steppe 3, Haunted 3, Swamp 3, Corrupted 3,
Snow 2, Peaks 2, Sacred 2, Highland 1, Desert 1, Mist 1. My proposal, labelled as mine, reasoned from what each biome
is in the world map and what its ground is:

| Biome | Levels (height · hills · levels · steepest) | Cliffs | Why |
| --- | --- | --- | --- |
| Grassland | 1.2 m · 16 m · 3 · 20° (as now) | off | open rolling meadow; the user liked it |
| Forest | 0.8 m · 12 m · 0 (smooth mounds) · 15° | off | a forest floor rolls; trees already close it in |
| Highland | 1.8 m · 18 m · 4 · 25° | **on** | "the rocky foot of the highlands" — stepped ground and rock cuts |
| Peaks | 2.4 m · 20 m · 4 · 30° | **on** | the steepest land; passes between rock is the point |
| Snow | 1.2 m · 20 m · 0 · 15° | off | smooth drifts; snow cover already hides detail |
| Steppe | 0.4 m · 30 m · 0 · 8° | off | "open plains to run" — almost flat, long swells |
| Coast | 1.0 m · 18 m · 2 · 20° | **on** | "the east sea cliffs" — a beach level and a cliff level |
| Desert | 1.5 m · 14 m · 0 · 20° | off | dunes: smooth, tall, no rock |
| Volcanic | 1.8 m · 14 m · 3 · 25° | **on** | cooled lava shelves and rock channels |
| Swamp | 0.3 m · 10 m · 0 · 8° | off | wet and flat; its held water sits in the lows |
| Sacred | 1.2 m · 16 m · 3 · 20° | off | terraces on a holy mountain above the clouds |
| Haunted | 0.8 m · 12 m · 2 · 20° | **on** | graves in cut ground; narrow passes feel hunted |
| Mist | 0.8 m · 14 m · 0 · 12° | off | soft, foggy, smooth |
| Corrupted | 1.5 m · 12 m · 3 · 30° | **on** | broken, tilted land; harsh rock cuts |

**Things to check before switching a biome on:** (a) cliffs make everything more than 4 m from a road high ground,
so a biome whose fields have few roads becomes mostly plateau — look at one built map first; (b) a biome's water or
fog (Swamp puddles, Mist fog) was tuned on flat ground; (c) with levels on, a map's props and creature spawn areas sit
on the slopes — spot-check that spawn areas are not all on one level.

## Dead ends (don't repeat)

- Widening hills or scaling them down to keep slopes walkable (hills went 150 m+ wide, or 1.2 m levels shrank to
  0.82 m) — cutting steep edges back into ramps (`LimitSlope`) is what works.
- A flat invisible wall and a flat invisible floor for cliff collision — feet sank below the drawn rim; the drawn mesh
  is the collider now.
- Judging each cliff square's facing on its own — ledge undersides came out wound backwards (grass showing through the
  rock); one winding per traced piece.
- Black lines on the cliff tops were overlapping grass tops from both sides of a narrow ridge crossing at different
  heights — found by casting rays through the dark pixels (see `docs/5-ai-learnings/find-a-visual-glitch-by-casting-through-its-pixel.md`).
- The route-finder's old "height difference ≤ 0.075 between cells" gate made every slope over ~5.4° a wall.

## Next single step

Stop the running game server, rebuild it, start it, and walk `test_grassland_cliffs` from the pass up a meeting point
while a creature chases — the one check that proves the walking rule, the cliffs and the route areas together.

## How to resume

```text
Read handoffs/terrain-levels-and-cliffs.md and continue: rebuild the server, walk the two test maps, then take the user's decision on the biome plan in that file and roll levels and cliffs out to the world's fields.
```
