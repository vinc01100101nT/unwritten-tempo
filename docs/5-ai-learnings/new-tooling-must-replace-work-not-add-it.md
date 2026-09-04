---
status:
owner: vince
last-updated: 2026-09-04
tags:
  - ai-learnings
---

# 🧰 You Added a Tool and Everything Got Slower

**Reach for this when:** you built something to make a job easier, and the job now costs MORE — more time, more
steps, more tokens — even though the new thing works exactly as designed.

> A tool only saves work if something is allowed to STOP. If the instructions still demand the old method, the
> new one is pure addition: read the new source, then do all the old work anyway.

## The rule

When you ship a tool that answers a question, go and find **every rule that says how that question must be
answered** — and change it to accept the new answer. Two things must both be true or you have added cost:

1. **The tool answers the question**, and
2. **the rules let its answer END the work**, rather than requiring corroboration.

Point 2 is the one that gets forgotten, because it lives in prose somewhere else, usually written by you,
usually months or hours earlier, and it reads as prudence rather than as a cost.

## Why — what it cost

2026-09-04. A categorised game log was added so a play-test could see why something did not happen: a blow
refused and why, a passive firing, a character asking to move and getting nowhere with the obstruction named.

It worked. On the very next run the game told the tester *"MOVEMENT REFUSED for 31.8s — nearest solid: Tavern"*
**twenty-four times**, and it quoted those lines in its findings.

**And the run was measurably worse than the one before it, on an identical task: +9% time, +21% tool calls,
+11% tokens.**

The cause was in the tester's own instructions, which said an in-game recorder was *"the ONLY technique that has
produced a real timed finding"*, and that every reported fault must carry four checks *"with a measurement"*. So
it read the log AND built five recorders to measure the same facts independently. The log became a second
opinion to cross-check, not a shortcut. Every line of it was additional reading on top of unchanged work.

Nobody had written a wrong rule. The rules were right when written — before the tool existed.

## How to apply it

- **After shipping a tool, grep the instructions for the old method by name** ("recorder", "probe", "measure it
  yourself", "run the query") and read every hit as a cost you are still paying.
- **Say explicitly what the tool's answer is sufficient FOR.** "When a log line answers the question, that IS
  the observation — quote it and move on" is the sentence that converts it from addition to replacement.
- **Then say what it is NOT sufficient for**, or the pendulum swings and real measurement stops happening. Scope
  the old method to what the new one genuinely cannot do — exact timings, per-frame values, anything with no
  line of its own.
- **Measure the before and after on an IDENTICAL task.** The regression above was only visible because the same
  three questions were asked twice. A tool that "obviously helps" is a claim, not a result.
- The same trap applies to a checklist, a linter, a doc, or a dashboard — anything that answers a question some
  other rule already dictates an answer method for.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Put the Recorder Inside — Don't Stop the World](put-the-recorder-inside-dont-stop-the-world.md) — the
  technique that became mandatory, and then had to be scoped back.
- [Give the Whole List, Not a Trickle](give-the-whole-list-not-a-trickle.md) — the sibling: sweep once, close
  the loop, rather than paying a round per finding.
