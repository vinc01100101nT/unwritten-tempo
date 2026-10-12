---
status:
owner: vince
last-updated: 2026-10-06
tags:
  - planning-production
---

# unwritten — Action Plan — Done

Tasks whose status is `[DONE]`. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

## Week 1 — Prove the ground is solid

**Goal:** nothing changes for a player — everything after this is built on a game we know works rather than one we assume does.

- [DONE] `MT-1` **`[E]` Finish the play-test open since 3 September** — its open passive question: does a passive fire when a normal attack lands, and when something dies. — **2–3 days**
  *Done when:* every line of that checklist reads passed or failed with none unrun, and the passive question has a watched answer either way.

- [DONE] `MT-38` **`[E]` GMCP — let an AI play the built game as text.** — **1 day** — see [GMCP — Playing the Game as Text](../../0-process/gmcp-playing-the-game-as-text.md)
  *Done when:* three players — a person in the editor and two AI clients with no window — stand in one town and read each other's chat lines. Watched 2026-09-17.

- [DONE] `MT-2` **`[E]` Kill the last descriptions of the deleted equipment window.** — **0.5 days** — see [HUD/UI System](../../2-systems/hud-ui-system.md)
  *Done when:* no document and no code comment describes an equipment window.

- [DONE] `MT-3` **`[D]` Decide the gear shape.** — **0.5 days** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)
  *Done when:* you have written down which places get gear, how many pieces each, and roughly what each does.

  - [DONE] `ST-3-A` **`[D]` Armour gives stats, not skills.** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)

  - [DONE] `ST-3-B` **`[D]` The two accessory places go.** — see [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md)

  - [DONE] `ST-3-C` **`[D]` Any piece can carry any of the six main stats — STR, AGI, VIT, INT, DEX, LUK — in any place.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-D` **`[D]` Main-stat bonuses are flat, never scaled to level.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-E` **`[D]` A piece's defence or attack comes from its folds and its quality grade, never from a number set on the copy and never scaled to the wearer's level.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-F` **`[D]` Each place's main job.** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)

  - [DONE] `ST-3-G` **`[D]` Gear has no level needed to wear it.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-H` **`[D]` Rarity is how rare a piece is to forge, never its grade** — Common, Uncommon, Rare, Epic, Legendary; a weapon takes its trademark's rarity (ultimates always Legendary), materials by kind and step, everything else Common (decided 2026-10-11). — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-I` **`[D]` Nothing is rolled when a piece is made.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

  - [DONE] `ST-3-J` **`[D]` How the jumps in main stats work.** — see [Pre-Renewal RO Port](../../2-systems/pre-renewal-ro-port.md) and [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)

  - [DONE] `ST-3-K` **`[E]` Remove the two accessory places from the game.** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)

  - [DONE] `ST-3-L` **`[E]` Make creatures count main stats from the gear they wear.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)

  - [DONE] `ST-3-M` **`[D]` Physical damage splits into melee attack and ranged attack.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)

  - [DONE] `ST-3-N` **`[E]` Build ranged attack.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)
    *Done when:* holding a bow, raising DEX raises the damage of normal attacks and physical skills by DEX's full amount; holding a sword, DEX still adds only 1 attack per 2 points.

  - [DONE] `ST-3-O` **`[E]` Build the jumps for all six main stats.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)
    *Done when:* crossing 10 in STR, INT, VIT, AGI or DEX moves that stat's headline number by 4 more than a normal point does, for a player and for a creature — and crossing 10 LUK adds 4% crit damage more than a normal point does.

  - [DONE] `ST-3-P` **`[E]` Show melee attack and ranged attack on the stat screen.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)
    *Done when:* the stat screen shows both attack numbers.

  - [DONE] `ST-3-Q` **`[D]` A staff's and a wand's normal attack rolls melee attack.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)

  - [DONE] `ST-3-R` **`[D]` LUK's jump counts in crit damage percent.** — see [Unified Unit Stats](../../2-systems/active-skill-system-v3/unified-unit-stats.md)

  - [DONE] `ST-3-S` **`[E]` A copy keeps its grade, its folds and what was written on it.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)
    *Done when:* a piece that was forged, folded, quenched and written on shows the same grade, folds and writing after the game restarts.

- [DONE] `MT-4` **`[D]` Gear comes from neither kills nor a shop.** — **0.25 days** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)

---

## Week 2 — Nothing can rot silently

**Goal:** still nothing for a player, but from here on a mistake in the loot data gets caught by a machine instead of by you, six weeks later.

- [DONE] `MT-6` **`[D]` The ring is not an item.** — **0 days** — see [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md)

- [DONE] `MT-7` **`[A]` Author the armour designs to forge for each of the four slots.** — **2–3 days** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)
  *Done when:* you can forge and wear one piece in each of the five slots and watch the character sheet numbers move for each.

- [DONE] `MT-8` **`[D]` How hard gear is to get.** — **0.25 days** — see [Mob Roster](../../3-world/mob-roster.md)

- [DONE] `MT-40` **`[D]` Decide whether the nine ground tiles ship uncompressed.** — **0.25 days** — see [Asset Guide](../../../unity/Assets/ASSETS.md)
  *Done when:* `tools/check-asset-rules.py` runs clean.

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something you can make, and a reason to keep working on the one you made.

- [DONE] `MT-9` **`[A]` Three designs for every armour slot — one per material.** — **3–4 days** — see [Armour Roster, Materials & Words](../../2-systems/gameplay-system-v4/armour-roster.md)
  *Done when:* every armour slot has its three designs, each forges at the station its material belongs to, and its folds and word show on the character sheet.

- [DONE] `MT-10` **`[E]` Check the whole item set at once.** — **0.5 days**
  *Done when:* `tools/check-asset-wiring.py` and `tools/check-unity-code.sh` both run clean, and the editor console is read in full and reported.

- [DONE] `MT-11` **`[D]` Decide which creatures drop which material band.** — **0.5 days** — see [Mob Roster](../../3-world/mob-roster.md)

- [DONE] `MT-39` **`[E]` Gameplay System V4 — forging, quality, Ink and mastery.** — **built; a play-test session remains** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)
  *Done when:* it has been played through and every part of it works.

  - [DONE] `ST-39-A` **`[E]` The Ring becomes part of the character.** — see [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md)
    *Done when:* the equipment window has no Ring row and the character sheet's numbers are unchanged.

  - [DONE] `ST-39-B` **`[A]` The 29 crafting materials.** — see [Crafting Tables](../../2-systems/gameplay-system-v4/crafting-tables.md)
    *Done when:* `/item copper 10` fills the bag's Material tab and a merge turns ten Watery Ink into one Thin.

  - [DONE] `ST-39-C` **`[E]` Quality and folds replace rarity and refine.** — see [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md)
    *Done when:* no tier word appears anywhere on screen, and a kill gives up materials and consumables only.

  - [DONE] `ST-39-D` **`[E]` The stations.** — see [Crafting Tables](../../2-systems/gameplay-system-v4/crafting-tables.md)
    *Done when:* a sword is forged from copper, folded five times, quenched, and written on — each step paid for.

---

## Week 4 — Killing things pays you

**Goal:** you kill something, it pays materials and consumables, you forge the materials into gear, you put it on, and your character sheet changes.

- [DONE] `MT-12` **`[A]` Creatures drop their band.** — **3–4 days** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* play-test batch 26, step 9 passes — each creature drops only its own band.

- [DONE] `MT-13` **`[E]` Rebuild the creatures so their data reaches the game.** — **0.5 days** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* killing the same creature about twenty times lands at least one item in your bag.

---

## Week 5 — The world opens

**Goal:** you can walk from the first town to any map in the game.

- [DONE] `MT-16` **`[D]` Decide the shape of the world map.** — **0.5 days** — see [The World Map — Three Regions](../../3-world/world-map.md)

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [DONE] `MT-19` **`[A]` Make an elite and a shadow worth the walk.** — **1–2 days** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* killing an elite reliably produces something an ordinary creature never gives.

---

## Week 7 — The world has people

**Goal:** you can talk to somebody with a name who says the same thing twice.

- [DONE] `MT-22` **`[D]` Decide how big a quest can be.** — **0.5 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)

---

## Week 9 — A quest can be finished

**Goal:** the full loop — a named person sends you somewhere, you go, you come back, you are paid.

- [DONE] `MT-27` **`[D]` Decide what a quest pays.** — **0.5 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)

---

## Week 11 — A stranger can start it

**Goal:** someone who has never seen the game can open it and know what to do.

- [DONE] `MT-31` **`[D]` Decide how much the game explains itself.** — **0.5 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)

---

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [DONE] `MT-47` **`[D]` Decide whether the ten smallest creatures get bigger collision shapes.** — **0.5 days** — see [Walking and Routing](../../2-systems/walking-and-routing.md)

- [DONE] `MT-52` **`[E]` A creature's name lives in one place.** — **0.5 days** — see [Creatures and Where They Live](../../3-world/biome-mob-families.md)
  *Done when:* every creature's art, model and spawns resolve under the new names (all 98 checked 2026-09-29).

- [DONE] `MT-53` **`[E]` A shadow carries no flat picture.** — **0.25 days** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* the 14 shadow models hold no flat animation (checked 2026-09-29).

- [DONE] `MT-54` **`[A]` The character sprites are one standard** — one 64×112 sheet and one 38×38 portrait per character folder. — **0.25 days** — see [Saved Characters](../../3-world/saved-characters.md)
  *Done when:* all 89 standard folders match (checked 2026-09-30).

- [DONE] `MT-55` **`[D]` Decide the story.** — **0.25 days** — see [Story & Setting (Canon)](../../3-world/story-setting-canon.md)

---
