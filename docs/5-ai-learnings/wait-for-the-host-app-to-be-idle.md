---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# ⏳ A Busy Host App Never Gets To Your Request In Time

**Reach for this when:** a script that drives another running application produced nothing, and there is no error where the work should be — only a "timed out" line in the *app's* own log.

> Work that runs INSIDE another application runs on that application's main thread. A request sent while it is
> still busy with the last one is not refused — it waits in line behind that work, and the **caller gives up
> first**, because the caller's patience is measured in seconds and the host's work is measured in minutes. Wait
> for the host to say it is idle BEFORE you ask, not only after.

## The rule

Two requests in a row to the same host application is the trap, and the second one is always the casualty. The
first request returns quickly — it only *starts* the work — so a script that waits for the artefact and then
immediately fires the next request is asking a host that is still busy.

Nothing about the failure looks like a failure. The caller sees a normal return. The host logs a main-thread
timeout, in its own log, which nobody is reading.

And **give-up is not undo**: a request still waiting in line when the caller quits is thrown away and never
happens, but one that had already started on the host's main thread runs to completion regardless — nobody is
left to collect the answer. So when a timeout lands on a request that CHANGES something, go and look at what it
did before sending it again.

So the wait belongs on **both** sides of the request:

- **Before**: poll the host's own status until it reports idle, with a bound.
- **After**: wait for the real artefact, and treat "the host went back to idle with nothing produced" as the
  failure condition — not a fixed number of ticks, which is a guess about duration dressed up as a test.

## Why — what it cost

A build script drove the running Unity editor to build two players in one run. The first always succeeded; the
second produced nothing, **four times**, and building that same half on its own straight afterwards succeeded
every time. Each cycle cost five to eight minutes, and worse, the script deletes the old output before building —
so every false failure also left the user with no game at all until it was rebuilt.

Three rounds were spent working around it — just rebuild the failed half — before anyone read the editor's own
console, where the answer had been sitting the whole time:

    Failed to handle /api/exec request: Main thread operation timed out after 30000ms

One request per run, not two. The second build had never started.

The fixed-tick wait made it worse in the other direction: it declared a still-running build "FAILED — nothing was
produced" after fifteen minutes, which is how a good build got thrown away and rebuilt.

## How to apply it

- Before sending anything to a long-running host app, poll its status endpoint until it reports ready:

      for w in $(seq 1 120); do
        "$CLI" cmd editor_status 2>/dev/null | grep -q '"status":"ready"' && break
        sleep 5
      done

- After sending, wait on the **artefact**, and use "host idle for N consecutive checks with nothing produced" as
  the give-up condition rather than a tick count.
- When a driven action produces nothing, **read the host's own log before theorising** — the caller's silence and
  the host's error live in two different places, and only one of them has the answer.
- The same shape applies to anything single-threaded you drive from outside: an editor, a browser session, a REPL,
  a device.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) — why a false failure here is expensive.
- [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — the other reason a driven app goes quiet.
