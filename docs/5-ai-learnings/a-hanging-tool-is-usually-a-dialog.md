---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - ai-learnings
---

# ⏳ A Hanging Tool Is Usually a Dialog

**Reach for this when:** a command driving a windowed app has hung, and your other requests to that same app are timing out too.

> When an automated command driving a windowed application seems to hang, and **every other request to that
> application also times out**, the cause is a window waiting for a click — not slow work.

## The rule

The tell is the pattern, not the duration.

A genuinely busy application still answers *something*. A modal dialog blocks its main thread, so every unrelated
request — even a trivial status check — dies at exactly the same timeout. That uniformity is the signature.

"It's probably still working" is the wrong call, and it costs the entire waiting period.

## Why — what it cost

A build driven through an editor "ran" for twenty minutes and was reported as *probably still building*. It had
never started. A dialog reading **"Modified scenes must be saved to continue"** had been sitting on screen the
whole time.

The evidence was already in hand: every unrelated request had been timing out at the identical limit for the whole
period. It was read as "busy". The user ended it by sending a screenshot — which is the wrong way round, because
they had to do the looking.

## How to apply it

- **Symptom check:** one command hangs **and** unrelated read-only requests to the same application time out ⇒
  suspect a dialog first, before suspecting slow work.
- **Say so immediately.** The user can see the screen and you cannot. Ask what is on it rather than waiting it
  out. Asking costs a line; waiting cost twenty minutes.
- **When automating a windowed application, remove the dialog's reason to appear** — satisfy the precondition, or
  check it and fail fast with a plain message. A command that looks unattended must never block on a window.
- A wrapper that gives up waiting should print **why** it gave up, not just "failed". "Nothing was produced" sent
  the diagnosis in the wrong direction; "gave up after 15 minutes, the build never started" would not have.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Observe With a Disposable Second Instance](observe-with-a-disposable-second-instance.md) — the other half of "look, don't assume".
- [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) — the same build, the second half of the damage.
