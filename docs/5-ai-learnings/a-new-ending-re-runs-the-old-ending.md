---
status: current
owner: vince
last-updated: 2026-09-05
tags:
  - ai-learnings
---

# Your new way of ending something quietly re-ran the old way's cleanup

## Reach for this when

You have added a new reason for something to finish — a hand-over, a takeover, a swap, a migration — and it goes
through machinery that already existed for the ORDINARY ending.

## The rule

**Find what the old ending already does on the way out, and decide, one by one, whether each of those still
applies.** Saving, releasing a name, writing a log line, telling other people, freeing a slot — every one was
written for "this thing is finished". Your new ending usually means "this thing has MOVED", and the two want
opposite behaviour from most of that list.

## Why

2026-09-05, twice in one hour, on the same feature.

**First:** a takeover was built by dropping the connection already playing that character. **Dropping a
connection is also how a player leaves, and leaving writes that character to the database.** So the machine being
taken over wrote its now-stale copy over the one the new machine was already playing — the exact duplication the
takeover existed to prevent, arriving through the back door.

**Second, after fixing that:** the character was read out of the database BEFORE the old machine was told to let
go. So whoever took over started from the last routine save, up to fifteen seconds old, and everything since was
**never written at all**. Not overwritten — never recorded.

Both were the same mistake: reusing an ending built for "gone" to mean "moved", without walking what that ending
already did. The second one was caught by the user asking a plain question about the order, not by any check.

## How to apply

1. **Open the ordinary teardown and read it line by line** — the disconnect handler, the destructor, the
   unsubscribe, the release. Write down every side effect it has.
2. **For each side effect, answer: does this still make sense when the thing has MOVED rather than finished?**
   Saving usually does not. Releasing a name usually does. Telling other people usually does.
3. **Mark the thing as moving BEFORE anything else happens to it**, so the teardown can tell the two apart. A
   flag read by the old path is cheaper and safer than a second teardown that has to stay in step.
4. **Then walk the ORDER out loud, as steps, naming who acts at each one.** Both bugs above were ordering bugs
   and both were obvious the moment the steps were written down as a list.
