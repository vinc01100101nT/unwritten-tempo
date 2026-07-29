# Ring System — Current Facts (Trio-Ring)

> **This is the single source of truth for the Ring.** It supersedes and replaces the retired docs *"Skill System — Trio-Ring Passive Draft (MVP Design)"* and *"Ring of Progression — Tradeable Life's-Work"*. Every "Built" fact below is grounded in code (`SkillRing.cs`, `Character.RingBonus`, `SkillEffectApplier`) and the locked *Active Skill System — V3*. Anything not marked Built is flagged as proposed.

## What the Ring is

The Ring is the character's **stat-growth engine** — a closed loop of ten generic percentage-passive stats you build up as you level. It is **built and shipped** (since 2026-06-18) and **persists in the save file** (the full ring walk is serialized). It is a separate layer from your equipped weapon: the **weapon grants your skills**, the **Ring scales their power**.

## The ten Ring stats — BUILT

Listed in **ring order** — the actual loop from `SkillRing.Ring` (the internal `StatId` enum order is different and must not be used for display):

| #   | Stat         | Short | Notes                                                                                                |
| --- | ------------ | ----- | ---------------------------------------------------------------------------------------------------- |
| 1   | Defense      | DEF   | **dual** — its % scales BOTH physical Defense and Magic Defense (there is **no separate MDef node**) |
| 2   | HP           | HP    | health                                                                                               |
| 3   | Attack       | ATK   | physical damage                                                                                      |
| 4   | Accuracy     | ACC   | % lift on Hit                                                                                        |
| 5   | Cast Speed   | CAST  | % lift on cast speed                                                                                 |
| 6   | Magic Attack | M.ATK | magic damage                                                                                         |
| 7   | MP           | MP    | mana                                                                                                 |
| 8   | Evasion      | EVA   | dodge                                                                                                |
| 9   | Critical     | CRIT  | **one node** — % to BOTH crit rate and crit damage (no separate CritChance/CritDmg nodes)            |
| 10  | Attack Speed | ASPD  | —                                                                                                    |

The loop wraps from Attack Speed back to Defense.

## The ring order (poles & tint) — BUILT

- **Poles sit dead-opposite:** Defense (#1, top) ↔ Magic Attack (#6, bottom). Accuracy/Cast Speed bridge the physical→magic transition; Critical/Attack Speed close the loop back to the top.
- **Colour families** (draft-card tint): physical/warm = Atk, Accuracy, Critical, AttackSpeed, Evasion; magic/blue = MAtk, CastSpeed, MP; survivability/green = HP, Def.

## The draft — BUILT

Every level-up you draft **one of four** options:

- **Three known (the Trio):** your current leader stat plus its **two ring neighbors** — a sliding window `[left · leader · right]`.
- **One wildcard:** a random stat from outside the trio.

Picks steer you around the ring: **center** = stack the same stat; **neighbor** = the window slides that way (that stat becomes the new leader); **wildcard** = jump across the ring. It's a steering wheel, not a menu.

## Growth curve (soft cap) — BUILT

Each stat grants a **percentage multiplier**, not flat points, so growth never goes obsolete. It follows a soft cap:

```
passive%(stat) = Cap × R / (R + K)      (R = ranks invested)
```

- **Default cap = +80%**, **K = 40** (ranks to reach half the cap).
- **Per-stat exceptions:** Critical caps at **+50%**, Evasion at **+60%** (both would trivialize combat uncapped).
- Because each further rank is worth less, the math **rewards spreading out** — a diversified build beats mindless single-stacking, though stacking is always allowed.

## The Ring only scales — role-lock — BUILT (per V3)

The Ring does exactly one thing to skills: **multiply their numbers.** It adds no new behaviors. `Character.RingBonus(StatId)` returns the passive %, and `SkillEffectApplier.AffinityMult` multiplies each skill effect's output by it, weighted per-effect (`affinity` lists).

- **Skills don't level** — a skill's power comes entirely from your Ring stats.
- **Role is locked by which stat scales what:** healing scales off **MP**; damage scales off **Attack / Magic Attack**. A support who invested MP hits like wet paper with a damage weapon; a damage build heals for crumbs with a staff. **Swapping weapons swaps your skills, never your power** — that stays bound to your Ring.
- **Ring behavioral "voices"** (auto-firing new behaviors from ring investment) are **deferred** to a later version (per V3). The Ring is numbers-only for now.

## Proposed — NOT committed (kept for reference)

A separate design direction once explored making the Ring a **tradeable in-world item** that holds the character's whole progression (levels/growth), with a visible **lineage of past bearers** and a **conservation-based economy** (rings are swapped, never minted). This is a **design concept, not committed and not in code** — recorded here only so the idea isn't lost. If it is ever adopted, it moves into the Built sections above.

---

*Source of truth: `unity/Assets/Scripts/SkillRing.cs` (stats, ring order, soft cap), `Character.RingBonus` + `SkillEffectApplier.AffinityMult` (scaling), and the locked "Active Skill System — V3" doc (role-lock, voices deferred).*
