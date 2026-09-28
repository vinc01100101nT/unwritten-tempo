---
status: current
owner: vince
last-updated: 2026-09-28
tags:
  - ai-learnings
---

# A Shader Array Keeps the Size It Was First Given

**Reach for this when:** values past a certain index of a Unity shader array read as zero, or the console says
`Property (_X) exceeds previous array size (N vs M). Cap to previous size. Restart Unity to recreate the arrays.`

## The rule

Unity fixes a global shader array (`Shader.SetGlobalFloatArray` / `SetGlobalVectorArray`) at the length it had the
FIRST time it was set in that editor session. Growing the array later in the same session silently cuts every write
to the old length; everything past it stays 0. When an array's length changes, give the property a new name (a
fresh name gets a fresh array) — or restart the editor — and always set it at its full length from the first call.

## Why

2026-09-28: the standing-water level list was first set 16 long, then grown to 64 while the editor stayed open.
Every lake from the 17th on had level 0 in the shader, so on the test map whole squares of lower ground drew as water
and higher ground drew dry — one of the two causes of square-edged puddles the user reported after a play-test. It
cost a play-test round and a 15-minute debugger run; the warning had been in the log the whole time.

## How to apply

- Grep the log for `exceeds previous array size` whenever array-driven shader values look wrong past some index.
- When changing the length of a global shader array, rename the property in the C# and in every shader that reads it.
- Set arrays at their full declared length from the very first call, even when most slots are unused.
