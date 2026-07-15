---
name: combat-multihit-status
description: "Reusable RO-style multi-hit (one cast, several ticks) + status-effect (stun) systems for the 2.5D game"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0ef68d6e-0ec8-4ede-9860-5ef602f27d9c
---

Built 2026-06-18 — the RO "one cast, several damage numbers, each a separate roll/proc" pattern, as reusable
pieces (first user: Mage **Meteor**). The point of multi-hit is per-hit PROC rolls (status/leech/cards), not raw
damage — see the design rationale we discussed.

- **`MultiHitBurst.Run(center, radius, hits, interval, onHit)`** (`MultiHitBurst.cs`) — snapshots the mobs in a
  blast once (death-safe), then calls `onHit(mob, hitIndex)` for each living target across `hits` waves spaced
  `interval`s apart (numbers sequence). Runs on its OWN GameObject (survives the caster being disabled). The
  caller's `onHit` does the per-tick damage roll AND any per-tick status proc. Reuse for any multi-hit AoE.
- **`MonsterAI3D.Stun(seconds)`** — freezes the mob (reuses the `Stagger` freeze: no move/attack) + a spinning
  **swirl over the head** (`FX/Magic/Spirit`, via `StatusFX`) + a one-time "Stun!" popup. `stunSheet` is
  assigned to all mobs in `BuildMonster`. The reusable status template — copy it for poison/freeze/etc.
- **`StatusFX.cs`** — looping head-indicator that follows a unit + billboards for a duration; self-destroys
  when the unit dies. Generalized [[GuardShieldFX]].

**Meteor wiring:** `Player3DMeteor` exposes `hits` (3), `hitInterval`, `stunChance` (0.25/hit), `stunSeconds`;
each tick deals `damageMult/hits` (so multi-hit ≠ power buff) and rolls stun independently. Per-tick `onHit`
RETURNS its damage → `MultiHitBurst` pops a big **yellow combo TOTAL** per mob at the end (RO-style); the per-tick
purple numbers still show. `DamagePopup.Spawn/SpawnText` got a `scaleMult` overload for the bigger total.

**Ground-target reticle:** `GroundTargeter.cs` (reusable) — `Begin(player, radius, castRange, onConfirm)` →
AIM mode: a LineRenderer circle follows the cursor (clamped to castRange), **left-click drops / Esc cancels**;
`Player3DController.ReadInput` skips while `GroundTargeter.Active`. Meteor's `Activate()` enters aim →
`CastAt(point)` casts (MP/cooldown only on confirm). Reuse for any placed AoE.

When real on-hit systems land (cards / lifesteal / status weapons — M2+ RPG depth), multi-hit becomes
build-defining; per-hit procs should roll inside the skill's hit callback. See [[class-system-design]],
[[hotbar-ihotbarskill]].
