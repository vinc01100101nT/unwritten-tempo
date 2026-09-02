---
status:
owner: vince
last-updated: 2026-09-02
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
| A parser or probe you just wrote says the data is corrupt, empty or wrong | [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — run it on something known GOOD first; if that looks broken too, your instrument is the broken thing. |
| A script that drives another app produced nothing, and only the app's own log mentions a timeout | [A Busy Host App Drops Your Request](wait-for-the-host-app-to-be-idle.md) — work sent while the host is busy is dropped, not queued. Wait for it to be idle BEFORE asking, not only after. |
| Something does nothing at all and there is no error to explain it | [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) — silence has no stack trace. Print the decision's INPUTS at the decision site, throttled, one subject. |
| An app you are driving throws the same error every frame, in files your change never touched | [You Reloaded Under a Live Session](never-reload-under-a-live-session.md) — a reload empties everything the app was holding and the setup never re-runs. Ask whether somebody is using it BEFORE you reload. |
| Your offline compile check passes, then the real build fails on compiler errors | [The Offline Checker Is Not the Editor](the-checker-is-not-the-editor.md) — the checker compiles the files on DISK, the build compiles what the editor has IMPORTED. Force a recompile and wait for it, then build. |

**11 of 12 pages used.** At the cap, a new lesson merges into an existing page or replaces one.

---

## Related

- [The Three-Brain Design Process](../0-process/design-process.md) — the workflow these habits sit inside.
- [Starting the Network](../1-planning/starting-the-network.md) — the runbook several of these lessons were learned against.
