---
name: feedback-check-standing-decisions-before-offering
description: "Before OFFERING to remake, rework or replace anything that already exists, search memory and handoffs for a standing decision on it — an offer that breaks a lock gets approved by mistake"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 605f945c-3281-4842-813b-03aa221e090f
  modified: 2026-10-10T07:03:45.120Z
---

Before putting "rework X", "rebuild X" or "replace X" into a list of things I can do, search for a decision the user
already made about X: grep the memory folder and `handoffs/` for X's name (the weapon, the clip, the screen) together
with "locked", "not to be", "keep", "never". A hit that locks X means X is reported as locked and is NOT offered.

**Why:** 2026-10-10 — I measured the greatsword attack's blade inside the body and offered a rework alongside three
legitimate fixes. The user said "go fix them all" and I built a new greatsword attack. They had locked that attack
the day before; the lock sat in memory (inside the dual-wield decision file) and in a handoff, and I never looked.
A user approving a list trusts that every item on it is allowed — the check belongs BEFORE the offer, not after the go.
It cost a built-and-discarded animation and their words: "why did you create a new one?"

**How to apply:** the search is one command and runs when I am WRITING the offer, per item:
`grep -rn -i "<thing>" .claude/memory handoffs | grep -i -E "lock|not to be|keep|never"`. Also: finding the lock late
is not a footnote — stop, say it first, and undo the work. Locked today: [[locked-attack-animations]].
