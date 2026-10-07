---
status:
owner: vince
last-updated: 2026-10-06
tags:
  - planning-production
---

# unwritten — Action Plan — On-going

Tasks whose status is `[ON-GOING]` — built, or under way, and not yet proven. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key for all three files are on this page, below.

Eleven weeks of work and the play-test between Week 6 and Week 7, as tasks. **This is the "what next" list.** [Roadmap to Launch](../roadmap-to-launch.md) is the "how far are we" picture — what is built, what is half-built, what is missing, all counted. Neither repeats the other.

**Each task is one short entry that links the doc describing it.** The design lives in that doc, never on the entry; an entry carries a fact of its own only when no doc holds it.

---

## How to read it

Each week opens with **the goal** — what you can do in the game at the end of it that you could not do at the start. Then its tasks.

Every task carries:

- **A status**, inside the brackets in front of the task: `[DONE]`, `[ON-GOING]`, or empty `[ ]` when it has
  not started — and a task sits in the file of its status: [done](done.md), [on-going](on-going.md),
  [not-started](not-started.md). Changing a status moves the task's whole block to the other file, wording
  untouched. **A task is never marked superseded, dead or stale.** When a later design changes what a task is
  about, the task is REWRITTEN to say the current design, keeps its ID, and takes the status that truth has
  today — a settled decision is `[DONE]`, work built but not yet watched is `[ON-GOING]`. Nothing on these pages
  describes a design that no longer exists.
- **An ID.** A main task is `MT-N`; a subtask sits indented under its main task as `ST-N-A`, where `N` is the parent's number and `A`, `B`, `C` … count its subtasks in order. An ID never changes and is never reused: a new main task takes the next free number, and a new subtask takes the next free letter under its parent.
- **`[D]` decision · `[A]` authoring · `[E]` engineering.** A decision task is yours alone. An authoring task can be handed to the data-author agent. An engineering task is code.
- **An estimate in days**, as a range. **The range already carries the swing from any decision it depends on** — you do not need to answer anything now for the estimate to hold.
- **A link** to the doc that describes the task.
- **Done when** — the concrete check. If you cannot watch it happen or count it, it is not a check.

**Decisions are tasks, never gates.** Each one sits in the week *before* the work that needs it, and its recommendation is in the doc it links, so you can settle it by agreeing. If a decision slips, the work after it still starts — on the recommendation — and the estimate already covers changing course.

**My assumptions, labelled as mine:** a week is five working days, you and me together. The weeks are ordered by dependency, not by calendar. Slipping one pushes the rest; it does not break them.

**Where the weeks are tight, said plainly.** Every week fits inside five days at the low end of its estimates. Two do not fit at the high end: **Week 4** (up to 5.5 days) and **Week 10** (up to 6). Those are the weeks where a decision going the long way costs you a day, and they are the two most likely to spill into the next week. The play-test is not a week: its tasks add up to 11–15 days, two to three weeks of work. Nothing is padded to hide it.

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something you can make, and a reason to keep working on the one you made.

  - [ON-GOING] `ST-39-E` **`[E]` Weapon Mastery.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)
    *Done when:* a kill with a sword raises Sword Mastery and nothing else, and a skill can be bought once its level is reached.

  - [ON-GOING] `ST-39-F` **`[E]` Skill levels.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)
    *Done when:* spending a point on one skill raises the number on its own card and no other.

---

## Week 5 — The world opens

**Goal:** you can walk from the first town to any map in the game.

- [ON-GOING] `MT-15` **`[A]` Connect every map of the new world by doors.** — **4–5 days** — see [The World Map — Three Regions](../../3-world/world-map.md)
  *Done when:* starting from Lastline you can reach every one of the 76 maps on foot, without a game-master command.
- [ON-GOING] `MT-44` **`[E]` Give the fields shape: ground levels, cliffs, meeting points, one walking rule.** — **built** — see [Biomes](../../3-world/biomes.md) and [Walking and Routing](../../2-systems/walking-and-routing.md)
  *Done when:* on both test maps you walk every level and climb a cliff at a meeting point, and a creature chases you up and down both.

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [ON-GOING] `MT-17` **`[E]` Give every elite its row.** — **1 day** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* every elite has a row, and each one's map still has its ordinary creatures.

---

## Play-test — strangers can play it

**Goal:** people you invite download the game, make their own account, and play it on a server we rent, not on this machine.

- [ON-GOING] `MT-42` **`[E]` Make a Windows version of the game.** — **1–2 days** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* on a Windows PC, double-clicking the game reaches the sign-in box of the rented server, with nothing typed into a terminal.

- [ON-GOING] `MT-43` **`[E]` A download page, and one place testers sign up and report problems.** — **1 day** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* somebody who has never seen the game follows one link, downloads it for Windows or Mac, and knows where to report what went wrong.

- [ON-GOING] `MT-50` **`[E]` A patcher launcher.** — **3–4 days** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* you change one thing in the game, publish the update, and a tester's launcher downloads only the changed files and starts the updated game — on Windows and on Mac.

- [ON-GOING] `MT-51` **`[E]` The server turns away an outdated game.** — **1 day** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* an old copy of the game is refused with a message telling the player to update, and the updated copy gets in.

---

## Week 7 — The world has people

**Goal:** you can talk to somebody with a name who says the same thing twice.

- [ON-GOING] `MT-20` **`[E]` Give a villager a name that can be authored.** — **1 day** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)
  *Done when:* two villagers standing side by side show different names.

- [ON-GOING] `MT-23` **`[E]` Store a character's quests.** — **1 day** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)
  *Done when:* a character logged out halfway through a quest comes back on the same step, after a restart of both servers.

---

## Week 8 — A quest can be given

**Goal:** somebody asks you for something and the game remembers being asked.

- [ON-GOING] `MT-24` **`[E]` Build the quest system.** — **4–5 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)
  *Done when:* a new character is given the crafting tutorial, the arrows lead to Gunnar, the guide walks forge, fold and wear to the end, and progress survives closing the game.

---

## Week 10 — Gold has a use

**Goal:** you can spend the money you have been collecting.

- [ON-GOING] `MT-28` **`[E]` Build a shop screen** — a buy price of 0 means no shop sells the item; a sell value of 0 means no shopkeeper buys it. — **4–5 days** — see [Roadmap to Launch](../roadmap-to-launch.md)
  *Done when:* you can sell a piece of loot for gold, buy a potion back, and both the gold and the bag survive a restart.

- [ON-GOING] `MT-29` **`[A]` Stock the first shops** — Lastline's potions cost twice their sell value (chosen 2026-10-04, yours to change). — **1 day** — see [Roadmap to Launch](../roadmap-to-launch.md)
  *Done when:* every town with a shopkeeper sells a list you chose, not a list the game made up.

---

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [ON-GOING] `MT-46` **`[E]` Let the World Creator put cliffs on a map.** — **built** — see [World Creator — Spec](../../3-world/world-creator-spec.md)
  *Done when:* a field set to "Cliffs: on" in the World Creator comes out of Build World with cliffs, and one set to off comes out without.

- [ON-GOING] `MT-48` **`[E]` Rain leaves standing water that works like water.** — **built** — see [Rain and Standing Water](../../2-systems/rain-and-standing-water.md)
  *Done when:* in a storm on `test_grassland` the lakes rise every 30 seconds, a hollow below an overflowing one fills, two players see the same water, the frame time in a storm is measured, and the 39 fields are rebuilt at 0.25 m.

- [ON-GOING] `MT-56` **`[E]` A beaten mob can turn back into the character the artist drew.** — **built** — see [Saved Characters](../../3-world/saved-characters.md)
  *Done when:* in play a kill turns back about 3 times in 20, each kind behaves as described, gold arrives only from a never-fights character, and two players see the same character, line and aura.

- [ON-GOING] `MT-49` **`[E]` Stop the hover plate throwing on destroyed icons** — its "has been destroyed" errors came from a map change destroying the shared canvas the plate hangs on, while the rebuilt plate kept its list of the old icons.
  *Done when:* a play session with fights and map changes logs none of them.

---

## Related

- [Roadmap to Launch](../roadmap-to-launch.md) — the counted picture of how far the game is from shippable.
- [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md) — forging, folding, quenching, recycling and mastery.
- [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md) — the progression engine; the ring is part of the character, not an item.
- [Weapon Roster — 14 Identities](../../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the weapon families and their skills.
- [Networking — Implementation Notes](../../2-systems/networking/implementation-notes.md) — the deliberate limits that are still true, and what they sit on top of.
- [Starting the Network](../../2-systems/networking/starting-the-network.md) — how to stand the game up to test any of this.
