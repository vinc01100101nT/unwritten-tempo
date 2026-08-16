---
name: stack-piles-two-states-and-readings
description: "Stack piles — when a mechanic needs TWO states, and the count-vs-carried-value reading a consumer cashes them by"
metadata: 
  node_type: memory
  type: project
  originSessionId: c033d9ec-424c-4812-b20d-61eefcb38f29
---

Built 2026-08-16 for Sword's **Brace → Retribution**. Three rules that generalise past that pair.

**A mechanic needs TWO states when one pile must vanish on use while another outlives it.** Brace = armed
charges (spend down to nothing, state disappears) + `StateId.DeferredDamage` = the debt they created (must
survive and still come due). Merging them is impossible, not untidy. Name the second for WHAT IT IS, never
for the skill that fills it — but a shared combat verb naming effect+state+skill is the established pattern
here (`SkillEffectKind.Parry` / `StateId.Parry` / `skill.sword.parry`), so `StateId.Brace` is correct.

**Consuming splits into TWO independent questions, two dropdowns.** `StackReading` (Count / CarriedTotal) =
how the pile is MEASURED; `StackPayoff` = what that measurement BUYS. Folding a reading into the payoff enum
destroys an axis meant to grow — the user's exact words: *"the damage is in the dropdown for a reason that we
will add more there later."* See [[dont-agree-reflexively]].

**A percentage needs no conversion.** `State.magnitude` is ALWAYS a bonus fraction (+30% is stored as 0.3;
ten +3% stacks fold to 0.30), so `CarriedFrom` sums `magnitude + intAmount` into ONE float. A pile carries one
kind of value, never both. Effects pull the cast's readings via `StackVars`, measured once at cast start and
handed to every effect — that's how one skill's damage and its lifesteal read the SAME pile.

**Gotchas:** `Heal` is NOT a self-verb (deliberately — heals can target others), so a lifesteal effect must set
`side: Caster` or it heals the mob. A stack-driven output is FLAT and cannot crit — there's no roll behind it.

Run the stacking-compatibility check in the project CLAUDE.md before ANY new state kind.
