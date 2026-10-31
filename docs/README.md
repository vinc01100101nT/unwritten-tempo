---
status:
owner: vince
last-updated: 2026-10-31
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
- [Performance Checkup — My Routine](1-planning/performance-checkup-routine.md) — the recurring `/perf-checkup` routine.
- [Where the Game Is Hosted](1-planning/where-the-game-is-hosted.md) — which machine to rent and why: the Manila latency numbers that rule Europe out, what Singapore costs across five providers, and what has to be true on that machine before anyone can play on it.
- [Live-Readiness Fixes](1-planning/live-readiness-fixes.md) — the plan for four risks to a live world: item checks, saves surviving a character server outage, encryption, and continuous backups, with the questions still open.
- [Starting the Network](2-systems/networking/starting-the-network.md) — the runbook for getting more than one player into the world: database → sign-in service → character server → game server → players, with how to tell each step worked; also stopping and restarting to pick up changes.
- [Querying the Database](1-planning/querying-the-database.md) — how to ask the accounts database questions by hand: find a player, look up a character by name or body, read a level out of a save, and the habits that keep the queries safe.
- [YouTube Update Format Template](1-planning/youtube-update-template.md) — the lowercase progress-update template.

## Systems & combat design

- [Active Skill System — V3](2-systems/active-skill-system-v3/README.md) — channels + role-locked scaling (design source of truth). Folder:
  - [Implementation Notes](2-systems/active-skill-system-v3/implementation-notes.md) — code-facing companion.
  - [Weapon Roster — 14 Identities](2-systems/active-skill-system-v3/weapon-roster-12-identities.md)
  - [Weapon Crafting & Economy](2-systems/active-skill-system-v3/weapon-crafting-economy.md)
  - [Skill Numbers & Balance Baseline](2-systems/active-skill-system-v3/skill-numbers-balance-baseline.md)
  - [State System — Buffs, Debuffs & Combo Marks](2-systems/active-skill-system-v3/state-system-buffs-debuffs-combo-marks.md)
  - [Unified Unit Stats](2-systems/active-skill-system-v3/unified-unit-stats.md) — players and mobs 1:1: one stat model, shared formulas + point budget; mobs wear gear/rings and drop copies of what they wear. No tier; a mob's one typed dial is its multiplier. The one home for every stat formula.
  - [Sword — Skill Roster](2-systems/active-skill-system-v3/sword-skill-roster.md) — first fully-authored weapon (3 ults + 10 actives + 9 passives; incl. the universal-but-specialized authoring rule).
- [Gameplay System — V4](2-systems/gameplay-system-v4/README.md) — BUILT 2026-09-20, not play-tested: gear is dropped by the creatures that wear it, then folded and quenched (F–A quality, every grade to +10; a piece is worth its base, rolled 85–115%, × fold and quench percentages that add up across grades; gear has no rarity; folds from +4 and quenches can fail, and the three Salts decide what a failed quench costs; Raw +0 to A +10 is meant to take about two months of eight hours a day, unmeasured), stats written on with Ink, weapon mastery whose points buy and level skills, a weapon's trademark that raises one skill, the ring as part of the character.
  - [Armour Roster, Materials & Words](2-systems/gameplay-system-v4/armour-roster.md) — BUILT 2026-09-23, not play-tested: Head, Body, Garment and Shoes in metal, leather and cloth (12 designs; the off-hand was removed 2026-10-06); the material decides what a piece's base buys, and no piece gives movement speed; every armour piece rolls one of its slot's words (Tireless, Bulwark, Emberward…); worn accuracy and evasion now reach the hit roll.
  - [Elements, Element Levels & Converters](2-systems/gameplay-system-v4/elements-and-converters.md) — BUILT 2026-09-23, not play-tested: creatures attack and defend with their element at level 1–4 (normal 1, elite 2, shadow 3) on Ragnarok's four pre-renewal tables, a negative value heals, and eight level-1 converters lend players an element for 5:00.
  - [Timed Effects Survive Logout](2-systems/gameplay-system-v4/timed-effects-survive-logout.md) — BUILT 2026-09-23, not play-tested: only death ends a timed effect; logout and a server change save it with the time left and pause the clock (save v15).
  - [Weapon Names & Trademark Skills](2-systems/gameplay-system-v4/weapon-names-and-trademarks.md) — BUILT 2026-09-21, not play-tested: a weapon rolls one of its weapon's trademark skills and is named for it — the skill's own form name ("+7 Petal Storm [C]"), raising every main number of that skill by its grade's percentage; the roll is a class first (an ultimate's 1%, an active's or passive's 99%).
- [Ring System — Current Facts (Trio-Ring)](2-systems/ring-system-trio-ring.md) — the stat-growth engine, source of truth.
- [Crafting & Recycling](2-systems/crafting-and-recycling.md) — one paragraph on the loop V4 runs, kept so old links land somewhere.
- [Hair and Headgear](2-systems/hair-and-headgear.md) — BUILT 2026-10-10, not play-tested: every body drawn bare-headed; 13 hairstyles chosen and coloured at creation; 15 hats as Head items, each with fixed primary stats and one effect that fits one weapon without saying so (built 2026-10-16), sold for 1u by a beta-testing Hatter in Lastline; players, townsfolk and Shadows all wear hair and hats one way; beards gone; one height for everyone.
- [Costumes](2-systems/costumes.md) — BUILT 2026-10-16, not play-tested: a Costume Body slot whose costume replaces how the body looks and bends with it; made picture → Trellis → `CostumeBaker`; one costume stored once, joined with each body the first time it is worn; test costume Adventurer Outfit on all 51 bodies.
- [Combat and Peace Mode](2-systems/combat-and-peace-mode.md) — BUILT 2026-10-09, not play-tested: weapon in hand or put away (X), drawn by the server for every unit; peace doubles natural recovery and halves how far creatures notice you after 5 quiet seconds; only the Katar and the Dagger rest on the body so far.
- [Walking and Routing](2-systems/walking-and-routing.md) — BUILT 2026-09-27, not play-tested: one walking rule (45° slopes, 0.075 m steps) for the player, every creature and every route; route areas per named area of the world, so an unreachable target is known at once.
- [Rain and Standing Water](2-systems/rain-and-standing-water.md) — BUILT 2026-09-27/28, partly play-tested: 1 cm puddle sheets on level ground, lakes that fill the hollows from the rain their slopes collect and spill downhill, the same for every player from the shared world clock; exact reflections, fading under water, ground drawn at 0.25 m.
- [Pre-Renewal RO Port — Phase Plan & Formulas](2-systems/pre-renewal-ro-port.md) — combat/stat math port.
- [Networking — Architecture](2-systems/networking/README.md) — the whole shape of the multiplayer layer on one page: the four processes and what each may decide, how a person gets from sign-in to standing in the world, what crosses the wire, and the single ownership table everything else points at. **Start here.** Folder:
  - [Implementation Notes](2-systems/networking/implementation-notes.md) — what is actually built, the conventions, and every trap already paid for. **Read before touching anything networked.**
  - [Action Alarms](2-systems/networking/action-alarms.md) — how a body says what it is DOING across the wire: which action, when it began, how long it lasts, so a swing, a walk and a damage number can never disagree. **Played and working**; a fight with two people in it is the part still unwatched.
  - [Readiness Audit](2-systems/networking/readiness-audit.md) — the July survey that chose the shape. Historical; kept for the reasoning.
- [HUD/UI System — Design & Relay](2-systems/hud-ui-system.md) — the HUD/UI workstream.
- [Quests and Tutorials](2-systems/quests-and-tutorials.md) — the Quests window, unlock conditions, free first runs and replays, ground arrows, the tutorial guide, and how quest progress is saved.
- [Loot-Owner Pointing System](2-systems/loot-owner-pointing.md) — BUILT 2026-10-13, not play-tested: everyone who fights a creature collects points (damage 60, holding its attention 15, healing 15, first hit 5, last hit 5); the most points owns the loot and the experience is split by points.
- [Auto-Combat](2-systems/auto-combat.md) — BUILT 2026-10-12, not play-tested: T or the System menu's window asks the server to hunt for you — Standing, Radius or Free Roam; four auto-skill slots; auto-loot by kind and drop rate; auto-potion; never in a town (by the named area stood in), never through a door; settings saved in their own row, on/off never.
- [Healing Potions and Mana Draughts](2-systems/healing-and-mana-potions.md) — BUILT 2026-10-13 as saved data, not play-tested: five sizes of each, fixed amounts and prices, a Mana Draught at half the points for the same price, the weights; sold only by the Tool Dealer, never dropped.
- [Town Supplies and Hunting Finds](2-systems/town-supplies-and-hunting-finds.md) — BUILT 2026-10-13, not play-tested: wings and the Antidote are bought and never dropped; each creature drops one find by its element (a converter or a food) at 5% / 50% / 100% by kind; no shop buys a find; the six foods last 5 minutes.
- [The Shop Cart, the Tax, and the Station Window](2-systems/shop-cart-and-station-window.md) — BUILT 2026-10-14, not play-tested: one value per item and a one-third Tax per unit on buying and selling; the shop is one window (For sale / Cart / Inventory) whose cart is one all-or-nothing trade; the station window is tabs over the bag's grid with a fold preview.
- [The Keeper and the Wings](2-systems/keeper-and-wings.md) — BUILT 2026-10-12, not play-tested: the Keeper — one per town — (Save Point at that Keeper, account-wide Storage, Teleport to every other Keeper priced 40u × the character's level a door, a door per 80 m of ground in the one world), how a storage move rides the character's save, and the Butterfly Wing and the Fly Wing (lands only in a named area of the world).

## World & content

- [Mob Roster — Normal, Elite, Shadow](3-world/mob-roster.md) — the three kinds of creature: levels, skill slots, gear, summoning, respawn and drops (design, not built).
- [Creature Materials — What Each Creature Drops and What It Sells For](3-world/creature-materials.md) — built 2026-10-13, not play-tested: no Unsigned from kills; every creature's own material, its tier set by the creature's multiplier, its sale price by level and tier, the five drop tiers and why, and how often a creature comes back.
- [Creature Gear — What Every Creature Wears and Wields](3-world/creature-gear.md) — built, not play-tested: every creature's weapon, armour and stat build on its file; the grade of what it wears set by its level, its folds, card slots and base rolled each life.
- [What a Kill Pays — Level and Multiplier](3-world/what-a-kill-pays.md) — built, not play-tested: a kill pays what its level gives × the creature's multiplier — experience, Temper Dust, Quench Oil, the chance of its Inks, Salts and own material, and saved-character Unsigned; kind and gear add nothing on their own; nothing measured.
- [Saved Characters — When a Mob Transforms Back](3-world/saved-characters.md) — a beaten mob can turn back into the character the artist drew: never-fights (pays Unsigned), allies, rebels; the Unsigned rule, the elite-to-ninja table and how each behaves (built, not play-tested).
- [Biomes — Ground Tiles and Props](3-world/biomes.md) — every biome's main ground, patch and road tiles, the props that fit it, and the props still to make; the all-biome map generator plan.
- [Biome References — Real Places Beside Our Maps](3-world/biome-references.md) — one real photo per biome beside pictures of its test map, to judge what looks right and what is missing.
- [Story & Setting (Canon)](3-world/story-setting-canon.md) — the anchor lore doc; the artist, Chaos, the corrupted pages and the Fourteen Kashira (one per weapon).

## Art & animation pipeline

- [GPT Concept Prompts — Paste-Me](4-art/gpt-concept-prompts.md) — props + weapons image-generation prompt sets.
- [Art Prompts — the Standard](4-art/website-art-prompts.md) — the one art style and Leonardo settings for every generated picture, with a prompt for every website box and the skill icon sheets, each weapon's icons built from its Kashira's theme.
- [Mixamo Animation Integration — Workflow](4-art/mixamo-animation-workflow.md) — no-code animation import.

## AI learnings & improvements

- [AI Learnings & Improvements](5-ai-learnings/README.md) — how to WORK, not what to build: habits that paid off and mistakes that cost real time, each page naming the incident that earned it. General method, useful on any codebase. **Open its index before a second attempt at anything already stuck** — the index is symptom-first, so look up what you are seeing. There is no page limit — the index is what keeps it readable; the write/read protocol is in `CLAUDE.md`.

## Player wiki

- [Unwritten Wiki](../wiki/) — the player-facing guide (lore, character, combat, monsters). Its pages are `content/*.json` JSON block documents at repo-root `wiki/` (nav in `content/manifest.json`) — NOT under `docs/` — shown by the website (`website/index.html`, served by the download server). Edit wiki pages there, not here. Run `python3 tools/wiki-sync-check.py` (or `/sync-wiki`) to find pages that have fallen behind their design docs.
