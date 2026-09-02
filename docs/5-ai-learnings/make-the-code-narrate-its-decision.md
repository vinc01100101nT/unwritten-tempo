---
status:
owner: vince
last-updated: 2026-09-02
tags:
  - ai-learnings
---

# Make the Code Narrate Its Own Decision

## Reach for this when

**Something does nothing at all — no movement, no damage, no output — and there is no error anywhere to explain
it.**

## The rule

Silence has no stack trace, so no amount of reading will produce one. Put a temporary line at the DECISION SITE
that prints the values the decision was made from, throttled and limited to one subject. Then read what the
program says it decided, instead of arguing about what it should have decided.

Print the **inputs**, never just the outcome. "It did not attack" is what you already knew; "it had no target,
because the person it can see is in another map" is the answer.

## Why

2026-08-31. Creatures in a networked game would not attack the player. The whole day went into inference: the
damage path was read end to end, the built binary was searched to prove the right code shipped, services were
checked, positions were compared. Every one of those was true and none of them found it. The user, correctly,
lost patience.

One temporary probe inside the creature's own per-frame decision — printing whether it had a target, what map
that target was in, whether it was chasing, the flat distance, the true distance and its own reach, twice a
second for one creature — answered it on the first run. It showed two things no amount of reading had:

- with a person in the world it still had **no target**, because that person was in a different map; and
- once they shared a map it closed to **0.29** against a reach of **0.59** and bit — health fell 82 → 30.

Hours of inference, one round of narration. The probe was deleted the same session and its absence confirmed in
the shipped build.

## How to apply

1. **Put the line where the decision is made**, not where the effect is missing. The effect site only knows that
   nothing happened.
2. **Print the inputs, one line, named.** Every value the branch reads, with a label. Include the identity of the
   subject and its state flags — "is this thing even allowed to think right now" is often the whole answer.
3. **Throttle it and pick ONE subject.** A static "first one wins" holder plus a half-second timer. Thirty
   creatures logging every frame is a flood you cannot read, and floods evict everything else from a log buffer.
4. **Filter the subject deliberately.** The first thing to tick is often a special case — a training dummy, a
   placeholder — and you will read its numbers thinking they are typical. Require a property only a real subject
   has.
5. **Get the output where you can actually read it.** If the process writes to a terminal nobody is watching,
   restart it with its output to a file first.
6. **"Nothing fired" means nothing fired in EVERY process's log — and the report must NAME the logs that were
   read.** A game with a client and a separate server is two programs with two separate logs, and a probe that
   lives in shared code prints into whichever one ran it. Reading one of them and concluding the probe never
   fired is not a measurement, it is half a measurement wearing a measurement's confidence. Before writing the
   word "nothing", list every process that was alive during the run and open each one's log — including a
   headless one nobody has a window on.
   **Cost: a whole session, 2026-09-02.** A report stated "I made every silent exit on both sides announce
   itself, and not one fired" and closed at 0% confidence. One HAD fired, in the standalone server's own log
   file, which was still sitting on disk untouched. Its single line named the exact path the damage was taking
   and the next session found the root cause from it in minutes.
7. **A probe's stated REASON can be wrong on a machine it was not written for.** The line that finally cracked
   this one blamed "no shell for that body" when the real reason on that machine was simply being the
   authority — the same early return, two different causes. Read what the probe PROVES (this code ran, here),
   not only what it CLAIMS.
8. **Delete it when done, and prove it is gone** — rebuild and confirm the probe's own text does not appear in
   the shipped artifact. Then say so in the report.
