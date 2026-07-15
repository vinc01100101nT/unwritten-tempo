# Effort protocol (Tempo per-prompt dials)

**UPDATED 2026-06-22: Model is now ALWAYS Opus 4.8 — it's no longer part of the assessment.** The user defaults the Model picker to Opus and leaves it there; Claude assesses **EFFORT ONLY** (Low / Medium / High / Extra High / Max). Don't recommend or reason about model tiers anymore.

On every task:
1. **Assess effort FIRST**, before executing — pick a recommended effort level.
2. **Auto-GO** if the task is **Low or Medium** — just do it, no pause.
3. **Pause and wait for GO** if the task needs **above Medium effort**. Tell the user the recommended effort + a one-line why; they set the Effort dial, then send GO; the heavy work runs that next turn.
4. **Tag every task response** with the effort read as a **prominent bold header at the very top** (its own first line), e.g.:
   - `**EFFORT: MEDIUM — proceeding**`
   - `**EFFORT: HIGH — hold for GO**`
   Always uppercase; put it before anything else so the user can read it at a glance and override. (Model is implied Opus; no need to print it.)
5. **Revise mid-task**: if something called Low/Medium turns out harder, stop at the safe point (before risky work) and flag for a bump — don't power through under-resourced.

Caveats:
- **Subagents**: Claude picks the model itself (Agent tool `model` arg) — but on this plan spawning subagents is the expensive cold path, so don't auto-spawn for routing; only when the user asks.

Rationale the user bought into: this is a **quality-router** on effort — biggest win is not under-thinking the hard minority of tasks. Skip the ceremony for trivial stuff; revise if reality diverges from the estimate.

Note: the dial applies to the *current* turn (set before sending), so a pause→set→GO always lands the right effort on the following message.
