---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# unwritten — Action Plan — On-going

Tasks whose status is `[ON-GOING]` — built, or under way, and not yet proven. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

Twelve weeks of work, as tasks. **This is the "what next" list.** [Roadmap to Launch](../roadmap-to-launch.md) is the "how far are we" picture — what is built, what is half-built, what is missing, all counted. Neither repeats the other.

**Every fact in this plan was measured on 2026-09-12 or 2026-09-13** by reading the game's own files, its database or its builds.

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

**My assumptions, labelled as mine:** a week is five working days, you and me together. The twelve weeks are ordered by dependency, not by calendar. Slipping one pushes the rest; it does not break them.

**Where the weeks are tight, said plainly.** Every week fits inside five days at the low end of its estimates. Three do not fit at the high end: **Week 2** (up to 5.25 days), **Week 4** (up to 5.5) and **Week 10** (up to 6). Those are the weeks where a decision going the long way costs you a day, and they are the three most likely to spill into the next week. Nothing is padded to hide it.

---

---

## Week 1 — Prove the ground is solid

**Goal:** nothing changes for a player. Everything after this is built on a game we know works rather than one we assume does.

  - [ON-GOING] `ST-3-K` **`[E]` Remove the two accessory places from the game.** Built: gone from the game code, the equipment screen and the creature table in the editor. No item and no saved character ever used them. Waiting on a play-test.

  - [ON-GOING] `ST-3-L` **`[E]` Make creatures count main stats from the gear they wear.** Built: a creature's attack, hit, max HP, max MP, HP and MP regen, defence, magic defence, evasion and crit shield are worked out from its bought stats plus its gear's, as a player's already were. Waiting on a play-test.

  - [ON-GOING] `ST-3-N` **`[E]` Build ranged attack.** Built: holding a Bow or Firearm, normal attacks and physical skills roll ranged attack, for players and creatures. Waiting on a play-test.
    *Done when:* holding a bow, raising DEX raises the damage of normal attacks and physical skills by DEX's full amount; holding a sword, DEX still adds only 1 attack per 2 points.

  - [ON-GOING] `ST-3-O` **`[E]` Build the jumps for all six main stats.** Built, for players and creatures. Waiting on a play-test.
    *Done when:* crossing 10 in STR, INT, VIT, AGI or DEX moves that stat's headline number by 4 more than a normal point does, for a player and for a creature — and crossing 10 LUK adds 4% crit damage more than a normal point does.

  - [ON-GOING] `ST-3-P` **`[E]` Show melee attack, ranged attack and each main stat's next jump on the stat screen.** Built. Waiting on a play-test.
    *Done when:* the stat screen shows both attack numbers, and next to each main stat what its next jump gives and at which value.

  - [ON-GOING] `ST-3-S` **`[E]` A copy keeps its grade, its folds and what was written on it** — through the bag, the body, a save and a restart. Built 2026-09-20 in `MT-39`: the save carries each piece's grade and its written stats. Waiting on a play-test.
    *Done when:* a piece that was forged, folded, quenched and written on shows the same grade, folds and writing after the game restarts.

---

## Week 2 — Nothing can rot silently

**Goal:** still nothing for a player, but from here on a mistake in the loot data gets caught by a machine instead of by you, six weeks later.

- [ON-GOING] `MT-7` **`[A]` Author the first design to forge for each of the four places** — Leather Cap (head), Padded Vest (armour), Traveller's Cloak (garment) and Wooden Buckler (shield), authored 2026-09-15 with **Create ▸ unwritten ▸ Item** and **Tools ▸ unwritten ▸ Catalogs ▸ Items ▸ Armor**. A design is a shape: it has no rolled range and no rarity tier, because what a piece is worth comes from its folds and its grade. `MT-9` adds two more per place. Waiting on a play-test. — **2–3 days**
  *Done when:* you can forge and wear one piece in each of the four places and watch the character sheet numbers move for each.

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something you can make, and a reason to keep working on the one you made.

**[Gameplay System V4](../../2-systems/gameplay-system-v4/README.md) rewrote this week on 2026-09-20.** Gear is no
longer found and no longer rolled: you forge it, fold it and quench it, and what a piece is worth comes from its
folds and its quality grade rather than from which design it is. `MT-39` is that work.

- [ON-GOING] `MT-39` **`[E]` Gameplay System V4 — forging, quality, Ink and mastery.** Built 2026-09-20 across six
  slices; **not play-tested**. A play-test session is what would prove
  it. — **built; a play-test session remains**
  *Done when:* it has been played through and every part of it works.

  - [ON-GOING] `ST-39-A` **`[E]` The Ring becomes part of the character.** No ring item, no ring slot; what it earned
    always counts. Nothing wearable is handed out at the start any more.
    *Done when:* the equipment window has no Ring row and the character sheet's numbers are unchanged.

  - [ON-GOING] `ST-39-B` **`[A]` The 29 crafting materials.** Seven bands each of metal, cloth and leather, five Inks
    and three Salts; ten of one Ink or Salt merge into one of the next.
    *Done when:* `/item copper 10` fills the bag's Material tab and a merge turns ten Watery Ink into one Thin.

  - [ON-GOING] `ST-39-C` **`[E]` Quality and folds replace rarity and refine.** Rarity is deleted from the game — no
    labels, no colours, no drop weights. A piece carries a grade (Raw, then F to A) and a fold count, and
    creatures stop dropping equipment altogether.
    *Done when:* no tier word appears anywhere on screen, and a kill gives up materials, consumables and gold only.

  - [ON-GOING] `ST-39-D` **`[E]` The stations.** Forge, fold, quench and write with Ink at a smith, a loom or a tannery.
    Every cost, every roll and every grade is the server's; the window draws what it is told.
    *Done when:* a sword is forged from copper, folded five times, quenched, and written on — each step paid for.

  - [ON-GOING] `ST-39-E` **`[E]` Weapon Mastery.** Earned by the share of a creature you took down with that weapon,
    scaled by the fight's challenge and the weapon's grade, capped by that grade. It raises three stats that fit
    the weapon and unlocks its skills. Creatures have one too, authored as their level.
    *Done when:* a kill with a sword raises Sword Mastery and nothing else, and a skill opens at its level.

  - [ON-GOING] `ST-39-F` **`[E]` Skill Mastery.** Earned per completed press, lifts that skill's own output up to +30%.
    A passive rides the weapon's mastery instead, since nobody presses one.
    *Done when:* pressing one skill repeatedly raises the number on its own card and no other.

---

## Week 4 — Killing things pays you

**Goal:** the phase that turns two months of finished combat into a game. You kill something, gear falls out, you put it on, your character sheet changes.

- [ON-GOING] `MT-12` **`[A]` Creatures drop their band.** Built 2026-09-22 as ONE rule rather than 96 hand-written lists:
  a creature's level picks its band and its kind picks the amounts and the Ink and Salt tiers (`MT-11`), so a new
  creature drops the right thing the day it is made. A creature's own drop list still exists for anything special to
  it. — **3–4 days**
  *Done when:* play-test batch 26, step 9 passes — each creature drops only its own band.

- [ON-GOING] `MT-13` **`[E]` Rebuild the creatures so their data reaches the game.** A creature's level, stats, gear and
  mastery are copied into its prefab when it is baked (**Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs**); the
  band it drops is worked out when it dies and needs no bake. Baked 2026-09-22 after the re-level, all 96. — **0.5 days**
  *Done when:* killing the same creature about twenty times lands at least one item in your bag.

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [ON-GOING] `MT-19` **`[A]` Make an elite and a shadow worth the walk.** Built 2026-09-22 as drops: an elite drops 5 to 10
  of each of its band's materials and the rarer Inks and Salts, a shadow the same at higher chances, and elites pay
  250 to 2000 gold against an ordinary creature's 2 to 24. — **1–2 days**
  *Done when:* killing an elite reliably produces something an ordinary creature never gives.

---

## Related

- [Roadmap to Launch](../roadmap-to-launch.md) — the counted picture of how far the game is from shippable.
- [Crafting & Recycling](../../2-systems/crafting-and-recycling.md) — designed, not scheduled.
- [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md) — the progression engine the ring item belongs to.
- [Weapon Roster — 12 Identities](../../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — ten families with no skill at all, and the staff's single unpublished one.
- [Networking — Implementation Notes](../../2-systems/networking/implementation-notes.md) — the six trust gaps, and what they sit on top of.
- [Starting the Network](../../2-systems/networking/starting-the-network.md) — how to stand the game up to test any of this.
