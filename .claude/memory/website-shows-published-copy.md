---
name: website-shows-published-copy
description: Any wiki/ or website/ edit is invisible to the user until tools/publish-update.py --site-only copies it into builds/patch
metadata:
  node_type: memory
  type: project
  originSessionId: ac62146f-3a46-4759-a9ec-6fb20ef8b734
  modified: 2026-10-06T02:51:32.977Z
---

The website the user browses (Tailscale `http://100.113.81.97:8090`, served by their own `tools/serve-updates.sh`) shows
`builds/patch/`, a COPY. Edits to `wiki/content/*.json` or `website/index.html` reach it only after
`tools/publish-update.py --site-only` (website, wiki pages and news only — no build, no version). The serve process
keeps running; a page reload picks the copy up.

**Why:** on 2026-10-06 a whole session rewrote the story, weapons and world pages, reported them "done on the
website", and never published — the user opened the site and saw none of it.
**How to apply:** finishing any wiki or website edit, run the site-only publish in the same turn (or say plainly it
was not run). Also see [[wiki-database-generated-pages]].
