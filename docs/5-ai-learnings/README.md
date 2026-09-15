---
status:
owner: vince
last-updated: 2026-09-13
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
| Replacing how something LOOKS, and about to re-implement its hover, clicks and drag too | [Reuse the Widget's Behaviour, Replace Only Its Look](reuse-the-behaviour-replace-the-look.md) — if the interaction component drives itself from DATA, attach it to the new presentation unchanged and write only the renderer. Its null-guarded visual fields are the permission. |
| A parser, probe, or hand-built test object you just made says the thing is corrupt, empty or does nothing | [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — run it on something known GOOD first; if that looks broken too, your instrument is the broken thing. A stand-in object can be dead, unregistered or torn down — make it SUCCEED once before trusting a failure. |
| A script that drives another app produced nothing, and only the app's own log mentions a timeout | [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) — work sent while the host is busy waits behind it, and the caller gives up first. Wait for it to be idle BEFORE asking, not only after. |
| Something does nothing at all and there is no error to explain it | [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) — silence has no stack trace. Print the decision's INPUTS at the decision site, throttled, one subject. |
| An app you are driving throws the same error every frame in files you never touched, or a play session dies for no reason | [Something Reloaded Under a Live Session](never-reload-under-a-live-session.md) — a reload empties everything the app was holding and the setup never re-runs. Ask whether somebody is using it first — and know that SAVING A FILE can be the reload. |
| Your offline compile check passes, then the real build fails on compiler errors | [The Offline Checker Is Not the Editor](the-checker-is-not-the-editor.md) — the checker compiles the files on DISK, the build compiles what the editor has IMPORTED, and the editor also remembers whether its last compile failed. Recompile and wait; if it says "up to date" and the build still refuses, the failure memory is stuck — clear the compile cache. |
| About to repeat something a doc says is current — especially under "still true" — or a code comment telling you a setting's value | [A Doc Cannot Vouch for Its Own Freshness](a-doc-cannot-vouch-for-itself.md) — that heading was written on the day it was true and nothing has re-checked it, and a comment is a document wearing the code's clothes. Check the claim against the file that holds the value, and when updating a doc, check EVERY claim. |
| You changed a value, read it back, and it had not moved — and you are about to call that a bug | [A Value You Just Changed Reads Stale](a-value-you-just-changed-reads-stale.md) — your read-back is slower than the value moves, so it changed and changed back before you looked. Probe from inside with timestamps. |
| You paused or froze a running system to read it, and everything reads empty | [Put the Recorder Inside — Don't Stop the World](put-the-recorder-inside-dont-stop-the-world.md) — a pause times out every heartbeat and frozen reads report zero on values that are really there. Sample every step from inside, read the recording after. |
| You added a tool to make a job easier and the job got slower | [You Added a Tool and Everything Got Slower](new-tooling-must-replace-work-not-add-it.md) — a tool only saves work if a rule lets something STOP. Find the rule that still demands the old method and let the new answer end the work. |
| Every fix round finds a new bug, the user is exhausted, or you are about to hand over a play-test with findings still open | [The Path Table Is the Reviewer's Checklist](the-path-table-is-the-reviewers-checklist.md) — list every path with today/after BEFORE the edit, walk it again after, give the reviewer that table as its checklist; no play-test until it is clean. |
| The user is asking "is there anything else?" for the third or fourth time | [Give the Whole List, Not a Trickle](give-the-whole-list-not-a-trickle.md) — one finding per reply is a loop with no exit. Sweep once, hand back the closed list with its boundary, then fix without asking again. |
| A tool printed a summary and deleted the detail, and you want to copy the tool to keep its log | [A Tool Threw Away the Detail You Needed](dont-copy-a-tool-to-keep-its-output.md) — a copy resolves the repo from its own path and cannot run. Ask the source first; otherwise run the original with its cleanup disabled. |
| A wait-loop never finishes, but the thing it is watching answers fine by hand | [A Wait-Loop Must Know Every Answer](a-wait-loop-must-know-every-answer.md) — it exits on the answers you expected, and the tool gave a different healthy one. Read the real answer first, or match on the failure state. |
| The reader keeps coming back asking "which one?" | [The Reader Keeps Asking "Which One?"](name-the-thing-never-the-position.md) — every noun is a name or a number, never a position. Number the actors and keep the numbers; a made-up name beats a correct word they must resolve. |
| About to guard the place the damage SHOWS, not the place the bad thing was let in | [Fixing a Hole Caused by Another Hole](fixing-a-hole-caused-by-another-hole.md) — trace it back to the first place that could have said no, and fix it there. And trace the real path before describing a failure, or you are inventing it. |
| You added a new reason for something to finish, reusing the machinery that ends it normally — or it keeps finishing and restarting in a loop | [A New Ending Re-runs the Old Ending](a-new-ending-re-runs-the-old-ending.md) — that teardown saves, releases and announces because it means "gone", and its RECOVERY undoes it because it means "failed". Yours means "moved". Read it line by line, mark the thing as moving first, and match the loop's beat to a constant. |
| You made something travel that never travelled before, and it arrives but LOOKS wrong | [The Far End Reads More Than You Sent](the-far-end-reads-more-than-you-sent.md) — the renderer reads a rate/anchor/component locally that the copy does not have, or something else on the same object OVERWRITES your value later in the frame; and some existing rule was only right because your case did not exist yet. |
| Your new second way of doing something completed fine, but what came out is missing something, in the wrong place, or looping | [It Arrived in the Wrong Place](it-arrived-in-the-wrong-place.md) — list what the OLD path carries and what the far end READS, then thread every one. An absent value has a defined meaning, and it is rarely the one you want. |
| A comment promises an order — "asked for first", "released after", "only once" | [A Comment Promising an Order Is Where the Bug Is](a-comment-promising-an-order-is-where-the-bug-is.md) — it is the only written record of what the code SHOULD do, and nobody re-reads it. Check the two lines beneath it. |
| About to say a cost is cheap, or to explain why it is affordable | [Cheap for Whom, and How Many Times](cheap-for-whom-and-how-many-times.md) — a timing settles nothing alone. Measure WHICH machine pays it and WHAT it multiplies by, and never volunteer an unmeasured reason beside a measured number. |
| You verified a new fallback or defaults file, and the user hit the exact failure it was built to prevent | [You Proved the Fallback — On a Machine That Still Had the Old Answer](tested-while-the-old-answer-was-still-there.md) — your environment still supplied the value, so the shipped state was never run. Take the old answer away, test again, and ship the file with a value in it. |
| Your change compiled and loaded, and nothing happened — no error anywhere | [Your Fix Ran on the Wrong Machine](your-fix-ran-on-the-wrong-machine.md) — find which machine actually RUNS that code before editing it; a guard three calls deep can make a whole feature a picture of itself. |
| Something new is wearing another thing's name, unit or value, and nothing reported a problem | [A Fallback That Answers With a Real Value](a-fallback-that-answers-with-a-real-value.md) — a catch-all returning a plausible real answer hides a missing row; make defaults say "undefined". |
| About to report a count or a size you got from a search, a folder listing or a `du` | [Your Number Is Real, and It Counts the Wrong Thing](your-number-counts-the-wrong-thing.md) — hits are not files, and disk is not memory. Name the unit before the number goes in a sentence. |
| Something built for one user now has several, and a widget shows only one of them — or the wrong one | [One Widget, One Subject](one-widget-one-subject.md) — sweep the presentation layer for singletons first. The assumption is written beside each one as a justification ("the player only does this one at a time"), which is why it survives review. |
| A feature has two routes in, and one of them looks like it worked and then quietly comes undone | [One Route Saves, the Other Doesn't](one-route-saves-the-other-doesnt.md) — do not debug the feature; diff the two routes' LAST step. The wrong commit helper is a sibling of the right one and shares its opening lines, so the screen updates and the write never happens. |
| About to offer a fix that depends on a tool or service being ABLE to do one thing you have not seen it do | [Probe the One Capability Before Offering the Plan](probe-the-capability-before-offering-the-plan.md) — circle the step everything rests on and prove it with one harmless call first. Three plans in a row died at step one on 2026-09-13, each a full round, each findable in one search. |
| An alarm keeps firing at you for doing exactly what a rule told you to do | [A Guard That Counts the Work Your Own Rules Demand](a-guard-that-counts-mandated-work.md) — a guard must exclude the normal, compliant case or it fires every session; prove the exclusion, and open the checker's prover before adding a new kind of thing to it. |
| You wrote a check and it passed first time, and you are about to move on | [A Test That Cannot Fail](a-test-that-cannot-fail.md) — break the thing on purpose and watch it complain; the usual reason it cannot fail is that it reads the value it is testing from the thing it is testing. Assert a PROMISED number as a literal, a DERIVED one against its real source. |
| You changed a setting or re-saved files, the call said success, and the files still hold the old value | [A Project Rule Overwrote the Setting You Just Wrote](a-project-rule-overwrote-your-setting.md) — an unchanged modified time means the write never happened (use a different save call); a changed one means an automatic rule (import hook, autofix, formatter) clobbered it afterwards. Verify the RESULT, never the return value. |

**No page cap.** A lesson that was paid for gets its own page; the index above is what keeps this skimmable, so keep each symptom line short and distinct. Merge two pages only when they genuinely share a symptom, and delete one the moment it is obsolete or has graduated into a hard rule.

---

## Related

- [The Three-Brain Design Process](../0-process/design-process.md) — the workflow these habits sit inside.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the runbook several of these lessons were learned against.
