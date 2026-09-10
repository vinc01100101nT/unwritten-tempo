---
name: deathblow-per-stack-term-is-inert
description: "OPEN DEFECT — Deathblow's documented '+40% per Momentum stack' resolves to +0.4 RAW DAMAGE per stack, because the pile side of a number never gets the basis applied."
metadata:
  node_type: memory
  type: project
---

**Found 2026-09-09 while reading the channel maths. NOT FIXED — nobody has decided the fix yet.**

**Symptom:** Deathblow's signature double-dip — the roster doc's "600% ATK + 40%/stack" — contributes almost
nothing. At 10 Momentum stacks it adds **4 damage** to a hit in the thousands.

**Root cause, measured.** `ScaledNumber.Resolve` (`SkillDef.cs:165`) is
`(value × basis) OP (pileReading × scaler) × ringMult`. For a damage multiplier the **basis is the caster's
rolled hit**, but the pile side is NOT multiplied by it. Deathblow's asset holds `value: 6`, `op: Add`,
`read: Count`, `scaler: 0.4`, so the arithmetic is:

    damage = (6 × rolledHit  +  stackCount × 0.4) × ringMult

`scaler: 0.4` therefore means **0.4 points of damage per stack**, not 40% of the hit. `StackVars.Read(Count)`
returns the raw stack count, so there is nothing else in the chain to scale it.

**Correctly scoped:** Momentum's OTHER contribution — its standing +3%-per-stack damage buff — rides a different
path entirely (the outgoing-boost pile in `UnitOffense.SpendCharges`, `UnitOffense.cs:44`) and is unaffected. So
Deathblow still benefits from Momentum; it is specifically its own per-stack bonus that is inert.

**Why it was not fixed on the spot:** making the pile side answer to the basis is a change to the ONE shared
resolve that every stack-scaled number on every skill goes through. It needs checking against the others first —
several deliberately want a flat pile contribution.

**Before touching it:** grep every asset with `useStacks: 1` and decide, per number, whether its pile side is in
damage units or in plain units. A blanket change will silently rescale skills nobody was looking at.
