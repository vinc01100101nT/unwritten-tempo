---
name: name-knobs-concretely-one-job
description: "HARD RULE from user (2026-07-19, angry) — name every field/label/effect after the concrete thing it changes; one job each; no dead knobs."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 898e1ee0-c2cc-40b1-a5ba-ae390cd842fe
---

# Every knob names ONE concrete thing it changes; one job each; no dead knobs

The user blew up (repeatedly) over three mistakes that are all the SAME root error — I designed
data/UI/effects around ABSTRACT umbrellas instead of the concrete thing each controls. The general fix:

**Before shipping ANY field, label, variable, enum, effect, or UI control, ask: "In THIS specific case,
does it map to exactly ONE real thing a designer/player would name — and is it named that?" If not:
rename it, split it, or delete it.**

Three concrete failure modes this kills (all from the active-skill work):

1. **Name by what it AFFECTS, never an abstract umbrella.** Banned words as labels: `potency`,
   `magnitude`, `strength`, `value`, `amount` (bare), `power`, `factor`. Instead: "proc chance",
   "damage per tick", "heal amount", "duration", "slow amount", "damage reduction". A reader must NOT
   have to decode what the word means in context. (I shipped an affinity channel called "potency" that
   meant *chance* on a stun and *damage* on a fireball — meaningless.)

2. **One unit = one job (single responsibility).** Never nest sub-effects inside another effect (I baked
   stun-proc + execute INSIDE the Damage effect). If tempted to add a 2nd concern, make it a SIBLING
   effect. Same for functions/fields.

3. **No dead knobs.** If a field has no meaning for a case, don't show/store/ask for it. (I showed
   `magnitude` on a Stun, which is binary and ignores it; and an affinity row that scaled nothing.)
   Presence implies meaning — a meaningless field reads as a lie. Hide it, or don't create it.

**Why:** vague/mixed/dead knobs force the user to reverse-engineer intent and breed real bugs (the
"potency scales chance vs magnitude" ambiguity). Concrete + single-purpose + no-dead-knobs is
self-documenting and can't drift. Apply this to gameplay data, editor tooling, and any authored schema.
See [[active-skill-mvp-ring-revamp-2026-07]].
