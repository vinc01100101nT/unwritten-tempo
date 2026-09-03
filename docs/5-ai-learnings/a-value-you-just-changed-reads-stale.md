---
status:
owner: vince
last-updated: 2026-09-03
tags:
  - ai-learnings
---

# ⏱️ You Changed a Value, Read It Back, and It Had Not Moved

**Reach for this when:** you do something that should change a number, read the number, and it still shows the old
value — and you are about to report that as a bug.

> Your read-back is not instant. If it arrives slower than the thing you are watching moves, you will see a value
> that has already changed and changed back — and call it a value that never changed at all.

## The rule

Before reporting "X did not happen", answer one question: **how long did my measurement take, and how fast does
this value move?** If the read is slower, the reading is meaningless and the honest answer is "I could not measure
this", not "it did not happen".

When the value moves faster than you can ask, stop asking from outside. **Put a probe INSIDE the running system
that timestamps every change**, then read the probe's record once. One trip, and it captures moments you could
never have caught by polling.

## Why — what it cost

A skill costs 12 mana. A tester pressed it and read mana back: **242 of 242, unchanged**. It flagged this as an
observation — correctly refusing to call it a bug — and the main session was one step from opening an
investigation into mana not being spent.

The truth, once a timestamping probe was installed: the press took mana **242 → 230 in the same instant**, exactly
the 12 it costs. Regeneration then refilled it at 1.55 per second, reaching 242 again after about **8 seconds**.
The read-back through the editor link takes **2 to 10 seconds**. So every reading landed after the refill had
already finished. The number was never wrong; the instrument was always late.

The cost was small only because the tester labelled it honestly. Reported as "mana is not being spent", it would
have bought a whole round of hunting a bug that does not exist.

## How to apply it

- **Write down the two numbers before you conclude anything:** how long your read takes, and how fast the value
  changes. If the first is bigger, you have no measurement.
- **Watch out for anything self-restoring** — regenerating resources, cooldowns, timed buffs, retry counters,
  caches that refill, animation states that return to idle. These are the values that lie to a slow observer,
  because the evidence deletes itself.
- **Probe from inside instead of polling from outside.** Have the running system record every change with a
  timestamp into somewhere you can read once:

      # in the running app: append (time, old, new) on every change to a list or a stored string
      # then read that record ONCE, rather than sampling the live value repeatedly

- **A single sample of a moving value is never evidence.** Take the before, the after, and the time between them —
  or take none and say so.
- **"I could not determine this" is the correct report** when the instrument is too slow. It costs one line. A
  false bug costs a round, and a false bug that a person tries to fix costs several.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) — the same move for a decision
  rather than a value: print it at the site instead of inferring it from outside.
- [Observe With a Disposable Second Instance](observe-with-a-disposable-second-instance.md) — when the thing you
  cannot see into is a whole process rather than one number.
