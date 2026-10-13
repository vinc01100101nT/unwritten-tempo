---
name: gear-is-the-only-difficulty-dial
description: "HARD RULING 2026-10-13 — a creature's level sets its body; gear grade and folds are the ONLY difficulty dial, per creature, not tied to level; pay = level × kind × grade × fold; never a measured difficulty number."
metadata:
  node_type: memory
  type: project
  originSessionId: 4989e013-49e9-42be-8bcd-639cd13c36f1
  modified: 2026-10-13T11:12:55.186Z
---

Ruled by the user on 2026-10-13, after five rejected versions of a measured "toughness" number.

- **Level sets the body.** A creature wearing nothing gets every stat from its level.
- **Gear grade and folds are the only layer that scales how hard a creature is.** They are chosen per creature and
  do NOT have to follow its level. The user: "GRADE AND FOLDS ARE THE SCALE HOW DIFFICULT TO FIGHT A MOB IS".
- **Grade and folds are two separate variables**, in difficulty and in pay. Grade is not a small bonus on folds:
  it also opens ink slots and a higher weapon mastery limit.
- **Pay depends on level, kind, gear grade and fold, and nothing else.** Agreed rule: what level and kind give ×
  (25% more per grade above Raw) × (1 + 5% per fold). The 25% and 5% were my picks, accepted.
- **How each creature's dial is set (built 2026-10-13, my picks, accepted):** grade from the rarest material it
  drops (normals Raw/F/E/D/C for none/Uncommon/Rare/Epic/Legendary; elites D/C/B for Rare/Epic/Legendary); folds =
  the cap of the grade below, plus 0 to 5 for how hard it is to find. A piece with no folds gives nothing from
  folds, so a grade never starts at +0 above Raw.
- **No third, measured number.** The user: "if we still need a third variable, then there is something wrong with
  our sub stats balancing".

**Why:** a measured number pays around a balance fault instead of exposing it, and nobody can read it off the creature.

**How to apply:** never propose simulating a fight to price a creature. When same-gear creatures differ in real
difficulty, that is a sub-stat or gear-value bug to fix at its source. Do not read "grade climbs with level" off the
current creature files as the design — I authored that and it is the wrong shape. Related: [[income-first-then-prices]],
[[no-typed-sub-stats-on-any-unit]], [[no-number-walls-in-decisions]].
