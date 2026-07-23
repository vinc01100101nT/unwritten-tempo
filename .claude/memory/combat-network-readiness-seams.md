---
name: combat-network-readiness-seams
description: All combat damage + RNG route through CombatAuthority/CombatRng seams; networking deferred until the vertical slice proves fun.
metadata: 
  node_type: memory
  type: project
  originSessionId: 549d036b-6591-4fd4-bdaf-d0d6b4bdc952
---

Committed `9f393fac` (2026-07-23). Client-side prep that makes combat plug-ready for a future server-authoritative layer, with **zero gameplay change** — every path resolves to today's local-player values.

**The seams (route new combat code through these — do NOT re-fork a direct call):**
- **`CombatRng`** — the ONE source of every combat-OUTCOME roll (crit, damage/magic spread, off-hand block, accuracy in `Combat.Lands`, proc in `SkillEffectApplier.Fires`, mob attack-spread + on-hit ailment). Movement/wander/VFX random stays on `UnityEngine.Random`. Later becomes server-seeded in one file.
- **`CombatAuthority`** — the single damage front door. `Apply(IDamageable, in DamageInfo)` for hits (9 sites: skill ticks, basic swings, mob attacks); `ApplyTrue(IDamageable, amount, color)` for raw DoT ticks + thorns reflect (3 sites). GM instakill cheat in `GmCommands` intentionally left direct.
- **`IDamageable` / `DamageInfo`** — one damage contract on both sinks (`Health` mobs + `Character` player). `DamageInfo` carries amount/element/isMagic/isCrit/attackerHit/overkill/source so routing preserves accuracy, overkill cascades, and reflect.
- **`SkillEffectApplier`** — the acting caster is threaded EXPLICITLY (param) instead of reaching for the `Character.Instance` global mid-resolve.

**DECISION (2026-07-24): target is a server-authoritative MMO, but networking is DEFERRED.** Prove the game is enjoyable in a vertical slice first (first class kit + one region + a quest→dungeon→boss thread) before building netcode. The seams sit dormant and cost nothing until then.

**When resuming networking:** phases N0–N5 (transport spike → player replication → server-authoritative mobs → server-authoritative combat via these seams → accounts/persistence → scale) live in the Tempo doc *"Network-Readiness — Combat Audit & Refactor Plan"* and epic **VIN-25** (marked done; reopen when ready). Netcode stack + topology were left UNDECIDED on purpose. See also [[direction-2p5d-pivot]].