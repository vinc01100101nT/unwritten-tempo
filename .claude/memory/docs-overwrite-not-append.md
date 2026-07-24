---
name: docs-overwrite-not-append
description: "When updating Tempo docs, overwrite superseded claims in place — never append history; keep docs lean."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4cb5b67c-828b-44f4-a43a-521808dddd9d
---

When editing a Tempo doc, **overwrite/replace the outdated claims in place** — do NOT append the new version at the bottom and leave the old one. Superseded design data is not wanted; it just bloats the doc. Prefer `docs_set_markdown_body` (full replace) or a targeted `docs_edit_markdown_body` that swaps the stale section, and fix the title if it no longer fits.

**Why:** the user actively manages doc size (the v1 skill doc hit ~65k chars / 16k tokens and overflowed a single read). Bloat costs tokens on every future read and buries the current truth under dead history.

**How to apply:** on any doc revision, replace the old section's text rather than adding a new one; condense rationale to a line, don't paste full transcripts. History lives in git/chat, not the living doc. **After every doc edit, RE-READ THE WHOLE DOC and grep the superseded term to confirm nothing elsewhere still states the old thing** — appending a new "SETTLED/Update (date)" block while leaving the old contradicting passages is the exact failure this rule forbids. Related: [[skill-system-v2-model]].

**SCAR (2026-07-24):** I appended an "Effects & Channels catalog — SETTLED" section to the "Active Skill System — V3" doc (docId `q17f4w7tvgkycbp8w58vyzy3gs8azyah`) and left §1 Strength, §2 phantom V2 effects (targeting/persistence/element-convert never built), and §5 archetype-verb list (had a stale 6th verb "Sustain") all contradicting it — plus wrote a wrong count ("20 verbs" vs real 17). The user was furious. V3 now carries a top ⛔ banner "HOW TO EDIT THIS DOC — NEVER APPEND, ALWAYS UPDATE IN PLACE" — obey it. Reconcile EVERY stale passage in the same pass, don't just bolt on a dated note.
