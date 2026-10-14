---
status:
owner: vince
last-updated: 2026-10-14
tags:
  - ai-learnings
---

# Before Promising to Free Something, Find Everything Made From It

**Reach for this when** you are about to call unloading, releasing, evicting or deleting something "small work" —
or about to size any change that takes away a thing other code has been allowed to assume is always there.

## The rule

Size the job only after searching for every long-lived store that keeps something DERIVED from the thing being
freed: caches keyed by it, pools of copies of it, tables built from it. Each one is a second place the change must
reach, and each was written on the assumption that the thing never goes away. Also list what the thing SHARES with
its siblings before splitting it into separate pieces, because a shared part is copied into every piece.

## Why

2026-10-14: asked to make creatures and character bodies unload from memory, I told the user it was "small work on
top of" a packing change, and they said go on that basis. One search afterwards found three caches that keep
things cut or built from those assets (sliced sprite frames, re-bound hat meshes, see-through materials), one of
which says in its own comment that it relies on textures never being unloaded. A second measurement found that all
98 creatures share three sounds, a shader and a material, which a naive one-file-per-creature split would copy 98
times. The estimate was a guess presented as a fact; it cost the user a go-ahead on the wrong size of job, and a
round to correct it.

## How to apply

1. Before sizing, search the runtime code for static collections whose key or value is the asset's type:
   `grep -rnE "static[^;(]*(Dictionary|List|HashSet)<[^;]*(GameObject|Mesh|Material|Texture|Sprite)" --include=*.cs`.
   Read each declaration's comment; "kept for the session" is the assumption being broken.
2. For each hit, write down what must happen to it when the asset goes (drop the entry, destroy what was made).
3. Before splitting a group into separate files, walk each member's references and list what two or more share.
4. Only then say how big the job is, with the count of places it touches.
