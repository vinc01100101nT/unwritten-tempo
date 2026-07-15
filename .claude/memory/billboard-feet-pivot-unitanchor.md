---
name: billboard-feet-pivot-unitanchor
description: "All billboards (player+mobs) are FEET-pivoted now; transform.position = feet on ground, collider stacks UP, vertical offsets go through UnitAnchor."
metadata: 
  node_type: memory
  type: project
  originSessionId: 0c3fe0d7-a3c9-4712-9faf-6095daf76c7e
---

Decided 2026-07-03. Extended the [[pivot-is-anchor-source-of-truth]] rule to BILLBOARDS — removing the old "mobs are centre-pivoted" carve-out. Now EVERY unit (player, mobs, NPCs) has `transform.position` = its FEET on the ground; the size-invariant anchor (base == position whatever the unit's size).

**The invariant + its owners:**
- **Sprite renders UP from the pivot.** Mobs: `SpriteSheetCache.Walk4` slices at foot-line pivot `(0.5, MobFootPivotY=0.1)` — `MobFootPivotY` is the SINGLE OWNER of the foot-padding fraction; `Build25DSpike.SpriteFoot` reads it. Player + NPCs (`BakedBillboard8`, via `SpriteSheetCache.Row`): now ALSO foot-pivoted — `Row` takes a `pivotY` (cache-keyed), `BakedBillboard8.PivotY = 0.5 - feetOffset` (the baked foot line), and the `BakedSprite` child sits at `localY=0` (no lift; `SetFeetOffset` re-slices). So the sprite pivot is at the feet, not the chest. NOTE: this is the VISUAL sprite pivot; the player's transform/collider pivot is a separate thing — see [[player-feet-float-skinwidth-fallback]].
- **Collider stacks UP from the feet:** `center.y = height/2` (bottom at pivot, never straddling). Player `Player3DController.DefaultCapsuleCenter/capsuleCenter` = `(0, height/2, 0)`; mob CC `center=(0, 0.5, 0)` in `Build25DSpike.SpawnMob`.
- **Spawn puts the pivot on the ground:** `Build25DSpike` mob+player `transform.position = foot` (dropped the old `FootOffsetY` centre-lift, now deleted).
- **`UnitAnchor.cs` = the ONE choke point for vertical anchors.** `Feet/Center/Head(t)` + `HalfHeight(t) = 0.4*lossyScale.y` (owns the constant that was typed thrice in MobNameplate/UnitTargeter). Any code needing a unit's centre/head reads it here — NEVER hand-adds `Vector3.up * magic`. Migrated: aim snap (`Player3DController`), feet ring (`UnitTargeter`), nameplate (`MobNameplate`), projectile homing (`Projectile3D`), mob projectile (`MonsterAI3D`), stun/total popups. (Generic damage numbers still use `DamagePopup`'s own +0.5 lift — left alone.)

**⚠️ Baked content is stale after this flip** — existing mobs baked at the OLD elevated Y (`ground + 0.4*lossyScale.y`) float once the sprite re-slices foot-pivoted. One-time fix = `Tools ▸ unwritten ▸ Quick Fixes ▸ Reground Baked Mobs` (`RegroundBakedMobs.cs`, QUICKFIX): drops each stale mob's pivot by `HalfHeight` + sets `CC.center.y=height/2`; idempotent (skips already-migrated). Ran it across all ~40 map scenes 2026-07-03. Player is re-baked via `Setup Systems Scene`.

Related bug this exposed: [[player-feet-float-skinwidth-fallback]].
