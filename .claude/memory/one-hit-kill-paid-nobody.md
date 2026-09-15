---
name: one-hit-kill-paid-nobody
description: "A creature killed by the first blow that hurt it paid no gold, no XP and fired no on-kill passive — the damage door recorded the attacker AFTER the sink where death runs. Fixed 2026-09-14."
metadata: 
  node_type: memory
  type: project
  originSessionId: 99f08ae2-01f3-49ab-a776-212cb043a3a2
  modified: 2026-09-14T11:52:03.356Z
---

**Symptom (play-test 2026-09-14):** killing an Axolot, a Spirit, a Mollusc (and a GoldRacoon) earned no gold and wrote
no loot line; a Reptile that took several blows paid. Looked like "spawner-born vs summoned" — it was not.

**Root cause (debugger, cited):** `CombatAuthority.Land` called `target.ApplyDamage(info)` — inside which `Health` runs
the death, `LootDropper` asks `Rewards.WhoEarned` (= `LastAttacker`), XP in `MonsterAI3D` asks the same, and
`PassiveEvents.Fire(LastAttacker, OnKill, …)` runs — and only AFTERWARDS recorded `hurt.NoteAttacker(info.attacker)`.
So the killing blow's author was never known on a one-blow kill. The log showed each "earned" line printed BEFORE the
hit line that caused it.

**Fix (uncommitted):** the door records the attacker before the sink and, when the blow then dealt 0 (miss, parry,
full absorb), hands the record back to the prior attacker — `NoteAttacker` now accepts null for that. Compiles, game
math clean; not yet play-tested. Test: `/summon 1 normal 1`, kill it in one blow, expect an "earned" line.

**Also found:** the training dummy keeps its loot component, so `/killall` after hitting it pays gold.
