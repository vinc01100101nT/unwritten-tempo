# Sweep a Generated Mesh for Holes Before Anyone Walks on It

**Reach for this when** — you have built or changed code that GENERATES walkable ground (curbs, banks, steps, a deck),
or the user reports blue or sky-coloured holes, a wall that disappears from one side, or a strip where you see through
the ground.

## The rule

A generated mesh is not done when it compiles and looks right from the angle you happened to look at. Before showing
it, run two mechanical sweeps over the saved mesh and drive both counts to zero (or explain each one left):

1. **Gaps** — every edge used by only one triangle, keyed by position INCLUDING height, inside the mesh's outer border.
   A step between two heights with no wall in it shows up here.
2. **Walls facing the wrong way** — for every wall triangle, cast straight down a hair in front of it and a hair behind
   it; the side it faces must be the LOWER one. A wall facing the high side is invisible from the side people see it
   from, and the sky shows through.

And when code decides which way a wall faces, derive it from the SAME numbers that cut the surface it closes (the
slope across that very triangle), never by sampling the shape again a small step away — the re-sample lands on the
wrong side wherever the outline bends tightly.

## Why

2026-10-28, a town's curved curbs: the user found blue holes at four spots from in-game screenshots and had to report
them. The sweeps found what the eye had missed: **118 curb walls facing the wrong way** (side chosen by sampling the
shape 0.02 units off each wall's middle) and **a missing wall wherever low paving met the stone band along the water**
(no code made one — the step was simply never closed). Both were in the mesh from the first build; a two-minute sweep
would have caught them before the user did. After the fix: wrong-facing walls 118 → 0 real ones, the band step closed.

## How to apply

- Load the saved mesh in a throwaway preview scene (`EditorSceneManager.NewPreviewScene()`), give it a mesh collider,
  and cast with that scene's own `GetPhysicsScene()` — nothing touches the open scenes.
- Gaps: a dictionary of edges keyed by rounded (x, y, z) of both ends; count edges seen once.
- Facing: for each wall triangle (normal near horizontal), cast down at centre ± normal × 0.02; the + side must be lower.
  Use a SMALL offset — at 0.06 the probe itself crossed tight curves and reported 7 false faults.
- Then look: render a private camera at the reported spots, low and from both sides, into a picture you read yourself.
