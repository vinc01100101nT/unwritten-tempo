---
name: gear-level-progression-plan
description: V4 gameplay design (forging, folds=refine, quench ladder, Ink stats, masteries) — BUILT 2026-09-20, not play-tested; the V4 doc is the record
metadata:
  type: project
---

**BUILT 2026-09-20 — all six slices. Not play-tested.** (The lines below describe the design; the doc is now a
record of what the code does.) Save is v15 (v14 = weapon trademarks, 2026-09-21; v15 = timed effects ride the save, 2026-09-23), both servers' build string `unwritten-character-v15`. The user must
re-bake mob prefabs and re-run Build UI Shell. Testing door added: `/mastery` (`/station` was removed 2026-10-05 once the smith, loom and tannery townsfolk existed). A reviewer found 16 real faults on 2026-09-20 and all the code ones
are fixed — the worst was that a townsperson's STATION was never sent to the client, so the whole crafting window
was unreachable. Decisions that day: keep the 95% ceiling on a quench; **one Ink writes ONE sub-stat**, so the
ring's two dual nodes (Defense, Critical) split into four (defence, magic defence, crit chance, crit damage) —
eighteen rollable stats, twelve sub-stats at 7.4% and six primaries at 1.85%; creature mastery = creature level,
to be balanced after a play-test.

**The V4 design was planning only until 2026-09-20.** Everything agreed lives in
`docs/2-systems/gameplay-system-v4/README.md`; its **Still open** list is what remains. Do not write a build
contract or code until the user says the open items are settled.

Key decisions, so a cold session does not re-argue them: gear is crafted, never dropped; **no Level on gear** —
a fold IS the existing refine (+N); quality F–A climbed one rung per quench, every quench consumes all folds,
each grade's fold cap is its quench gate; **rarity never comes from the grade** — since 2026-10-12 it is how rare an
item is to come by (a weapon = its trademark's rarity, materials by tier, all else Common); **no Potential**; stats come from
**Ink** (five tiers Watery→Sharp) rolling one of 16 stats, primaries rarer with a premium; weapon mastery raises
three ring stats (table in the doc) and unlocks skills, skill mastery lifts output up to +30%, both capped and
sped by weapon quality, none earned while capped; the ring becomes a character system and its slot is removed;
no level requirement to wear gear.

**Fold materials are gated by the piece's GRADE, never by its fold count** (proposed 2026-09-20, user asked it be
remembered): each grade folds with its own material band (Raw=band 1 … A=band 7), each band dropping from one
slice of creature levels, so a quench reset to +0 keeps folding with the NEW grade's band. Seven bands per craft.

**The level cap is UNDECIDED.** 46 is only the highest creature authored so far — never state it as the max; the
cap may be 99, 100 or 150. Never divide bands or curves by 46.

**2026-09-20 evening:** every design decision is closed except three deferrals (material drop plan — GM command
for now; card sockets; unlock tables for the 11 unauthored weapons). §9 of the V4 doc holds the first numbers
(user-owned, tune in play). Crafting is at town stations. Ring keeps the name "Ring". The file-by-file build plan is written:
docs/2-systems/gameplay-system-v4/implementation-plan.md — six slices, spot-checked, not started. 15 build questions
await the user at its Part 4.

**Why:** a long design conversation (2026-09-19/20) that must not be re-derived.
**How to apply:** open the V4 doc first; update it IN PLACE as decisions land. Related [[the-five-anti-invention]].
