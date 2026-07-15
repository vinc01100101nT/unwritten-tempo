---
name: loot-and-melee-reach
description: "Loot pickup = click→route(arriveDistance 0.12)→collect; collect reach now = Player3DAttacker.EffectiveRange (melee). Sizes: TargetIndicator 0.3, WorldPickup.displaySize 0.2"
metadata: 
  node_type: memory
  type: reference
  originSessionId: f3e5f22b-f7a2-42c5-b701-e170d3811ed5
---

Tuned 2026-07-01.

- **Melee reach** = `Player3DAttacker.EffectiveRange` = `range(0.5) × UnitScale.MeleeReachFactor` (factor = `0.5*(ForPlayer+ForMob)`, so reach shrinks with scaled units).
- **Loot pickup flow** (`WorldPickup` + `Player3DController`): click loot → `BeginPickup` → `order=Move` routes to it (`arriveDistance 0.12`) → `TryCollectPending` collects when in range. `WorldPickup.collectRadius` (1.1) sizes the clickable trigger sphere AND was the collect threshold.
- **Change:** loot collect now gates on `attacker.EffectiveRange` (matches melee — no looting from afar); `collectRadius` stays the click-target size only. Safe because `arriveDistance 0.12` < any EffectiveRange.
- **Sizes (were "huge"):** `TargetIndicator` (procedural gold down-arrow over attack target) localScale `0.6→0.3`. `WorldPickup.displaySize` (world-unit billboard size, normalized via `displaySize*ppu/texSize`) `0.35→0.2`. Both runtime (TargetIndicator code-built; WorldPickup added by `LootDropper`) → no rebake, effect on next Play.
