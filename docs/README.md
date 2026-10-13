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
- [Action Plan](1-planning/action-plan/on-going.md) — the trackable list of what to do next, phase by phase, in three files by status: [on-going](1-planning/action-plan/on-going.md) (which also holds the key), [not started](1-planning/action-plan/not-started.md) and [done](1-planning/action-plan/done.md), with a done-check on every item.
- [Dev Contribution Log](1-planning/dev-contribution-log.md) — gamified record of the dev's design/diagnosis contributions.
- [Performance Checkup — My Routine](1-planning/performance-checkup-routine.md) — the recurring `/perf-checkup` routine.
- [Where the Game Is Hosted](1-planning/where-the-game-is-hosted.md) — which machine to rent and why: the Manila latency numbers that rule Europe out, what Singapore costs across five providers, and what has to be true on that machine before anyone can play on it.
- [Live-Readiness Fixes](1-planning/live-readiness-fixes.md) — the plan for four risks to a live world: item checks, saves surviving a character server outage, encryption, and continuous backups, with the questions still open.
- [Starting the Network](2-systems/networking/starting-the-network.md) — the runbook for getting more than one player into the world: database → sign-in service → character server → game server → players, with how to tell each step worked; also stopping and restarting to pick up changes, and how zones are placed on servers and let go again.
- [Querying the Database](1-planning/querying-the-database.md) — how to ask the accounts database questions by hand: find a player, look up a character by name or body, read a level out of a save, and the habits that keep the queries safe.
- [YouTube Update Format Template](1-planning/youtube-update-template.md) — the lowercase progress-update template.

## Systems & combat design

- [Active Skill System — V3](2-systems/active-skill-system-v3/README.md) — channels + role-locked scaling (design source of truth). Folder:
  - [Implementation Notes](2-systems/active-skill-system-v3/implementation-notes.md) — code-facing companion.
  - [Weapon Roster — 14 Identities](2-systems/active-skill-system-v3/weapon-roster-12-identities.md)
  - [Weapon Crafting & Economy](2-systems/active-skill-system-v3/weapon-crafting-economy.md)
  - [Skill Numbers & Balance Baseline](2-systems/active-skill-system-v3/skill-numbers-balance-baseline.md)
  - [State System — Buffs, Debuffs & Combo Marks](2-systems/active-skill-system-v3/state-system-buffs-debuffs-combo-marks.md)
  - [Unified Unit Stats](2-systems/active-skill-system-v3/unified-unit-stats.md) — players and mobs 1:1: one stat model, shared formulas + point budget; mobs wear gear/rings and drop what they wear. No tier, no multiplier.
    - […Implementation Notes](2-systems/active-skill-system-v3/unified-unit-stats-implementation-notes.md) — code-facing companion.
  - [Sword — Skill Roster](2-systems/active-skill-system-v3/sword-skill-roster.md) — first fully-authored weapon (3 ults + 10 actives + 9 passives; incl. the universal-but-specialized authoring rule).
- [Gameplay System — V4](2-systems/gameplay-system-v4/README.md) — BUILT 2026-09-20, not play-tested: gear is forged, folded and quenched (F–A quality; rarity is how rare an item is to come by, never its grade), stats written on with Ink, weapon mastery whose points buy and level skills, a forged weapon's trademark that raises one skill, the ring as part of the character.
  - [Implementation Plan](2-systems/gameplay-system-v4/implementation-plan.md) — the six build slices, all landed.
  - [Crafting Tables](2-systems/gameplay-system-v4/crafting-tables.md) — every station's bands, what each design costs to forge, fold and quench costs and odds per grade, Salt, every Ink value, mastery caps by grade.
  - [Armour Roster, Materials & Words](2-systems/gameplay-system-v4/armour-roster.md) — BUILT 2026-09-23, not play-tested: Head, Body, Garment and Shoes in metal, leather and cloth (12 designs; the off-hand was removed 2026-10-06); the material decides what a fold buys, and shoes' folds also buy movement speed; every armour piece rolls one of its slot's words (Tireless, Bulwark, Emberward…); worn accuracy and evasion now reach the hit roll.
  - [Elements, Element Levels & Converters](2-systems/gameplay-system-v4/elements-and-converters.md) — BUILT 2026-09-23, not play-tested: creatures attack and defend with their element at level 1–4 (normal 1, elite 2, shadow 3) on Ragnarok's four pre-renewal tables, a negative value heals, and eight level-1 converters lend players an element for 5:00.
  - [Timed Effects Survive Logout](2-systems/gameplay-system-v4/timed-effects-survive-logout.md) — BUILT 2026-09-23, not play-tested: only death ends a timed effect; logout and a server change save it with the time left and pause the clock (save v15).
  - [Weapon Names & Trademark Skills](2-systems/gameplay-system-v4/weapon-names-and-trademarks.md) — BUILT 2026-09-21, not play-tested: a forged weapon rolls one of its weapon's trademark skills and is named for it — the skill's own form name ("+15 Petal Storm [C]"), raising every main number of that skill by its grade's percentage; the forge rolls a rarity first (Common 50% to Legendary 1%, every ultimate Legendary), and the weapon is as rare as its trademark.
- [Ring System — Current Facts (Trio-Ring)](2-systems/ring-system-trio-ring.md) — the stat-growth engine, source of truth.
- [Crafting & Recycling](2-systems/crafting-and-recycling.md) — player-facing weapon crafting loop.
- [Hair and Headgear](2-systems/hair-and-headgear.md) — BUILT 2026-10-10, not play-tested: every body drawn bare-headed; 13 hairstyles chosen and coloured at creation; 15 look-only hats as Head items; players, townsfolk and Shadows all wear hair and hats one way; beards gone; one height for everyone.
- [Combat and Peace Mode](2-systems/combat-and-peace-mode.md) — BUILT 2026-10-09, not play-tested: weapon in hand or put away (X), drawn by the server for every unit; peace doubles natural recovery and halves how far creatures notice you after 5 quiet seconds; only the Katar and the Dagger rest on the body so far.
- [Walking and Routing](2-systems/walking-and-routing.md) — BUILT 2026-09-27, not play-tested: one walking rule (45° slopes, 0.075 m steps) for the player, every creature and every route; each map's route areas, so an unreachable target is known at once.
- [Rain and Standing Water](2-systems/rain-and-standing-water.md) — BUILT 2026-09-27/28, partly play-tested: 1 cm puddle sheets on level ground, lakes that fill the hollows from the rain their slopes collect and spill downhill, the same for every player from the shared world clock; exact reflections, fading under water, ground drawn at 0.25 m.
- [Pre-Renewal RO Port — Phase Plan & Formulas](2-systems/pre-renewal-ro-port.md) — combat/stat math port.
- [Networking — Architecture](2-systems/networking/README.md) — the whole shape of the multiplayer layer on one page: the four processes and what each may decide, how a person gets from sign-in to standing in the world, what crosses the wire, and the single ownership table everything else points at. **Start here.** Folder:
  - [Implementation Notes](2-systems/networking/implementation-notes.md) — what is actually built, the conventions, and every trap already paid for. **Read before touching anything networked.**
  - [Action Alarms](2-systems/networking/action-alarms.md) — how a body says what it is DOING across the wire: which action, when it began, how long it lasts, so a swing, a walk and a damage number can never disagree. **Played and working**; a fight with two people in it is the part still unwatched.
  - [Readiness Audit](2-systems/networking/readiness-audit.md) — the July survey that chose the shape. Historical; kept for the reasoning.
- [HUD/UI System — Design & Relay](2-systems/hud-ui-system.md) — the HUD/UI workstream.
- [Quests and Tutorials](2-systems/quests-and-tutorials.md) — the Quests window, unlock conditions, free first runs and replays, ground arrows, the tutorial guide, and how quest progress is saved.
- [Loot-Owner Pointing System](2-systems/loot-owner-pointing.md) — BUILT 2026-10-13, not play-tested: everyone who fights a creature collects points (damage 60, holding its attention 15, healing 15, first hit 5, last hit 5); the most points owns the loot and the experience is split by points.
- [Auto-Combat](2-systems/auto-combat.md) — BUILT 2026-10-12, not play-tested: T or the System menu's window asks the server to hunt for you — Standing, Radius or Free Roam; four auto-skill slots; auto-loot by kind and drop rate; auto-potion; never in town, never through a door; settings saved in their own row, on/off never.
- [The Keeper and the Wings](2-systems/keeper-and-wings.md) — BUILT 2026-10-12, not play-tested: one Keeper in every town (Save Point, account-wide Storage, Teleport priced by doors), how a storage move rides the character's save, the door check, and the Butterfly Wing and Fly Wing.

## World & content

- [World Creator — Spec](3-world/world-creator-spec.md) — browser graph-editor → JSON → Unity orchestrator pipeline.
- [Mob Roster — Normal, Elite, Shadow](3-world/mob-roster.md) — the three kinds of creature: levels, skill slots, gear, summoning, respawn and drops (design, not built).
- [Creature Materials — What Each Creature Drops and What It Sells For](3-world/creature-materials.md) — built 2026-10-13, not play-tested: no gold from kills; every creature's own material, its sale price by level, the five drop tiers and why, a rare material on 24 normal creatures and on all 18 elites, and what an hour on each map sells for.
- [Creature Gear — What Every Creature Wears and Wields](3-world/creature-gear.md) — built 2026-10-13, not play-tested: a full set for all 84 normal creatures and elites, armour by what the creature is, weapon by the towns it lives around, stats built for the weapon, grade by the rarest thing it drops, folds by how hard it is to find, and what the grade and folds pay.
- [What a Kill Pays — Level, Kind, Grade and Folds](3-world/what-a-kill-pays.md) — built 2026-10-13, not play-tested: pay is what the level and kind give, times 25% more a gear grade, times 5% more a fold, on experience, material prices and saved-character gold; nothing measured, no cap.
- [Saved Characters — When a Mob Transforms Back](3-world/saved-characters.md) — a beaten mob can turn back into the character the artist drew: never-fights (pays gold), allies, rebels; the gold rule, the elite-to-ninja table and how each behaves (built, not play-tested).
- [Creatures and Where They Live](3-world/biome-mob-families.md) — every creature's home biome, element and level, and the creatures each map holds (the sandbox layout).
- [Biomes — Ground Tiles and Props](3-world/biomes.md) — every biome's main ground, patch and road tiles, the props that fit it, and the props still to make; the all-biome map generator plan.
- [Biome References — Real Places Beside Our Maps](3-world/biome-references.md) — one real photo per biome beside pictures of its test map, to judge what looks right and what is missing.
- [The World Map — Three Regions](3-world/world-map.md) — proposal, partly built (the 76 maps are generated and wired, with the hand-made lastline as Lastline): a sandbox in three regions whose creatures mostly sit at one stretch of levels (Midlaine 1–33, Lirenne 34–66, The Far Shores 67–99), a big city for each one-family weapon and a border town for each hybrid, every place named, eight dungeons, and each of the fourteen shadows at its own place with its way in; no real-country culture.
- [Story & Setting (Canon)](3-world/story-setting-canon.md) — the anchor lore doc; the artist, Chaos, the corrupted pages and the Fourteen Kashira (one per weapon).

## Art & animation pipeline

- [GPT Concept Prompts — Paste-Me](4-art/gpt-concept-prompts.md) — props + weapons image-generation prompt sets.
- [Website Art Prompts — Paste-Me](4-art/website-art-prompts.md) — one prompt per art box on the website, as Ragnarok-style anime illustration caught mid-moment, plus the character reference sheets they attach.
- [Mixamo Animation Integration — Workflow](4-art/mixamo-animation-workflow.md) — no-code animation import.

## AI learnings & improvements

- [AI Learnings & Improvements](5-ai-learnings/README.md) — how to WORK, not what to build: habits that paid off and mistakes that cost real time, each page naming the incident that earned it. General method, useful on any codebase. **Open its index before a second attempt at anything already stuck** — the index is symptom-first, so look up what you are seeing. There is no page limit — the index is what keeps it readable; the write/read protocol is in `CLAUDE.md`.

## Player wiki

- [Unwritten Wiki](../wiki/) — the player-facing guide (lore, character, combat, monsters). Its pages are `content/*.json` JSON block documents at repo-root `wiki/` (nav in `content/manifest.json`) — NOT under `docs/` — shown by the website (`website/index.html`, served by the download server). Edit wiki pages there, not here. Run `python3 tools/wiki-sync-check.py` (or `/sync-wiki`) to find pages that have fallen behind their design docs.
