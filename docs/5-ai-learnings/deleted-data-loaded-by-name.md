---
status:
owner: vince
last-updated: 2026-10-28
tags:
  - ai-learnings
---

# Deleting Data Nothing Points At Can Still Switch a Feature Off

## Reach for this when

You are about to delete assets because a search found nothing referencing them — or a feature stopped working right
after a deletion that "nothing used".

## The rule

A reference search by asset id only finds links saved in other files. Code that loads a thing **by name**
(`Resources.Load("WorldAtlas")`, a path string) is invisible to it, and so is code that **switches itself off** when
a loaded thing is empty or missing. Before deleting, search the code for the asset's name and folder, and read every
early `return` / `enabled = false` near where it is loaded.

## Why

On 2026-10-28 the old zone world was deleted: 77 maps, their ground, minimaps — and the in-game atlas was emptied and
its world-map picture deleted. An id search had found nothing else using that picture. The map window, though, loaded
the atlas by name and, in its first lines, turned itself off when the atlas had no picture. Pressing M then did
nothing at all, with no error, and the user had to find it.

## How to apply

1. Search the code for the asset's file name and its `Resources/` path, not only its id.
2. For each hit, read the lines after the load: a `if (x == null || x.picture == null) { enabled = false; return; }`
   is a feature that dies silently when the data goes.
3. After the deletion, open each feature you found and use it once in Play.
