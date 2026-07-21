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

**How to apply:** on any doc revision, replace the old section's text rather than adding a new one; condense rationale to a line, don't paste full transcripts. History lives in git/chat, not the living doc. Related: [[skill-system-v2-model]].
