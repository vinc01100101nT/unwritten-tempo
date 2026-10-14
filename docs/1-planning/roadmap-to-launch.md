---
status:
owner: vince
last-updated: 2026-10-05
tags:
  - planning-production
---

# unwritten — Roadmap to Launch

How far the game is from being something a stranger can buy and finish. **This document is the picture. [Action Plan](action-plan/on-going.md) is the list of what to do next** — it has the checkboxes, the done-checks and the order. Neither repeats the other.

**Every number on this page was counted on 2026-10-05 by reading the game's own files, its database, or its builds.** Nothing here is an estimate. Where something cannot be counted, it says so.

---

## The one-paragraph answer

The technology is built and the game has started to fill. Fighting is finished and deep, and the whole of a fight runs on a server. Since mid-September a fight pays something worth having: ten crafting materials (Temper Dust, Quench Oil, five Inks, three Salts), gear that drops from what creatures wear to fold and quench with them at three stations, a shop, two quests, sixteen townsfolk with names, and a Keeper in every town who sets your save point, keeps your account's storage and sends you to any other town. What is still thin is breadth. Only the sword, one of fourteen weapon families, has skills anybody can learn, and only the sword and the bow exist as weapons to hold. Every named person and both quests are in one town of sixteen. None of the fourteen shadows has a place to stand. And most of what was added this month is built but has not yet been played.

---

## What is built, counted

| Thing | Count | What the count means |
| --- | --- | --- |
| Game and tooling code files | 454 | C# files in the Unity project, 104 of them editor tools exposing 82 menu commands; the character server is 13 more C# files |
| Playable maps | 77 | the three-region world: 16 towns (Lastline among them) · 39 fields · 22 dungeon floors, joined by 88 two-way doors. 14 shadow places are in the world layout with no map built. 15 test maps sit outside the world |
| Creature definitions | 98 | 66 normals, 18 elites and 14 shadows |
| Skills | 51 | see the split below |
| Items in the whole game | 223 | 14 weapons · 27 other worn pieces · 29 crafting materials · 126 creature materials · 27 to use (10 potions, 6 foods, 2 wings, the Antidote and 8 element converters) |
| Cards | 0 | no card exists, and the card roster has not been decided |
| Quests | 2 | a crafting tutorial given to every character, and one side quest from Torvald in Lastline; no main-story quest |
| Townsfolk with a name | 16 | all in Lastline: three keep a crafting station (smith, loom, tannery) and one keeps the only shop |
| Saved characters | 91 | the characters a beaten creature can turn back into |
| Live characters in the database | 7 | the furthest is level 8; two test characters hold 99,987,850 gold each |
| Builds that exist | 6 | made on this PC: the Windows game (847 MB), the Windows game server (420 MB) and the Mac game (1,009 MB), all on 5 Oct; the launcher for Windows (99 MB), Mac with an Apple chip (108 MB) and Mac with an Intel chip (101 MB), on 4 Oct. The sizes leave out the debugging folder Unity writes beside a build, which never ships |

**The 51 skills, split by what can reach them:** 22 belong to the sword and all 22 are published, so each can be learned with sword mastery points and rolled as a dropped sword's trademark. 19 are carried by items you use — the 11 potions and foods and the 8 element converters. 1 belongs to the staff and is unpublished, and no staff exists to earn staff mastery with. 1 is the summon, which only creatures cast. The remaining 8 — two heals, a healing circle, a fire bolt, a hex field, a bulwark, a war banner and a blink — belong to no weapon and no item, so nothing in the game can grant them.

**The parts that are genuinely finished:**

- **Fighting.** Players and creatures resolve damage through one shared set of rules and one landing point. Crit, then hit, then damage, in a fixed order. Flinch, footing, weapon reach, parry charges, banked damage, combo runs. Every timed stat change is one buff effect or one debuff effect, never a separate verb per stat. Every ultimate shares one 40-second cooldown on top of its own.
- **The numbers behind it**, pinned and checked in about twenty seconds on this PC without opening Unity: among them the experience curve, the stat-point budget, the accuracy window, the stat jumps, the element chart, the ring, the quality ladder, quench odds, Ink, the mastery curve, the walking rule, standing water, saved characters and quest steps.
- **The world's shape.** Movement, camera, pathfinding, crowd pass-through, doorways, additive map loading, weather on a calendar, day and night with lamps, a music director, a minimap, and a Map window with a view of the map you stand in and a view of the whole world.
- **The programs that make it online.** A sign-in service; a character keeper that owns every character and every save; a game server that owns the world and never touches the database; one shared database; and a download server that hands out the website, the launcher and every game update. Two people have been watched standing in the same town seeing each other move, and a fight against server-owned creatures has been played. The whole of a skill runs on the server. An area is placed on the least busy machine when somebody wants it and released 45 seconds after the last person leaves. One account can only be signed in once. There is deliberately no offline game.
- **Saving.** Save format version 18 holds level, experience, the six primaries and unspent points, the full ring walk, gold, each weapon's mastery and the level bought in every skill learned, the bag and worn gear with their folds, grade, written stats, trademark and each piece's own id, the cooldowns (the ultimates' shared cooldown included) and timed effects still running, where the character stands and its save point, and outfit dyes. The hotbar and each quest are stored in rows of their own beside the save, and what an account keeps in storage in a table of its own.
- **The screens.** Sign-in, character select, loading, inventory, the character sheet with the ring draft beside it, the skills window, the three-row hotbar, the Map window, the Quests window, the crafting station window, the shop window, the calendar, the system menu and settings, the death screen, chat, the dialogue box, tooltips, the target panel, and the right-click item menu. The game is PC-only: Windows and Mac.
- **The checks.** The game's code compiles in about eight seconds without opening Unity, including once as a shipped PC build sees it; the arithmetic is checked outside the editor entirely; a sweep asks whether everything the game loads still points at something that exists, against 29,168 asset ids, and finds nothing broken. Eleven checks have a prover that breaks them on purpose to confirm they complain.

---

## What is half-built — measured, each one

**Making gear is built and not yet played** ([Gameplay System V4](../2-systems/gameplay-system-v4/README.md)). Gear is dropped by the creatures that wear it, always Raw, then folded, quenched up the quality ladder and written on with Ink at a smith, a loom or a tannery.

**Loot follows the creature's kind and level.** A creature gives up **no gear at all** — materials and consumables only. Every creature drops its own level's grade of metal, cloth and leather with a chance of an Ink or a Salt — elites much more, shadows more again — and every kill has a chance of the creature's own find, a food or an element converter ([Town Supplies and Hunting Finds](../2-systems/town-supplies-and-hunting-finds.md)). What drops is worked out at the moment of death from the creature's kind and level, so a change to the rule needs no rebuild of the creatures. Every creature's own hand-written drop list is empty. It lands on the ground, held for the killer for 10 seconds, and is gone after 60.

**Only the shadows wear anything.** Each of the 14 shadows wears gear authored for it, at grade A with 30 folds. None of the 66 normals and 18 elites has a piece authored for it.

**Gear fills every place on the body, and every weapon type can be held.** Five places can hold something — head, body, weapon, garment and shoes. Each of the four armour places has three designs, one of metal, one of leather and one of cloth. The sword and the bow have their own models; the other twelve weapon types are stand-ins that borrow one of those two looks until their own models arrive.

**Cards do not exist yet, and cannot be slotted.** No cards are in the game, and "Insert card" is off the item menu until slotting has a server side — it used to change only the player's own copy, and offered every card to anyone with an empty socket, whatever they owned. Cards also cannot be carried — a drop list can only name an item, and the bag can only hold an item, and a card is neither.

**Elites are placed; shadows are not.** All 18 elites have rows in the world spawn list — 43 rows, 35 on fields and 8 on dungeon floors — beside the ordinary creatures of those maps. None of the 14 shadows has a row, and the 14 places meant for them exist only in the world layout, with no map built and no door leading to them.

**The world is joined up, and nobody has walked it.** Every one of the 77 maps can be reached from Lastline through doors placed in the maps themselves: 176 door ends, every one leading back the way it came. Every field and dungeon floor has creatures; no town does.

**Every town has a Keeper; one town has anybody else.** Each of the sixteen towns has one Keeper standing inside one of its doors ([The Keeper and the Wings](../2-systems/keeper-and-wings.md)). Lastline also has sixteen townsfolk, each with a name and a line saying what they are. The other fifteen towns have nobody but their Keeper. Outside a quest, talking to any of them gives a conversation picked at random from one shared set, by time of day.

**Quests and the shop are built and not yet played** (2026-10-04, [Quests and Tutorials](../2-systems/quests-and-tutorials.md)). Three quests exist: two tutorials (crafting your first gear, and buying and drinking healing potions after coming back on low health) and Torvald's side quest. One shop exists: Fenwick the Tool Dealer in Lastline, selling four potions, the Fly Wing and the Butterfly Wing, and buying anything with a sell value.

**No death pays gold:** hunting earns it by selling each creature's own material to a shop, and by saving a character that never fights — gold worth 4, 8 or 12 kills of its creature (ordinary, helpless, wealthy) plus 3, 6 or 9 of that creature's material.

**Saved characters are built and not yet played** (2026-09-30, [Saved Characters](../3-world/saved-characters.md)): 15% of normal and elite kills turn the creature back into one of 91 characters for 30 seconds — one that never fights, an ally or a rebel — on one rule for who is whose enemy.

---

## What is missing entirely

No main-story quest. No class or job advancement. No title screen or new-game flow — a sign-in box is the first thing a player meets. No controller support, no translations. Nothing to do with Steam: no store page. No installer: the download is a zip holding the launcher, which fetches the game and every update after it. No parties, guilds, trading between players, auction house or player-versus-player. No monitoring beyond a public Server Status page, and no anti-cheat. No rented machine: the addresses the game and the launcher are given point at this PC. Where the game will be hosted is decided ([Where the Game Is Hosted](where-the-game-is-hosted.md)); nothing there is set up.

*(The absences in this section were established by searching the code for each thing by name. A search proves absence only as far as its own reach; these were not exhaustively re-derived.)*

---

## The honest distance

**The blocker is not technology, and has not been for months.** Everything left in the shippable version is content and connective tissue: things to find, people to talk to, reasons to enter a dungeon, and a wrapper that makes it a product. That is work that can be chewed through item by item, which is what the [Action Plan](action-plan/on-going.md) is for.

**Four things put a real floor under the calendar,** and none of them is an estimate — they are counts of work not yet begun: thirteen of the fourteen weapon families have no skill anybody can obtain; twelve of the fourteen have no weapon; fifteen of the sixteen towns have nobody but a Keeper and no quest; and none of the fourteen shadows has a map to stand on.

**The game is online only.** The world runs across several machines, the server owns the fight, and progress is written where a player's own machine cannot forge it. Content has been added on top of that since mid-September, and most of it is waiting to be played.

**Still not built inside the online half:** movement is reported by the player's machine and checked by the server, and the networking notes say predicting it on the server is deliberately not the plan. Six entries are written down in those notes under a heading calling them deliberate limits — among them that a creature has no sense of who is fighting it, so two people attacking one is not resolved fairly.

---

## Where the last month went

Fifty-five commits from 5 September to 5 October, in this order: the world split across several machines, one login per account (5 Sep) → a far cast walks you there and reach became a stat, plus a client performance pass (7 Sep) → hosting decided, townsfolk networked, dropped things left lying on the ground (10 Sep) → a dangling-reference sweep and an arithmetic checker (12 Sep) → stat jumps, a bow that swings with DEX, the server drawing every number (13–14 Sep) → creatures given every skill moment, two dragons retired (17–19 Sep) → gear forged at stations, with mastery, Ink and recycling, and the ring made part of the character (21 Sep) → creatures split into normals, elites and shadows, and drops became one rule by grade (22 Sep) → armour, elements and timed effects (23 Sep) → ground tiles and all fourteen biomes in the map generator (24–25 Sep) → the 41 older maps replaced by the three-region world (26 Sep) → fields with ground levels and cliffs, then rain water (27–28 Sep) → a patcher launcher and one settings file (29 Sep) → saved characters and the story's lore (30 Sep) → cliffs made part of the one ground, and one set of scripts for Windows and Mac (1–2 Oct) → the website with the wiki inside it, and skills bought with mastery points (3 Oct) → the three-row hotbar, quests and the crafting tutorial, the Map window, the shop and named townsfolk (4 Oct) → other players shown in their chosen outfit colours (5 Oct).

**The content number that matters:** across those fifty-five commits the game gained 52 items (15 armour pieces, 29 materials, 8 element converters), the 8 skills the converters carry and the creatures' summon, 14 shadows, 2 quests, 16 named townsfolk, 1 shop and 91 saved characters, and swapped 41 maps for 76. It gained no weapon skill and no weapon family with skills, and the 4 placeholder cards were deleted.

---

## What is open right now

Eighteen tasks in the [Action Plan](action-plan/on-going.md) are built or under way and not yet proven in play — among them weapon mastery and skill levels, the doors between all 77 maps, the field shapes and rain water, quests and the tutorial, the shop, saved characters, and the launcher with the server turning away an outdated game.

---

## Related

- [Action Plan](action-plan/on-going.md) — the trackable list of what to do next, phase by phase, in three files by status.
- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — what the finished combat actually is.
- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — dropped gear, folding, quenching, Ink and mastery; built, not yet played.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — twelve families with no skill at all, and the staff's single unpublished one.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the progression engine.
- [Quests and Tutorials](../2-systems/quests-and-tutorials.md) — the quest system and the crafting tutorial.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — why the 98 creatures are comparable to a player.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — what the online half is, and its deliberate limits.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — how to stand the multiplayer half up.
- [World Map](../3-world/world-map.md) and [World Creator — Spec](../3-world/world-creator-spec.md) — the three-region world and the pipeline that made its maps.
- [Creatures and Where They Live](../3-world/biome-mob-families.md) and [Mob Roster](../3-world/mob-roster.md) — how maps get populated.
- [Saved Characters](../3-world/saved-characters.md) — what a beaten creature can turn back into.
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — the reasons the world still needs put into it.
- [Where the Game Is Hosted](where-the-game-is-hosted.md) — the machine, the latency numbers, and the cost.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the recurring measurement habit.
- [Dev Contribution Log](dev-contribution-log.md) — who steered which call.
