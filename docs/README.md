---
status:
owner: vince
last-updated: 2026-09-17
tags:
---

# unwritten — Docs

Native markdown docs for the **unwritten** project, migrated out of Tempo Docs on 2026-07-30. This folder is now the source of truth for design/spec/notes; edit these files directly.

The folders match the sections below, so the file tree and this index say the same thing. Every doc carries a **Related** block at its foot linking the docs it actually depends on, in both directions — that mesh is what the graph view draws.

## Process

- [The Three-Brain Design Process](0-process/design-process.md) — the standing Claude+Vince+GPT workflow for designing any system (frame → ideate → filter → co-design → validate → stress-test → review → lock+document). Reuse for every design unit.
- [Reading the Game Log](0-process/reading-the-game-log.md) — the categorised game log: which categories exist, how to switch them on, and the three questions it answers that nothing else can. Read before any play-test.
- [GMCP — Playing the Game as Text](0-process/gmcp-playing-the-game-as-text.md) — the game's MCP: an AI plays the built client with no window, reading the screen as text and giving the orders a click gives; three players met in-game this way on 2026-09-17.

## Planning & production

- [Roadmap to Launch](1-planning/roadmap-to-launch.md) — how far from a shippable game, every figure counted rather than estimated: what is built, what is half-built, what is missing.
- [Action Plan](1-planning/action-plan.md) — the single trackable list of what to do next, phase by phase, with a done-check on every item.
- [Play-Test Checklist](1-planning/play-test-checklist.md) — everything built but never watched working, in two lists — the checks settled without Play mode, and the checks in Play mode with what to rebuild and restart first — each saying what to do, what should happen, and what counts as a failure.
- [Dev Contribution Log](1-planning/dev-contribution-log.md) — gamified record of the dev's design/diagnosis contributions.
- [Performance Checkup — My Routine](1-planning/performance-checkup-routine.md) — the recurring `/perf-checkup` routine.
- [Where the Game Is Hosted](1-planning/where-the-game-is-hosted.md) — which machine to rent and why: the Manila latency numbers that rule Europe out, what Singapore costs across five providers, and what has to be true on that machine before anyone can play on it.
- [Starting the Network](2-systems/networking/starting-the-network.md) — the runbook for getting more than one player into the world: database → sign-in service → character server → game server → players, with how to tell each step worked; also stopping and restarting to pick up changes, and how zones are placed on servers and let go again.
- [Querying the Database](1-planning/querying-the-database.md) — how to ask the accounts database questions by hand: find a player, look up a character by name or body, read a level out of a save, and the habits that keep the queries safe.
- [YouTube Update Format Template](1-planning/youtube-update-template.md) — the lowercase progress-update template.

## Systems & combat design

- [Active Skill System — V3](2-systems/active-skill-system-v3/README.md) — channels + role-locked scaling (design source of truth). Folder:
  - [Implementation Notes](2-systems/active-skill-system-v3/implementation-notes.md) — code-facing companion.
  - [Weapon Roster — 12 Identities](2-systems/active-skill-system-v3/weapon-roster-12-identities.md)
  - [Weapon Crafting & Economy](2-systems/active-skill-system-v3/weapon-crafting-economy.md)
  - [Skill Numbers & Balance Baseline](2-systems/active-skill-system-v3/skill-numbers-balance-baseline.md)
  - [State System — Buffs, Debuffs & Combo Marks](2-systems/active-skill-system-v3/state-system-buffs-debuffs-combo-marks.md)
  - [Unified Unit Stats](2-systems/active-skill-system-v3/unified-unit-stats.md) — players and mobs 1:1: one stat model, shared formulas + point budget; mobs wear gear/rings and drop what they wear. No tier, no multiplier.
    - […Implementation Notes](2-systems/active-skill-system-v3/unified-unit-stats-implementation-notes.md) — code-facing companion.
  - [Sword — Skill Roster](2-systems/active-skill-system-v3/sword-skill-roster.md) — first fully-authored weapon (3 ults + 10 actives + 9 passives; incl. the universal-but-specialized authoring rule).
- [Ring System — Current Facts (Trio-Ring)](2-systems/ring-system-trio-ring.md) — the stat-growth engine, source of truth.
- [Crafting & Recycling](2-systems/crafting-and-recycling.md) — player-facing weapon crafting loop.
- [Pre-Renewal RO Port — Phase Plan & Formulas](2-systems/pre-renewal-ro-port.md) — combat/stat math port.
- [Networking — Architecture](2-systems/networking/README.md) — the whole shape of the multiplayer layer on one page: the four processes and what each may decide, how a person gets from sign-in to standing in the world, what crosses the wire, and the single ownership table everything else points at. **Start here.** Folder:
  - [Implementation Notes](2-systems/networking/implementation-notes.md) — what is actually built, the conventions, and every trap already paid for. **Read before touching anything networked.**
  - [Action Alarms](2-systems/networking/action-alarms.md) — how a body says what it is DOING across the wire: which action, when it began, how long it lasts, so a swing, a walk and a damage number can never disagree. **Played and working**; a fight with two people in it is the part still unwatched.
  - [Readiness Audit](2-systems/networking/readiness-audit.md) — the July survey that chose the shape. Historical; kept for the reasoning.
- [HUD/UI System — Design & Relay](2-systems/hud-ui-system.md) — the HUD/UI workstream.

## World & content

- [World Creator — Spec](3-world/world-creator-spec.md) — browser graph-editor → JSON → Unity orchestrator pipeline.
- [Biome Mob Families — Tiered Spawning](3-world/biome-mob-families.md) — biome × tier mob data + generator wiring.
- [Story & Setting (Canon)](3-world/story-setting-canon.md) — the anchor lore doc; premise + the Twelve Unwritten (one per weapon).

## Art & animation pipeline

- [GPT Concept Prompts — Paste-Me](4-art/gpt-concept-prompts.md) — props + weapons image-generation prompt sets.
- [Mixamo Animation Integration — Workflow](4-art/mixamo-animation-workflow.md) — no-code animation import.

## AI learnings & improvements

- [AI Learnings & Improvements](5-ai-learnings/README.md) — how to WORK, not what to build: habits that paid off and mistakes that cost real time, each page naming the incident that earned it. General method, useful on any codebase. **Open its index before a second attempt at anything already stuck** — the index is symptom-first, so look up what you are seeing. There is no page limit — the index is what keeps it readable; the write/read protocol is in `CLAUDE.md`.

## Player wiki

- [Unwritten Wiki](../wiki/) — the player-facing guide (lore, character, combat, monsters). It's a **served website** at repo-root `wiki/` (viewer `index.html` + `content/*.json` JSON block documents, nav in `content/manifest.json`) — NOT under `docs/`. Edit wiki pages there, not here. Run `python3 tools/wiki-sync-check.py` (or `/sync-wiki`) to find pages that have fallen behind their design docs.
