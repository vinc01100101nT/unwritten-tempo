---
status:
owner: vince
last-updated: 2026-10-24
tags:
  - ai-learnings
---

# 🗣️ The User's Word Is Not My Word

## Reach for this when

The user's instruction uses a word you have also been using for something specific — "volley", "set", "hit", "wave",
"stack", "round" — and you are about to build on it. Or a fix you shipped matched your reading of their sentence and
they say it is still wrong.

## The rule

A word you both use can mean two different things. Before building on a user's word that you also use with a meaning
of your own, say back what you take it to mean in one plain sentence ("by volley you mean one drop of rain, a single
moment every creature is hit once — not the whole cast?"). And when their instruction narrows a feature ("it should
only total per X"), narrow it — never switch the feature off as the fix.

## Why

2026-10-21, this project. The user wrote "Cloudburst's multi-hit damage accumulates on the next set — it should only
total per volley." To me a volley was one drop of rain; to the user it was the whole cast. Three rounds followed:

1. I switched the running yellow total off — the user lost the yellow digits entirely and was furious.
2. I made each drop start its own total — each drop hits a creature once, so the "total" just repeated the hit.
3. Only when the user asked "what do you mean by volley?" did the two meanings surface. The original behaviour (one
   total for the whole cast) plus a reset on the next cast was the whole fix.

Every round was spent building correctly on a wrong meaning.

2026-10-24, the same failure on a whole subject. The user found a hole in the skill-balancing formula and wrote
"make a general fix, should we spawn the planner and the red team". They meant the balancing SYSTEM; I read "fix" as
the HTML calculator I had just built, declined the planner and red team as overkill for a browser tool, and patched
the tool. A round spent, and the question of how the formula becomes part of the project went unanswered. The tell
was in their own sentence: nobody asks for a planner and a red team for a one-file tool.

## How to apply

- When an instruction leans on a word you have used yourself, define it back before the first edit — one sentence,
  in the user's terms, no theory.
- If the existing switches cannot express the narrower behaviour they asked for, build the narrower option in the
  same round; never ship "off" and ask afterwards.
- When a fix "doesn't work" twice, stop and check the meaning of their key word before a third attempt.
