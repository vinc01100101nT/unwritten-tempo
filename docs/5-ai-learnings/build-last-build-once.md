---
status:
owner: vince
last-updated: 2026-09-02
tags:
  - ai-learnings
---

# Build Last, Build Once

## Reach for this when

**You have run the same build two or three times in one session and the whole thing is crawling.**

## The rule

A build is the most expensive thing in the loop — minutes each, and the editor is unusable while it runs. So it
goes at the END, once, after every code change is finished and compile-checked. Never build to test half a
change: the second half will change something the first half already shipped, and you pay the whole cost again,
often twice over because two artefacts have to agree with each other.

Before starting one, know its two failure modes, because both look like your fault and neither is:

- **"Unsaved scenes" on the first build after a code reload.** The content step refuses; the player build that
  follows saves the scenes, so the very next run succeeds. Check the scenes are clean before building instead of
  discovering it three minutes in.
- **A repo script run from the wrong directory** blames something else entirely. The shell's working directory
  persists between commands, so a script invoked by a relative path silently resolves the repo to nowhere.

## Why

2026-09-02. One afternoon's fix cost five build runs and roughly 25 minutes of wall clock, and at least half of
that was avoidable:

- The first build went in after only half the change was written. The second half changed the shape of a message
  that two programs both have to agree on, so **both** artefacts had to be rebuilt — turning one build into three.
- Two runs died on "unsaved scenes" before the pattern was spotted: reload code, first content build refuses,
  next one works.
- One run reported "Unity is not running with this project open" when Unity was running perfectly well. The
  script had been invoked from a directory left over from an earlier command.

The fix that was actually being built took under an hour to write and verify. The builds around it took nearly
half as long again, and every one of those minutes was spent watching a progress bar.

## How to apply

1. **Write the WHOLE change first.** Every file, every side. Compile-check after each edit — that is seconds and
   catches almost everything a build would.
2. **Ask what has to agree with what.** A change to anything two programs both read — a message on a wire, a
   saved file's shape, a shared constant — means every one of them is rebuilt, so decide that before the first
   build rather than after it.
3. **Check the preconditions, then build.** Scenes clean, editor idle, and run the script from the repo root
   with an absolute path.
4. **One run, then verify the artefact rather than rebuilding "to be sure".** Search the built file for a name
   that only exists in your change, with a second name you know predates it as a control. That is seconds and it
   is proof; another build is minutes and is not.
5. **When a build fails, read WHY before running it again.** Both failure modes above name themselves in the
   output. A second identical run on a hunch is the round this page exists to stop.
