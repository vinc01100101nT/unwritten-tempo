---
status:
owner: vince
last-updated: 2026-09-13
tags:
  - ai-learnings
---

# Probe the One Capability Before Offering the Plan

**Reach for this when:** you are about to hand the user a fix whose every step rests on one thing a tool or
service must be ABLE to do — delete, create, change a type, rename — and you have not seen it do that thing.

## The rule

Before presenting a plan, name the single capability it cannot survive without, and prove it exists with the
smallest harmless call — on a leftover, a sample, a throwaway. Only then offer the plan. If the probe fails,
the user hears one sentence ("this service cannot do X") and one real option, not a plan that dies at step one.

## Why

On 2026-09-13 an angry user wanted a project's ticket numbers made clear. Three plans were offered in a row,
each presented as the fix and each dying on its first step: deleting tickets (the connection has no delete
operation), creating a fresh project (it cannot create projects), then swapping ticket content between types
(it cannot change a ticket's type — "The issue type selected is invalid"). Each cost a full round of a
helper run of one to five minutes, and each report told a user who was already swearing that nothing had
changed. Every one of the three blockers was findable with one search or one call on a sample ticket BEFORE
the plan was written.

## How to apply

1. Write the plan's steps, then circle the step everything else depends on.
2. Probe it: search the tool's operation list for it, then make one real call on something disposable.
3. Probe fails → drop the plan; tell the user what the service cannot do, in one line, and offer only what
   survives. Probe passes → present the plan and run it.
4. Record every "cannot" you find where the next session will see it, so nobody searches for it again.
