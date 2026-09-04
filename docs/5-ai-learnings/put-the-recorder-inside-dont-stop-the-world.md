---
status:
owner: vince
last-updated: 2026-09-03
tags:
  - ai-learnings
---

# ⏱ Put the Recorder Inside — Don't Stop the World to Read It

**Reach for this when:** you paused or froze a running system so you could read it, and everything comes back
empty — or the pause killed the thing you were measuring.

> Freezing a live system to observe it **changes what you are observing, and can kill it**. When your reads are
> too slow for what you are watching, the answer is not to stop the world — it is to put a recorder INSIDE it and
> read the recording afterwards.

## The rule

1. **Never trust a value read while the system is artificially stopped.** Paused, the thing you are reading may
   already have been torn down, may not have been built yet, or may be served by a path that only runs while the
   clock advances. An empty read from a frozen system is not evidence of absence.
2. **A pause is not free.** Anything with a heartbeat — a network connection, a session, a lease, a watchdog —
   counts real seconds, not simulated ones. Stop the clock and the far end times you out and tears everything
   down while you are still deciding what to look at.
3. **The instrument that works is one that runs at full speed inside the system**, samples every step, and writes
   to somewhere you can read at your leisure. Cause the thing, let it run, read the recording.

## Why — what it cost

Play-testing a networked game, the checks were "cast this, then look at what it granted". Each look took 2 to 10
seconds; the things being looked at lasted 4 to 8. So the standing instruction was to FREEZE the moment: cause it,
pause immediately, then read at leisure.

Both halves of that failed, and it took most of a session to find out:

- **Pausing the editor killed the run.** The pause stops the heartbeat, the server drops the connection after 10
  seconds, and the whole world tears down. It ended play mode outright once.
- **Stopping only the clock kept the connection alive — and still lied.** Every read taken with the clock stopped
  reported zero states, on a character that demonstrably had two full stack piles at that instant. Every
  "nothing happened" reported from a frozen read was WRONG, and each one was nearly filed as a bug.

The run that produced real findings used neither. It ran a recorder inside the game that sampled every frame into
a file. Everything that was actually learned — a pile arriving 0.35 seconds after a press, 13 stacks becoming 20
two hundredths of a second later, a pile decaying one stack at a time, two piles vanishing in the single frame a
level-up landed — came from reading that file afterwards. None of it was visible any other way.

## How to apply it

- Before reaching for a pause, ask: **does anything here count real seconds?** A connection, a session, a lease,
  a lock, a watchdog. If yes, a pause is a teardown with extra steps.
- **Write the recorder first.** A loop inside the system that samples the handful of values you care about, every
  step, with a timestamp, into a file. It is usually a dozen lines and it answers questions you did not know to
  ask — the ordering, the gap, the one frame where two things changed together.
- **Prove the recorder sees a KNOWN-PRESENT value** before trusting its zeros — same discipline as
  [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md).
- When a frozen read and a running read disagree, **the running one is the true one**. Discard the frozen
  reading; do not average them and do not report the frozen one as a finding.
- A control run matters more than a careful one: a "nothing fired after I switched it off" result is worthless
  unless you can show the surrounding activity was still happening. Prove the machine was running before you
  report its silence.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — the sibling: your
  instrument is the first suspect when it reports nothing.
- [A Value You Just Changed Reads Stale](a-value-you-just-changed-reads-stale.md) — the other half: when your
  read-back is simply slower than the value moves.
