---
status: Built — creatures are grouped by biome and drawn by level; the theme tagging pass is the one step left
owner: vince
last-updated: 2026-09-22
tags:
  - world-content
---

# Biome Mob Families — Spawning by Biome and Level

**Status (re-measured 2026-09-22):** **built.** **96 creature definitions** sit in `Resources/Monsters`: 66
normals, 18 elites and 12 shadows (the three kinds are [Mob Roster](mob-roster.md)). The one step still open is the
theme tagging pass (step 4 below).

There is **no tier** anywhere in the creature code: difficulty is a creature's **level**, and a spawn area draws from
a **level range**. See [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md).

**Owner:** vince · **Written:** 2026-06-27

The layer that turns the NinjaAdventure monster sprites into creature data, groups it by **biome**, and lets a spawn
area ask for "this biome's normals within these levels" instead of naming creatures one by one.

## Scope note — sprites don't matter yet

This is a **functionality-first** effort. Which sprite a creature uses does not matter right now — any sheet is fine,
looks get swapped freely later. Don't let art block the data work.

## Goals

- Every NinjaAdventure monster folder becomes a usable `MonsterDef` asset in `Resources/Monsters`.
- Creatures are grouped by **biome** (reusing the existing `Biome` enum) and separated by **level**.
- A spawn area asks for a biome and a level range and gets a weighted mix of the matching normals.
- Adding a new creature asset makes it eligible for its biome with nothing else edited.

## Non-goals

- No art work. **Normals and elites stay NinjaAdventure sprites**, assigned arbitrarily for now. (Shadows are 3D
  characters from the player cast.)
- Not changing combat math, AI, or how a creature is built from its data (`MonsterDef.ToArchetype()` stays the
  contract).

## What exists

- **`MonsterDef`** (`unwritten/Monster`) — the creature asset, keyed by a stable kebab `id` (e.g. `mob.slime`). It
  carries its `kind` (normal, elite or shadow), `biome`, `level`, the six primaries, `weaponMasteryLevel`,
  `startingGear`, ring ranks, element, and gold and experience rewards.
- **`MonsterCatalog`** — load-once cache over `Resources/Monsters`; `Get(id)`, `RandomOfKind(kind)`,
  `WeightedPick(biome, minLevel, maxLevel, includeCommons)`.
- **The world spawn list** (`Resources/WorldSpawns`) — where every creature lives: one row per spawn area, read by
  the server as it loads each map. A row either names one creature or asks for a biome and a level range. See
  [Mob Roster](mob-roster.md).

## Decision: reuse the existing `Biome` enum

Creatures are tagged with the **same** `Biome` enum that ground already uses (`Grassland, Snow, Desert, Volcanic,
Corrupted`). **`Biome.None` is the biome-agnostic common**, eligible in every biome's mix. No new enum.

## How a spawn area picks

Grouping is a **query**, never a hand-kept roster: `MonsterCatalog.WeightedPick(biome, minLevel, maxLevel,
includeCommons)` draws among the **normals** of that biome inside the level range, weighted by each one's `weight`;
commons join when asked. **Only normals are ever drawn into a mix** — an elite or a shadow is placed by a row naming
it. When nothing matches, the pick falls back to any normal, so an area is never left empty.

## Biome assignment (arbitrary first pass)

Every monster sheet is dropped into one of the biomes. **The assignment is arbitrary for now** — purely to populate
the data. Re-tag freely.

## Counts & division (actual, 2026-09-22)

- **66 normals** from the monster folders: 12 in each of the five biomes, and 6 biome-agnostic commons.
- **18 elites** from the boss folders, 3 or 4 per biome. **DragonBlue and DragonGreen are not on the roster**: their
  art is separate body parts with no animation picture.
- **12 shadows**, one per weapon type, biome-agnostic.
- Levels: normals spread evenly over **1 to 99**, elites over **6 to 99**, shadows **105 to 160**. Because the normals
  were spread in the order they stood before, each biome still holds one stretch of the ladder: the commons 1–9,
  Grassland 10–27, Snow 28–45, Desert 46–63, Volcanic 64–81, Corrupted 82–99.
- Boss art is one picture per animation (`Idle.png`, `Walk.png`, …), each a single row of front-facing frames. Their
  defs store the folder name in `sheet`; `RigBuilder25D.TryStripArt` resolves the Idle picture (played while
  standing) and the Walk picture, or Jump for the creatures that move by jumping.

## Build phases

1. DONE — **Fields**: `MonsterDef` carries `biome` (with `Biome.None` as the commons marker) and `level`.
2. DONE — **Bulk import tool**: **Tools ▸ unwritten ▸ Mobs & NPCs ▸ Import NinjaAdventure Monsters**
   (`MobBiomeImporter.cs`) creates one `MonsterDef` per folder, the boss folders as elites. It has been run;
   re-running skips what exists.
3. DONE — **Catalog query**: `MonsterCatalog.WeightedPick(Biome, minLevel, maxLevel, includeCommons)`.
4. TODO — **Tagging pass**: refine each creature's biome across the roster, for theme. Levels are set; this step is
   about theme, not difficulty.

## Related

- [Mob Roster — Normal, Elite, Shadow](mob-roster.md) — the three kinds, the level ladder, summoning, drops, and the
  world spawn list.
- [World Creator — Spec](world-creator-spec.md) — the pipeline that authors each map's look.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — what replaced tier, and the model
  every creature carries.
- [Unified Unit Stats — Implementation Notes](../2-systems/active-skill-system-v3/unified-unit-stats-implementation-notes.md) — the Mob Table creatures are authored in.
- [Story & Setting (Canon)](story-setting-canon.md) — what a creature *is* in the fiction.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the milestone these creatures populate.
