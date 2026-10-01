---
name: saved-characters-and-sides
description: How a beaten mob turns back into a 2D character (SavedCharacters) and the general side system every targeting path asks (Sides.Hostile); built 2026-09-30, not play-tested.
metadata:
  type: project
---

Built 2026-09-30 (design: docs/3-world/saved-characters.md; lore: story-setting-canon.md — the artist, Chaos, the Fourteen Kashira).

- **Sides** (`Side.cs`): `Health.side` (Creatures / Players / None); a person is always Players. `Sides.Hostile(a,b)` is THE answer for skills (`SkillEffectApplier.GatherOpponents`, single target, re-aim, landing), creature prey (`MonsterAI3D.NearestEnemy`, drops prey that stops being hostile), the server's swing refusal (`GameNetwork.OnHitRequest`/`LandTheBlow`) and the player's cursor/auto-pilot (`Sides.HostileToPeople`). PvP later = change `Hostile` only. Player-side creatures kept in a small registry (`Health.SetSide` files them).
- **Turning back**: server-only `TurnsBackOnDeath` (added in `MobSpawner.Clone` for Normal/Elite) → `SavedCharacters.OnDeath` rolls 15% on a kill earned by a person; elite → its ninja (`SavedCharacter.eliteId`), normal → random non-ninja. The character is a body of the SAME mob def (fresh or banked by def id via `PooledMob.returnTo`) with `BillboardAnimator3D.UseSheet` swapping the picture and `MobBody.Fit` to size 1 — so stats/level/gear are the mob's. 91 `SavedCharacter` assets in `Resources/SavedCharacters` (crown measured at author time by `RigBuilder25D.MeasureGridSheet`).
- **Wire**: `NetMob` private SyncVars `savedCharacter`, `netSide`, `netLine`/`netLineAt`; `BodyKind` includes the character id; `AdoptView` wears it.
- **Gold**: `LootDropper` pays NO gold on any death; rescue gold = `SavedCharacterRules.RescueGold` (mob gold range × 20/40/60) paid when a never-fights character appears. `MonsterDef.goldMin/goldMax` survive only as that base. Rebel loot = `LootKindOfRebel` (one kind up). Ally kills credited to the saver in `Rewards.WhoEarned`.
- Every character stays 30s (`StaySecondsForAll`), drawn at half width (`BillboardAnimator3D.UseSheet` drawnWidth → `FitToHeight`), with a 2s appear aura on watchers (`NetMob.netSavedAt`, `SavedCharacters.AppearAuraFor`). Poses (attack row 4, last row: dead / cast hold / release / instant release) live in `BillboardAnimator3D` for ANY 7-row grid sheet, driven by `NetMob` (held Casting, swing actions, `ShowCast` → release) and `DeathDissolve`.
- **Feet line**: a saved character stands on its sheet's own measured feet row (`SavedCharacter.footPx`, 0 for all 91; `UseSheet` feetPivot → `Walk4`/`FootRow` pivot), NOT the mobs' `MobFootPivotY` (1.6px) — at character scale that pivot sank 0.035 units vs the 0.020 lift every body has, burying the feet (fixed 2026-09-30).
- **Portraits**: `MonsterDef.faceset` (84 filled; shadows none) and `SavedCharacter.faceset` (91) → `CreaturePortraits.For(health, shell)` → shown left of the name on `HoverPlate` and `UnitStatusPanel`. Appear aura drawn at `AppearAuraScale` 0.5.
- Lines live only in `SavedCharacterLines.cs`. Numbers pinned in tools/game-math-tests (`SavedCharacterNumbers`).

**Why:** the user's lore (mobs are corrupted pages; saving one reveals the character the artist drew) and gameplay decisions of 2026-09-30.
**How to apply:** any new targeting/aggro code asks `Sides.Hostile`, never `caster is Health`; any new gold source goes through the rescue path, never a death. Related: [[mob-summon-addressables]].
