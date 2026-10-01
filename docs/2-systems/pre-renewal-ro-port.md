---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# Pre-Renewal RO Port — Phase Plan & Formulas

Porting the game's combat/stat math from its custom **linear** system to authentic **pre-renewal Ragnarok Online** formulas, in shippable phases. Each phase is independently testable; combat-touching phases are gated behind a higher effort dial.

> **Legal note:** game mechanics and math formulas are not copyrightable (only specific expression — art, names, marks, code — is). Reusing RO's formulas is fine; we use our own art/marks/code. See the chat discussion for detail.

## Decisions locked

- **Model:** pre-renewal (not renewal). Rationale: the codebase is already linear/pre-renewal in spirit, the player-base nostalgia leans pre-renewal, and it's simpler to tune.
- **Combat fidelity:** **authentic % DEF + per-class HP/SP tables** (chosen by the dev). Applies to Phase 3 (DEF split) and Phase 4 (HP/SP + ASPD).
- **Approach:** phased, smallest-blast-radius first. No big-bang — the game is balanced around the current combat, so swapping % DEF + HP tables in one commit would desync every mob/number with no stable baseline.

## Phase status

- **DONE — Phase 1: stat-allocation cost curve**
- **DONE — Phase 2: HIT / FLEE / Crit authentic values**
- **DONE — Phase 3: ATK/MATK sub-bonuses + the % DEF split + crit-bypass + crit-shield + mob-DEF authoring.** ⚠️ **The mob half was superseded by the unified stat model** (2026-08-10) and this line's original mechanism is gone: mob DEF/LUK is no longer hand-authored per archetype, and the class it named as the stamper no longer exists in the project. Today a mob's numbers are derived from its level and six primaries, and the roster is authored in the Mob Table. Fine-tuning is still ongoing playtest work. See [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md).
- **DONE — Phase 4: per-class HP/SP + ASPD.** Vitals are now per-class with VIT/INT as PERCENT multipliers; ASPD gains a DEX term. :warning: Notable rebalance — see Phase 4 section.
- **BUILT, not yet played — Phase 5: the experience curve.** Ragnarok's base table and its creatures' experience, each as one formula; earning stops at level 99; the ring and weapon mastery paid a share of every kill on the same curve. See Phase 5 section.

## Phase 1 — Stat allocation (DONE)

- Cost to raise a stat from value V to V+1 = **⌊V/10⌋ + 2** (1→2 costs 2, 10→11 costs 3, 20→21 costs 4 …). The pre-renewal soft cap.
- Status points gained on reaching base level L = **⌊L/5⌋ + 3**.
- Starting grant = **48** points at level 1 (exactly enough to take three stats from 1→9, the classic creation budget).
- Base stat cap = **99**.
- Code: `Character.cs` (`StatCost`, `StatusPointsForLevel`, `TotalStatPointsEarned`, `StatPointsSpentRaising`, `CostToRaise`, `CanRaise`, `MaxStatValue`, `GrantStatPoints`, `RespecStats`), `AddStat` rewritten, `ReconcilePointsToLevel` + level-up award updated. `StatPanel.cs` shows per-stat next-point cost / MAX. The `/statpts` GM command grants points. `Systems.unity` `startingStatPoints` set to 48.
- Migration: old saves keep stored points (no schema change); use the **Respec** button to rebudget onto the new curve.

## Phase 2 — Accuracy + Crit (DONE)

- HIT = **BaseLvl + DEX + ⌊LUK/3⌋**
- FLEE = **(BaseLvl + AGI + ⌊LUK/5⌋) × (1 + Evasion%)**
- Hit chance = **100 + HIT − FLEE**, clamped 5–95 (already in `Combat.cs`; unchanged).
- Crit chance = **LUK × 0.3%**
- Crit damage = **×1.40**
- Code: `Character.cs` Hit/Flee getters + `CritPerLuk`, `BaseCritMultiplier`, `LukPerHit`, `LukPerFlee` constants.

## Phase 3 — ATK/MATK + DEF split (DONE)

The combat-feel rewrite. It landed as one unit (formulas + mob stats + retuning) — the "TODO, HIGH" this section used to carry contradicted the status list above it and was the stale half. **Verified in code 2026-08-30**; every line below is a thing that exists.

- **Status ATK bonus — DONE, in this game's own shape.** Melee attack = STR + STR's jump (plus DEX/LUK contributions); ranged attack mirrors it with DEX leading; MATK = INT + INT's jump. The jump is `UnitStatMath.StatJumpBonus` = ⌊stat/10⌋² + 3×⌊stat/10⌋ — RO's plain square plus a step, so the early jumps are felt — and VIT, AGI and LUK carry one too (defence, evasion, crit damage).
- **DEF split (authentic) — DONE.** damage = `ATK × (1 − hardDef/100) − softDef`, where hard DEF = % reduction, soft DEF = flat (from VIT). Same split for MATK vs MDEF. Lives in `Combat.AfterDefense`.
- **Crit semantics — DONE.** A crit **ignores FLEE** (guaranteed hit) AND **ignores both hard + soft DEF**; `Combat.Missed` skips the miss roll on a crit and `AfterDefense` returns the raw attack. Element/race/size modifiers still apply. All three wiring tasks are in: the `crit` flag reaches the damage call, the miss roll is skipped, and the DEF step is skipped whole.
- **Crit shield — DONE.** Target LUK lowers attacker crit chance (`UnitStatMath.CritShieldPerLuk`, 0.3%/pt), and mobs carry LUK because they carry all six primaries now.
- **Mob-stat pass — DONE, by a different route than planned.** Rather than hand-shaping DEF/VIT/LUK per mob, the whole roster was reset onto the player's own stat model, every creature on its point budget — today 98 creatures, levels 1 to 170 (see [Mob Roster](../3-world/mob-roster.md)). See [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md).
- **Retuning pass — STILL OPEN.** % DEF, the skill passive-% layer and cast breakpoints all interact, and this needs play rather than code.

## Phase 4 — HP/SP + ASPD (DONE)

- **HP/SP per class:** the level-driven base curve is scaled by a class factor, then VIT/INT scale it as a PERCENT (authentic — they multiply, not add flat). `MaxHP = round((baseMaxHP + hpPerLevel×(lvl−1)) × classHP × (1 + VIT×0.02)) + equipFlat`; SP analogous with INT×0.015. Class factors: **Swordsman HP 1.4 / SP 0.7**, **Mage HP 0.85 / SP 1.7** (`Character.VitalsFor`).
- **ASPD:** AGI is primary, **DEX adds ¼** (`DexAspdShare`). Kept as the game's speed-MULTIPLIER model, not the RO 0–200 scale (that would require rewriting the attacker's cooldown — out of proportion).
- **Class-switch clamp:** `Character.ReconcileVitals()` re-clamps HP/MP on a class change (wired into `Player3DController.SetClass`).
- **Tuning dials** (consts in `Character.cs`): `VitHpPercent`, `IntSpPercent`, `DexAspdShare`, and the factors in `VitalsFor`.

:warning: **Notable rebalance — expected, by design:**

- VIT switched from flat (×8/pt) to %, so VIT is now a *multiplier* on level-driven base HP — weaker early, scales with level (the authentic relationship). 
- The default **Mage** is now genuinely squishy (≈17 HP at L1 vs the old 28) and has a deeper SP pool; **Swordsman** is the opposite. Early game is more fragile until you level / invest VIT. Retune via the dials above if too punishing.

## Phase 5 — Experience (BUILT 2026-09-30, not yet played)

Ragnarok's base experience table is written by hand; here it is one formula fitted to it, and so is what its normal
creatures pay by level. Both live in `UnitStatMath` and are pinned by `tools/check-game-math.sh`.

- **A level's cost**, base level L to L+1 = **6.25 × L^1.23 × e^(0.098 × L + 0.021 × max(0, L − 90)²)**, rounded. The
  last term is Ragnarok's climb from 90 to 99. Against Ragnarok's normal-class table a typical level is within 8%,
  the worst (the levels it jumps by hand: 29, 39, 49, 59, 69, 70) within 41%, and 1 → 99 costs 409,648,693 in all,
  1.01× Ragnarok's 405,234,427.
- **A kill pays**, by the creature's own level = **0.31 × L² × e^(0.02 × L)**, rounded, never below 2 — the curve
  Ragnarok's normal creatures follow. An **elite pays 10×** and a **shadow 20×** (Ragnarok's bosses pay 5–10× a normal
  creature of their level). Nothing typed onto a creature changes it, and there is no level-gap rule, as in
  pre-renewal. A creature that levels up mid-fight pays its new level.
- **Kills a level takes:** 3 at level 1, 15 at 30, 49 at 50, 180 at 70, 706 at 90, 4,728 at 98 — about 26,800 from 1
  to 99, 62% of them after 90.
- **The cap is base level 99.** At 99 the character earns nothing more; the ring and weapon mastery still do. The
  cap stops EARNING only: a GM's `/lvl` sets any level, and a level above 99 is kept through a sign-in.
- **The ring is paid a third of every kill's experience, the weapon in hand two thirds** (times its grade's
  training speed), and both climb the SAME curve. All three tracks are paid by one rule — the kill's experience
  times the track's scale (1, ⅓, ⅔ × grade speed), kept to two decimals (the nearest hundredth, a half going up)
  — so a kill worth 2 pays 2.00 / 0.67 / 1.33. Every track stores and shows its experience to two decimals. Beyond that: ring level R costs what base level R does, and mastery M to M+1
  what base level M+1 does. The ring has no cap and keeps levelling after 99 — a third of a climb to 99 stands it at
  about ring 93. Weapon mastery stops only at its grade's cap; at par, mastery 70 (the highest cap) comes around
  base level 74.
- **Older saves (below v16)** keep every level, and the experience inside the
  character's level, the ring's level and every weapon's mastery keeps the same share of the way through it.


- **Breakpoint inflation:** pre-renewal has hard breakpoints (DEX 150 = instant cast, ASPD thresholds). Stat-granting gear must be budgeted / soft-capped, or gear trivializes the breakpoints. Options: budget stat gear, soft-cap gear stat contributions, or keep a fixed cast floor.
- **Equipment + cards complement pre-renewal:** cards are pre-renewal's native progression and map cleanly onto the game's two-layer model (flat-stat cards → flat layer; %-cards → passive-% layer; proc cards → StatusController/MultiHitBurst hooks). Card-slot count is authored per item and is not gated by anything else — a piece's own numbers come from its folds and its quality grade, and the stats written onto it with Ink.

## Source

Designed in chat with the dev, 2026-06-25. Phases 1–2 implemented same session.

## Related

- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — where these formulas ended up living, for players and mobs alike.
- [Unified Unit Stats — Implementation Notes](active-skill-system-v3/unified-unit-stats-implementation-notes.md) — the code-facing record of the same.
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — the percentage layer that sits on top of these numbers.
- [Skill Numbers & Balance Baseline](active-skill-system-v3/skill-numbers-balance-baseline.md) — the skill-side bands anchored to these player stats.
- [Active Skill System — V3](active-skill-system-v3/README.md) — the combat rules (crit ignores defence, magic never misses) these formulas serve.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the RPG-numbers line under the foundation.
