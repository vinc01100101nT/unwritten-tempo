---
status:
owner: vince
last-updated: 2026-09-26
tags:
  - ai-learnings
---

# A Look Agreed in Words Was Rejected on Screen

**Reach for this when:** you are about to build a visual effect (weather, a sky, clouds, a glow, a UI look) from a
description, or the user has just seen something you built and said "that's not what I meant".

## The rule

Words describe a look badly: "above the clouds" meant one thing to the builder and another to the user, and both
readings fit the sentence. Before building any look, show a picture of it — paint it over a real screenshot or a
reference image from the game's own camera — and get a yes on the PICTURE. Say where it sits relative to the player
(under them, around them, at the edge, overhead), because that is what a sentence leaves open.

## Why

On 2026-09-25 the user asked for a map to "show some clouds … that gives the feel that the player is in a place above
the clouds". It was built as a sea of clouds BELOW the map's ground, visible only past the map's edges, and the user
approved a three-bullet description of exactly that. On screen they saw nothing, and said the clouds had to float
INSIDE the map at treetop height. The whole effect, its shader and its setting were rebuilt the next round by a second
agent. The words had been agreed; the look never had.

## How to apply

- Take a screenshot from the game camera on a map of the right biome (or use the world-map picture for a map-level
  look) and paint the effect onto it: where it is, how dense, what stays clear.
- Ask one question with the picture: "this?" — not a description.
- Put the camera's real height and tilt next to it (here: 3–8 m above the player, tilted down at most 50°), because
  "above", "below" and "around" all depend on it.
