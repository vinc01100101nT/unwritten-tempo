---
name: additive-not-compounding-buffs
description: "LOCKED — multipliers in the same category ADD their bonuses, never compound; players mentally sum percentages, so the tooltip and the engine must agree."
metadata: 
  node_type: memory
  type: project
  originSessionId: 38ccde61-b0b2-4cef-b2af-28d7ab1bb477
---

**The rule (locked 2026-08-12, user's call):** when several sources modify the SAME value, sum their bonuses and
apply once. Two +30% attack buffs = **×1.60**, never ×1.69.

**Why (the user's reasoning, as a gamer):** "when I see different buffs or skills that multiply the same value, I
always tend to add them and multiply to that value — it is easier than the compound." Compounding makes a "+30%"
tooltip a lie: its real worth depends on what else happens to be running and on the order things landed. Additive
keeps the player's mental math and the engine's math identical, and keeps ceilings predictable by hand (which the
attack-speed cap and Deathblow's per-stack scaling are both tuned against).

**Where it lives:** `UnitStates.Recompute` accumulates BONUSES per channel (a ×1.3 pile contributes +0.30) and
converts to a multiplier once at the end — attack, attack speed, cast speed, vulnerability. Additive pools (guard cut,
shield, leech, reflect, blind) already summed. **Slow is the deliberate exception** — the harshest wins, since two
slows aren't a bigger slow. Each final multiplier is floored at 0 so debuff-shaped piles can't flip damage to healing.

**Apply it to:** any NEW scaling channel, the skill tooltips, and the wiki — present the SUM ("+60% Attack"), not each
buff separately for the player to combine. See [[name-knobs-concretely-one-job]] for the sibling rule on naming what a
number actually changes.
