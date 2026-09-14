---
name: stat-jumps-and-melee-ranged-attack
description: "Every primary jumps every 10 points (⌊s/10⌋² + 3⌊s/10⌋, gear counted); physical attack is MELEE (STR-led) or RANGED (DEX-led) by the held weapon; every creature's ordinary attack is physical and crits by the player's rule. Decided and built 2026-09-13."
metadata:
  node_type: memory
  type: project
---

**Decided 2026-09-13 with the user, all built the same day (unplayed — see `docs/1-planning/play-test-checklist.md`).**

- **One headline jump per primary, every 10 points, gear included:** `UnitStatMath.StatJumpBonus(s)` =
  ⌊s/10⌋² + 3×⌊s/10⌋ ("shape C": crossing 10 adds 4, 100 adds 22). STR → melee attack, DEX → ranged attack,
  INT → magic attack, VIT → defence, AGI → evasion, LUK → crit damage in PERCENT (by 100 LUK the jumps alone are
  +130% crit damage — the user accepted that). `NextJumpAt` / `JumpGainAt` feed the stat screen's `next +N at X`.
- **Physical attack is two numbers:** `MeleeAttack` (STR×1 + STR jump + DEX/2 + LUK/4 …) and `RangedAttack` (the
  mirror: DEX-led, STR/2). `PhysicalAttack(WeaponType, …)` picks by the HELD weapon — `WeaponTypes.IsRanged` (Bow,
  Firearm) is the single test; staff/wand/bare hands are melee. Normal attacks AND physical skills use the pick;
  magic skills always roll magic attack. The ring's Atk node and attack buffs lift whichever is in use. The stat
  screen shows `MELEE ATK` and `RANGED ATK`; a weapon tooltip says `MELEE attack` / `RANGED attack`.
- **Creatures follow every one of these** (`UnitStatBlock.Rederive`, fed `inputs.WithGear(Bonuses)` so worn
  primaries count). They crit (`MonsterAI3D.critChance/critMultiplier`, `RollBasicCrit` via
  `UnitStatMath.EffectiveCritChance`), a crit ignores DEF on the player too, and the player's LUK shields
  (`Character.CritShieldLuk` = P(LUK)). "Always crits" effects ignore LUK — the user: "because it is always".
- **A creature's ordinary attack is ALWAYS physical.** `MonsterAI3D.AttackType` was DELETED (every asset was
  Physical anyway); a creature holding a ranged weapon fires its projectile with crit/miss on the payload.
- **Evasion no longer touches magic** (`Combat.MagicGlanceMult` deleted at the user's instruction).
- **Main-stat bonuses on gear are FLAT, never level-scaled** — the user's reasoning: +10 STR on a maxed stat
  passes the 99 cap buying stops at, so a +1 is worth chasing at 119.
- The two accessory places (`EquipSlotType` 6, 7) are gone; never reuse those numbers.

Design record: `docs/1-planning/action-plan.md` MT-3 sub-tasks; formulas table in
`docs/2-systems/active-skill-system-v3/unified-unit-stats.md`; wiki `stats.json`.
