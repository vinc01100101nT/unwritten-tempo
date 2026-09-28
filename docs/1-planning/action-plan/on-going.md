---
status:
owner: vince
last-updated: 2026-09-28
tags:
  - planning-production
---

# unwritten — Action Plan — On-going

Tasks whose status is `[ON-GOING]` — built, or under way, and not yet proven. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

Eleven weeks of work and the play-test between Week 6 and Week 7, as tasks. **This is the "what next" list.** [Roadmap to Launch](../roadmap-to-launch.md) is the "how far are we" picture — what is built, what is half-built, what is missing, all counted. Neither repeats the other.

**Every fact in this plan was measured between 2026-09-12 and 2026-09-27** by reading the game's own files, its database or its builds.

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
- **Done when** — the concrete check. If you cannot watch it happen or count it, it is not a check.

**Decisions are tasks, never gates.** Each one sits in the week *before* the work that needs it, with a recommendation attached so you can settle it by agreeing. If a decision slips, the work after it still starts — on the recommendation — and the estimate already covers changing course.

**My assumptions, labelled as mine:** a week is five working days, you and me together. The weeks are ordered by dependency, not by calendar. Slipping one pushes the rest; it does not break them.

**Where the weeks are tight, said plainly.** Every week fits inside five days at the low end of its estimates. Two do not fit at the high end: **Week 4** (up to 5.5 days) and **Week 10** (up to 6). Those are the weeks where a decision going the long way costs you a day, and they are the two most likely to spill into the next week. The play-test is not a week: its tasks add up to 7–10 days, two weeks of work. Nothing is padded to hide it.

---

## Week 5 — The world opens

**Goal:** you can walk from the first town to any map in the game. The three-region world is laid out and generated
(2026-09-26): 75 maps plus Lastline — 8 cities, 6 towns, 39 fields and 22 dungeon floors — joined by doors, with every
place named ([World Map](../../3-world/world-map.md)). None of it has been walked in play.

- [ON-GOING] `MT-15` **`[A]` Connect every map of the new world by doors.** Every city, town, field and dungeon floor has its
  doors from the world layout; the fourteen shadow places are not built. — **4–5 days**
  *Done when:* starting from Lastline you can reach every one of the 75 maps on foot, without a game-master command.
- [ON-GOING] `MT-44` **`[E]` Give the fields shape: ground levels, cliffs, meeting points, one walking rule.** Built
  2026-09-27: a biome's ground settles into flat levels joined by slopes, each biome with its own shape (Grassland: 0, 0.6 and 1.2 m, slopes to 20°);
  a map recipe can line its roads with carved rock cliffs 1.5–2.7 m tall, with meeting points where a cliff slopes down
  to meet the pass; the player, every creature and every route walk by one rule (45° slopes, 0.075 m steps); and a map
  knows at load which of its areas connect, so an unreachable target costs no search
  ([Biomes](../../3-world/biomes.md), [Walking and Routing](../../2-systems/walking-and-routing.md)). The test maps
  `test_grassland` and `test_grassland_cliffs` and the 39 world fields (rebuilt 2026-09-27) use it. — **built**
  *Done when:* on both test maps you walk every level and climb a cliff at a meeting point, and a creature chases you
  up and down both.

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [ON-GOING] `MT-17` **`[E]` Give every elite its row.** One row in the world spawn list per elite, naming it, at the spot
  the world shape gives it (`/where` prints the numbers). A map keeps its row for ordinary creatures beside it. — **1 day**
  *Done when:* every elite has a row, and each one's map still has its ordinary creatures.

---

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [ON-GOING] `MT-46` **`[E]` Let the World Creator put cliffs on a map.** Built 2026-09-27: each biome has a cliffs switch in
  the Biome Table, a map can set cliffs on or off for itself from its menu in the browser World Creator, and a build
  gives the field template's cliffs (1.5 m, 8 m pass, 3 meeting points) to every map that has them. — **built**
  *Done when:* a field set to "Cliffs: on" in the World Creator comes out of Build World with cliffs, and one set to
  off comes out without.

- [ON-GOING] `MT-48` **`[E]` Rain leaves standing water that works like water.** Built 2026-09-27/28: 1 cm puddle sheets
  on level ground; lakes that fill every hollow at once from the rain its slopes collect, spill downhill into the next
  and flood the lowest ground; the same water for every player from the shared world clock; an exact reflection per
  water height; things fade under water by depth, their outlines with them; shaped ground drawn at 0.25 m so level
  edges curve ([Rain and Standing Water](../../2-systems/rain-and-standing-water.md)). Only `test_grassland` has the
  0.25 m ground. — **built**
  *Done when:* in a storm on `test_grassland` the lakes rise every 30 seconds, a hollow below an overflowing one fills,
  two players see the same water, the frame time in a storm is measured, and the 39 fields are rebuilt at 0.25 m.

---

## Related

- [Roadmap to Launch](../roadmap-to-launch.md) — the counted picture of how far the game is from shippable.
- [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md) — forging, folding, quenching, recycling and mastery; built.
- [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md) — the progression engine; the ring is part of the character, not an item.
- [Weapon Roster — 14 Identities](../../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — ten families with no skill at all, and the staff's single unpublished one.
- [Networking — Implementation Notes](../../2-systems/networking/implementation-notes.md) — the deliberate limits that are still true, and what they sit on top of.
- [Starting the Network](../../2-systems/networking/starting-the-network.md) — how to stand the game up to test any of this.
