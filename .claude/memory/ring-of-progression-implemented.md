---
name: ring-of-progression-implemented
description: "Ring of Progression IMPLEMENTED 2026-07-01: RingProgress vessel (own level/XP) on an equippable Ring item; unequip suppresses passives+skills; active-skill pick per 10 ring points ALLOCATED; SaveData v4"
metadata: 
  node_type: memory
  type: feature
  originSessionId: f3e5f22b-f7a2-42c5-b701-e170d3811ed5
---

Built 2026-07-01 (was design-concept [[ring-of-progression-concept]], now code). High-effort multi-file feature.

**Model:** the Trio-Ring passive draft + active-skill ownership moved OFF the character onto a **`RingProgress`** vessel (`RingProgress.cs`): its own `Level`/`XP` (curve `20 + level*12`), and it owns `PassiveRanks Passives` + `SkillRoster Skills`. `Character.Ring` owns one; `Character.Passives`/`Character.Skills` are pass-throughs to it.

- **Same kills feed both tracks:** `Character.AddXP` also calls `Ring.AddXP` (only while `RingEquipped`). Char level → flat stats; RING level → banks one passive draft point (grant moved OFF char level; removed the old `Passives.AddPoint()` + `SetBankedPoints` reconcile).
- **Active-skill picks gate on ring points ALLOCATED, not char level:** `SkillRoster.EarnedPicks(pointsAllocated)` = allocated/10 (renamed const `UnlockEveryLevels`→`PointsPerUnlock`; added `PointsToNextPick`). Allocated = `Ring.PointsAllocated` = `Passives.TotalRanks()`.
- **Equippable vessel:** new `EquipSlotType.Ring = 12`; authored "Ring of Beginnings" item (ItemStarterSet, in Resources/Items). `Character.EnsureRingEquipped()` auto-equips it at Awake + after load (equipment isn't persisted yet). InventoryStore/BagSeeder SKIP ring-slot items (Character owns the one vessel). Paperdoll got a Ring slot (ProgressionWindows).
- **Suppression ("diminished, not erased"):** `Character.RingEquipped` gates everything. `PassivePercent(stat)` returns 0 when no ring → all effective-stat getters route through it. HotbarPanel blocks activation + dims the bar when no ring. SkillDraftPanel shows only "No ring equipped" (hides level/XP/walk entirely).
- **Save:** `SaveData` v4 adds `ringLevel`/`ringXp`; pre-v4 saves derive ring level from `TotalRanks + BankedPoints`.

**HUD:** `XpBarHUD.cs` — bottom-of-screen bars, character XP left half + ring XP right half meeting at center; wired in UIShellBuilder (needs Build UI Shell to appear). Other bottom HUD lifted +28px (in UIShellBuilder).

**Other fixes same session:** SkillDraftPanel content now in a ScrollRect (skills grid was overflowing the window); hotbar hover tooltip moved to a shared canvas-root panel (per-cell tooltip was occluded → "sometimes didn't show"); hotbar icons drag-to-rearrange (`HotbarBindings.Swap` + `HotbarSlotDragSource`).
