---
status:
owner: vince
last-updated: 2026-09-26
tags:
  - ai-learnings
---

# 🧱 The Code Adds It — but What Actually Runs Was Built or Started Before You Wrote That Line

## Reach for this when

A feature you wrote is simply absent at runtime — a button does nothing, a component is missing, a route is not
registered — and reading the code shows it plainly there, with no error anywhere to explain it.

## The rule

**Ask what the running program actually loads.** When a thing is assembled by a build step into an artifact that
is then COMMITTED — a prefab, a bundle, a generated client, a compiled schema, a lockfile, a baked atlas — the
program reads that artifact, not your source. Your line said "add this component"; the file on disk was written
before your line existed and knows nothing about it.

The tell is that the feature is **absent rather than broken**: a wrong implementation throws or misbehaves, but
a missing one is silent, because nothing ever ran.

**A long-running process is the same trap.** A server loads its code once, when it starts. Rebuilding its files
on disk changes nothing in the copy already running, so a server started before your change runs without it for as
long as it stays up — while the files on disk, which are all you can grep, contain the feature perfectly.

So: before diagnosing a feature that does nothing, **check the artifact's date — or the process's START time —
against the code's date**, and grep the artifact for the thing the code claims to put in it.

## Why

2026-09-21, this project. Pressing F on a smith did nothing at all. The interaction code was correct, the
network command was correct, the window class existed and compiled. The HUD is built by an editor tool into a
prefab that is committed to the repo, and that prefab had last been generated on **2026-09-07** — two weeks
before the station window was written. The line adding the window to the HUD had never run. Cost: a full round
diagnosing a feature that had never been assembled, and it was found only by grepping the prefab for the
component's GUID and getting zero hits.

The same check caught its sibling a minute later: the prefab also lacked a second component added since, which
another script re-added at runtime — so one was invisible and one silently worked, from the same stale file.

2026-09-26, this project, the process form. A character saved in a deleted map was meant to be sent to the
starting town; instead the user could not sign in at all. The map server's log showed it doing its half — refusing
the old map and naming the starting town, twice. The character server's half never happened: it had been started
three days before the feature was written, and its rebuilt files on disk (which did contain the feature) had never
been loaded. Cost: a lost sign-in for the user and a round to diagnose; the answer was one `ps` line.

## How to apply

- Date it against the code, in one line:
  ```bash
  git log -1 --format=%cd -- path/to/artifact.prefab      # when was it last generated?
  git log -1 --format=%cd -- path/to/TheCode.cs           # when was the feature written?
  ```
- Prove the artifact's contents rather than trusting the date — search it for the thing itself. For a Unity
  prefab that means the script's GUID, not its name:
  ```bash
  guid=$(grep guid path/to/Thing.cs.meta | awk '{print $2}')
  grep -c "$guid" path/to/artifact.prefab      # 0 = the artifact predates your code
  ```
- For a running process, read its start time and compare it with the change:
  ```bash
  PID=$(lsof -nP -iTCP:<port> -sTCP:LISTEN -t); ps -o pid,lstart,command -p $PID
  ```
  Started before the change ⇒ it is running without it; it needs a restart, not a fix.
- Re-run the generator, then **prove the artifact changed** — a generator that failed quietly leaves the same
  file and the same symptom.
- If a rebuild step is a prerequisite for a feature to exist at all, it belongs in the play-test instructions as
  a numbered step, not as a sentence somebody has to remember.
