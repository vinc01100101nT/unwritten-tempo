---
name: debug-symptom-composition-not-interpretation
description: Debug trap — a bug report names a CAUSE as if observed; verify what the symptom is made of / which layer, or you fix the wrong layer for rounds.
metadata:
  type: feedback
---

When a symptom could plausibly come from more than one layer (render vs geometry, cache vs source, UI vs data, timing vs logic, overlay vs underlying object), the #1 way debugging stalls for many rounds is: **a causal INTERPRETATION gets smuggled into the report as if it were a raw OBSERVATION, and every later fix targets the layer the interpretation named instead of the layer that's actually broken.** "grass shows through" (really a missing overlay stone), "the value is null because the API is down" (really the parser drops it), "the cache is stale" (really the source is wrong), "it's a race" (really a plain ordering bug) — all are guesses dressed as facts.

**Why:** the wrong-layer premise is invisible once inherited — it silently scopes the whole search. Fixes aimed at the wrong layer can't work *and* often perturb things (making the bug look like it "fights back"), burning rounds and trust. The tell is **several DIFFERENT fixes all failing**: they almost always share one false premise, so the bug that ate N rounds is usually cracked the instant someone questions the premise instead of trying fix N+1.

**How to apply:**
- **Split "what I SEE" from "what I THINK causes it."** Restate any report (or handoff) that way; treat the causal half as a hypothesis to verify, never a fact to build on — even your own from earlier.
- **Classify the symptom's COMPOSITION, not just its location.** Don't stop at "there's a gap / a null / a wrong pixel"; ask *what is the wrong thing made of* — a gap's actual color, an error's exact bytes, a wrong value's provenance. Composition points straight at the producing layer (same-color-as-the-surface ⇒ missing overlay, not a hole).
- **≥2 distinct fixes failed ⇒ stop fixing, attack the shared premise.** This is the "what to measure" for the project's second-fix→ground-truth rule: the thing to re-measure is the assumption every attempt shares, not more variations of the fix.
- **Get the cheapest disambiguating artifact FIRST.** For visual bugs a screenshot; for data bugs the raw bytes/row; for timing a trace/log. One direct look at the artifact usually collapses the ambiguity that no amount of reasoning could.
- **Distrust inherited "facts."** A handoff or prior note can promote a hypothesis into its "measured facts" section; re-verify any inherited claim that asserts the *nature* of the defect before designing around it.

Related: [[learn-protocol-persist-to-memory]]; the project `CLAUDE.md` "second-fix rule" (measure, don't theorize) — this note says *what* to measure.
