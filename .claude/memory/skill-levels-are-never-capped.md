---
name: skill-levels-are-never-capped
description: HARD LAW 2026-10-11 — a skill can be levelled to its max and every level bought always counts; nothing (weapon grade or any other source) may cap it.
metadata:
  type: project
---

Decided by the user on 2026-10-11: "skill can be levelled up to max, and benefit any level it is accumulated, anywhere, anytime. no hard-capping from any sources."

The weapon-grade limit on how many skill levels count (Raw 1 of 5 … A 5 of 5) was removed that day from the code, the maths tests, the design docs and the wiki.

**Why:** the user met it as a bug — Still Air stayed at 50% from level 1 to 4 on a Raw sword.

**How to apply:** never add a rule that makes a bought skill level not count (grade, map, mode, gear). Weapon grade still caps WEAPON MASTERY (how much is used and earned) — that is a separate rule the user did not remove. Related: [[no-typed-sub-stats-on-any-unit]], [[mobs-get-everything-players-get]].
