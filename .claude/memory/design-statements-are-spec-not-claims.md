---
name: design-statements-are-spec-not-claims
description: "While designing something unbuilt, the user's sentences are the SPEC — answer the brainstorm on its own terms and quarantine any code fact-check in an \"Additional notes\" section at the end."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: af75b40f-fbdb-4ceb-8721-ffc3482df11a
---

When we are designing or brainstorming a thing that **does not exist yet**, every sentence the user says about it is the **specification**, not a claim to verify. Record it and build on it. Do NOT go read the current code and reply with what today's version does — a want cannot be wrong, so "correcting" it is answering a question nobody asked.

**Why:** during a sword-skill brainstorm the user described what a proposed taunt skill *should* do (single target, locked on, no skills while taunted, attacks with no pauses). I measured the existing taunt code and replied with a list of what it "actually" does — treating his design as a mistaken belief. It read as calling him wrong about his own game, twice in a row, and cost the session. His words: *"i demonstrated my explanation based on the taunt functionality itself, not on what the existing functions mob currently have."*

**How to apply — one mechanical test before contradicting the user:** ask *"is this a claim about what EXISTS, or a statement of what they WANT?"*
- **Not built yet / we are designing it ⇒ it's a WANT.** Restate it back as spec and build on it. Never "correct" it. If a piece is genuinely impossible or expensive to build, say only that — as a cost, not as a correction.
- **Already built / they asked "what does it do", "is this a bug", "why" ⇒ it's a CLAIM.** Then RULE ZERO applies in full: measure the real code before saying anything.

**Measuring is still allowed during a brainstorm — but it is QUARANTINED (his refinement, 2026-08-24).** Never let a code finding into the brainstorm answer itself. Put every code-derived fact in one clearly separated **"Additional notes"** section at the very END of the reply; that is where he reads them, deliberately after he has read the design answer. The brainstorm section answers the design question on its own terms and nothing else. His words: *"pwede ka mag measure ng code, pero ihiwalay mo sa sagot mo sa brainstorm… dun ko yon babasahin, hindi sa brainstorm section."*

The tell I missed: the skill had no asset, no code, no name in the game — literally nothing to be wrong about. Also relevant: he was answering a question I had asked him, which makes his reply an answer, not an assertion to audit.

Related: [[the-five-anti-invention]], [[dont-agree-reflexively]] (that rule is about not rubber-stamping his *opinions* — it never licenses fact-checking his *design*), [[answer-design-questions-with-the-real-window]].
