---
status:
owner: vince
last-updated: 2026-09-01
tags:
  - ai-learnings
---

# 🔴 Errors Everywhere, in Code You Never Touched — You Reloaded Under a Live Session

**Reach for this when:** an application you are driving suddenly throws the same one or two errors every frame, and the stack traces point at files your change never went near.

> Reloading code inside a running application wipes every value it was holding in memory, and the setup code
> that filled those values does not run again. Anything that read them now fails forever, in files you never
> edited. **Ask the application whether a person is using it BEFORE you reload it.**

## The rule

A reload is not a read. It is the most destructive thing you can do to a session somebody is sitting in front
of, and it costs them the run they were in the middle of.

So the reload gets the same treatment as any other write: **measure the state first, act second.** One call
answers it. If a person is playing, testing, recording or debugging in that window, do not reload — finish the
edits, tell them what is waiting, and let them choose the moment.

Two things make this worse than it sounds:

- **The damage does not look like yours.** The errors name files you never opened, so the obvious reading is
  "my change broke something over there", and the next twenty minutes go into a bug that does not exist.
- **The evidence of what came before is destroyed by the same event.** An error thrown every frame fills the
  message buffer in seconds, so whatever was there before it is gone, and you can no longer check whether the
  errors predate you.

## Why — what it cost

The user was play-testing. I triggered a recompile to check my own work, three times in a row, without ever
asking what the editor was doing.

Their screen filled with red. Two exceptions, both thrown every single frame: one from the on-screen unit
readout, one from the music crossfader. Neither file had been touched all session. Both failed on the same
shape — a plain field, assigned once in the object's setup and nowhere else, now empty because the reload had
cleared it and the setup never ran a second time.

I then spent a round proving they were not mine: reading both crash sites, checking the project's reload
setting, and finally asking the editor what it was doing — which returned `"playMode":"playing"` and settled it
in one line. That call would have cost the same two seconds *before* the first reload and saved the whole round,
plus the user's play session.

The evidence was already gone by then. The message buffer holds 1000 entries; at two exceptions per frame that
is about **eight seconds** of history, and the on-disk log turned out to be stale — 48 lines, from two days
earlier. There was no way left to check whether those errors had been there before I recompiled. The only
honest answer was reasoning about the crash sites, which is exactly the position this rule exists to avoid.

## How to apply it

- **Before any reload, recompile, restart or hot-swap, ask the host what state it is in:**

      unity cmd editor_status        # prints "playMode":"playing" when somebody is in a run

  Anything other than idle means a person is mid-session. Do not reload.

- **Batch the edits instead.** A reload after every edit is what makes the collision likely; one reload at the
  end of a finished piece of work is one chance to collide instead of twenty.

- **If you have already done it, say so in the first sentence and name what you caused.** The user is looking at
  errors that appear to be a bug in their game. Leaving them to work out that you did it is worse than the
  reload.

- **Read the failing lines before blaming your own change, and look for the shape:** a value assigned only in a
  one-time setup step, read afterwards without a null check. That pattern failing everywhere at once is a
  reload, not a bug.

- **The same rule covers anything stateful you drive from outside** — a browser session, a REPL, a debugger
  attached to a process, a device mid-recording. Reloading resets what it was holding; the person using it loses
  their place.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Busy Host App Drops Your Request Without Failing](wait-for-the-host-app-to-be-idle.md) — the other half of asking the host its state first.
- [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — when a driven app goes quiet instead of loud.
