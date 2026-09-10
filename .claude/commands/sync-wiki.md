---
description: Bring the player wiki back in sync with the design docs it's written from
argument-hint: "[optional page ids to limit to, e.g. 'sword skills']"
---

You are syncing the **player wiki** (`wiki/content/*.json`) with the **design docs** (`docs/**/*.md`) it is
written from. The wiki is player-facing; the docs are designer-facing. This is a **rewrite in a different
voice**, never a copy-paste.

Focus this run: **$ARGUMENTS** (if empty, handle every stale page).

## 1. Find what's actually stale — don't guess

```
python3 tools/wiki-sync-check.py check
```

It prints each stale page plus the commits that landed in its source docs since the page was last synced.
That list is your work queue. Do NOT decide a page is stale or fine by reading dates or by intuition — the
mapping lives in `sourceDocs` in `wiki/content/manifest.json`.

## 2. For each stale page, read the real diff

For every source doc the checker flagged:

```
git log -p <stamped-sha>..HEAD -- <doc path>
```

Read the actual diff, not just the commit subject. Then decide, per change: **is this player-relevant?**

- **Player-relevant** — a rule, number, cost, name, or behavior a player would notice or need. Sync it.
- **Internal** — implementation notes, code structure, balance rationale, designer meta-commentary,
  deferred/"still open" items, anything marked planned-but-unbuilt. Do NOT put it in the wiki.

When it's genuinely ambiguous, ask the user rather than guessing.

## 3. Rewrite the page in player voice

Read the current page JSON first and preserve its structure and tone. Then apply the change.

- Wiki pages are **JSON block documents**: `{ title, blocks: [] }`. Block types the renderer
  (`wiki/index.html`) understands: `lede, heading, paragraph, list, table, quote`/`callout, image,
  skillTable, serverStats, hr`. Inline `**bold**` / `*italic*` / `` `code` `` / `[links]()` work in any text field.
- Skills render as cards via a `skillTable` block (`skills:[{name,icon,cost,flavor,effect}]`).
- **Voice**: skill text reads as an in-game tooltip — declarative rules, bold numbers, no article prose and
  no designer commentary about *why* a thing is balanced that way. The player reads what it DOES.
- Never invent a number. Every number in the wiki must appear in a source doc. If a doc changed a rule but
  didn't state the new number, say so and ask.
- Edit in place; don't append a "changed 2026-XX-XX" section. The wiki reads as one current voice.

## 4. Re-stamp

Once a page's content matches its docs — **including pages where you decided nothing player-facing
changed** — stamp it so it stops being reported:

```
python3 tools/wiki-sync-check.py stamp <page-id> [<page-id> ...]
```

Then re-run `check` to confirm the queue is clean.

## 5. Report

Tell the user, per page: what changed for players, and what you deliberately left out as internal. Show the
`git diff` of `wiki/` so they can approve before committing. **Do not commit** — that's the user's call.

If a source doc has changed in a way that makes a page structurally wrong (a whole system renamed, a page
that should no longer exist), stop and propose it rather than forcing the old shape.
