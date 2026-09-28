---
status: current
owner: vince
last-updated: 2026-09-27
tags:
  - ai-learnings
---

# An Agreed Item Is Not "Still Missing"

**Reach for this when:** you are about to write "still missing", "next piece", or a question about a part of the task
the user already agreed to, or the user asks "why isn't X there, it was part of the task?"

## The rule

Before reporting a build as done, check every sentence of the agreed contract against what was built, case by case,
**including the cases your first idea does not reach**. If one isn't covered, build it before reporting. Listing it
as "still missing" or turning it into a question hands the user a hole they already asked you to fill. If a genuine
decision stands in the way, make it, label it as yours, and build.

## Why

Agreed task (2026-09-27): exact puddle reflections, with puddles "on the spots where it makes sense" on fields with
slopes and levels. The first build put water only on exactly level ground, and added a runoff bonus there. It never
asked what happens on a field with **no** level ground. Measured afterwards: 17 of 39 fields, including 3 Swamp fields
that are supposed to always hold water, came out with no puddles at all. The report listed that as "still missing" and
ended with a question about pond depth. The user had to point out it was already in the task, which cost a full round,
and the pond version then took one more build.

## How to apply

1. Before the report, write out the contract's cases as a list: every kind of map, state or input the sentence
   covers (flat town, stepped field, cliff field, rolling field). Your design will have been drawn from one of them.
2. Measure each case against the build, using the real data, not reasoning. Here that was one pass over every built
   map's ground, counting where water ends up.
3. Any case that comes out empty or wrong: fix it in the same turn. A zero for a whole category is a hole, not a
   finding to report.
