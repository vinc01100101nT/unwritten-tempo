---
status:
owner: vince
last-updated: 2026-09-25
tags:
  - ai-learnings
---

# A Generated Scene Came Back Looking Like a Diagram

**Reach for this when:** an image generator returned a map, landscape or scene that follows every instruction and
still looks bad — flat colour blocks, evenly spaced shapes, one idea per blob — or you are about to write a prompt for
a whole scene rather than a repeating tile.

## The rule

Exact fractions, exact counts and "clearly different" are right for a tile and wrong for a scene: the generator obeys
them literally and draws a diagram. For a scene, attach a picture that already looks right and ask for changes to it,
describe the mood and how areas blend, and keep only the few hard rules that truly matter (no text, what must connect).

## Why

On 2026-09-25 a world map was requested for a three-region design, with a prompt built from the tile lessons: land
shares as percentages, maximum widths as fractions, "exactly five islands", "exactly 12 islets", "clearly different in
colour". ChatGPT obeyed all of it and the user's verdict was "it looks so bad": every region was one flat biome, the
islands sat in a neat ring like a board game, and nothing blended. The map the user had chosen the day before — made
from a looser prompt — already had every landmass the new design needed. One generation and one round were spent to
get something worse than what was already in hand.

## How to apply

- **Check what you already have first.** If an earlier picture was accepted, test the new design against it before
  asking for a new one.
- **Attach the accepted picture** and say "same style, same painting quality; change only …".
- **Describe feel and transitions**, not quotas: "the green heartland fades into dry steppe towards the west",
  "coastlines irregular, with bays, capes and scattered islets of many sizes".
- **Keep hard rules to the few that matter** — no text or labels, which lands must connect by land, which must be
  separated by sea — and leave counts and sizes loose.
- For repeating tiles, the opposite applies: [Image-Generator Tiles](image-generator-tiles-sizes-counts-seams.md).
