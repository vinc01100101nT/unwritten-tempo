---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - ai-learnings
---

# 📋 Long Agent Runs: Report Per Step, and Ask Them

**Reach for this when:** you are about to brief a helper for a long run, or one has been stopped and you need to know where it got to.

> A long run that saves its report for the end hands back **nothing** when it is killed. And when you want to know
> where one got to, **ask it** — never read its raw transcript.

## The two rules

**1. Require reporting after every step, in the brief.** Write it in as an instruction: *report one short line of
what you observed after each step, not one report at the end; if you are cut off, whatever you have written is the
report.*

**2. To get a status, send it a message.** A helper already holds its own context and can summarise itself in one
round. Reading its raw transcript is slower, floods your own context, and produces a worse answer than the one it
would have written for you.

Ask for exactly five things: where it got to, what it **observed** (labelled observed versus reasoned), what it was
in the middle of, what is untouched, and what a resume should be told so it does not rediscover the same ground.

## Why — what it cost

The first long test run was killed by a rate limit after roughly an hour and handed back a single fragment of a
sentence. An hour of real work, nothing usable.

The replacement was briefed to report per step. It was stopped twice — once by a limit, once deliberately — and
both times its findings survived intact, including the two things that mattered most.

On the asking half, the correction came from the user directly: *"can you just ask that sub agent tester rather
than you reading its entire transcript. it already has context itself."* They were right, and the answer it wrote
was better than anything the transcript would have given.

## How to apply it

- Put the per-step sentence in **every** brief for work expected to run more than a few minutes.
- When resuming, hand over the **current measured state** so it does not re-derive what is already known, and name
  the single loose end to settle first.
- A stopped helper keeps its context and can be resumed by messaging it. Stopping one is not throwing it away.
- If a heavier model keeps hitting a session limit, respawn on a lighter one rather than losing the run entirely.
- Never predict or invent a running helper's results. **"Still running"** is the honest answer, and the only one.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [The Three-Brain Design Process](../0-process/design-process.md) — where helper runs sit in the workflow.
