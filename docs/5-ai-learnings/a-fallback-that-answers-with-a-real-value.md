---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# A Fallback That Answers With a Real Value

## Reach for this when

Something new is showing up wearing another thing's name, unit or value — and nothing anywhere reported a
problem.

## The rule

A catch-all that returns a **plausible real answer** turns a missing entry into a working-looking one. When you
add a member to an enum, a column to a table, or a case to a lookup, **find every switch over that type and
check what its default does**. A default that names a genuine member of the set is the dangerous kind: the
screen reads as correct and there is nothing to search for.

When you write one, make it answer something no real member could — the word "undefined", a zero nobody could
mean, a loud marker. A broken thing that looks broken costs a second; a broken thing that looks fine costs a
round.

## Why — what it cost

A new stat was added and wired through everything that used it. One lookup — the one that turns a stat into
words for the player — had no row for it and a bottom line reading `default: return "max MP"`.

So a buff granting two units of reach displayed as "+2 MP per stack". Nothing failed, nothing logged, and the
stat behaved correctly the whole time; only its name was another stat's. The user found it on screen and had to
ask.

The same table's own comment claimed "every stat has a row, so a new one cannot silently fall through" — written
by somebody who believed it, with the catch-all doing the opposite three lines below.

## How to apply it

- After adding a member to any enum: `grep` for switches over that type and read **every** default branch.
- Treat a default that returns a real member as a bug in its own right, whether or not anything is missing today.
- The check extends past enums: a config lookup falling back to a valid-looking value, a parser defaulting to a
  real unit, an id resolver returning the first row.
- Never trust a comment that says a fall-through is impossible. Read the fall-through.
