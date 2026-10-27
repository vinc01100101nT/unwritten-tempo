---
status:
owner: vince
last-updated: 2026-10-28
tags:
  - ai-learnings
---

# Turn a Knob to Its Extreme Before Tuning It

## Reach for this when

You are about to tune a look, effect or setting (saturation, fog, a tint, a light) and compare pictures of a few
values.

## The rule

First set it to an absurd value (saturation −100, fog 0, a pure red tint) and check the picture changes. If it does
not, the setting is not reaching the screen — fix that before tuning anything.

## Why

On 2026-10-28 four screenshots at saturation 0, 20, 35 and 50 came out identical (colour strength 0.600–0.602). Setting
−100 changed nothing either: the renderer had no post-processing data, so the world's whole colour profile — contrast,
warmth, bloom, vignette — had never been drawn since the first 2.5D scene in June. Months of "the look" existed only in
a file. The same day the fog was found not running at all in a test session.

## How to apply

1. Set the extreme value, take one screenshot, and measure it (mean colour strength or brightness of the pixels).
2. Only once the extreme moves the picture, take the pictures of the real values.
