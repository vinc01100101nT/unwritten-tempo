---
status:
owner: vince
last-updated: 2026-09-28
tags:
  - ai-learnings
---

# Moving a Child One Level Deeper Breaks the Code That Climbs To It

**Reach for this when:** you are about to wrap, re-parent or nest something one level deeper in a tree (a UI
hierarchy, a scene, a DOM, a JSON document) — or, after such a change, something "cannot be opened" / "is
inactive" / is found as the wrong object.

## The rule

Code finds things in a tree in three ways: by walking DOWN a path (`Find("a/b")`), by walking UP (`.parent`,
`parentElement`, `..`), and by POSITION (`GetChild(0)`, `children[0]`). Before moving anything, search for ALL
three shapes around the moved thing's name AND around every variable that holds it — not just the name. Then fix
each by naming the thing it actually wants ("the window"), not by adding another hop.

## Why

The interface theme change of 2026-09-28 moved a window's content area one level deeper, inside the window's
body. I searched for code finding `"Content"` by name, found the two windows, and fixed their downward path. I did
not search for `content.parent` — and both windows took "the parent of my content" to mean "the window". It now
meant the body, which sits inside the still-closed window, so pressing the key tried to open an object inside
something switched off. Calendar and Wardrobe both stopped opening. The user found it in Play mode, and it cost a
round of their play-testing plus a round of echo and fix.

## How to apply

1. Before the move, search for the moved thing's name AND every variable it is stored in, followed by `.parent`
   (or the language's upward step), and every `GetChild` / index into its old parent.
2. Read each hit and write down what it WANTS (the window, the row, the list) — not what it walks to.
3. Replace each walk with a lookup of the wanted thing by its own name, so the next re-nesting cannot break it.
4. After the move, repeat the search on the new structure before reporting.
