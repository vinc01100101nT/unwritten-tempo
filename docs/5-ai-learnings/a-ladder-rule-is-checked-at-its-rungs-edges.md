# A Ladder Rule Is Checked Where Two Rungs Meet

**Reach for this when:** you are about to propose a rule that sorts many things onto a ladder of tiers, each tier
with its own range inside it — a grade and a fold count, a rank and a level, a plan and a seat count.

## The rule

Before showing the rule, take the WEAKEST thing the higher tier can hold and the STRONGEST thing the lower tier can
hold, and measure both with the system's own numbers. If the higher one is not the stronger, the rule is wrong
however sensible each half looks. It takes one measurement, and it is the first thing the reader will ask.

## Why

2026-10-13, creature gear. I proposed "grade from the rarest thing a creature drops, folds from how hard it is to
find", with folds counted from zero inside every grade. It put four creatures in grade C with no folds, paying
double. The user asked one question: "is C +0 stronger than Raw +5?" Measured on a level 19 creature: C +0 had
attack 47 and evasion 27, identical to wearing Raw gear with no folds, while Raw +5 had attack 56 and evasion 33. A
piece with no folds gives nothing from folds whatever its grade. The rule paid double for the weaker body. It cost
a round, and 42 of 84 rows had to be written again.

I had read the function that returns nothing at zero folds earlier in the same session. Having read it was not the
same as checking my rule against it.

## How to apply

- Write the two edge cases down by name before the rule goes in a reply: "lowest of tier N+1" and "highest of
  tier N".
- Measure both through the real system, never by reasoning about what a tier "should" give. Here that was one call
  to the creature stat function with the gear set two ways, on a copy that was never saved.
- If they are the wrong way round, the usual repair is that a tier starts where the one below it ends.
