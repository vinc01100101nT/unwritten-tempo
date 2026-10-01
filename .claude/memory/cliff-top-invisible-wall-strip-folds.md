---
name: cliff-top-invisible-wall-strip-folds
description: Invisible walls on cliff tops (player stops or creeps) were folded, upside-down triangles of a separate grass sheet laid over a hole in the ground; fixed 2026-09-30 by making the cliff part of the one ground.
metadata:
  type: project
---

**Symptom (2026-09-30, khn_fild07 near shown X 2 Y -13, played as Alpha):** walking one way on a cliff top stopped the
character dead or let it creep a little; nothing was visible there. The server log said nothing (the legs are the
player's machine's), so the cause was client physics.

**Root cause (measured):** the old cliff was a second mesh attached to the ground. The ground stayed low for 2.4 m
behind the rock, its ramp triangles were deleted (a hole), and a solid grass strip up to 3.9 m wide covered both. On
bends tighter than the strip, its slices crossed and folded: the folded triangles faced DOWN 1–4 cm above the real top,
were culled by GroundBlend (`Cull Back`), and pinned the CharacterController (`collisionFlags` 7 = Sides|Above|Below).
Every cliff map had 600–1,800 of them.

**Fix:** the user ruled the whole arrangement a workaround and made RULE ONEGROUND (project rules). The cliff is now
the ground itself, shaped in `GroundCliffs.BuildGround` — see [[biome-table]] for how. All 17 cliff maps had only
their ground re-laid (`WorldCreator.RebuildGroundCore`, see [[world-creator-pipeline]]); props, doors and arrivals are
byte-identical in the scene files, and the old `_cliffs.asset` meshes were deleted.

**Proof method (reusable):** open the map with `EditorSceneManager.OpenPreviewScene`, drop a CharacterController
matching the player (r 0.15, h 0.5, scale 0.7, step 0.075, slope 45, skin 0.02, layer Crowd) on walkable spots, `Move`
it 8 ways, count walks stuck with `CollisionFlags.Above`; plus downward rays for holes and an upward ray from under the
top hit for stacked surfaces. After the fix: 0 holes, 0 stacked walkable spots, 1 pinned walk in ~380,000 (khn_fild10,
at the foot of a visible rock face under a carved overhang).
