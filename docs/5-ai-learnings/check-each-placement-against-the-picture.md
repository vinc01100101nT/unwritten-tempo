---
status:
owner: vince
last-updated: 2026-09-26
tags:
  - ai-learnings
---

# A Placement on a Picture Landed on the Wrong Terrain

**Reach for this when:** you placed things on a painted map, a screenshot or an image by coordinates (towns, zones,
markers, labels), or the user says "why is X in the sea / on the mountain?".

## The rule

A land-or-water test on the centre pixel is not a check. After placing anything on a picture, crop around EVERY
placement and look at it, asking two questions: is it on the right KIND of terrain (sand, snow, forest), and does the
label it carries match what is painted there? A biome name written in a table proves nothing about the pixels.

## Why

On 2026-09-25/26, cities and towns were placed on the world map by coordinates. Two rounds were spent on the user
finding the misses: the Sword town sat in the sea and the Kick town was across a channel from the land it was meant
to join (the draft had only been checked by eye at full-map scale), and the Dagger city — a desert city in the design
— stood in red rock mountains that the table simply labelled "Desert". A centre-pixel water test was run and passed on
the Dagger spot, because mountains are land.

## How to apply

- Sample a neighbourhood, not a pixel: the share of the kind of terrain you want (green for forest, near-white for
  snow, pale gold for sand) inside the box, and move the box to the best spot nearby.
- Crop each region at 2× and look at the crops before showing the user anything.
- When a design word and the painting disagree (a "desert" city with no desert near), move the thing or ask — never
  relabel the terrain to fit.
