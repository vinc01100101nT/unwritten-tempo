---
name: attacker-owns-the-accuracy-roll
description: "Attack order is crit -> accuracy -> damage. Combat.Lands is RANDOM so it must roll exactly once: the ATTACKER rolls it and the result travels in DamageInfo.missed; defenders only present the miss."
metadata: 
  node_type: memory
  type: project
  originSessionId: ea2b8e03-67d0-4d72-917c-7b6916fc1138
---

**The rule.** An attack resolves in this order, and the order is load-bearing:

```
roll crit  ->  roll accuracy (once, on the ATTACKER)  ->  roll damage magnitude  ->  defence  ->  land
```

**Why it can't be reordered naively.** `Combat.Lands` is `CombatRng.Chance(...)` — a **random roll**. Calling it twice gives two different answers. Before 2026-08-13 the ATTACKER computed the damage and the DEFENDER separately rolled whether it hit, which meant the miss check could not be moved earlier without either double-rolling or restructuring. Also, a crit **cannot be evaded**, so the miss check needs the crit result first — crit had to be split out of the damage roll (`Character.RollCrit` / `RollDamageMagnitude`, with `RollDamage` kept as a wrapper).

**Fix (commit `54473237`).** `Combat.Missed(crit, attackerHit, defenderFlee)` is the ONE place the whole accuracy condition lives. The attacker calls it and the answer rides along in **`DamageInfo.missed`**; `Health.TakeDamage` and `Character.TakeDamage` only PRESENT the miss (red = player-dealt, white = mob-dealt) and never re-roll. On a miss, `amount` is 0 — **no damage roll happens at all**.

**Facts worth not re-deriving:**
- Only **TWO** accuracy rolls exist in the whole game: player→mob (`Player3DAttacker`) and mob→player (`MonsterAI3D`). Skill strikes always land (`attackerHit: -1`), including the overkill paths.
- Mobs never crit, so their `Combat.Missed(false, …)` reduces to exactly the old condition.
- `CombatRng` is unseeded Unity `Random` — there is **no** replay/netcode determinism to preserve, so changing the draw order is safe.
- Verified over 400k simulated attacks: miss rate and damage-on-hit unchanged; ~12% fewer random draws per attack.

**Do NOT do this for performance.** The user asked for the reorder believing the damage roll was wasted work. Measured with the real .NET runtime: **one damage roll = 21 nanoseconds**. Even at 10,000 attacks/sec that's 0.2 ms/sec (~0.02% of a core). The real reason is ORDERING — knowing hit/miss before anything is spent is what a one-shot "next hit" buff needs, and it removes the trap where a whiff eats a charge. Combat arithmetic is never the perf problem here; per-frame allocation across many mobs is (see `PERFORMANCE.md`).

**Verification trap hit this session:** the first equivalence run used HIT 120 vs FLEE 80, which clamps to a 100% hit chance — it produced **zero misses** and "passed" while testing nothing. Re-ran with FLEE > HIT. A green result that cannot go red is not evidence. See [[debug-empty-result-verify-tool-coverage]].
