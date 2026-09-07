---
status: current
owner: vince
last-updated: 2026-09-06
tags:
  - ai-learnings
---

# Your new way of ending something quietly re-ran the old way's cleanup — and its recovery

## Reach for this when

You have added a new reason for something to finish — a hand-over, a takeover, a swap, a migration — and it goes
through machinery that already existed for the ORDINARY ending. **Or the thing keeps repeating itself: a loop of
finishing and starting again that nobody asked for.**

## The rule

**Find what the old ending already does on the way out, and decide, one by one, whether each of those still
applies.** Saving, releasing a name, writing a log line, telling other people, freeing a slot — every one was
written for "this thing is finished". Your new ending usually means "this thing has MOVED", and the two want
opposite behaviour from most of that list.

**And the old ending's RECOVERY counts as one of those side effects.** If the ordinary ending is a FAILURE, the
machinery around it exists to undo it — retry, reconnect, roll back, re-open. Your deliberate ending arrives at
that same machinery looking exactly like the failure, so the recovery fires and fights you. Worse, a recovery
that re-runs the whole entry sequence can UNDO the very move that triggered it, which triggers it again: a loop
with no end and no error message anywhere.

## Why

2026-09-05 into 2026-09-06, three times on the same feature — twice in one hour, then again the same night.

**First:** a takeover was built by dropping the connection already playing that character. **Dropping a
connection is also how a player leaves, and leaving writes that character to the database.** So the machine being
taken over wrote its now-stale copy over the one the new machine was already playing — the exact duplication the
takeover existed to prevent, arriving through the back door.

**Second, after fixing that:** the character was read out of the database BEFORE the old machine was told to let
go. So whoever took over started from the last routine save, up to fifteen seconds old, and everything since was
**never written at all**. Not overwritten — never recorded.

**Third, the same night, on the same feature — and the most expensive of the three.** Moving to another world
starts by hanging up on the current one. That hang-up is reported through the SAME handler as a connection that
dropped, so the handler did what it is for: it scheduled a retry two seconds later. The move meanwhile succeeded.
Two seconds later the retry re-entered the world from scratch — and entering asks the character server to let you
in, which evicts you from wherever you already are. So the retry dropped the player, and the drop scheduled
another retry, which dropped them again: **thirty-two rounds of reconnecting over sixty-five seconds**, with no
error in any log, on a move that had worked perfectly. The code even carried a comment saying it "does not walk
the ordinary reconnect path". It did.

All three were the same mistake: reusing an ending built for "gone" to mean "moved", without walking what that
ending already did. The second was caught by the user asking a plain question about the order. The third was
found only by lining up the timestamps and noticing the retry delay was the exact length of the cycle.

## How to apply

1. **Open the ordinary teardown and read it line by line** — the disconnect handler, the destructor, the
   unsubscribe, the release. Write down every side effect it has.
2. **For each side effect, answer: does this still make sense when the thing has MOVED rather than finished?**
   Saving usually does not. Releasing a name usually does. Telling other people usually does.
3. **Mark the thing as moving BEFORE anything else happens to it**, so the teardown can tell the two apart. A
   flag read by the old path is cheaper and safer than a second teardown that has to stay in step.
4. **Then walk the ORDER out loud, as steps, naming who acts at each one.** The first two bugs above were
   ordering bugs and both were obvious the moment the steps were written down as a list.
5. **A COMMENT CLAIMING YOUR PATH IS SEPARATE IS NOT EVIDENCE.** "This does not go through the ordinary
   reconnect" was written in good faith and was false: the hang-up called the shared function, which called the
   shared handler. Follow the call, do not read the promise.
6. **When something repeats on a fixed beat, measure the beat and find the constant that matches it.** The
   sixty-five-second storm was thirty-two cycles of exactly two seconds, and there was exactly one two-second
   constant in the codebase — the retry delay. That single match named the culprit outright, after an hour of
   reasoning had named three wrong ones.
