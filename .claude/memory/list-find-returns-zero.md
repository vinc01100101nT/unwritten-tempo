---
name: list-find-returns-zero
description: C# List<int>.Find answers 0 (a real index) when nothing matches, not -1 — a "walk until none left" loop built on it never ends and freezes the Unity editor
metadata:
  type: project
---

`List<int>.Find(pred)` returns `default(int)` = 0 when no element matches. Used as "the next piece, or -1", it hands back piece 0 forever. It froze the Unity editor twice on 2026-10-28 (GroundBlendBuilder.Lengths, the curb-length walk of the curved-curb ground); each time the user had to end Unity in Task Manager. A Python replay of the same walk finished in under a second, because Python's `next(..., -1)` really answers -1 — replaying it with "answer 0" reproduced the endless loop at once.

**Why:** the editor runs builders on its own thread; an endless loop there freezes it with no error.
**How to apply:** for "find the next index or none" use a foreach with an explicit -1 (or FindIndex on the index list). When porting a C# loop to Python to test it, port the C# library's exact not-found answer too. Related: the world-authoring skill.
