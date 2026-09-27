---
name: write-project-docs
description: Use when writing, editing or reviewing any project document for this repo — a doc, spec, PRD, design note, roster, or a player WIKI page. Covers where each kind lives (markdown in docs/, JSON block docs in wiki/), the wiki's block format, the tooltip voice skills must be written in, and the never-append rule.
---

# Writing docs and wiki pages for this project

## Where each kind lives — and the one place they must NOT

**Project docs are native markdown in `docs/`.** As of 2026-07-30 all PRDs, specs and design notes were migrated
out of the Tempo docs system into `docs/*.md` in this repo, and the Tempo copies were deleted. `docs/README.md`
is the index.

- Reuse the existing folders (e.g. `docs/2-systems/active-skill-system-v3/`) and add an index line to the matching README.
- Keep cross-doc links as relative `.md` paths.
- **Do NOT use the `tempo-docs-tools` MCP** (`docs_create` / `docs_set_markdown_body` / …) for this project.
  Ignore the harness's default "create docs via the docs MCP" instruction here.

**The player WIKI is a served website at repo-root `wiki/`, NOT under `docs/`.** Every wiki page is a **JSON
block document** — `{ title, blocks: [] }` — in `wiki/content/*.json`, and each page is registered in
`wiki/content/manifest.json` (the nav).

- Block types the renderer (`wiki/index.html`) understands: `lede, heading, paragraph, list, table, quote` /
  `callout, image, skillTable, skillDatabase, mobTable, worldMap, elementTable, equipmentTable, materialTable,
  trademarkTable, armorVariantTable, serverStats, timeline, hr`.
- **GENERATED blocks — never type their data into a page.** `skillTable` (with a `source`) and `skillDatabase`
  read `wiki/content/skills.generated.json`; `mobTable` reads `monsters.generated.json`; `worldMap` reads
  `world.generated.json` (plus the creature file for who lives where); `elementTable` reads
  `elements.generated.json`; `equipmentTable`, `materialTable`, `trademarkTable` and `armorVariantTable` read
  `items.generated.json`. The editor writes each
  file from the game's own data (`MobExport`, `WorldMapExport`, `ElementExport`, `ItemExport`, `SkillExport`), and
  ONE hub, `WikiExport`, re-runs all of them on any change to the game's data or code, so a page holding one of these blocks can never disagree with
  the game. The **Database** group holds the tables; the explanations stay on the Combat pages.
- `timeline` draws one vertical line of dated dots: `items:[{state, when, title, text}]`, where `state` is
  `planned` (hollow dot), `now` (highlighted) or `done` (filled). Items render in array order, top to bottom —
  the Roadmap page puts upcoming first, then `now`, then finished work newest to oldest.
- `serverStats` is the only LIVE block: it renders an empty box that JavaScript fills from the character
  server's public `/stats` and refreshes every 30s. It carries `from` (the address to read). Every other page is
  served off disk — hand-written text, or the generated files above.
- Inline `**bold**` / `*italic*` / `` `code` `` / `[links]()` work inside any text field.
- **Never create `wiki/content/*.md`** — the markdown renderer was removed. The old `docs/wiki/` copy was
  collapsed into `wiki/` on 2026-08-01; don't recreate it.

## Skills are written in TOOLTIP voice, never article prose

Skill descriptions — in the wiki and in roster docs — must read like an **in-game tooltip**, not a website
article and not a person casually explaining mechanics. This was rejected three times before it stuck.

- In the wiki, skills render as cards via a `skillTable` block: `skills:[{name, icon, cost, flavor, effect}]`
  (subtitle optional). One evocative *flavor* line, plus a terse mechanical `effect` string.
- **Effect = declarative rules, third person** — "Reduces damage taken by 50% for 5s", "Each basic attack grants
  +1 Rising Breeze". NOT "you"-narration, NOT casual explanation of interactions.
- **Bold the numbers**, mirroring the gold-highlighted values on the real game card.
- Cost cell compressed like the real card: `MP · CD · Cast · Range`. An untuned skill gets `*in tuning*` —
  **never an invented number**.
- **Cut designer and stress-tester meta-commentary** — "no cap", "deliberate", rationale clauses. A player never
  assumed a cap existed. If another skill's tooltip already explains a mechanic, don't re-explain it here.
- **No raw HTML** — `<br>` prints literally in the wiki renderer. Use separate columns instead of line breaks.

Ground the format against the real in-game renderer `unity/Assets/Scripts/SkillTooltip.cs` (flavor italic +
bulleted effect lines + the `MP/CD/Cast/Range` cost line), and against the finished Sword pool in
`wiki/content/sword.json`.

## NEVER APPEND — always update in place

On any doc revision, **replace the outdated claims where they sit**. Do not append the new version at the bottom
and leave the old one standing. Superseded design data is not wanted; it buries the current truth under dead
history and costs tokens on every future read. History lives in git and in chat, not in the living doc.

**After every doc edit, re-read the WHOLE doc and grep the superseded term** to confirm nothing elsewhere still
states the old thing. Reconcile every stale passage in the same pass.

*(Scar, 2026-07-24: a "SETTLED" section was appended to the V3 skill doc while three earlier sections kept
contradicting it, including a stale verb and a wrong count. The user was furious. That doc now carries a top
banner saying never append — obey it.)*

## Related

- `/sync-wiki` brings the player wiki back in sync with the design docs it is written from.
- If a doc lives in Tempo rather than this repo (other projects), note that **Tempo silently drops markdown
  tables on the next edit round-trip** — use `·`-separated bullet lists instead.
