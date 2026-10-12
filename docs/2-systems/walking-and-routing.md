---
status: "BUILT 2026-09-27, not play-tested in the game: one walking rule for every body and every route; route areas per map."
owner: vince
last-updated: 2026-09-27
tags:
  - systems
---

# Walking and Routing

How anything in the world walks — the player, every creature, every townsperson — and how a route is planned for
it. There is one rule, and everything that walks or plans a walk uses it, so what a body can walk and what a route
plans for it are always the same answer.

## The walking rule

Two numbers, both in `GroundElevation`:

- **The tallest step a body steps straight up: 0.075 m.** A one-notch curb (0.05 m) is walked; a two-notch ledge
  (0.1 m) is a wall.
- **The steepest slope a body walks: 45°.** A 20° hillside or a cliff's meeting point (about 25°) is walked; a
  50° slope or a cliff face is a wall.

**On the bodies.** `GroundElevation.ApplyWalkRules` puts both numbers on a body's controller. The player and every
townsperson get them through the shared character capsule (`CharacterSpec.ApplyCapsule`), and every creature gets
them when its mover starts (`PathAgent3D`). A creature's baked prefab no longer decides its own step: before this,
creature steps ran from 0.039 m to 0.23 m. **Every body takes the full step, the smallest included:** Unity
refuses a step taller than a controller's capsule can take, so a capsule too short for it is grown just tall enough
(0.084 m of scaled height), its bottom kept where it was and its width untouched — only the invisible collision shape
changes, never what is drawn. A creature resized to its size (`MobBody.Fit`) gets the rule put back on at the new
size. Measured on the 98 creature prefabs: every one steps 0.075 m; the ten that were too short (mice, butterflies,
larvae, moles, molluscs) grew from 0.044–0.078 m to 0.083 m. Pinned in the maths checks too.

**On routes.** The route-finder asks `GroundElevation.Walkable` before stepping from one cell to the next: neither
spot steeper than 45°, and the height change no more than a step plus what the slope under them explains. That is
how it tells a slope from a ledge — a 20° hillside is walked, while a 0.1 m ledge between two flat spots is a wall,
exactly as a body's controller treats them. The route's straight-line check and the area labelling below use the
same rule. The rule is pinned in `tools/check-game-math.sh` (curb, ledge, 20°, 44°, 50°, the foot of a slope, a
drop beside a slope, a cliff).

## Route areas

When a map loads, it works out once which areas of it connect to each other (`Pathfinder3D.LabelAreas`, called by
the map's `Pathfinder3DConfig`). Before any route search, a check asks whether the start and the goal are in the
same area; if not, the search is skipped and the body walks straight toward the goal, stopping at the last spot
still in its own area — the edge of a cliff above a creature, or the foot of it below one.

- **Cost, measured on a 60 × 60 m map:** about 13 ms once when the map loads, 28.8 KB of memory while it is held,
  and 28 ns per check. Without it, a chase toward an unreachable target searched every reachable cell up to its
  6,000-cell budget each time: 52–57 ms, three times a second.
- **The labelling is never stricter than the search:** it uses the thinnest possible body, so "different areas" is
  only said when it is certainly true; when in doubt the normal search runs.
- **A random landing spot comes from the same labels** (`Pathfinder3D.RandomSpotInArea`, what a Fly Wing uses): each
  area's cells are listed once at labelling (4 bytes per walkable cell — at most about 101 KB for an 80 × 80 m map), a
  cell of the area the unit stands in is picked evenly with one lookup (measured 1.4 µs a pick), and it is moved to the
  nearest cell a real body fits when the first is too tight — kept only if that is still the same area, so whoever
  lands can always walk back. Asked once per use, never per frame.
- **Where it applies:** only maps with a route-finder setup (every generated map has one). The labels go when the
  map unloads, and are cleared at the start of every Play.

## What this changed, measured on 2026-09-27

- `test_grassland` (three ground levels, slopes to 20°): routes run between levels.
- `test_grassland_cliffs`: routes run from the pass up onto the cliff tops through the meeting points, and along
  the pass as before.
- `lastline` used to let routes climb 0.6 m rises no body could climb; all 28 routes between its doors and arrival
  points still route under the one rule.

Related: [Biomes](../3-world/biomes.md) (ground levels, cliffs, meeting points).
