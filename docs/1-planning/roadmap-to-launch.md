---
status:
owner: vince
last-updated: 2026-09-12
tags:
  - planning-production
---

# unwritten — Roadmap to Launch

How far the game is from being something a stranger can buy and finish. **This document is the picture. [Action Plan](action-plan/on-going.md) is the list of what to do next** — it has the checkboxes, the done-checks and the order. Neither repeats the other.

**Every number on this page was counted on 2026-09-12 by reading the game's own files, its database, or its builds.** Nothing here is an estimate. Where something cannot be counted, it says so.

---

## The one-paragraph answer

The technology is built and the game is empty. Fighting is finished and deep: one weapon has a full skill set, the arithmetic behind every hit is pinned and checked, and the whole of a fight now runs on a server that four separate programs stand up between them. What is missing is everything that makes fighting worth doing — there are thirteen items in the entire game, two of which can be worn; eighty-five of the eighty-six creatures give up nothing but gold; twenty bosses exist and none is placed anywhere; and twenty-three of the forty-one older maps (now set aside) have no doorway, so nothing leads to them. A stranger would enjoy twenty minutes and then run out of reasons.

---

## What is built, counted

| Thing | Count | What the count means |
| --- | --- | --- |
| Game and tooling code files | 375 | 95 of them editor tools, exposing 88 menu commands |
| Playable maps | 75 new + Lastline | the three-region world, generated 2026-09-26: 8 cities · 6 towns · 39 fields · 22 dungeon floors, plus the hand-made Lastline (`lastline`); the 40 older maps were deleted 2026-09-26 |
| Creature definitions | 98 | 66 normals, 18 elites and 14 shadows |
| Skills | 42 | see the split below |
| Items in the whole game | 13 | 11 consumables · 2 that can be worn (a sword and a bow) |
| Cards | 0 | the four Ragnarok-named placeholders were deleted 2026-09-26; the card roster is being designed |
| Live characters in the database | 14 | the furthest is level 80 with 4,396 gold |
| Builds that exist | 2 | a 458 MB player build (8 Sep) and a 227 MB server build (10 Sep) |

**The 42 skills, split by what can reach them:** 22 belong to the sword and all 22 are published, which is what lets a dropped weapon carry them. 11 are carried by consumable items. 1 belongs to the staff and is unpublished. The remaining 8 — two heals, a healing circle, a fire bolt, a hex field, a bulwark, a war banner and a blink — belong to no weapon and no item, and all 8 are unpublished, so nothing in the game can currently grant them.

**The parts that are genuinely finished:**

- **Fighting.** Players and creatures resolve damage through one shared set of rules and one landing point. Crit, then hit, then damage, in a fixed order. Flinch, footing, weapon reach, parry charges, banked damage, combo runs. Every timed stat change is one effect rather than a verb per stat.
- **The numbers behind it**, pinned and checked in three seconds without opening Unity: the experience curve, the stat-point budget, the accuracy window, the element chart, the ring's ceilings.
- **The world's shape.** Movement, camera, pathfinding, crowd pass-through, doorways, additive map loading, weather on a calendar, day and night with lamps, a music director, a minimap and a world map.
- **Four programs that make it online.** A sign-in service; a character keeper that owns every character and every save; a game server that owns the world and never touches the database; one shared database. Two people have been watched standing in the same town seeing each other move, and a fight against server-owned creatures has been played. The whole of a skill runs on the server. An area is placed on the least busy machine when somebody wants it and released 45 seconds after the last person leaves. One account can only be signed in once. There is deliberately no offline game.
- **Saving.** Save format version 18 holds level, experience, the six primaries and unspent points, the full ring walk, gold, each weapon's mastery and the level bought in every skill learned, the bag and worn gear with their folds, grade, written stats, trademark and each piece's own id, the cooldowns (the ultimates' shared cooldown included) and timed effects still running, where the character stands, and outfit dyes. The hotbar is stored in a row of its own beside the save, written only when it changes.
- **The screens.** Sign-in, character select, loading, inventory, character sheet, the ring draft, the skills window, the calendar, the world map, the system menu and settings, the death screen, chat, the dialogue box, tooltips, and the right-click item menu. The game is PC-only.
- **The checks.** The game's code compiles in about eight seconds without opening Unity, including once as a shipped PC build sees it; the arithmetic is checked outside the editor entirely; a sweep asks whether everything the game loads still points at something that exists — 28,341 links, all resolving. Each has a prover that breaks it on purpose to confirm it complains.

---

## What is half-built — measured, each one

**Loot follows the creature's kind and level.** Since [V4](../2-systems/gameplay-system-v4/README.md) a creature gives up **no gear at all** — materials and consumables only; since 2026-09-30 gold comes from saving a character that never fights ([Saved Characters](../3-world/saved-characters.md)). Since 2026-09-22 ([Mob Roster](../3-world/mob-roster.md)) every creature drops its own level's grade of metal, cloth and leather with a chance of an Ink or a Salt — elites much more, shadows more again — and every kill has a 30% chance of one random consumable. It lands on the ground, held for the killer for 10 seconds. A creature wears exactly the pieces authored for it, at the grade, folds and Ink authored on its own catalog entry — normals from Raw +0 to E +15, elites from D +0 to B +30, every shadow A +30.

**Gear is one place deep.** Six places on the body can hold something — head, armour, garment, weapon, shield and the ring. Only the weapon has any item authored for it.

**Drop lists name items by typing their names, and nothing checks the spelling.** The sweep that checks everything else follows proper links only. Rebuilding the creatures prints one warning per missing name and then never mentions it again. This is not hypothetical: the older creature roster already asks for nine items that do not exist — Life Potion, Silver Ring, Wooden Guard, Leather Vest, Fire Scroll, Jade Amulet, Katana, Iron Sword and Boots.

**Loot attaches when creatures are rebuilt, not when the game runs.** Editing a drop list and then play-testing without rebuilding shows nothing, and looks exactly like a bug.

**Cards do not exist yet, and cannot be slotted.** No cards are in the game, and "Insert card" is off the item menu until slotting has a server side — it used to change only the player's own copy, and offered every card to anyone with an empty socket, whatever they owned. Cards also cannot be carried — a drop list can only name an item, and the bag can only hold an item, and a card is neither.

**Bosses are defined but unreachable.** None is placed in any map. All 34 creature spawn points are set to pick any ordinary creature. Each dungeon has exactly one spawn point, so a boss cannot simply be added to it without removing that dungeon's ordinary creatures. The two shortcuts also mislead: asking for "a boss" gives a different random one on every respawn, and the one-click placement tool always places the first boss in the list.

**Over half the world cannot be walked into.** 36 doorways across 18 maps. The other 23 maps have none at all — 7 towns, 4 fields, and 12 of the 14 dungeons — and nothing anywhere leads to them.

**Townsfolk have no identity.** Every villager in the game returns the same hardcoded name, "Villager", and speaks randomly chosen ambient lines. There is no field to author a name into.

**The ring nobody authored.** Every character wears a "Ring of Beginnings" that the game builds in memory as it loads, because no ring item exists. The game already prefers an authored one if it finds it, so authoring it needs no code change.

**No death pays gold** since 2026-09-30: a creature's gold range is what a character saved from it is paid from, times its tier — ordinary ×20, helpless ×40, wealthy ×60.

**Saved characters are built and not yet played** (2026-09-30, [Saved Characters](../3-world/saved-characters.md)): 15% of normal and elite kills turn the creature back into one of 91 characters for 30 seconds — one that never fights, an ally or a rebel — on one rule for who is whose enemy. Nobody has watched it in play yet.

---

## What is missing entirely

No quest system — the only trace is a category an item can be filed under, which today does nothing but stop that item being dropped. No shop, no buying, no selling; every item carries a sell value, but nothing carries a buy price. No crafting or recycling, though a full design exists. No class or job advancement. No title screen or new-game flow — a sign-in box is the first thing a player meets. No tutorial, no pause, no controller support, no translations. Nothing to do with Steam: no store page, no installer, no update path. No parties, guilds, trading, auction house or player-versus-player. No monitoring, no anti-cheat and no way to patch a running game; hosting is decided and costed — one always-on machine in Singapore, 49 ms from Manila against Frankfurt's 244 — and nothing else in live-service is started.

*(The absences in this section were established by searching the code for each thing by name. A search proves absence only as far as its own reach; these were not exhaustively re-derived.)*

---

## The honest distance

**The blocker is not technology, and has not been for months.** Everything left in the shippable version is content and connective tissue: things to find, people to talk to, reasons to enter a dungeon, and a wrapper that makes it a product. That is work that can be chewed through item by item, which is what the [Action Plan](action-plan.md) is for.

**Three things put a real floor under the calendar,** and none of them is an estimate — they are counts of work not yet begun: thirteen of the fourteen weapon families have no skill anybody can obtain; twelve of the thirteen kinds of gear have no item at all; and none of the maps has a quest, a named person, or a placed boss in it.

**The multiplayer half went further than the plan intended.** The strategy was to ship a single-player or small-co-op game first and let success justify an online one. Through late August and September the online layer was built anyway, and it now works — while the content gap did not shrink. That is a trade already made, not a proposal. What it bought is real: the world runs across several machines, the server owns the fight, and progress is written where a player's own machine cannot forge it. What it did not buy is a reason to play.

**Still not built inside that half:** movement is reported by the player's machine and checked by the server rather than predicted and reconciled, which the code itself marks as belonging to a later phase. Six gaps are written down in the networking notes under a heading calling them deliberate limits — among them that a creature has no sense of who is fighting it, so two people attacking one is not resolved fairly.

---

## Where the last month went

Almost entirely the online layer and the checking tools, in this order: characters moved onto a server and a sign-in door appeared (26 Aug) → the server took ownership of creatures, then of each fighter (27–31 Aug) → the server took ownership of whole skills, and damage numbers began crossing the wire (2 Sep) → a categorised game log, so a play-test can see why something did not happen (4 Sep) → the world split across several machines, one login per account (5 Sep) → reach became a real stat and a far cast walks you there, plus a client performance pass (7 Sep) → hosting decided, townsfolk networked, dropped things left lying on the ground (10 Sep) → boss artwork fixed, a dangling-reference sweep, an arithmetic checker (12 Sep).

**The content number that matters:** across those thirty commits, the only new game content added anywhere was 11 consumables and their 11 skills, on 26 August. No new wearable item, no new creature, no new map, no new weapon skill. The last new map was added 29 June; the last new creature 17 July; the last sword skill 24 August.

---

## What is open right now

One play-test has been open since 3 September, covering eleven server and skill changes, with about half its checklist still unrun. Its one inconclusive item — the only thing that came back neither passed nor unrun — is whether a passive fires when a normal attack lands and again when something dies.

---

## Related

- [Action Plan](action-plan/on-going.md) — the trackable list of what to do next, phase by phase, in three files by status.
- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — what the finished combat actually is.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — ten families with no skill at all, and the staff's single unpublished one.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the progression engine.
- [Crafting & Recycling](../2-systems/crafting-and-recycling.md) — designed, no code.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — why the 98 creatures are comparable to a player.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — what the online half is, and its deliberate limits.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — how to stand the multiplayer half up.
- [World Creator — Spec](../3-world/world-creator-spec.md) — the pipeline that made the maps.
- [Creatures and Where They Live](../3-world/biome-mob-families.md) and [Mob Roster](../3-world/mob-roster.md) — how maps get populated.
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — the reasons the world still needs put into it.
- [Where the Game Is Hosted](where-the-game-is-hosted.md) — the machine, the latency numbers, and the cost.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the recurring measurement habit.
- [Dev Contribution Log](dev-contribution-log.md) — who steered which call.
