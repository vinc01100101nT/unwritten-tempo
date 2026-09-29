---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# unwritten — Action Plan — Done

Tasks whose status is `[DONE]`. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

## Week 1 — Prove the ground is solid

**Goal:** nothing changes for a player. Everything after this is built on a game we know works rather than one we assume does.

- [DONE] `MT-1` **`[E]` Finish the play-test open since 3 September.** About half its checklist was never run. Start with its one inconclusive item: whether a passive fires when a normal attack lands, and again when something dies. — **2–3 days**
  *Done when:* every line of that checklist reads passed or failed with none unrun, and the passive question has a watched answer either way.

- [DONE] `MT-38` **`[E]` GMCP — let an AI play the built game as text.** The game's MCP: the client runs with no window and answers one text command per line — the screen as text out, the same orders a click gives in (walk, attack, cast, chat, pick up, equip, spend a point, respawn) — behind three plain MCP tools. A tester or debugger can now stand in the world as a second or third player beside you, which the editor alone never could. Doc: `docs/0-process/gmcp-playing-the-game-as-text.md`. — **1 day**
  *Done when:* three players — a person in the editor and two AI clients with no window — stand in one town and read each other's chat lines. Watched 2026-09-17.

- [DONE] `MT-2` **`[E]` Kill the last descriptions of the deleted equipment window.** The editor-tools catalogue says one tool drops an Inventory/Equipment/Character trio toggled with I, E and C. The window code's own comments still call them a trio and still mention dragging onto a paperdoll. Those stale comments are what misled the first draft of this plan. — **0.5 days**
  *Done when:* no document and no code comment describes an equipment window.

- [DONE] `MT-3` **`[D]` Decide the gear shape.** Armour has five slots — Head, Body, Off-hand, Garment and Shoes — beside the weapon, each in metal, leather and cloth: fifteen designs (decided 2026-09-23, see [Armour Roster](../../2-systems/gameplay-system-v4/armour-roster.md)). — **0.5 days**
  *Done when:* you have written down which places get gear, how many pieces each, and roughly what each does.

  - [DONE] `ST-3-A` **`[D]` Armour gives stats, not skills.** The weapon stays the only item that grants skills.

  - [DONE] `ST-3-B` **`[D]` The two accessory places go.** The ring already fills that role. That leaves head, armour, garment, weapon, shield and the ring.

  - [DONE] `ST-3-C` **`[D]` Any piece can carry any of the six main stats — STR, AGI, VIT, INT, DEX, LUK — in any place.** *Widened by `MT-39`:* a piece also carries SUB-stats now. One Ink writes one thing onto it — twelve sub-stats or, far more rarely, one of the six primaries — so the decision that any stat may sit in any place still stands; what changed is that the ring is no longer the only source of a sub-stat.

  - [DONE] `ST-3-D` **`[D]` Main-stat bonuses are flat, never scaled to level.** +10 STR is +10 at level 1 and at level 99. It stays worth chasing late because the last points a player buys into a stat cost the most, and gear adds on top of the 99 that buying stops at.

  - [DONE] `ST-3-E` **`[D]` A piece's defence or attack comes from its folds and its quality grade, never from a number set on the copy and never scaled to the wearer's level.** Better gear still comes from harder creatures: a piece folds only with the material band of its own grade, and the strongest creatures drop the highest bands. Decided 2026-09-20 with Gameplay System V4 (`MT-39`).

  - [DONE] `ST-3-F` **`[D]` Each place's main job.** Armour — defence, the largest share; head — defence and magic defence, smaller; garment — magic defence; shield — defence plus block chance, which already works in combat. The main job is the one exception to `ST-3-C`'s "no sub-stats". It does not overlap the ring: the ring multiplies the defence a character already has and adds none of its own.

  - [DONE] `ST-3-G` **`[D]` Gear has no level needed to wear it.** Any character can wear any piece.

  - [DONE] `ST-3-H` **`[D]` There is no rarity.** A piece's quality grade — Raw, then F to A, moved one rung by quenching — is what lifts it. No labels, no colours, no drop weights. Decided 2026-09-20 with `MT-39`.

  - [DONE] `ST-3-I` **`[D]` Nothing is rolled when a piece is made.** Gear is forged, not dropped, and its stats are written onto it afterwards with Ink — two copies of one design differ only by what was done to each. Decided 2026-09-20 with `MT-39`.

  - [DONE] `ST-3-J` **`[D]` How the jumps in main stats work.** Each main stat has one headline jump every 10 points, gear included: STR → melee attack, INT → magic attack, VIT → defence, AGI → evasion, DEX → ranged attack, LUK → crit damage. Every jump uses the same shape: (the stat ÷ 10, rounded down) squared, plus 3 for every 10 — so crossing 10 adds 4 and crossing 100 adds 22, on top of what each point already gives. The stat screen shows what the next jump will give.

  - [DONE] `ST-3-K` **`[E]` Remove the two accessory places from the game.** Built: gone from the game code, the equipment screen and the creature table in the editor. No item and no saved character ever used them. Waiting on a play-test.

  - [DONE] `ST-3-L` **`[E]` Make creatures count main stats from the gear they wear.** Built: a creature's attack, hit, max HP, max MP, HP and MP regen, defence, magic defence, evasion and crit shield are worked out from its bought stats plus its gear's, as a player's already were. Waiting on a play-test.

  - [DONE] `ST-3-M` **`[D]` Physical damage splits into melee attack and ranged attack.** Melee attack gets +1 per STR plus STR's jump, and +1 per 2 DEX. Ranged attack gets +1 per DEX plus DEX's jump, and +1 per 2 STR. Level, LUK and gear attack feed both. The weapon held decides which one a normal attack and a physical skill use: a Bow or Firearm uses ranged attack, anything else uses melee attack. Magic damage is untouched — a skill whose damage is magic rolls magic attack whatever is held. The ring's attack node and attack buffs raise whichever of the two is in use, creatures follow the same rule, and the stat screen shows both.

  - [DONE] `ST-3-N` **`[E]` Build ranged attack.** Built: holding a Bow or Firearm, normal attacks and physical skills roll ranged attack, for players and creatures. Waiting on a play-test.
    *Done when:* holding a bow, raising DEX raises the damage of normal attacks and physical skills by DEX's full amount; holding a sword, DEX still adds only 1 attack per 2 points.

  - [DONE] `ST-3-O` **`[E]` Build the jumps for all six main stats.** Built, for players and creatures. Waiting on a play-test.
    *Done when:* crossing 10 in STR, INT, VIT, AGI or DEX moves that stat's headline number by 4 more than a normal point does, for a player and for a creature — and crossing 10 LUK adds 4% crit damage more than a normal point does.

  - [DONE] `ST-3-P` **`[E]` Show melee attack, ranged attack and each main stat's next jump on the stat screen.** Built. Waiting on a play-test.
    *Done when:* the stat screen shows both attack numbers, and next to each main stat what its next jump gives and at which value.

  - [DONE] `ST-3-Q` **`[D]` A staff's and a wand's normal attack rolls melee attack.** It is a physical melee swing, as a staff's is today; magic damage comes only from skills.

  - [DONE] `ST-3-R` **`[D]` LUK's jump counts in crit damage percent.** The jump numbers are percentages: crossing 10 LUK adds 4% crit damage, crossing 100 adds 22%, and by 100 LUK the jumps add up to +130% — on top of the 0.2% each point already gives and the 140% every crit starts at.

  - [DONE] `ST-3-S` **`[E]` A copy keeps its grade, its folds and what was written on it** — through the bag, the body, a save and a restart. Built 2026-09-20 in `MT-39`: the save carries each piece's grade and its written stats. Waiting on a play-test.
    *Done when:* a piece that was forged, folded, quenched and written on shows the same grade, folds and writing after the game restarts.

- [DONE] `MT-4` **`[D]` Gear comes from neither kills nor a shop.** A player forges it at a station from materials, and a kill pays materials and consumables (gold comes from saving a character that never fights, since 2026-09-30). Every item still carries a sell value, so selling works the moment a shop exists; nothing carries a buy price. Decided 2026-09-20 with `MT-39`. — **0.25 days**

---

## Week 2 — Nothing can rot silently

**Goal:** still nothing for a player, but from here on a mistake in the loot data gets caught by a machine instead of by you, six weeks later.

- [DONE] `MT-6` **`[D]` The ring is not an item.** It is part of the character — no ring item, no ring slot, nothing to author; the work that made it so is `ST-39-A`. Decided 2026-09-20 with `MT-39`. — **0 days**

- [DONE] `MT-7` **`[A]` Author the armour designs to forge for each of the five slots** — Head, Body, Off-hand, Garment and Shoes, each in metal, leather and cloth: fifteen designs, the last eleven authored 2026-09-23 (the Wooden Buckler renamed Leather Buckler), see [Armour Roster](../../2-systems/gameplay-system-v4/armour-roster.md). A design is a shape: it has no rarity tier, because what a piece is worth comes from its folds, its grade and its material; its one rolled thing is its word. Waiting on a play-test. — **2–3 days**
  *Done when:* you can forge and wear one piece in each of the five slots and watch the character sheet numbers move for each.

- [DONE] `MT-8` **`[D]` How hard gear is to get.** Creatures give up no equipment and there is no rarity, so there is no drop chance to set: how hard a piece is to get is which creatures drop which material band, which is `MT-11`. Decided 2026-09-20 with `MT-39`. — **0.25 days**

- [DONE] `MT-40` **`[D]` Decide whether the nine ground tiles ship uncompressed.** Settled: they
  ship compressed. The import rule for `1_Authored/Art/GroundTiles/` already said so; the nine textures had simply
  never been re-imported since, and still carried `textureCompression: None`. Re-imported 2026-09-23 — each now
  compressed, with the four odd-sized tiles resampled to the nearest power of two, as the rule says. — **0.25 days**
  *Done when:* `tools/check-asset-rules.py` runs clean.

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something you can make, and a reason to keep working on the one you made.

- [DONE] `MT-9` **`[A]` Three designs for every armour slot — one per material.** Metal, leather and cloth for Head, Body,
  Off-hand, Garment and Shoes; the material decides what a fold buys and every piece rolls one of its slot's words.
  **Not tiers:** a design is a shape. All fifteen authored 2026-09-23 ([Armour Roster](../../2-systems/gameplay-system-v4/armour-roster.md));
  waiting on a play-test. — **3–4 days**
  *Done when:* every armour slot has its three designs, each forges at the station its material belongs to, and its folds and word show on the character sheet.

- [DONE] `MT-10` **`[E]` Check the whole item set at once.** — **0.5 days**
  *Done when:* `tools/check-asset-wiring.py` and `tools/check-unity-code.sh` both run clean, and the editor console is read in full and reported.

- [DONE] `MT-11` **`[D]` Decide which creatures drop which material band.** Decided 2026-09-22: a creature drops the
  band its level falls in — the seven bands split levels 1–99 evenly, and a shadow drops the top band — and its kind
  decides how much and which Inks and Salts. The table is in [Mob Roster](../../3-world/mob-roster.md). — **0.5 days**

- [DONE] `MT-39` **`[E]` Gameplay System V4 — forging, quality, Ink and mastery.** Built 2026-09-20 across six
  slices; **not play-tested**. A play-test session is what would prove
  it. — **built; a play-test session remains**
  *Done when:* it has been played through and every part of it works.

  - [DONE] `ST-39-A` **`[E]` The Ring becomes part of the character.** No ring item, no ring slot; what it earned
    always counts. Nothing wearable is handed out at the start any more.
    *Done when:* the equipment window has no Ring row and the character sheet's numbers are unchanged.

  - [DONE] `ST-39-B` **`[A]` The 29 crafting materials.** Seven bands each of metal, cloth and leather, five Inks
    and three Salts; ten of one Ink or Salt merge into one of the next.
    *Done when:* `/item copper 10` fills the bag's Material tab and a merge turns ten Watery Ink into one Thin.

  - [DONE] `ST-39-C` **`[E]` Quality and folds replace rarity and refine.** Rarity is deleted from the game — no
    labels, no colours, no drop weights. A piece carries a grade (Raw, then F to A) and a fold count, and
    creatures stop dropping equipment altogether.
    *Done when:* no tier word appears anywhere on screen, and a kill gives up materials and consumables only.

  - [DONE] `ST-39-D` **`[E]` The stations.** Forge, fold, quench and write with Ink at a smith, a loom or a tannery.
    Every cost, every roll and every grade is the server's; the window draws what it is told.
    *Done when:* a sword is forged from copper, folded five times, quenched, and written on — each step paid for.

  - [DONE] `ST-39-E` **`[E]` Weapon Mastery.** Earned by the share of a creature you took down with that weapon,
    scaled by the fight's challenge and the weapon's grade, capped by that grade. It raises three stats that fit
    the weapon and unlocks its skills. Creatures have one too, authored as their level.
    *Done when:* a kill with a sword raises Sword Mastery and nothing else, and a skill opens at its level.

  - [DONE] `ST-39-F` **`[E]` Skill Mastery.** Earned per completed press, lifts that skill's own output up to +30%.
    A passive rides the weapon's mastery instead, since nobody presses one.
    *Done when:* pressing one skill repeatedly raises the number on its own card and no other.

---

## Week 4 — Killing things pays you

**Goal:** the phase that turns two months of finished combat into a game. You kill something, it pays materials and consumables (and, when it turns back into a character that never fights, gold), you forge the materials into gear, you put it on, your character sheet changes.

- [DONE] `MT-12` **`[A]` Creatures drop their band.** Built 2026-09-22 as ONE rule rather than 96 hand-written lists:
  a creature's level picks its band and its kind picks the amounts and the Ink and Salt tiers (`MT-11`), so a new
  creature drops the right thing the day it is made. A creature's own drop list still exists for anything special to
  it. — **3–4 days**
  *Done when:* play-test batch 26, step 9 passes — each creature drops only its own band.

- [DONE] `MT-13` **`[E]` Rebuild the creatures so their data reaches the game.** A creature's level, stats, gear and
  mastery are copied into its prefab when it is baked (**Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs**); the
  band it drops is worked out when it dies and needs no bake. Baked 2026-09-22 after the re-level, all 96. — **0.5 days**
  *Done when:* killing the same creature about twenty times lands at least one item in your bag.

---

## Week 5 — The world opens

**Goal:** you can walk from the first town to any map in the game. The three-region world is laid out and generated
(2026-09-26): 75 maps plus Lastline — 8 cities, 6 towns, 39 fields and 22 dungeon floors — joined by doors, with every
place named ([World Map](../../3-world/world-map.md)). None of it has been walked in play.

- [DONE] `MT-16` **`[D]` Decide the shape of the world map.** Decided 2026-09-26: three regions (Midlaine, Lirenne, The
  Far Shores), a city per one-family weapon, a border town per two-family weapon, fields named after the town you walk
  to them from, and dungeons whose entrance is in one of a town's fields — the [World Map](../../3-world/world-map.md).
  — **0.5 days**

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [DONE] `MT-19` **`[A]` Make an elite and a shadow worth the walk.** Built 2026-09-22 as drops: an elite drops 5 to 10
  of each of its band's materials and the rarer Inks and Salts, and a shadow the same at higher chances. — **1–2 days**
  *Done when:* killing an elite reliably produces something an ordinary creature never gives.

---

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [DONE] `MT-47` **`[D]` Decide whether the ten smallest creatures get bigger collision shapes.** Decided 2026-09-27 by the
  user: yes, as one general rule, not per creature. Built the same day: any body whose collision shape is too short
  for the full 0.075 m step is grown just tall enough (bottom and width unchanged); all 98 creatures now step 0.075 m.
  — **0.5 days**

- [DONE] `MT-52` **`[E]` A creature's name lives in one place.** Built 2026-09-29: a creature's shown name is one field on
  its definition; its art is linked to its folder by Unity's own link and its model is looked up by its permanent id,
  so renaming a creature, its folder or its file breaks nothing. The 84 creatures were renamed the same day to names
  with no numbers, in the Ragnarok style (Plopling, Gloop King…). — **0.5 days**
  *Done when:* every creature's art, model and spawns resolve under the new names (all 98 checked 2026-09-29).

- [DONE] `MT-53` **`[E]` A shadow carries no flat picture.** Built 2026-09-29: a shadow is drawn only as a 3D character,
  so its baked model no longer holds a flat animation or the placeholder art it never showed. — **0.25 days**
  *Done when:* the 14 shadow models hold no flat animation (checked 2026-09-29).

- [DONE] `MT-54` **`[A]` The character sprites are one standard.** Done 2026-09-30: every character folder holds one
  64×112 sheet and one 38×38 portrait; the duplicate pose folders, loose files and a duplicate villager are gone, and no
  folder name carries a number. Child and OldWoman keep their 2-row sheets. — **0.25 days**
  *Done when:* all 89 standard folders match (checked 2026-09-30).

- [DONE] `MT-55` **`[D]` Decide the story.** Decided 2026-09-30 with the user: an artist's unfinished book; Chaos, his
  unwanted thoughts, corrupts pages and never erases; the mobs are corrupted pages; the Fourteen Kashira sealed Chaos
  and his paranoia turned them into the fourteen shadows; the player ends the paranoia
  ([Story & Setting](../../3-world/story-setting-canon.md)). — **0.25 days**

---
