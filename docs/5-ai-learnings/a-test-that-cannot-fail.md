---
status:
owner: vince
last-updated: 2026-09-12
tags:
  - ai-learnings
---

# ✅ A Test That Cannot Fail

**Reach for this when:** you wrote a check and it passed first time, and you are about to move on.

> A check that has never been watched FAIL proves nothing. The most common reason it cannot fail is that it
> reads the value it is testing from the very thing it is testing.

## The rule

**Before trusting any new check, break the thing on purpose and watch it complain.** If it stays quiet, the
check is decoration.

And look specifically for the self-referential shape:

```
// cannot ever fail — move the ceiling and the assertion moves with it
assert(chance <= Combat.MaxHitChance);

// can fail — the promised value is stated, so a change to it is caught
assert(chance <= 0.95);
```

Where a number is a **promise** — a cap, a floor, a ratio somebody decided — write the literal. Reading it from
the code turns the check into "the code agrees with itself", which is true of every codebase including a broken
one.

Where a number is **derived**, the general form is right and the literal is wrong: "the table matches its list's
real size" survives an eleventh entry being added; "the table is 10 by 10" silently stops covering the thing it
names the moment someone adds one.

## Why — what it cost

The first batch of arithmetic checks written for this project passed immediately and looked finished. Its prover
then changed the accuracy ceiling from 95% to 105% — a change that would let a target never dodge — and the
check said nothing at all, because it compared the ceiling against itself. Two of the eight checks had that
shape. They would have been wired into the end-of-turn guard, run on every turn for months, and caught nothing,
while their silence was reported as good news.

It cost one round because a prover existed. Without one it would have cost however long it took for somebody to
notice the game had changed.

## How to apply it

- **Write the prover in the same sitting as the check.** Not later: later is when the check is already trusted.
- **The prover changes ONE real value in the real code, runs the check, and puts the code back** — then runs the
  check once more to confirm it goes quiet again. Both halves matter; a check stuck on "fail" is as useless as
  one stuck on "pass".
- **Plant the break in more than one file.** One blind spot should not be able to pass the whole prover.
- **Make the failure name the file it is about.** A guard that filters output to "faults in files this session
  touched" will silently drop a failure that names only itself — so the check fails, the turn ends clean, and
  the silence reads as good news.
- **Take the anchor seriously.** If the string the prover swaps is not in the file exactly once, it proves
  nothing — report that as a failure rather than passing quietly.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — the same doubt pointed at an instrument instead of a check.
- [A Fallback That Answers With a Real Value](a-fallback-that-answers-with-a-real-value.md) — the other way a thing looks healthy while telling you nothing.
