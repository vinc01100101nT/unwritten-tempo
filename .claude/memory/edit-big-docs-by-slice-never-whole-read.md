---
name: edit-big-docs-by-slice-never-whole-read
description: "Never read a big doc top to bottom just to edit one step — grep for the heading, read ~40 lines around it, edit that slice (or edit from the shell with no read at all). Standing rule from the user, 2026-09-21."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4e06a6c6-6ed3-47eb-9a1e-d1cdb2b32e4d
  modified: 2026-09-20T21:35:59.265Z
---

**Editing any doc longer than a few hundred lines never starts with a whole-file read.** The move is: grep the file for the heading or a unique phrase → read only that slice (the matched line plus ~40 lines) → edit that slice. When the change is a one-line replace, do it from the shell (`grep -n` then `sed -i ''` on that line) and load nothing into context at all. The editing tool accepts a partial read, so a full read is never required.

**Why:** the user asked on 2026-09-21 whether I read the whole checklist every time I update it — I did, by habit, and that file is huge, so every tester/debugger/fix turn was paying thousands of tokens to re-read text I was not changing. They said "yes, do your recommendation" to slicing.

**How to apply:** before any Read of a doc, check its line count (`wc -l`). Over ~300 lines and the task is a targeted edit ⇒ grep first, read the slice, edit. A whole read is only for a full sweep (the RULE NOSTALE pass after a rewrite), never for a status flip or a new step. Related: [[docs-overwrite-not-append]], [[just-do-it-dont-instruct]].
