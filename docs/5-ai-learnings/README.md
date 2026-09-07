---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# 🧠 AI Learnings & Improvements

Lessons about **how the AI works on this project** — habits that paid off, mistakes that cost real time, and the
change of behaviour each one earned. These are working-method notes, not game design.

Two rules keep this folder worth reading:

- **General only.** A lesson lands here when it would help on any codebase — a way of measuring, a way of
  diagnosing, a way of running a long task. Anything true only of this game belongs in a systems doc.
- **Earned, not guessed.** Every entry names the incident that produced it, with what it actually cost. A lesson
  nobody paid for is advice, and advice is not worth filing.

**When to open this folder:** before a SECOND attempt at anything that already failed once — a stalled tool, a
measurement you cannot get, a fix that did not take, a long helper run — and whenever a tool hangs or returns
nothing. That is the moment these pages exist for.

Each page opens with the SYMPTOM that should make you reach for it, then the rule, then the incident and what
it cost, then how to apply it. The protocol itself lives in the project's `CLAUDE.md`.

## The index — find your SYMPTOM, then open the page

You arrive here knowing the symptom and nothing else, so that is what these lines lead with.

| You are seeing this | Open this |
| --- | --- |
| Something is running that you need to measure, and you cannot see into it | [Observe With a Disposable Second Instance](observe-with-a-disposable-second-instance.md) — stand up a throwaway copy on its own port with its output to a file; never reason around the live one. |
| A command driving a windowed app has hung, and your other requests to that app are timing out too | [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — that is a window waiting for a click, not slow work. Ask the user what is on screen. |
| About to run a build or regenerate script you have not read | [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) — many delete the current output before building; a failed rebuild then leaves less than you started with. |
| Briefing a long helper run, or one was stopped and you need its status | [Long Agent Runs: Report Per Step, and Ask Them](long-agent-runs-report-per-step.md) — a run that saves its report for the end hands back nothing when killed; and ask it, never read its transcript. |
| Something worked before your change and breaks after it, and reading has not told you why | [Bisect by Removing Your Own Change](bisect-by-removing-your-own-change.md) — check what ELSE moved first, then set your change aside, rebuild, run. One control beats three theories. |
| You have rebuilt the same thing two or three times and the session is crawling | [Build Last, Build Once](build-last-build-once.md) — finish the whole change first, then one build; know its two self-inflicted failures before you start it. |
| A parser, probe, or hand-built test object you just made says the thing is corrupt, empty or does nothing | [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — run it on something known GOOD first; if that looks broken too, your instrument is the broken thing. A stand-in object can be dead, unregistered or torn down — make it SUCCEED once before trusting a failure. |
| A script that drives another app produced nothing, and only the app's own log mentions a timeout | [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) — work sent while the host is busy waits behind it, and the caller gives up first. Wait for it to be idle BEFORE asking, not only after. |
| Something does nothing at all and there is no error to explain it | [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) — silence has no stack trace. Print the decision's INPUTS at the decision site, throttled, one subject. |
| An app you are driving throws the same error every frame in files you never touched, or a play session dies for no reason | [Something Reloaded Under a Live Session](never-reload-under-a-live-session.md) — a reload empties everything the app was holding and the setup never re-runs. Ask whether somebody is using it first — and know that SAVING A FILE can be the reload. |
| Your offline compile check passes, then the real build fails on compiler errors | [The Offline Checker Is Not the Editor](the-checker-is-not-the-editor.md) — the checker compiles the files on DISK, the build compiles what the editor has IMPORTED. Force a recompile and wait for it, then build. |
| About to repeat something a doc says is current — especially under "still true" | [A Doc Cannot Vouch for Its Own Freshness](a-doc-cannot-vouch-for-itself.md) — that heading was written on the day it was true and nothing has re-checked it. Check the claim against the code, and when updating a doc, check EVERY claim, not the ones you changed. |
| You changed a value, read it back, and it had not moved — and you are about to call that a bug | [A Value You Just Changed Reads Stale](a-value-you-just-changed-reads-stale.md) — your read-back is slower than the value moves, so it changed and changed back before you looked. Probe from inside with timestamps. |
| You paused or froze a running system to read it, and everything reads empty | [Put the Recorder Inside — Don't Stop the World](put-the-recorder-inside-dont-stop-the-world.md) — a pause times out every heartbeat and frozen reads report zero on values that are really there. Sample every step from inside, read the recording after. |
| You added a tool to make a job easier and the job got slower | [You Added a Tool and Everything Got Slower](new-tooling-must-replace-work-not-add-it.md) — a tool only saves work if a rule lets something STOP. Find the rule that still demands the old method and let the new answer end the work. |
| The user is asking "is there anything else?" for the third or fourth time | [Give the Whole List, Not a Trickle](give-the-whole-list-not-a-trickle.md) — one finding per reply is a loop with no exit. Sweep once, hand back the closed list with its boundary, then fix without asking again. |
| A tool printed a summary and deleted the detail, and you want to copy the tool to keep its log | [A Tool Threw Away the Detail You Needed](dont-copy-a-tool-to-keep-its-output.md) — a copy resolves the repo from its own path and cannot run. Ask the source first; otherwise run the original with its cleanup disabled. |
| A wait-loop never finishes, but the thing it is watching answers fine by hand | [A Wait-Loop Must Know Every Answer](a-wait-loop-must-know-every-answer.md) — it exits on the answers you expected, and the tool gave a different healthy one. Read the real answer first, or match on the failure state. |
| The reader keeps coming back asking "which one?" | [The Reader Keeps Asking "Which One?"](name-the-thing-never-the-position.md) — every noun is a name or a number, never a position. Number the actors and keep the numbers; a made-up name beats a correct word they must resolve. |
| About to guard the place the damage SHOWS, not the place the bad thing was let in | [Fixing a Hole Caused by Another Hole](fixing-a-hole-caused-by-another-hole.md) — trace it back to the first place that could have said no, and fix it there. And trace the real path before describing a failure, or you are inventing it. |
| You added a new reason for something to finish, reusing the machinery that ends it normally — or it keeps finishing and restarting in a loop | [A New Ending Re-runs the Old Ending](a-new-ending-re-runs-the-old-ending.md) — that teardown saves, releases and announces because it means "gone", and its RECOVERY undoes it because it means "failed". Yours means "moved". Read it line by line, mark the thing as moving first, and match the loop's beat to a constant. |
| Your new second way of doing something completed fine, but what came out is missing something, in the wrong place, or looping | [It Arrived in the Wrong Place](it-arrived-in-the-wrong-place.md) — list what the OLD path carries and what the far end READS, then thread every one. An absent value has a defined meaning, and it is rarely the one you want. |
| A comment promises an order — "asked for first", "released after", "only once" | [A Comment Promising an Order Is Where the Bug Is](a-comment-promising-an-order-is-where-the-bug-is.md) — it is the only written record of what the code SHOULD do, and nobody re-reads it. Check the two lines beneath it. |
| About to say a cost is cheap, or to explain why it is affordable | [Cheap for Whom, and How Many Times](cheap-for-whom-and-how-many-times.md) — a timing settles nothing alone. Measure WHICH machine pays it and WHAT it multiplies by, and never volunteer an unmeasured reason beside a measured number. |
| Your change compiled and loaded, and nothing happened — no error anywhere | [Your Fix Ran on the Wrong Machine](your-fix-ran-on-the-wrong-machine.md) — find which machine actually RUNS that code before editing it; a guard three calls deep can make a whole feature a picture of itself. |
| Something new is wearing another thing's name, unit or value, and nothing reported a problem | [A Fallback That Answers With a Real Value](a-fallback-that-answers-with-a-real-value.md) — a catch-all returning a plausible real answer hides a missing row; make defaults say "undefined". |

**No page cap.** A lesson that was paid for gets its own page; the index above is what keeps this skimmable, so keep each symptom line short and distinct. Merge two pages only when they genuinely share a symptom, and delete one the moment it is obsolete or has graduated into a hard rule.

---

## Related

- [The Three-Brain Design Process](../0-process/design-process.md) — the workflow these habits sit inside.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the runbook several of these lessons were learned against.
