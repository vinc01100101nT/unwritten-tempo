---
status:
owner: vince
last-updated: 2026-09-03
tags:
  - ai-learnings
---

# 🔴 Errors Everywhere, in Code You Never Touched — Something Reloaded Under a Live Session

**Reach for this when:** an application you are driving suddenly throws the same one or two errors every frame and the stack traces point at files your change never went near — OR a play session you or a helper was running dies for no apparent reason.

> Reloading code inside a running application wipes every value it was holding in memory, and the setup code
> that filled those values does not run again. Anything that read them now fails forever, in files you never
> edited. **Ask the application whether a person is using it BEFORE you reload it — and know that SAVING A FILE
> can be the reload.**

## The rule

A reload is not a read. It is the most destructive thing you can do to a session somebody is sitting in front
of, and it costs them the run they were in the middle of.

**And you do not have to ASK for a reload to cause one.** Many editors watch their project folder and re-import
the moment a file changes on disk — so an ordinary text edit, made in another window, IS the destructive action.
That is the version of this that catches you out, because editing a file does not feel like touching the running
app at all.

So the reload gets the same treatment as any other write: **measure the state first, act second.** One call
answers it. If a person is playing, testing, recording or debugging in that window, do not reload — finish the
edits, tell them what is waiting, and let them choose the moment.

Two things make this worse than it sounds:

- **The damage does not look like yours.** The errors name files you never opened, so the obvious reading is
  "my change broke something over there", and the next twenty minutes go into a bug that does not exist.
- **The evidence of what came before is destroyed by the same event.** An error thrown every frame fills the
  message buffer in seconds, so whatever was there before it is gone, and you can no longer check whether the
  errors predate you.

## Why — what it cost, the second time (2026-09-03): the reload nobody asked for

A tester helper was playing the game through the editor, working a checklist. In another window I edited one C#
file — and told both the user and the tester, in writing, that this was safe, because a note from an earlier
session said this project keeps automatic re-import switched OFF.

**I had not checked. It was ON**, and in the mode that re-imports during play as well. About five minutes in, my
edit triggered a **6.4-second code reload** mid-play. The player's character handle went to nothing and the game
carried on against it. Two components then threw **about 120 errors per second**; in roughly **8 seconds** that
filled the entire 1000-entry console and erased every earlier line, so the run's history was unrecoverable from
the console and only the on-disk log still held the cause. The tester counted **eight re-imports in fifty
seconds**. The play session was destroyed and the checklist run was lost.

The whole thing was one command away from being avoided — and the note I trusted instead was a snapshot somebody
had widened into a standing fact.

## Why — what it cost, the first time (2026-09-01): the reload I did ask for

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

- **Treat EDITING A FILE as a reload until you have measured otherwise.** Never edit anything inside the project
  while a play session — yours, the user's, or a helper's — is running. Queue the edit and make it after.

- **Measure the re-import setting; never quote a note about it.** It is per-machine, it changes, and a stale note
  reads exactly as authoritative as a true one:

      defaults read com.unity3d.UnityEditor5.x kAutoRefreshMode   # 0 = off, 1 = on incl. during play, 2 = on but not during play

- **When you brief a helper to drive the app, tell it nobody will touch the project while it runs** — and then
  do not.

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
- [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) — the other half of asking the host its state first.
- [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — when a driven app goes quiet instead of loud.
