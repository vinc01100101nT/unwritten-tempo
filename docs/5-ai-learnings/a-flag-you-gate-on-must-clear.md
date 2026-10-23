---
status:
owner: vince
last-updated: 2026-10-22
tags:
  - ai-learnings
---

# 🚩 A Flag You Gate On Must Actually Clear

## Reach for this when

You make new behaviour wait on a flag that already exists — "a run is in progress", "is busy", "has a pending
request" — or a feature works the first time and then never again until a restart.

## The rule

**Before gating anything on an existing "is it happening" flag, read every place that SETS it and every place that
CLEARS it.** A flag nobody else waited on can be wrong for years without anyone noticing: it only has to be right
the moment something starts trusting it. If it is set in more places than it is cleared — a finished coroutine, an
early exit, a disabled component — the new gate locks shut the first time it closes.

## Why

2026-10-22, the Staff's orbs. Orb presses were made to wait while the caster's own skill run was going, so the
stats a run was cast with stay fixed for it. The gate read the existing `RunInProgress`, which is set when a run
starts — and was only ever cleared when the server refused a press. A run that finished normally left it set
forever. Nothing had noticed, because its only reader was a debug line. After the first Ashfall every orb press did
nothing and the Release button stayed stuck; the user found it in play-test and it cost that round plus a rebuild
of the client and the game server. One search for the field's name would have shown a single place that cleared it.

## How to apply

1. Search the flag's name. List every assignment: where it becomes true, where it becomes false.
2. Walk every way the thing it describes can END — finishing normally, breaking out early, the object disabled or
   destroyed, the press refused — and check each one clears it.
3. If one does not, fix the flag in the same change, at the end of the thing it describes, and say so in the report:
   it changes what the flag's older readers see too.
