---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# One Widget, One Subject

## Reach for this when

Something that worked fine for one user is being made to work for several, and a piece of the interface can only
show one of them — the bar appears over the wrong head, the second one never appears, the label says "you" about
somebody else, or two subjects fight over the same widget.

## The rule

**Before touching the feature, sweep the presentation layer for singletons and statics — each one is a widget
that can only ever describe ONE subject, and that limit is invisible until a second subject exists.** Then key it
by the subject it belongs to, pool the instances, and give each one a way to put itself away.

The assumption is almost always written down in plain English right beside the field, phrased as a
justification — *"there is exactly one in the game, the player only does this one at a time"*. That sentence is
the bug, and it reads as a design note, so it survives every review.

## Why — what it cost

Two separate reworks in one afternoon, both found by reading code rather than by any rule catching them.

- **The cast bar** was a single static instance, with a comment explaining that the player casts one spell at a
  time. True of the player, false of a world with other people in it — so nobody could see anybody else wind up,
  and the fix was not "send the bar over the network" but "make a bar belong to the body it floats over".
- **Chat** was a single controller that knew how to speak for exactly one person: one hard-coded speaker label,
  one lookup of "the player's head" to hang a bubble on. Making other people's words appear meant giving it a
  speaker rather than adding a message.

Both were invisible until a second player existed, and both had already been solved correctly elsewhere in the
same codebase — the speech bubbles keep one per character, keyed by the body they follow, pooled under a single
shared canvas. **The pattern existed; what was missing was the trigger to go looking for the ones that had not
adopted it.**

## How to apply

1. **Grep the presentation layer for a static field of the widget's own type** — `static Foo instance`,
   `Foo.Current`, a module-level singleton. In this codebase: `grep -rn "static [A-Z][A-Za-z]* instance"`.
   Each hit is a candidate.
2. **Read the comment next to it.** If it explains why one is enough, that explanation names the assumption you
   are about to break. Check it against the new world rather than trusting it.
3. **Key by the subject, not by the screen.** The right key is the thing the widget is ABOUT — the body it hangs
   over, the row it annotates — so the widget is found by naming that thing and cannot be handed to the wrong one.
4. **Copy the sibling that already got it right** rather than inventing a second scheme. There is usually one.
5. **Give each instance a self-expiry.** Something else now decides when a widget should go away, and that
   decision may arrive over a network or not at all. Have the widget put itself away when nothing refreshed it
   for a frame, so a lost "stop" costs a frame instead of stranding it on screen forever.
6. **Check the ONE-subject wording too, not just the plumbing.** A label that says "You" and a lookup that finds
   "the player" are the same bug wearing different clothes.
