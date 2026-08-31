---
name: council
description: Use when the user types /council, or asks for several agents/brains to investigate one thing together, debate it, and agree before acting. For hunting a bug in a large feature, auditing a system, or weighing an optimization where one opinion is not enough. Covers the round protocol, the shared record file, and the unanimity gate.
---

# Council

Several brains investigate ONE target independently, argue in writing, and nothing is acted on until they all
agree. The coordinator (you, the main session) is one of the brains AND the only voice the user hears.

## Defaults (set by the user 2026-08-27 — do not change without being told)

| Knob | Default |
| --- | --- |
| Model for every spawned brain | `opus` — pass `model: "opus"` on EVERY `Agent` call, never omit it |
| Total brains | **3, INCLUDING the coordinator** → spawn **2** sub-agents |
| Subagent type | `general-purpose` (full tool access) |
| Effort per brain | The coordinator's call, per brain, per round — see *Setting effort* below |
| Agreement | **Unanimous.** A split STOPS the work and goes to the user |
| Record | `handoffs/council-<slug>.md` |

`/council <target>` may override: `agents=N` (total, including the coordinator → spawn N−1).

## Step 0 — ask for the angles. ONCE.

The user names the angles per run. Before spawning anything, ask ONE batched question (`AskUserQuestion`):
what angles do they want for this target, one per brain (coordinator included). Offer 3–4 concrete suggestions
drawn from the actual target, not generic ones — e.g. for a combat bug: "state lifecycle", "the maths",
"the authored data". Include the coordinator's own angle in that list.

That is the ONLY question this skill is allowed to ask before working. Everything else ambiguous gets the
coordinator's decision, labelled plainly as the coordinator's.

Distinct angles are the entire point. Brains given the same brief converge on the same wrong answer and the
council is worth nothing.

## Step 1 — open the record

Write `handoffs/council-<slug>.md` with: the target in one plain sentence, the roster (each brain's name +
angle + which one is the coordinator), and an empty `## Round 1` heading. The user reads this while it runs and
a cold session resumes from it, so it is plain language throughout — no jargon, no invented shorthand.

## Step 2 — the round

Spawn all sub-agents **in one message** so they run in parallel, then do the coordinator's own angle
first-hand while they work — the coordinator INVESTIGATES, it does not just referee.

Every brief MUST carry all of this. It is not optional and it is not summarisable — a spawned brain starts
COLD with none of the conversation, so anything not written here does not exist for it:

1. **The target**, concretely: the feature, the symptom, the files or systems in scope.
2. **This brain's angle**, and that it must NOT stray into the others' angles.
3. **The other brains' angles**, so it knows what is already covered.
4. **Everything already established** this session that bears on the target — measured facts, ruled-out causes,
   the user's own words about the symptom. Over-include. A thin brief is the top failure mode here.
5. **The standing rules, verbatim in the brief** (do not assume they are inherited):
   - Measure, never guess. Every claim about a cause, a value, or what code DOES carries a `file:line` you
     opened THIS run. No citation ⇒ delete the sentence or go read the file.
   - Never invent an identifier, a path, a menu string, or a number. "The source does not say" is a valid
     and preferred answer.
   - Never widen a precise fact into a general claim when restating it.
   - **Do not edit ANY file.** This is an investigation. Findings only.
   - Say "I did NOT verify X" out loud for anything reasoned but not measured.
6. **The required output shape**:
   - `FINDINGS` — each with the evidence and the `file:line` it came from.
   - `PROPOSED NEXT STEP` — one concrete action, or "none".
   - `UNVERIFIED` — what it reasoned about but did not measure.
   - `CONFIDENCE` — a percentage, honest.
7. **Its effort level**, stated as the first line of the brief (`EFFORT: HIGH`).

### Setting effort

There is no effort parameter on a spawned agent — effort is set BY THE BRIEF. State it explicitly and match
the depth of instruction to it:

- **LOW** — a bounded lookup with a known answer shape ("does this path ever run for a mob?").
- **MEDIUM** — a sweep across a handful of files, or verifying a specific hypothesis.
- **HIGH** — an open hunt through a large feature, a lifecycle walk, or anything that already survived one
  failed fix. Default to HIGH for round 1 on a real bug.

Pick per brain per round. A brain confirming one narrow fact does not need HIGH; the one walking every state
of a system does.

## Step 3 — write the round down

Append each brain's findings to the record **in its own words**. Do not compress a dissent into agreement, and
do not drop a finding because it looks weak — the weak-looking one is the tie-breaker later. Then state, in
the record, whether the brains AGREE on the next step.

## Step 4 — the gate

- **All agree** → report the agreed next step to the user in plain words and ask for the go. Nothing is edited
  until they say so.
- **They split** → STOP. Report both positions to the user in the brains' own words, with the evidence behind
  each, and say plainly which parts are measured and which are reasoned. The user breaks the tie. Never pick a
  side, never average them, never let the majority quietly carry it.
- **They agree but nobody measured anything** → that is not agreement, it is three guesses. Run another round
  demanding evidence.

## Step 5 — further rounds

Continue an EXISTING brain with `SendMessage` (its id or name) — it keeps its own investigation and does not
restart cold. Spawning a fresh `Agent` for round 2 throws that away and repeats round 1.

Give each continuation the other brains' findings from the last round so it can attack them. That is where a
council beats one brain: round 2 is each angle trying to break the others.

## When it is done

Once the user accepts a step and it is carried out, the coordinator does the work — the brains investigate,
they do not edit. Update the record with what was decided and what happened. Delete the record once the target
is resolved and the lesson is in a memory file, same as a handoff.
