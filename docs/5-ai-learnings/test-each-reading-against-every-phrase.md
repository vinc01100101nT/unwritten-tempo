---
status:
owner: vince
last-updated: 2026-09-30
tags:
  - ai-learnings
---

# Test Each Reading Against Every Phrase

**Reach for this when:** an instruction gives a number, ratio or share ("a third", "2×", "half") and it could apply to
more than one quantity — a level or the experience that buys it, a price or a rate, a count or a total.

## The rule

Write down every quantity the number could apply to, then test each reading against **every other phrase in the
same instruction**. The right reading makes all of them true and none of them redundant. If more than one reading
still survives, ask before building — saying your reading in the plan and building anyway is not the same as
asking, because the person only finds out when they read the finished report.

## Why

Asked to "scale the ring and mastery with base level — the ring is 1/3 of base, mastery 2/3, so they all follow the
same curve", the ratios were read as LEVEL ratios (ring level = a third of base level). The plan said so in one
line, and the whole thing was built: new level mappings, a ring cap of 33, fractional curve lookups, save upgrades,
pinned checks and doc rewrites. The user meant a share of each kill's EXPERIENCE. That cost a full round, and all of
that work was rewritten.

The instruction already ruled out the wrong reading. "So they all follow the same curve" is only literally true if
the ring and mastery climb the SAME cost table as the base level — which is what the experience reading gives. Under
the level reading, each track needed its own remapped curve, so that phrase became false. One pass over the
sentence, phrase by phrase, would have caught it.

## How to apply

1. List the candidate quantities: "1/3 of base" → a third of base LEVEL, or a third of base EXPERIENCE.
2. For each candidate, re-read the rest of the instruction and mark every phrase true, false or redundant under
   that reading.
3. Keep the reading with no false phrase. If two survive, ask one question that names both, before any code.
4. In the plan, state the consequence that tells the two readings apart ("the ring stops at 33" vs "the ring keeps
   levelling after 99"), not just the reading. A visible consequence is something the user can veto at a glance; a
   restated ratio is not.
