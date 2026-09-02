---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - planning-production
---

# unwritten — Roadmap to Launch

RO-style **2.5D** action-RPG (3D world + billboard sprites), built in Unity. *Counts re-measured 2026-08-30; the percentage estimates below are still the 2026-08-24 ones and have not been re-judged.*

> **Progress snapshot (percentages estimated 2026-08-24, counts re-measured 2026-08-30):** against **Tier 1 (Minimum Shippable Steam game)** we were, by estimate, **~45–50% done** (up from ~30–35% on 2026-07-02); against the **full MMORPG dream** ~**10–12%**. Per-milestone estimate: **M1 ~95%**, **M2 ~20%**, **M3 ~35%**, **M4 ~0%**, **M5 ~0%**. ⚠️ **Those percentages predate the multiplayer layer** (see Tier 2 below, which is no longer untouched) and predate the consumables and the build pipeline — they need re-judging, which is a call for vince, not a measurement.
>
> The July snapshot's headline gap — *the first fully-realized class kit* — is **closed**: the sword pool finished on 2026-08-24 at 22 composed skills. The gap has moved. It is now that **the world has no reasons in it**: only 2 wearable items exist, no quests, no named NPCs, no boss placed in any map, no title screen, and no client build has ever been produced.

> **What this doc is.** The single source of truth for *how far we are from a real, shippable game* and *what stands between here and a Steam launch*. It replaces the old phase-by-phase 2D plan (the 2D build order is now history — see **Appendix**). It is deliberately honest about scope: the dream is a full MMORPG; the **shippable path** is a single-player (or small co-op) RO-style ARPG first.

---

## TL;DR — the honest distance

- **Against the full MMORPG dream** (huge map, 8 classes w/ advancements, dungeons, NPCs, stories, **massively-multiplayer**, polished on Steam): roughly **10% of the way** there. The remaining 90% is a **multi-year, multi-person, funded-studio** effort. The "MMO" alone is a *second entire game* the size of the one we have.
- **Against a Minimum Shippable Game** (single-player/small-co-op RO-style ARPG, one region, one class line, a few dungeons, a real story thread, on Steam): **about halfway**, and reachable on a plannable horizon — **months, not years**.
- **The shape of what remains has changed.** Through July and August the work was *combat technology*, and it is now deep and finished for one weapon. What is left is almost entirely **content and connective tissue**: things to find, people to talk to, reasons to enter a dungeon, and a wrapper that makes it a product.
- **Strategy:** Ship Tier 1 first. *The MMO is what kills projects like this* — it's the most expensive, least-finished part, and it makes the whole thing feel "0% done" forever. Prove the game is fun and find an audience with Tier 1; let success justify Tier 2.

---

## Where we actually are (counts measured 2026-08-30)

**The counts, so nobody has to guess:**

| Thing | Count | Note |
| --- | --- | --- |
| Game + tooling C# files | 357 | 93 of them editor tools |
| Playable maps on disk | 41 | 10 towns · 17 fields · 14 dungeons — all carry real content |
| Mob definitions | 86 | 20 flagged as bosses — **0 placed in any map** |
| Composed skills | 42 | 22 sword (pool complete) · 9 magic/support · 11 carried by consumable items |
| Items in the entire game | 13 | **2 wearable** (one sword, one bow) · 11 consumables |
| Cards | 4 | |
| Client builds ever produced | **0** | the build script makes a client, but only the server has been built |

**The foundation — the hardest part to start — is real:**

- **2.5D rendering:** 3D world + billboard sprites, merged terrain platforms, per-feet depth sorting, occluder fading, planar reflection, hand-drawn grade pass.
- **Spatial layer:** WASD/click movement, camera rig, grid A* pathfinding, crowd pass-through, portals/spawns, persistent global-systems scene with additive map loading.
- **Combat — the deep, finished part (built 2026-08-11 → 08-24):** player and mob resolve damage through **one shared set of rules** and **one landing choke point**; crit → hit → damage resolves in a fixed order; stacks belong to the skill that granted them and finishers cash them; every timed stat change is **one** sub-stat effect (the per-stat verbs were deleted); modifiers fire at a moment and finish; flinch, earned footing, weapon reach, parry charges, banked damage, combo runs counted by the fight.
- **The first class kit is done:** 22 composed sword skills, each with an authored description, all written by one composer that everything else reads from. Nine more magic/support skills exist across staff and healing.
- **RPG numbers:** RO-style primaries (STR/AGI/VIT/INT/DEX/LUK) + allocatable points, per-level growth, 10 elements + modifier table, pre-renewal ATK/MATK formula port, the **Trio-Ring** percent-passive draft.
- **Gear machinery — deeper than the item count suggests:** equipment instances carry **refine levels, slotted cards, per-copy rolled rarity, and rolled skills**, and all of it persists (save format v7, alongside level, XP, stats, ring walk, bag, gold, hotbar bindings, dyes).
- **World & life:** procedural town/map generator, tiered interiors, ambient townsfolk with reactive barks, weather (rain/snow/wet ground/reflections) on a calendar, day/night with night lamps, music director, minimap + world map.
- **VFX pipeline:** particle library behind one play-at choke point, an event→cue layer, and an edit-mode composer window.
- **Art pipeline:** GPT→Trellis→Blender→Unity prop pipeline proven, 8-dir sprite baker, live-3D player and NPC rigs, locked style guide v2.1 and charcoal+gold UI palette.
- **HUD:** inventory / equipment / character trio, skill book, data-driven hotbar, buff bar, cast bar, damage popups, death screen.
- **Workflow:** offline C# and skill-data checkers that run in seconds without opening Unity, plus a live editor link for reading real values.
- **Multiplayer — started, and no longer a Tier-2 abstraction (see Tier 2 below):** **four** processes now — a sign-in service, a character server that owns every character and every save, a headless game server that owns the world and never touches the database, and one shared Postgres. Two players have been watched standing in the same town seeing each other move. A player is let in by the character server, which pushes their whole character to the game server before telling them where to knock. The combat half is written and largely unrun. There is deliberately **no offline game any more** — pressing Play needs a running server. Details in [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md); how to stand it up in [Starting the Network](starting-the-network.md).

**Honest read:** this is a combat system a stranger could enjoy for twenty minutes and then run out of reasons to keep playing. The technology is not the blocker and has not been for a while.

---

## TIER 1 — Minimum Shippable Game (the Steam target)

**Definition of done:** a stranger buys it on Steam, plays 3–8 hours, and feels they got a complete RO-flavored ARPG. **Single-player or small co-op.** No "MMO."

Milestones are in dependency order; each ends in something you can actually *play*.

### M1 — Core loop is fun for 30 minutes — **~95%, effectively done**

- **Death → respawn/game-over flow** — built. "YOU DIED" vignette, death pose, countdown, revive at full HP at the map spawn point.
- **Save/load** — built and now well past the original slice: level, XP, HP/MP, the six primaries + unspent points, the full Trio-Ring walk, gold, hotbar bindings, active passives, **equipment and bag with refines/cards/rarities/rolled kits**, and outfit dyes. Auto-loads on launch; auto-saves on level-up / respawn / quit / every 20s when changed.
- **Enemy variety + behavior feel** — 86 mob definitions with kiting and fleeing behavior axes, weighted field scatter, hit-flash, death poof, floating combat text.
- **One fully-realized starting class** — **done 2026-08-24.** The sword pool closed at 22 skills.
- *Remaining:* a combat-feel polish pass (hit-stop, knockback, crit emphasis) and a real ranged projectile for casters.
- *Playable when:* you can start a character, fight a varied field, level, die, reload — **and your progress survives a restart.** ✅ true today.

### M2 — A world worth exploring (one region) — **~20%**

- **Built:** 41 maps exist with real geometry, mob spawning and portals; minimap and world map work; map transitions work. The four largest towns are hand-finished.
- **Missing, and this is the milestone:** **no quest system exists at all** (not partial — no such code). **No named NPCs** — townsfolk are anonymous and speak random ambient lines; the dialogue box exists but nothing drives it. **No boss is placed in any map**, so no dungeon has a destination. The 14 dungeons are generator output with no identity.
- *Playable when:* you can follow a quest from the town into a dungeon and beat its boss.

### M3 — Progression & loot spine — **~35%**

- **Built:** the *machinery* — equip/inventory/stat-bonus UI, refine, card slotting, rolled rarity and rolled skills per copy, gold, loot-dropper hooks on death, all persisted.
- **Missing:** the *content* — **only 2 wearable items in the whole game**, no drop tables authored, no armor, no shop or economy, no crafting (despite a crafting design already written). **Class advancement does not exist** in any form. No respec, no demo level cap.
- **Landed since this milestone was last judged:** **11 consumables** (potions, food, an antidote), each carrying its own composed skill and going through the same one door every other item use does.
- *Playable when:* killing things drops gear that meaningfully changes your build.

### M4 — Game-shaped product — **~0%**

- No title screen or new-game flow; no pause; no controller support; no tutorial; no localization scaffolding. In-game settings and audio wiring exist, and a sign-in + character-select door now stands in front of the game, which is the only part standing.
- **Steam integration** — nothing started. **Packaging is no longer entirely unproven:** a one-command build script exists that produces either half, and the **server** has actually been built and run. A **client** build has still never been produced, so the player-facing half of packaging is the unproven part.
- *Shippable when:* a first-time player needs zero outside instructions.

### M5 — Content multiplication (the real 80%) — **~0%**

- One weapon of the twelve declared has a skill pool. Scaling to 7–8 classes (+ advancements), hundreds of items, more zones and story is where most of the calendar time goes.
- Balance pass, QA/bug pass, "no unfinished work" polish.
- **Optional small co-op** (2–4 players, friend-hosted) — far cheaper than a true MMO and a big Steam selling point.

> **Recommended first public step:** a **vertical-slice demo** (M1 + a thin M2) — one class, one dungeon, one story beat — as a **WebGL build** or a **Steam "Next Fest" demo** to test whether it's fun *before* committing to M5's content grind.

---

## The recommended next move — the loot spine, before quests

Two items means every fight the last two months perfected pays out **nothing**. Loot is also the cheapest large win available: the gear machinery is already built and proven, so authoring items is data entry against a working system rather than new technology. Fifty items with real drop tables would turn the existing 86 mobs and 41 maps into a loop, with no engine work at all.

Then, in dependency order:

1. **Loot spine** — ~50 items (weapons, armor, consumables), drop tables per mob, a first shop.
2. **One dungeon with a point** — place a boss (20 are already defined), give that dungeon a layout and a reward.
3. **A minimal quest system** — track and turn in, with three named NPCs to carry it.
4. **A title screen and a first real build** — prove packaging early, while it is cheap to fix.

---

## TIER 2 — Full MMORPG Vision (started, ahead of the strategy above)

**This tier is no longer untouched.** The plan said defer it until Tier 1 lands; the work went ahead anyway through late August, so the honest statement is that the foundation of Tier 2 exists and Tier 1's content gap did not shrink while it was being built. That is a real trade already made, not a proposal. Whether to keep spending here before the loot spine is the open strategic question — see *The recommended next move* above, which still says content first.

What now stands, measured (details in [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md)):

- **Networking core** — **partly built.** Server-authoritative on headless Unity, Mirror as the library, a shell/body split so only ids and positions cross the wire, range-based interest at 60 units, and a reconnect-then-give-up loop. **Watched working:** the server boots, players sign in and see each other move. **Written and never run:** server-owned creatures and the whole combat round-trip. **Not built:** client prediction/reconciliation — movement is relayed and trusted, which is the largest piece left — and zoning/sharding.
- **Backend & accounts** — **built for the basics, and the authority moved.** Login/auth, a real database of accounts and characters, and a separate character server that owns the list, the saves and the door into the world. **The server now writes progress, and owns the bag, the money and the worn gear**; a save from a player's own machine is refused while they are in a world, and movement reports are checked against what was possible. **Not built:** sharding, trade, and resolving skills server-side (a skill's damage and a heal's size are still the client's word).
- **Live service** — **nothing started.** 24/7 hosting + ops cost, anti-cheat, monitoring, patching/live-ops, community/moderation, customer support.
- **MMO content systems** — **chat exists.** Parties/guilds, trading, auction house, PvP, world bosses and social spaces do not.

**Reality check (unchanged):** solo devs almost never ship a *true* MMO; they ship co-op or "online-lite." Plan for **funding and/or a small team and a backend specialist** before finishing this tier — or scope it permanently to small co-op and call the dream answered.

---

## What it would realistically take

- **Honest playable demo (M1 + thin M2):** months of focused content work — no new technology required.
- **Full Tier 1 on Steam (M1–M5):** roughly a **year-plus** of sustained work, dominated by M5 content volume.
- **Tier 2 (true MMO):** **multi-year**, and the part that most needs people/money beyond just direction.

Nothing in Tier 1 is blocked by missing *technology*. It is blocked by **content and connective tissue** — which is exactly the kind of work we can chew through milestone by milestone.

---

## Appendix — build history (the 2D → 2.5D path)

The original plan built a **2D** foundation in phases A–G (engine → character/world → towns/interaction → monsters/combat → UI shell → RPG numbers → skill draft), single-player-first with MMO deferred. As of **2026-06-16** the project **pivoted to RO-style 2.5D** (3D world + billboard sprites); the RPG/UI/combat/art systems ported over while the spatial layer (movement/depth/pathfinding/maps) was rebuilt for 3D. On **2026-06-18** the skill system was redesigned to **TRIO-RING** (the old 28-node graph is retired) and the RO-style flat stat layer was added. Through **July–August 2026** the effort was almost entirely combat: unified player/mob rules, one damage choke point, the stack and sub-stat collapses, and the composed skill pool that closed the sword line on **2026-08-24**. Those built systems are folded into "Where we actually are" above; this appendix exists so the lineage isn't lost.

---

## Related

- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — what M1's combat actually is.
- [Weapon Roster — 12 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the twelve pools M5 has to fill.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the progression engine behind M3.
- [Crafting & Recycling](../2-systems/crafting-and-recycling.md) — the loot loop M3 is missing the content for.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — why the 86 mobs are comparable to a player.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — what Tier 2 has actually become.
- [Starting the Network](starting-the-network.md) — how to stand the multiplayer half up.
- [World Creator — Spec](../3-world/world-creator-spec.md) — the pipeline that made the 41 maps.
- [Biome Mob Families — Tiered Spawning](../3-world/biome-mob-families.md) — how M2's maps get populated.
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — the reasons M2 needs to put in the world.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the habit that keeps M1 smooth.
- [Dev Contribution Log](dev-contribution-log.md) — who steered which of these calls.
