---
name: conservation-as-balance-driver
description: "The through-line design principle for unwritten — conservation (every gain pays a matching cost, nothing minted from nothing) is the universal balance driver across economy, duels, and combat"
metadata: 
  node_type: memory
  type: project
  originSessionId: b844183b-62dd-496e-b164-5924a53135ce
---

Design principle the user arrived at 2026-06-30/07-01 during the Ring/skill brainstorm and explicitly wants to carry forward: **the law of conservation is the universal driver of "balance" — apply it to any system to keep it running.** Every gain must pay a matching cost somewhere, or self-limit on use; nothing is minted from nothing.

Same one principle now runs three unwritten systems coherently (strong sign the design is sound):
- **Ring economy** — rings can't be minted/crafted/dropped, only swapped; total rings = total players. See [[ring-of-progression-concept]].
- **Ring duels** — winner's level/stat gain = loser's loss; power flows between players, none created.
- **Combat balance (PvP)** — powerful effects conserve: CC decays on repeat (DR), costs are paid via finite picks/slot caps/opportunity cost. See [[active-skill-design-charter]] §10.

**Why:** gives a single test to apply when designing ANY new system — "where does this gain's cost come from?" If the answer is "nothing," it will inflate/imbalance.

**How to apply:** when adding a new mechanic that grants power/resources, locate the matching sink or self-limit BEFORE shipping. Faucet without drain = the failure mode.
