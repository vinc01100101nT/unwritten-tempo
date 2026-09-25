---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - process
---

# The Three-Brain Design Process

The standing workflow for designing **any** system in unwritten — weapons, skills, mobs, progression, economy, anything. It exists because ad-hoc design drifted on us (a feature got "stress-tested" into a redesign, a resource got mis-framed as default behavior, docs fell out of sync). This flow puts a human filter early, separates *inventing* from *breaking* from *polishing*, and ends every round by writing the result down.

Reuse it for every design unit. It's process, not game content — so it lives here, game-wide, not under any one feature's folder.

## The three brains

| Brain | Role |
| --- | --- |
| **Claude** | Ideation, **grounding/measuring** against real code + docs (never guesses), triage, and documentation. |
| **Vince** | Decisions, the early **filter**, and the **sole authority to LOCK** anything. |
| **GPT** | The third brain — three hats, switched by an explicit header, **never blended**: **Co-Designer**, **Stress-Tester**, **Reviewer**. |

**GPT's three hats (it wears exactly one per message):**

- **CO-DESIGNER** — challenge assumptions, brainstorm, propose mechanics/alternatives, simplify. Nothing is sacred unless marked LOCKED.
- **STRESS-TESTER** — QA only. Treats V3 + LOCKED as the spec; never redesigns intentional features. Reports **BREAKS-WITHIN-RULES** and **CONSEQUENCES**, ranked by severity, with reproduction + math/order-of-operations. Ends with **"Needs a design decision."** when no in-rules fix exists.
- **REVIEWER** — architecture/editor pass: clarity, DRY, naming consistency, composability, will-future-authors-understand-it, doc quality.

## The flow — advance only when the gate is met

 1. **Frame** — *Claude + Vince.* State the design target and pull the constraints it must obey (identity, bans, anything already LOCKED), grounded in real code/docs — **measure, don't guess.** **Gate:** constraints written down and agreed.
 2. **Open ideas** — *Claude.* A divergent candidate list: name + one-line fantasy + how each maps to existing systems (trigger / effect / channel). No filler forced to hit a count. **Gate:** a filterable list exists.
 3. **Filter** — *Vince.* Add / cut / update / ask. Claude answers and measures where needed. **Gate:** Vince's shortlist.
 4. **Shallow brainstorm** — *Claude + Vince.* Quick alignment on the shortlist — resolve obvious gaps/overlaps, lock each item's *intent*. Stay shallow; deep mechanics are stage 4's job. **Gate:** a clean brief ready for GPT.
 5. **Co-design** — *GPT: CO-DESIGNER.* Extend, prune, propose alternatives, simplify. **Gate:** GPT returns a candidate set.
 6. **Validate** — *Vince + Claude.* Triage GPT's set against LOCKED rules; Claude flags identity leaks / dead knobs / stat-sticks and measures; Vince decides changes. May loop **4↔5**. **Gate:** an agreed draft.
 7. **Stress-test** — *GPT: STRESS-TESTER.* Only BREAKS-WITHIN-RULES + CONSEQUENCES, ranked, with repro/math; never redesigns. **Gate:** report delivered.
 8. **Resolve** — *Vince + Claude.* Fix each finding or make the decision; Claude grounds fixes in real data. **Loop 6↔7 until zero unresolved BREAKS-WITHIN-RULES.**
 9. **Review** — *GPT: REVIEWER.* Clarity, DRY, naming, composability, future-author friendliness. **Gate:** naming/DRY clean.
10. **Lock + document** — *Claude.* Mark LOCKED; write to the design doc + wiki (+ any code TODOs); one source of truth, no drift. **Gate:** docs updated.

## Standing rules

- **GPT's hats never blend** — switch by explicit header (`ROLE: STRESS-TESTER`, etc.). Blending is what caused an intentional feature to be "fixed" as a bug.
- **Nothing is sacred unless LOCKED.** Once Vince LOCKs something, only Vince reopens it — GPT and Claude treat it as spec.
- **Dedup on the one-sentence fantasy, not mechanics** — two skills can differ mechanically yet be the same fantasy (that's a clone).
- **Measure, don't guess** — Claude grounds every claim about how the system behaves in real code / docs / data, and says so when something is unverified.
- **Loops are bounded** — only **4↔5** (co-design) and **6↔7** (stress-test) may iterate, each until its gate is met. Everything else moves forward.
- **A round isn't done until it's documented** (stage 9). No drift between the design doc, the wiki, and the code.

## Worked example

The **Sword** weapon roster (see *Active Skill System — V3 / Sword — Skill Roster*) was the first design taken through this flow end-to-end — including the failure that motivated it (GPT stress-testing an intentional Bladedance feature as a bug, and Momentum being mis-framed as default instead of a rollable passive). Read that roster as the reference for what a completed pass looks like.
---

## Related

- [Sword — Skill Roster](../2-systems/active-skill-system-v3/sword-skill-roster.md) — the worked example: the first design taken through this flow end to end.
- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — the LOCKED spec a STRESS-TESTER treats as the rules.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the identity litmus test stage 1 pulls constraints from.
- [Skill Numbers & Balance Baseline](../2-systems/active-skill-system-v3/skill-numbers-balance-baseline.md) — what "measure, don't guess" reads from when the design is numeric.
- [Dev Contribution Log](../1-planning/dev-contribution-log.md) — the record of who steered which round.
