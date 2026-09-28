---
status: current
owner: vince
last-updated: 2026-09-27
tags:
  - ai-learnings
---

# Natural Detail Must Be In the Reference

**Reach for this when:** you are about to add detail to something natural — rock, bark, water, ground — from a
reference picture the user gave, or the user asks "why is this there, it looks drawn with a ruler".

## The rule

Every feature you add must be one you can point at in the reference picture. And nothing natural repeats at a
fixed spacing: a pattern with even gaps reads as drawn by hand, however faint it is. Before adding a feature,
name where it is in the reference; if you can't, leave it out.

## Why

On 2026-09-27 the user sent a photo of a sea cliff and asked for a cliff face with its shape, its cracks and
shades of one colour. I proposed, and then built, thin darker horizontal lines about 0.3 m apart across the face.
The photo has no such lines; the idea came from nowhere in it. The user found them in play: "like you used a
pencil to draw them evenly and perfect gaps… cliffs in real life do not have them. delete that." It cost a
play-test round and a rebuild of the shader. The cracks built the same day went through the same trap first:
the first version drew closed loops, because the edges of a smooth noise always close on themselves, and was
replaced with the borders of irregular rock cells before the user saw it.

## How to apply

- List the reference picture's features in words before building: "vertical cracks, jutting slabs, lighter top,
  darker foot". Build only that list.
- Anything periodic (a `frac`, a `Repeat`, a fixed step) that shows on screen needs its spacing broken up —
  noise on the gap AND on the position — or it will read as drawn.
- Render a close-up and compare it side by side with the reference before reporting; ask of each thing on screen
  "where is this in the photo?"
