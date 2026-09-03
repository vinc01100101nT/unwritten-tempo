---
name: scaling-fixes-are-never-deferred
description: NEVER defer a fix because the player count is still small — "wait until you have players" is an invalid answer, always. Fix it the day it is found.
metadata:
  type: feedback
---

When something is found that will hurt at a large player count — a per-connection cost, a
per-player allocation, a linear scan that grows with the catalog, anything whose cost is
multiplied by the number of people online — **it gets fixed in the same session it is found.
Today. Not filed, not deferred, not "revisit when there are real numbers."**

The user's standing instruction, given 2026-09-03, verbatim in substance: *"when there are
fixes that assumes large amount of players, we do not defer it, we fix it now. statements like
'dont do it now, you dont have a lot of players yet' is absolutely invalid. whatever that needs
fix for large player base will be fixed today. not tomorrow. today."*

**Why:** this project is a server-authoritative MMO, so almost every server-side cost is
multiplied by the number of people signed in. A cost that is invisible at one player is the
same cost at two hundred — the code does not change, only the multiplier does. Deferring means
the fix lands later, on top of more code that assumed the slow shape, when it is harder and
riskier than it is today. "We have no players yet" describes the TEST CONDITIONS, not the code,
so it is never evidence about whether the code is right.

**These are BANNED as reasons to defer, in a report or in my own reasoning:**
- "leave it alone until you have real player numbers"
- "fine at this scale / trivial at ten players"
- "I have not profiled a running server, so let's wait"
- "it is dwarfed by the cost next to it" — the neighbour being expensive is a reason to fix
  BOTH, never a reason to keep the small one.

**How to apply:**
1. The moment I write a sentence containing a per-player multiplication ("at 200 players that's
   N"), that is the trigger. Fix it in the same turn; do not end the turn having only named it.
2. Not having profiled is a reason to GO MEASURE, never a reason to postpone — the same move
   [[changes-must-persist-in-repo]] and RULE ZERO already demand.
3. When the fix is genuinely a design change rather than a small edit, say the three bullets and
   get the go IN THAT TURN — the contract still applies, but the schedule does not slip.
4. Sits alongside [[performance-first-prebake]]: that one protects the frame, this one protects
   the player count. Both are hard constraints, neither is a "later".

*(Scar: I found the server rebuilding a throwaway container on every buff change, wrote out the
per-player arithmetic myself, and then recommended NOT fixing it because "you have no player
numbers yet" and it was small next to the network send. The user rejected the whole category of
reasoning.)*
