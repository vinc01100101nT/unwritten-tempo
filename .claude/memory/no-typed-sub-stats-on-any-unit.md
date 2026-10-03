---
name: no-typed-sub-stats-on-any-unit
description: "HARD LAW 2026-10-02 — no unit (player, creature, elite, shadow, NPC) carries a hand-typed sub-stat; every one is computed from level, primaries, gear, ring or a shared spec."
metadata:
  type: feedback
---

**No unit carries a hand-typed sub-stat. Ever.** Defence, armour cut, health, attack, accuracy, evasion, crit,
regen, movement speed, attack reach, swing timing, lifesteal, reflect — every one is COMPUTED: from level, the six
primaries, worn gear (and its words), the ring, timed states, or one shared value every unit reads
(`CharacterSpec`, `UnitStatInputs.Default`, `UnitStatMath` constants). A field that lets someone type one of these
onto a creature, the player, or a body is a bug, not a design knob. Ruled by the user 2026-10-02: "that is an absolute
bug we must remove. delete any fields that allows inputting substats manually."

**Why:** a typed number is a second answer the formulas never see. The scar: every creature carried a typed "armour
cut" (hard DEF %, 0–32%) that nothing computed and no player could ever get, which alone turned a fresh player's
fair fight against a level-1 Plopling into a coin flip (56% wins; 83% with the typed cut removed — offline replay of
the real formulas).

**How to apply:**
- Before adding any combat field to `MonsterDef`, `Health`, `MonsterAI3D`, `Character` or an archetype: if it is a
  sub-stat, it is not a field — compute it, or read the shared spec. Runtime results that the stat block writes are
  `[System.NonSerialized]` so no inspector can type them.
- What stays authored on a creature are its INPUTS (level, primaries, gear, ring ranks, weapon mastery) and its
  BEHAVIOUR (aggro/leash/wander radius, preferred range, flee threshold), element, on-hit ailment, loot.
- When the user says "remove all of X", every item of X is in — never offer "keep it" as an option. See
  `docs/5-ai-learnings/an-agreed-item-is-not-still-missing.md`.
- Related: [[mobs-get-everything-players-get]], [[unified-unit-stats-implemented]].
