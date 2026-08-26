# unwritten — Docs

Native markdown docs for the **unwritten** project, migrated out of Tempo Docs on 2026-07-30. This folder is now the source of truth for design/spec/notes; edit these files directly.

## Process

- [The Three-Brain Design Process](design-process.md) — the standing Claude+Vince+GPT workflow for designing any system (frame → ideate → filter → co-design → validate → stress-test → review → lock+document). Reuse for every design unit.

## Planning & production

- [Roadmap to Launch](roadmap-to-launch.md) — how far from a shippable game; Tier 1 (Steam ARPG) vs Tier 2 (MMO), milestones M1–M5.
- [Dev Contribution Log](dev-contribution-log.md) — gamified record of the dev's design/diagnosis contributions.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the recurring `/perf-checkup` routine.
- [Starting the Network](starting-the-network.md) — the runbook for getting more than one player into the world: database → account service → game server → players, with how to tell each step worked.
- [YouTube Update Format Template](youtube-update-template.md) — the lowercase progress-update template.

## Systems & combat design

- [Active Skill System — V3](active-skill-system-v3/README.md) — channels + role-locked scaling (design source of truth). Folder:
  - [Implementation Notes](active-skill-system-v3/implementation-notes.md) — code-facing companion.
  - [Weapon Roster — 12 Identities](active-skill-system-v3/weapon-roster-12-identities.md)
  - [Weapon Crafting & Economy](active-skill-system-v3/weapon-crafting-economy.md)
  - [Skill Numbers & Balance Baseline](active-skill-system-v3/skill-numbers-balance-baseline.md)
  - [State System — Buffs, Debuffs & Combo Marks](active-skill-system-v3/state-system-buffs-debuffs-combo-marks.md)
  - [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — players and mobs 1:1: one stat model, shared formulas + point budget; mobs wear gear/rings and drop what they wear. No tier, no multiplier.
    - […Implementation Notes](active-skill-system-v3/unified-unit-stats-implementation-notes.md) — code-facing companion.
  - [Sword — Skill Roster](active-skill-system-v3/sword-skill-roster.md) — first fully-authored weapon (3 ults + 4 actives + Momentum passive; incl. the universal-but-specialized authoring rule).
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — the stat-growth engine, source of truth.
- [Crafting & Recycling](crafting-and-recycling.md) — player-facing weapon crafting loop.
- [Pre-Renewal RO Port — Phase Plan & Formulas](pre-renewal-ro-port.md) — combat/stat math port.
- [Network-Readiness — Combat Audit & Refactor Plan](network-readiness-audit.md) — multiplayer seam prep.
- [Networking — Implementation Notes](networking-implementation-notes.md) — code-facing companion for the multiplayer layer: the shell/body split, who owns what, the conventions, the traps already paid for, and the remaining order of work. **Read before touching anything networked.**
- [HUD/UI System — Design & Relay](hud-ui-system.md) — the HUD/UI workstream.

## World & content

- [World Creator — Spec](world-creator-spec.md) — browser graph-editor → JSON → Unity orchestrator pipeline.
- [Biome Mob Families — Tiered Spawning](biome-mob-families.md) — biome × tier mob data + generator wiring.
- [Story & Setting (Canon)](story-setting-canon.md) — the anchor lore doc; premise + the Twelve Unwritten (one per weapon).

## Art & animation pipeline

- [GPT Concept Prompts — Paste-Me](gpt-concept-prompts.md) — props + weapons image-generation prompt sets.
- [Mixamo Animation Integration — Workflow](mixamo-animation-workflow.md) — no-code animation import.

## Player wiki

- [Unwritten Wiki](../wiki/) — the player-facing guide (lore, character, combat, monsters). It's a **served website** at repo-root `wiki/` (viewer `index.html` + `content/*.json` JSON block documents, nav in `content/manifest.json`) — NOT under `docs/`. Edit wiki pages there, not here. Run `python3 tools/wiki-sync-check.py` (or `/sync-wiki`) to find pages that have fallen behind their design docs.