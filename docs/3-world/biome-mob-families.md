---
status: Built — plumbing done; the tagging pass is the one step left
owner: vince
last-updated: 2026-08-30
tags:
  - world-content
---

# Biome Mob Families — Spawning by Biome and Level

**Status (re-measured 2026-08-30):** **built.** The header used to read *Design / not yet built* while four of the five build steps below were already marked DONE inside this same doc — the header was the stale half. The importer has been run: **86 mob definitions exist** on disk, the catalog query and the generator wiring are in, and the only step still open is step 5, the tagging pass.

⚠️ **The tier half of the original design was deleted.** This doc used to propose an `int tier` (1–4) per mob as the difficulty band. **Tier was removed from the whole project** on 2026-08-10 — difficulty is a mob's **level**, and a spawn band is a **level band**. Every passage below has been rewritten to say so; there is no `tier` field on a mob and no tier anywhere in the mob code. See [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md).

**Owner:** vince · **Written:** 2026-06-27

A layer on top of the existing mob data foundation (`MonsterDef` + `MonsterCatalog` + `MapRecipe` + `RecipeGenerator`). Goal: turn the NinjaAdventure monster sprites into ready-to-use mob data, group them by **biome**, and have the map generator auto-scatter the right mobs (and their spawners) for a map's biome and level band during the generate phase — no per-recipe hand-rostering.

## Scope note — sprites don't matter yet

This is a **functionality-first** effort. We do **not** care which sprite a mob uses right now — any sheet is fine, looks get swapped freely later. The whole goal is the **plumbing**: tag mobs by biome, and have the generator spread them into maps grouped by biome. Don't let art block the data/generator work.

## Goals

- Every NinjaAdventure mob in `Art/NinjaAdventure/Actor/Monster/*` becomes a usable `MonsterDef` asset in `Resources/Monsters`.
- Mobs are grouped by **biome** (reusing the existing `Biome` enum) and separated by **level**.
- A `MapRecipe` already declares a `Biome`; the generator pulls that biome's mobs within the recipe's level band from the catalog and bakes spawners automatically.
- Adding a new mob asset auto-joins the right biome without editing every recipe.

## Non-goals

- No art work. **Mobs stay NinjaAdventure sprites**, assigned arbitrarily for now — swap looks anytime later. (Charcoal/Quaternius applies only to player + NPCs.)
- Not changing combat math, AI, or how a mob is stamped into a scene (`MonsterDef.ToArchetype()` stays the contract).
- No new "town/field/dungeon" generators — still the single `RecipeGenerator`.

## Current foundation (what already exists)

- `**MonsterDef**` (`unwritten/Monster`) — the mob asset, keyed by stable kebab `id` (e.g. `mob.slime`); at the time this was written it had `sheet` (folder under `Actor/Monster`), `weight`, `isBoss`, element/status/loot, and **no biome field**. *It now carries `biome` and `level` (plus the six primaries and `startingGear`) — see the build phases below.*
- `**MonsterCatalog**` — load-once cache over `Resources/Monsters`; `Get(id)` / `Boss` / `WeightedPick`.
- `**MapRecipe**` — already has a `Biome biome` field (`Grassland, Snow, Desert, Volcanic, Corrupted`), used today only for ground look. `RecipeMobGroup` references a **single** mob by name/id, or empty = weighted mix of all field mobs.
- **No** `Boss/` **art folder** — "bosses" are ordinary mobs flagged `isBoss`. Tier 4 can map onto that.

## Decision: reuse the existing `Biome` enum

Mobs are tagged with the **same** `Biome` enum that ground already uses (`Grassland, Snow, Desert, Volcanic, Corrupted`). No new enum, no decoupling. A map's recipe already has a `Biome`, so "this map's mobs" = "mobs tagged with this biome." Simplest thing that ships the functionality. (If we later want ground-look and mob-population to diverge — e.g. a cave on grassland — we add a separate field then; not now.)

## Proposed model

### 1. New fields on `MonsterDef` — as built

- `Biome biome` — which biome this mob belongs to (reuses the existing enum). **`Biome.None` is the biome-agnostic common**, matching any biome — that is how the "ubiquitous critters" idea was actually expressed; there is no separate flag.
- `int level` — the difficulty band. *(The original proposal here was `int tier` 1–4. It was built that way and then deleted; level replaced it.)*

Grouping stays a **query** (`MonsterCatalog.WeightedPick(biome, minLevel, maxLevel, includeCommons)`), never a hand-maintained roster that drifts. High-level mobs can also be `isBoss` where appropriate.

### 2. Difficulty semantics — settled by the unified stat model

Difficulty is a mob's **level**, and its numbers are derived from that level plus its six primaries through the same formulas a player uses. The "per-tier stat multiplier" this section once floated was explicitly rejected: there is no mob-only multiplier anywhere. Retuning a mob means changing its level, its primary spread, or its gear.

### 3. Recipe → biome binding (rule, not a hardcoded list)

`RecipeMobGroup` (or the recipe itself) declares intent, not a roster:

- a target **level band** — `MapRecipe.minLevel` / `maxLevel`, plus `includeCommons`
- optional overrides (force-include / exclude specific mob ids)

`RecipeGenerator` reads `recipe.biome` + the level band, queries `MonsterCatalog.WeightedPick(...)`, and weighted-picks mobs to place. Spawners are baked during generation exactly like prop groups are today.

## Biome assignment (arbitrary first pass)

Every Monster sheet gets dropped into one of the biomes. **Assignment is arbitrary for now** — purely to populate the data so the generator has something to spread. We re-tag freely once the pipeline works. (\~40 sheets available: Butterfly, Mouse, Racoon, Bear, Cyclope, Bat, Skull, Spirit, Spider, Axolot, Fish, Octopus, Kappa, Flam, Dragon, TRex, Lantern, Heart, etc.)

## Counts & division (actual)

- **66 mob folders** (`Actor/Monster/*`, each has `SpriteSheet.png`) + **20 boss folders** (`Actor/Boss/*`).
- Mobs: spread across the 5 biomes, with a handful left as biome-agnostic commons (`Biome.None`).
- Bosses: **20**, flagged `isBoss` and weighted 0 so the field spawner never rolls them.
- Boss art isn't billboard-ready (split Idle/Walk files, no single `SpriteSheet.png`). Boss defs store their real folder name in `sheet`, but rendering falls back to the default monster sheet today (`BuildMonster: MonsterSheet(sheet) ?? MonsterSheet()`). Boss art = deferred pass. Bosses are weight 0 so the field spawner never rolls them.

## Build phases

1. DONE — **Fields**: `MonsterDef` gained `biome` (reusing `Biome`, with `Biome.None` as the commons marker) and a difficulty band. *The band shipped as `tier` and was later replaced by `level`.*
2. DONE — **Bulk import tool**: **Tools ▸ unwritten ▸ Mobs & NPCs ▸ Import NinjaAdventure Monsters** (`MobBiomeImporter.cs`) scans both folders and creates one `MonsterDef` per folder in `Resources/Monsters`, with a deterministic biome. Idempotent (skips existing). `Editor/TOOLS.md` updated. **It has been run** — 86 assets exist.
3. DONE — **Catalog query**: `MonsterCatalog.WeightedPick(Biome, minLevel, maxLevel, includeCommons)` + `Eligible(...)`; falls back to the unfiltered pick so a map never spawns empty.
4. DONE — **Generator wiring**: the recipe carries `minLevel`/`maxLevel`/`includeCommons`; `RecipeGenerator.PlaceMobs` plumbs `recipe.biome` + the band in, so an empty Archetype Name draws from the map's biome.
5. TODO — **Tagging pass**: refine each mob's biome across the roster (only when we care about balance/theme). Levels are already set by the roster reset, so this step is about *theme*, not difficulty.

## Next step — the import already happened

**Done.** The importer was run and **86 mob definitions sit in `Resources/Monsters`** today (counted 2026-08-30), so the "awaiting first click" note on step 2 above is spent. Generating any recipe draws that map's biome set.

The roster has since been reset onto the shared player/mob stat model — 86 mobs across levels 3–46, every one on its stat-point budget — so a mob's difficulty is its level, not a tier tag.

## Related

- [World Creator — Spec](world-creator-spec.md) — the upstream pipeline that authors each map's biome.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — what replaced tier, and the model every one of these 86 mobs now carries.
- [Unified Unit Stats — Implementation Notes](../2-systems/active-skill-system-v3/unified-unit-stats-implementation-notes.md) — the Mob Table these mobs are authored in.
- [Story & Setting (Canon)](story-setting-canon.md) — what a mob *is* in the fiction (a blank page), which the eventual tagging pass should respect.
- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — the cards-vs-mob-categories axis waiting on this tagging.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the M2 milestone these mobs populate.