---
status: current
owner: vince
last-updated: 2026-09-06
tags:
  - ai-learnings
---

# It arrived — but in the wrong place, or in the wrong state

## Reach for this when

**You built a SECOND way to do something the system already does, and it completes without error — but what
comes out the far end is missing something, sitting in the wrong spot, or in a state nobody expected.** Often
there is no error at all, and sometimes it loops.

## The rule

**Before writing the new path, open the OLD one and list every value it carries from one end to the other. Then
thread every single one of them through yours.** Not the values you need to make it work — the values the far
end READS. Those are not the same list, and the difference is invisible while you are writing it, because the
new path is correct about everything it thought to include.

The far end is shared. It was written for the old path and it will quietly use whatever it is given, including
nothing.

## Why

2026-09-05. A game got a second kind of doorway: the ordinary one, which loads a map on your own machine, and a
new one, which hands you to a different server. The new one worked — the player was written down, the far side
was told, the address and ticket arrived, and the map came up.

**But every ordinary door in the game passes along WHICH ARRIVAL POINT you come out at, and the new one did
not.** The far end took "no arrival point" to mean "leave them where they are". Where they were was **inside the
doorway they had just stepped into**, measured to the centimetre: the body at (21.12, 0.02, −0.61), the
doorway's box centred on (21.14, 0.65, −0.50). So the door fired again. And again. The player sat behind a
loading screen that could not come down, because the journey never stopped starting over.

Nothing errored. The logs showed a healthy, successful move — repeated forever.

Cost: a full round, and the user's play session. The missing value was one word wide and every other door in the
game already passed it.

## How to apply

1. **Read the old path end to end and write down what it CARRIES**, not what it does — every id, name, point,
   flag and index that travels with the work. That list is the specification for your new path.
2. **Then read the FAR END and write down what it READS.** Anything on that list your path does not supply
   arrives as zero, empty or null — which are real, legal-looking values, so nothing will complain.
3. **Ask what each missing value MEANS when absent.** "No arrival point" did not mean "fail"; it meant "don't
   move them", which is a perfectly reasonable rule that was catastrophic here. An absent value usually has a
   defined meaning, and it is rarely the one you want.
4. **Check the state you arrive IN, not just that you arrived.** Position, which container you are in, what is
   overlapping you, what will fire next frame. "It completed" is not "it landed correctly".
5. **A loop with no error is this shape until proven otherwise.** Something completed, put the world into the
   state that starts it again, and completed again. Look for what the new path failed to change, not for what it
   did wrong.
