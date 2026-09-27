---
name: wiki-deployed-on-vercel
description: The player wiki (repo-root wiki/) is deployed on Vercel — what's on disk is not what players see until it is deployed; no polling or server-side logic in the wiki.
metadata:
  type: project
---

The player wiki at repo-root `wiki/` is deployed on Vercel (user, 2026-09-26). No Vercel config lives in the repo, and
how a deploy is triggered has not been verified — ask before claiming it.

**Why:** a change that only lands in the local `wiki/` folder (a generated Database file, a page) is invisible to
players until it is deployed. And the user treats cost as a constraint: no timers re-reading files from the page.

**How to apply:** the wiki is static files, so never add browser polling, and never assume a local server. When
reporting a wiki change, say that it reaches players only once deployed. The generated files must be committed for a
git-driven deploy to carry them. See [[wiki-database-generated-pages]].
