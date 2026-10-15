---
name: no-doc-keeper-for-sweeps
description: "Don't spawn the doc-keeper for a doc sweep — the user stopped it for burning tokens; do doc updates inline with grep + slice edits"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 83019dbf-fb4f-4716-9bf2-815f8aa8394c
  modified: 2026-10-15T18:19:30.946Z
---

Do doc and wiki updates myself, inline: grep the subject, read ~40 lines around each hit, edit that slice. Never
hand a multi-file doc sweep to the doc-keeper sub-agent.

**Why:** 2026-10-16, after the creature-rarity build, a doc-keeper briefed with 12 docs + wiki pages hit a session
limit after finishing one doc, was resumed, and the user killed it: "the doc keeper consumes an unimaginable amount
of fucking tokens". It reads whole docs cold to re-derive what I already know.

**How to apply:** one doc at a time, slice edits only (see [[edit-big-docs-by-slice-never-whole-read]]); ask before
starting a large sweep if the user is watching cost.
