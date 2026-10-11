---
status:
owner: vince
last-updated: 2026-10-11
tags:
  - ai-learnings
---

# 🪟 Run a Windowed Program on a Hidden Desktop

**Reach for this when:** you are about to report a windowed program as "built but untested" because running it
would put a window on the user's screen.

## The rule

On Windows, start the real built program on a separate desktop that is never shown. Its windows exist and work, the
user sees nothing, and you read the result off the disk. A program it starts inherits the same hidden desktop, so a
whole chain of programs can run. A button can be pressed by posting mouse messages from a thread that has joined that
desktop.

## Why

2026-10-11: a launcher was rebuilt as two programs, one starting the other, both with windows. Two reports in a row
said "the window is untested" because opening it would interrupt the user. The parts that could be tested were tested
through harnesses that compile the source — which is not the packaged program that ships (see
[Test the Packaged Form That Ships](test-the-packaged-form-that-ships.md)). On a hidden desktop the real files ran
end to end in 19 seconds: the downloaded zip's program fetched the 100 MB launcher, the launcher downloaded all 512
game files from the live server, and the first launcher players already had was driven through its Start button to
prove the hand-over. None of it was visible.

## How to apply

1. `CreateDesktopW("some-name", …, GENERIC_ALL)` — a new desktop in the same window station.
2. `CreateProcessW` with `STARTUPINFO.lpDesktop = "some-name"`. Python's `ctypes` is enough.
3. Wait on the process handle, then **read what it did from the disk** (files written, folders moved). Do not trust
   that it merely stayed alive.
4. To press a button: from a NEW thread, `SetThreadDesktop(desktop)`, find the window with `EnumDesktopWindows`,
   take its client size, and post `WM_MOUSEMOVE`, `WM_LBUTTONDOWN`, `WM_LBUTTONUP` at the button's position scaled
   to that size.
5. List the processes of that name BEFORE starting, and afterwards stop only the ones that were not in that list —
   by process id, never by name.
6. It proves the program RUNS. It does not prove how the window LOOKS on a real screen (transparency, dragging);
   say that those are still unseen.
