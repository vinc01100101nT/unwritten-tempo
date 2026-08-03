# Biome Mob Families — Tiered Spawning System

**Status:** Design / not yet built · **Owner:** vince · **Date:** 2026-06-27

A layer on top of the existing mob data foundation (`MonsterDef` + `MonsterCatalog` + `MapRecipe` + `RecipeGenerator`). Goal: turn the NinjaAdventure monster sprites into ready-to-use mob data, group them by **biome × difficulty tier**, and have the map generator auto-scatter the right mobs (and their spawners) for a map's biome during the generate phase — no per-recipe hand-rostering.

## Scope note — sprites don't matter yet

This is a **functionality-first** effort. We do **not** care which sprite a mob uses right now — any sheet is fine, looks get swapped freely later. The whole goal is the **plumbing**: tag mobs by biome+tier, and have the generator spread them into maps grouped by biome. Don't let art block the data/generator work.

## Goals

- Every NinjaAdventure mob in `Art/NinjaAdventure/Actor/Monster/*` becomes a usable `MonsterDef` asset in `Resources/Monsters`.
- Mobs are grouped by **biome** (reusing the existing 5-value `Biome` enum) and a **difficulty tier 1–4**.
- A `MapRecipe` already declares a `Biome`; the generator pulls that biome's mobs at the requested tier band from the catalog and bakes spawners automatically.
- Adding a new mob asset auto-joins the right biome/tier without editing every recipe.

## Non-goals

- No art work. **Mobs stay NinjaAdventure sprites**, assigned arbitrarily for now — swap looks anytime later. (Charcoal/Quaternius applies only to player + NPCs.)
- Not changing combat math, AI, or how a mob is stamped into a scene (`MonsterDef.ToArchetype()` stays the contract).
- No new "town/field/dungeon" generators — still the single `RecipeGenerator`.

## Current foundation (what already exists)

- `**MonsterDef**` (`unwritten/Monster`) — full mob stat block keyed by stable kebab `id` (e.g. `mob.slime`); has `sheet` (folder under `Actor/Monster`), `weight`, `isBoss`, element/status/loot. **No biome or tier field yet.**
- `**MonsterCatalog**` — load-once cache over `Resources/Monsters`; `Get(id)` / `Boss` / `WeightedPick`.
- `**MapRecipe**` — already has a `Biome biome` field (`Grassland, Snow, Desert, Volcanic, Corrupted`), used today only for ground look. `RecipeMobGroup` references a **single** mob by name/id, or empty = weighted mix of all field mobs.
- **No** `Boss/` **art folder** — "bosses" are ordinary mobs flagged `isBoss`. Tier 4 can map onto that.

## Decision: reuse the existing `Biome` enum

Mobs are tagged with the **same** `Biome` enum that ground already uses (`Grassland, Snow, Desert, Volcanic, Corrupted`). No new enum, no decoupling. A map's recipe already has a `Biome`, so "this map's mobs" = "mobs tagged with this biome." Simplest thing that ships the functionality. (If we later want ground-look and mob-population to diverge — e.g. a cave on grassland — we add a separate field then; not now.)

## Proposed model

### 1. Two new fields on `MonsterDef`

- `Biome biome` — which biome this mob belongs to (reuses the existing enum). Optionally an `Any` value for ubiquitous critters.
- `int tier` (1–4) — difficulty band within its biome.

Grouping stays a **query** (`MonsterCatalog.ByBiome(biome, minTier, maxTier)`), never a hand-maintained roster that drifts. Tier 4 mobs can also be `isBoss` where appropriate.

### 2. Tier semantics (what "level" scales)

Tier = difficulty. Each mob def carries its own stats, so a tier is just a tag we filter/scale on. Start simple: assign mobs to tiers and let their authored stats differ. A per-tier stat multiplier can come later if we want one base mob to scale across tiers — not required for v1.

### 3. Recipe → biome binding (rule, not a hardcoded list)

`RecipeMobGroup` (or the recipe itself) declares intent, not a roster:

- a target **tier band** (e.g. tiers 1–3, weighted toward 2)
- optional overrides (force-include / exclude specific mob ids)

`RecipeGenerator` reads `recipe.biome` + the tier band, queries `MonsterCatalog.ByBiome(...)`, and weighted-picks mobs to place. Spawners are baked during generation exactly like prop groups are today.

## Biome × tier assignment (arbitrary first pass)

Every Monster sheet gets dropped into one of the 5 biomes and a tier 1–4. **Assignment is arbitrary for now** — purely to populate the data so the generator has something to spread. We re-tag freely once the pipeline works. (\~40 sheets available: Butterfly, Mouse, Racoon, Bear, Cyclope, Bat, Skull, Spirit, Spider, Axolot, Fish, Octopus, Kappa, Flam, Dragon, TRex, Lantern, Heart, etc.)

## Counts & division (actual)

- **66 mob folders** (`Actor/Monster/*`, each has `SpriteSheet.png`) + **20 boss folders** (`Actor/Boss/*`).
- Mobs: 5 biomes × 4 tiers × **3** = 60 into the cells; the remaining **6** become biome-agnostic commons (tier 1).
- Bosses: 5 biomes × 4 tiers × **1** = 20, one per cell, `isBoss`.
- Boss art isn't billboard-ready (split Idle/Walk files, no single `SpriteSheet.png`). Boss defs store their real folder name in `sheet`, but rendering falls back to the default monster sheet today (`BuildMonster: MonsterSheet(sheet) ?? MonsterSheet()`). Boss art = deferred pass. Bosses are weight 0 so the field spawner never rolls them.

## Build phases

1. DONE — **Fields**: `MonsterDef` gains `biome` (reuses `Biome`), `tier` (1–4), `biomeAgnostic` (the commons flag).
2. DONE — **Bulk import tool**: `Tools ▸ unwritten ▸ Mobs ▸ Import NinjaAdventure Monsters` (`MobBiomeImporter.cs`) scans both folders, creates one `MonsterDef` per folder in `Resources/Monsters` with tier-scaled stats + deterministic biome/tier/common. Idempotent (skips existing). `Editor/TOOLS.md` updated. *Awaiting first click by vince.*
3. DONE — **Catalog query**: `MonsterCatalog.WeightedPick(Biome, minTier, maxTier, includeCommons)` + `Eligible(...)`; falls back to the unfiltered pick so a map never spawns empty.
4. DONE — **Generator wiring**: `RecipeMobGroup` gains `minTier`/`maxTier`/`includeCommons`; `RecipeGenerator.PlaceMobs` plumbs `recipe.biome` + the band into `ResolveArchetype`, so an empty Archetype Name draws from the map's biome.
5. TODO — **Tagging pass**: refine biome + tier across all mobs (only when we care about balance/theme).

## Next step (verify)

vince clicks **Import NinjaAdventure Monsters** → \~86 assets appear in `Resources/Monsters`. Then generate any recipe (its `Biome` is already set) and the spawned mobs should be that biome's set. Legacy MobRoster-migration assets (Slime/Bat/Kappa/Flam/Cyclope/Dragon by display name), if present, can be deleted — the folder-based set supersedes them.

## Related

- Foundation: mob/item/map data tables (`MonsterDef` + `MonsterCatalog`).
- Generator: `MapRecipe` + `RecipeGenerator` (the one content-driven world generator).
- World Creator / Map Painter pipelines (biome authoring upstream).