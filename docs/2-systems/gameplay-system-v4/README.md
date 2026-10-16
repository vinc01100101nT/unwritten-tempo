---
status: "BUILT 2026-09-20 (slices 1-6), weapon trademarks and the main-number rule BUILT 2026-09-21, creature drops by level BUILT 2026-09-22, element levels, converters, timed effects surviving logout and the armour roster BUILT 2026-09-23, the world's fourteen biomes in the map generator and the weather a biome always has BUILT 2026-09-25, skills bought and levelled with weapon mastery points BUILT 2026-10-03, gear dropped by creatures, Temper Dust and Quench Oil BUILT 2026-10-13, every piece's base, its roll, fold luck, the station fees and the new Salt BUILT 2026-10-14; not yet play-tested. The numbers in §9 are live in the code."
owner: vince
last-updated: 2026-10-14
tags:
  - systems-combat-design
---

# Gameplay System — V4 (Folding, Mastery, Trademarks & Ink)

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else. When a decision lands, rewrite the passage it
> changes and delete it from Still open.

**What V4 is.** V3 made the weapon the source of a character's skills: a dropped weapon rolled a random kit, and
its rarity lifted how hard that kit hit. V4 moves progression into the character's own hands. **Gear drops Raw and
unfolded, and is made worth more at a station. Skills are bought with weapon mastery points, not rolled. Power over
a skill comes from the level bought in it, and from the trademark of the weapon in hand.**

**How it relates to V3.** This is now what the code does. [V3](../active-skill-system-v3/README.md) still
describes the channels, effects and triggers, which V4 keeps; where the two disagree — rolled
weapon kits, V3's rarity (a rolled tier that lifted a weapon's skill output), the worn ring item,
"skills don't level" — V4 is what runs and V3's passages on those points are dead. V4's own rarity (§4) is a
different thing: a dropped piece rolls it by the kind of creature that dropped it, and it multiplies the piece's
base. **Not yet play-tested.**

## 1. Gear is dropped Raw, and worked at a station

- **Gear is dropped, never made.** Every kill, of every kind of creature, has a **10%** chance to drop **one**
  random piece of what the creature wears. The drop is a new copy of that item — nothing leaves the creature — and
  it is always **Raw, +0**, with its own trademark (a weapon) or word (an armour piece) rolled, and its rarity
  rolled by the creature's kind (§4). There is no way to make a piece from materials.
- **A new character starts wearing a Sword, a Chainmail and Iron Sabatons** — Raw, +0, Common, unrolled (§2), with
  no trademark and no word.
- **Players make their gear worth more with materials.** A piece is worked where its material is worked: **metal
  at a smith, cloth at a loom, leather at a tannery**. The actions below are the same at all three.
- **The work happens at a station in town.** Never from the bag.
- **There is no level requirement to wear gear.** Anyone can equip anything.
- **Weapons and armour scale the same way**: what a piece is worth is its **base**, times its **roll** and its
  **rarity**, times what its **folds** and **quenches** add (§2) — attack on a weapon; on armour, what its
  **material** buys (metal defence and HP, leather attack speed and evasion, cloth magic defence and MP). There is
  no Level on gear. Armour has four slots — Head, Body,
  Garment, Shoes — each in all three materials, and every armour piece rolls one **word** from its slot
  ([Armour Roster, Materials & Words](armour-roster.md)).

## 2. Fold, Quench, Recycle

**What the player gets.** A higher grade is always stronger: **A +0 is more than eight times F +10**, and a grade at
+0 is always worth more than the grade below it at +10. A quench is the big moment — a successful one doubles what
the last grade's folds gave — and a fold is the small one. Every piece is worth wearing from the moment it drops,
because it has a base under its folds, and two dropped pieces are rarely the same, because each rolls its base.
**Raw +0 to A +10 takes about two months of eight hours a day.**

**How a piece arrives.** A dropped piece is **Raw: +0, no quality**. Four things are rolled as it comes into the
world and never again: its **base roll** (below), its **rarity** (§4), and a weapon's **trademark** (§6) or an armour
piece's **word**.

**What a piece is called.** A weapon with a trademark goes by its trademark skill's own name: its folds, the name,
then its grade — **+7 Thousand Petals [C]**. Any other piece: its grade, its folds, then the item — **B +6 Sword**. That one title
is used everywhere a piece is named: the bag list (which sorts and searches by it), its card, the station window
and the chat replies.

### What a piece is worth

**Worth = base × roll × rarity × (1 + every fold and quench percentage, added together).** The percentages add;
they never multiply each other. A Raw +0 piece is worth exactly its base × roll × rarity.

- **A weapon's base is 50 × its swing time in seconds**, divided by 1.5 for a pair weapon (one item in both hands,
  whose second hand lands at half power), so every weapon deals the same damage a second at equal gear. The swing
  time comes from the weapon's weight: 0.8 s plus 0.005 s per point a hand carries. Staff, Wand
  and Scythe give Magic Attack; every other weapon gives Attack.
- **An armour piece's base:** a body or a garment is worth one sixth of a 1-second weapon's base (50, a Dagger's) on the armour exchange rate
  (1 Defence = 8 HP = 4 MP = 0.5% Attack Speed, Evasion at double Defence), split between its material's two stats;
  head and shoes half of that. At equal gear a full metal set's Defence is **25% of a Dagger's Attack** (20% of a Sword's). No piece
  gives movement speed.
- **The roll.** Only a piece a creature DROPS rolls its base, at **85% to 115%** in whole percents, evenly — on the
  server, when it drops, saved on the copy and never changed. Every other piece — a new character's, a shop's, a
  reward, a GM grant, and what a creature wears — is unrolled and counts as 100%. A piece that was never rolled can
  be told apart from one that rolled 100%.
- **Rarity multiplies the base** (§4): Common ×1.00 to Legendary ×1.50.
- **Folds and quenches add percentages of the base**, by grade (§9): each fold adds 10% at Raw up to 70% at A, and
  the quench that reached a grade adds 100% at F up to 600% at A.
- **The tooltip shows the base:** **Base 54 (108%)** — base × roll, before rarity; an armour piece shows its first
  stat. An unrolled piece shows **Base 50**, with no percent. A piece shown with no copy behind it — a shop's row of
  gear for sale — shows no Base line.

### Fold (raise it)

Each fold costs materials and Unsigned and raises the piece by one: **a fold IS the existing refine (+N)**. A sword
folded seven times is a +7 sword.

- **Every grade's fold cap is +10**, and the cap is the quench gate: a piece can be quenched only at +10, and cannot
  be folded past it.
- **+0 to +4 always succeed.** From +4 a fold can fail, at a chance set by the grade and the folds already on the
  piece (§9). **A failed fold takes off one fold and never goes below +4.** Every try is paid for, whether it
  succeeds or not.
- **Every fold is paid in Temper Dust, and how much is decided by the piece's GRADE, never by its fold count.**
  Temper Dust is one material shared by all three crafts. A fold costs the grade's step in units — **1 at Raw, 2 at
  F, 3 at E, 4 at D, 5 at C, 6 at B, 7 at A** — plus a fee in Unsigned (§9). After a quench resets a piece to +0, it
  folds at the NEW grade's price. Every kill drops Temper Dust (§4), more from stronger creatures.
- **A tutorial's free fold** is checked before the roll and used up only on a success, so a failed free fold stays
  free. It is free only while the piece is below the tutorial step's own fold target
  ([Quests & Tutorials](../quests-and-tutorials.md)).
- **The station window shows the try's odds:** "Chance: X% · If it fails: back to +N", once a fold can fail.

### Quench (raise its grade)

**Quality** — the one label on every crafted piece, weapon or armour — is a grade **F (poorest) to A (highest)**,
climbed **one rung at a time**.

- **A quench either moves the piece up one grade, to +0, or fails.** There is no "unchanged" outcome, never a jump,
  never a roll to "any" grade. A is the top and is never quenched.
- **A quench costs Quench Oil — the grade's step in units, 1 at Raw to 6 at B — plus a fee in Unsigned** (§9), and
  optionally **one Salt**. Quench Oil is one material shared by all three crafts. Every try is paid for.
- **What a failure does is the Salt's answer.** One Salt may go in, and it is spent whatever happens:
  - **No Salt:** the piece drops one grade, to +0 (a Raw piece stays Raw, at +0), and loses the last stat on its Ink
    list (§3).
  - **Coarse Salt:** it keeps its grade and goes to +4.
  - **Fine Salt:** it keeps its grade and goes to +4, and the chance to go up is × 1.25.
  - **Pure Salt:** it keeps its grade and goes to +9, and the chance to go up is × 1.5.
- **The chance to go up never passes 95%.**
- **Salt is one item shared by all three crafts**: **Coarse Salt → Fine Salt → Pure Salt**, each its own item, the
  better dropping less often. Nobody is told which Salt to use; each is simply the cheapest at some grades (§9).
- **The station window says what a failure does:** "Chance to rise: X% · If it fails: drops to E +0", or "… stays
  F, back to +4".

**What quality does:** it sets how much each fold adds and what the quench that reached it added, the size of an
armour piece's word, it caps and speeds weapon mastery (§5), and it sets **how many stat slots** the piece has (§3).
A Raw piece caps mastery **below F**.

**No Potential.** The folds are the budget, and they refill through work.

**Recycle (take it apart).** At the station of its craft, any piece can be recycled: it is destroyed, and
**half** of what it provably consumed comes back — for each rung it climbed, that grade's ten folds and one
quench (priced at the grade it was made from, which the gate above makes provable); the folds standing on it now
(at its own grade's price); and the Inks whose stats are written on it. That is Temper Dust, Quench Oil, Unsigned and
Ink. **Folds made at Raw return nothing** — no Temper Dust and no Unsigned, standing or climbed — because the first
tutorial gives its Raw folds free. Nothing records a failed try, fold or quench, so those come back as nothing;
Salt is never returned. Each material is halved and rounded down on its own, so a single unit never comes
back whole. A piece the first tutorial still needs cannot be recycled, sold or dropped
([Quests & Tutorials](../quests-and-tutorials.md)).

## 3. Stats are written on with Ink

- **One material, Ink, in five tiers**, each its own item, dropping less often the better it is:
  **Watery → Thin → Faint → Clear → Sharp** Ink. The tier sets how big the number is.
- **Using Ink on a piece rolls ONE stat** from a pool of eighteen: **twelve sub-stats** and the **six primaries**
  (STR, AGI, VIT, INT, DEX, LUK). **Primaries roll far less often and are worth far more** — the jackpot.
- **One Ink writes one stat, never two.** Two of the ring's ten nodes each move a pair — its Defense node moves
  defence AND magic defence, its Critical node moves crit chance AND crit damage — so those four are separate
  here: defence, magic defence, crit chance and crit damage. With HP, Attack, Accuracy, Cast Speed, Magic Attack,
  MP, Evasion and Attack Speed, that is the twelve.
- **The primary premium.** A primary rolled by a tier must be worth clearly more, once converted, than that
  same tier's roll of the sub-stat it feeds (a premium factor to be tuned, e.g. 1.5×). Measured against, with
  the game's real conversions today: STR → Attack (1 STR = +1 Attack), AGI → Attack Speed (+0.5% each),
  VIT → HP (+2% each), INT → Magic Attack (+1 each), DEX → Accuracy (+1 each), LUK → Critical (+0.3% crit each).
- **Gear stats are flat numbers** (+12 Attack, +40 HP). The ring and the masteries stay the percentages on top.
- **The player picks the slot.** An empty slot is filled; an occupied one is replaced. A stat already on the
  piece cannot roll again.
- **There is no failure outcome.** A bad roll is the failure, and the Ink is always spent.
- **Quality sets the number of slots.** When a quench lowers the grade, **the last stat on the list is removed**
  and its Ink is gone; if the grade comes back, that slot is simply empty again.

## 4. Materials and rarity

- **Five rarities: Common, Uncommon, Rare, Epic, Legendary.** A piece of gear's rarity is decided once, when the
  copy is made, saved on the copy, and never changed by a fold or a quench. A grade is never coloured.
- **On gear, rarity multiplies the piece's base** (§2): Common **×1.00**, Uncommon **×1.10**, Rare **×1.20**, Epic
  **×1.35**, Legendary **×1.50** — and with it everything the folds and quenches add, since those are percentages
  of the base.
- **Only a dropped piece rolls a rarity, by the kind of creature that dropped it:** a normal creature's is Common,
  Uncommon or Rare at shares of 50 : 30 : 14 (53.2%, 31.9%, 14.9%); an elite's is always Epic; a shadow's is always
  Legendary. Every other piece is Common — a new character's three pieces, a GM grant, a reward, and everything a
  creature WEARS, so no creature's numbers depend on rarity.
- **A weapon's trademark has nothing to do with its rarity**, and neither has an armour piece's word
  ([Weapon Names & Trademarks](weapon-names-and-trademarks.md) §4).
- **Consumables, quest and other items are Common.**
- **Materials by how hard they are to find:** Temper Dust and Quench Oil are Common; Watery Ink and Coarse
  Salt Uncommon; Thin Ink and Faint Ink Rare; Clear Ink, Sharp Ink and Fine Salt Epic; Pure Salt Legendary. A
  creature's own material is the tier its step names
  ([Creature Materials](../../3-world/creature-materials.md)). Read off the material's kind and tier
  (`Rarities.OfMaterial`), so it can never drift from the item.
- **An item's name is written in its rarity's colour** — Common the ordinary text colour, Uncommon green, Rare blue,
  Epic purple, Legendary orange-gold — in the tooltip (which also names the rarity), the bag, the worn list of a unit
  you select, and on the floor. One function owns the answer and the colour (`Rarities`). Skills keep their own
  colour theme: each skill is coloured by the weapon it requires (red for sword skills, and so on).
- **Ink and Salt are the surprise drop**, and **each drops only from the levels it is used at and above** — Watery
  Ink from level 15, Thin from 43, Faint from 57, Clear from 71, Sharp from 85; Coarse Salt from 15, Fine from 57,
  Pure from 71 — so a low-level hunter never finds one worth a thousand of their kills
  ([Crafting Tables](crafting-tables.md) §1). Which tiers, and how often, is the creature's kind's answer (normal,
  elite, shadow).
- **How much Temper Dust and Quench Oil a kill gives follows what the kill pays.** Creature levels 1–99 split
  evenly into seven steps — 1–14 is step 1, 15–28 step 2, 29–42 step 3, 43–56 step 4, 57–70 step 5, 71–84 step 6,
  85–99 step 7 — and everything above 99, the shadows, is step 7. A kill gives **0.6 Temper Dust and 0.06 Quench
  Oil for each step**, times **10** for an elite and **20** for a shadow, times what the grade and folds of the
  creature's gear pay ([What a Kill Pays](../../3-world/what-a-kill-pays.md)): whole units, and the part left over
  is the chance of one more. A level 1 normal creature in Raw unfolded gear gives a 60% chance of one Temper Dust
  and a 6% chance of one Quench Oil. The level is the one in the creature's file, never one it grew by hitting
  people. The table is in [Mob Roster](../../3-world/mob-roster.md).
  Every kill also has a chance of the creature's own find — a food or an element converter, by its element
  ([Town Supplies and Hunting Finds](../town-supplies-and-hunting-finds.md)); materials are never in that roll.
- **Tiered materials merge upward, deterministically**: a fixed number of one tier always becomes one of the next
  tier, no failure. This applies to **Ink** (10 → 1) and to **Salt** (20 Coarse → 1 Fine, 5 Fine → 1 Pure — a Salt's
  merge follows its worth, the kills it takes to find one), so surplus low tiers are never trash and the top tier is
  reachable by work as well as by luck. **Temper Dust and Quench Oil have no tiers, so there is nothing of theirs to merge.**

## 5. Weapon Mastery

**One mastery per weapon type** (Sword Mastery, Bow Mastery, …). It is the character's skill with that kind of
weapon. It is also the "fourteen mastery tracks" the [lore](../../3-world/story-setting-canon.md) already names as
the endgame.

- **It is permanent.** Mastery a character has earned is never lost.
- **It raises three ring stats that fit the weapon's identity** (from its sentence in the
  [Weapon Roster](../active-skill-system-v3/weapon-roster-12-identities.md)). Only the ten ring stats are used:

  | Weapon | Identity | Mastery raises |
  | --- | --- | --- |
  | Sword | Precise duels, timed defence | Attack Speed, Attack, HP |
  | Greatsword | Delete groups with cleaves | Attack, Critical, HP |
  | Axe | Relentless aggression | Attack Speed, Attack, Critical |
  | Warhammer | Deny enemy actions | Attack, Defense, HP |
  | Spear | Control positioning | Accuracy, Attack, HP |
  | Fist | Physical bursts, or stand in front and take the hits | Attack, HP, MP |
  | Bow | Mobility and spacing | Evasion, Accuracy, Attack Speed |
  | Dagger | Poison from the blade | Attack Speed, Evasion, Attack |
  | Katar | Chains of critical hits | Critical, Evasion, Attack Speed |
  | Shuriken | Bouncing throws, or many-bladed swings | Attack Speed, Accuracy, HP |
  | Sniper | One painful shot from far away | Critical, Accuracy, Magic Attack |
  | Staff | Destructive area magic | Magic Attack, Cast Speed, MP |
  | Wand | Keep allies alive | MP, Cast Speed, Defense |
  | Scythe | Overwhelm with curses | Magic Attack, MP, HP |

  The engine's weapon types do not all match that list, and the code maps them this way: **Greatsword** is the
  two-handed sword type; **Warhammer** is the mace type; **Fist** is the
  knuckle type; **Sniper** is the firearm type.

- **Its bonus is added to the ring's bonus for the same stat, never multiplied.** +30% Attack from the ring and
  +20% Attack from Sword Mastery make +50% Attack.
- **Only the ring has diminishing returns.** The ring's own share still stops at its cap (+80% for most stats);
  mastery has no diminishing returns and adds on top, so a maxed ring still gains from mastery. There is no
  combined cap.
- **It earns mastery points.** Each Weapon Mastery level gives **one mastery point for that weapon**, spent in
  the Skills window (K) to learn and level that weapon's skills (§7). Nothing is learned automatically: a skill
  can be bought only once the weapon's mastery has reached the level the skill names, **and** the skill it needs
  (if any) has at least one point. A skill needs another only when it literally uses it — its stacks, its pile,
  its cast, or a moment it causes — e.g. **Crossed Branches requires Piercing Gust**; a requirement always asks
  for level 1 of the parent. The rules for laying out a tree are the project's `skill-tree` skill
  (`.claude/skills/skill-tree/SKILL.md`).
- **The equipped weapon's quality caps it.** Each quality grade allows mastery up to a limit. A master swordsman
  holding a poor sword uses only the mastery that sword allows. The earned mastery is not lost; it counts again
  the moment a better sword is equipped.
- **Quality also sets how fast mastery grows.** A better weapon trains faster; a poor one trains slower.
- **While capped, no mastery is earned.** If a character's mastery has reached what the weapon allows, fighting
  with it teaches them nothing more.
- **Creatures follow the same mastery rules, in the same store as a player's** (decided 2026-10-04). A creature has
  mastery in ONE weapon only and knows only the skills it has bought with that mastery's points; nothing is opened
  for free. Its weapon and its mastery level are set on its catalog entry (the Mob Table: the weapon with its gear,
  the mastery in its own column) and put on it when it is made. **Every creature's mastery is 0 today, and nothing spends a creature's
  points, so no creature casts a skill** (which weapon, mastery and skills each gets: Still open, below).

### How mastery is earned

**Exactly the way your level and your ring are earned.** Two thirds of the experience every kill pays you also goes
to the mastery of the weapon in your hands — the same kill, paid to whoever the experience is paid to. Two things adjust it, both already above: the weapon's **grade sets how fast it trains** (the speed column in
§9), and **nothing is earned while the grade's cap is reached**. Weapon mastery levels on **the character's own
curve** (§9), and keeps levelling after the character reaches 99. A creature that cannot die (a training dummy) pays no experience, so it teaches nothing.

## 6. Skills leave the weapon

- **A weapon no longer carries skills.** There is no rolled kit.
- **Every skill is tagged with the weapon type it requires.** A character learns skills into their own **character
  skill window** by spending that weapon's mastery points, and can use a learned skill only while holding that
  weapon type.
- **A learned passive works on its own.** Nothing switches it on and there is no limit on how many: the moment a
  point learns it, it fires on its trigger while a weapon of its type is in hand. A passive never goes on
  the hotbar.

### A weapon's trademark

A weapon does not grant a skill; it **empowers** one. Full design: [Weapon Names & Trademarks](weapon-names-and-trademarks.md).

- **Rolled once, as the weapon comes into the world, and never changed.** A dropped piece, a GM grant, a reward
  and a creature's gear all roll it the same way; folds, quenches and a failed quench leave it alone. A new
  character's starting Sword is the one weapon made with none.
- **Which skills can come up:** every **published** skill of the weapon's type with a **main number** (§7) on
  any of its effects — decided from the skill's own effects every time, so a skill that gains a main number joins
  the roll, and one that loses its last leaves it. For the sword that is 18 skills; Bending Reed, Held Breath, Howling Wind and
  Deep Roots have no main number. **The roll is two draws: a class first — an ultimate's on 1% of weapons, an
  active's or a passive's on the other 99% — then a skill of that class evenly** (§9). It has nothing to do with the
  weapon's rarity.
- **Its name is the skill's own name** — there is no separate weapon-name field, and the skill-data check fails on a
  skill file that still carries one. A skill carries no trademark rarity.
- **What it does:** while the weapon is held, **every main number of that skill** rises by the grade's percentage
  (§9) — added to the skill-level lift (§7), never multiplied onto it. Creatures follow the same rule.
- **What the cards say.** The weapon's card: "Trademark Skill: Thousand Petals", then one line per raised effect
  ("+20% damage", "+20% damage buff"). The skill's card: each effect's base number, then "Trademark skill bonus
  damage: +20%" on its own line, only when the weapon in hand empowers that skill.

## 7. Skill levels

**Every skill has a level, bought with its weapon's mastery points** (§5). The first point **learns** the skill
(level 1); each further point raises it one level. The server decides every spend. Pressing a skill does not
train it.

- **It powers the skill.** It takes over the job V3's rarity used to have: every level after the first adds **+5%** to
  **every main number on every effect** of that skill — level 1 is the skill as authored, level 5 is **+20%**,
  level 10 is **+45%**. Actives, passives and ultimates all grow the same way. It is added to the ring's term,
  never multiplied onto it, and it counts only while a weapon of the skill's type is in hand, so a skill that
  belongs to no weapon (a potion's, for one) gets nothing.
- **Each skill has a max level, decided by what grows:** **10** when what grows is mostly damage, **5** when it is
  a defensive or utility percentage or a charge count, **1** when nothing on it grows. The sword's split is in §9.
- **Main numbers, and only main numbers — no exception.** A main number is: a strike's or an execute's damage, a
  heal, a shield, an ailment's damage per tick (Poison, Bleed, Burn), the size of a sub-stat buff or debuff
  (drawbacks included — Squall's double damage taken grows too), an outgoing-damage boost or cut, and the
  stacks a skill adds to another skill's pile. A skill with several (Thousand Petals' strike AND its damage buff)
  has all of them raised. Chance, duration, number of hits and seconds added are raised by the ring alone. The
  weapon trademark (§6) raises exactly the same numbers.
- **Every level bought COUNTS, with any weapon of the skill's type.** The weapon's grade does not limit skill
  levels, and nothing else does: a skill can be raised to its max level and every level raises its numbers.
- **What the Skills window shows.** One weapon at a time, as a horizontal timeline of mastery 0 → 70: each skill's
  left edge sits at the mastery level that opens it, a skill and the skills that need it share rows joined by
  wires, a skill out of reach is faint, one in reach with no point is solid grey, and a bought one wears the
  weapon's colour; a **+** button on each skill spends a point. The skill's card shows **PASSIVE / ACTIVE /
  ULTIMATE** beside the name, its requirements (green when met, red when not), "Skill level N / max", and each
  growing number now → at the next level. Those numbers are what an ordinary cast lands: they include the
  character's ring investment and, for a skill of the weapon in hand, its trademark.
- **Existing characters** had their automatically-learned skills cleared and every point their masteries earned
  handed back to spend (save version 17). There is no refund for players; the GM command `/resetskills
  [weapon|all]` takes points back for testing.

## 8. The Ring becomes part of the character

- **The ring is no longer an item.** Its growth (the ten stats, the level-up draft, the soft cap) belongs to the
  character and always counts. Nothing has to be worn to switch it on.
- **The ring equipment slot is removed.**
- **It keeps the name "Ring".**

## 9. First numbers — decided 2026-09-20, tuned in play

Every value here is a **first guess the user asked for and owns**; it is anchored to the game's real baselines
(a level-46 creature has about 83 attack, 58 defence and 510 HP; the level-3 ones about 14, 5 and 37) and it will
move once it is played. Change it here, in place, when it does.

### Grades, folds and quenches

| Grade | Fold cap = quench gate | Each fold adds | The quench that reached it adds | Ink slots | Weapon Mastery cap | Weapon Mastery speed |
| --- | --- | --- | --- | --- | --- | --- |
| Raw | +10 | 10% | — | 0 | 10 | ×0.5 |
| F | +10 | 20% | 100% | 1 | 20 | ×0.75 |
| E | +10 | 30% | 200% | 2 | 30 | ×1.0 |
| D | +10 | 40% | 300% | 3 | 40 | ×1.1 |
| C | +10 | 50% | 400% | 4 | 50 | ×1.2 |
| B | +10 | 60% | 500% | 5 | 60 | ×1.35 |
| A | +10 | 70% | 600% | 6 | 70 | ×1.5 |

- **Base, Common, at a 100% roll:** a weapon **50 × its swing time in seconds** (÷ 1.5 for a pair weapon) — a Sword
  63, a Dagger 50, a Greatsword 80; a body or garment one sixth of a Dagger's base — metal **4 Defence and 33 HP**,
  leather **2.1% Attack Speed and 2 Evasion**, cloth **4 Magic Defence and 17 MP**; head and shoes half. Every
  weapon's and slot's base is in [Crafting Tables](crafting-tables.md).
- **A Common Sword at +10** of each grade is worth 125 · 313 · 625 · 1,063 · 1,625 · 2,313 · **3,125** Attack (Raw …
  A). A Legendary A +10 Sword at a 115% roll is 62.5 × 1.15 × 1.50 × 50 = **5,391 Attack**.
- **Rarity multiplies the base:** Common **×1.00** · Uncommon **×1.10** · Rare **×1.20** · Epic **×1.35** ·
  Legendary **×1.50**. **The roll:** 85% to 115%, creature drops only.
- **Fold cost:** Temper Dust, the grade's step in units (1 at Raw … 7 at A), plus **0.6 kills of income** at the
  grade's price level. **Quench cost:** Quench Oil, the grade's step in units (1 at Raw … 6 at B), plus **6 kills of
  income**, and optionally one Salt. A kill's income is 10u × the creature's level, and a grade is priced at the
  middle of its seventh of levels 1–99: **7, 21, 35, 50, 64, 78, 92** (Raw … A). The fees, in full, are in
  [Crafting Tables](crafting-tables.md).

### Fold odds

+0 to +4 always succeed. A failed fold takes off one fold, never below +4.

| Grade | +4→5 | +5→6 | +6→7 | +7→8 | +8→9 | +9→10 |
| --- | --- | --- | --- | --- | --- | --- |
| Raw | 90% | 80% | 70% | 60% | 50% | 40% |
| F | 76% | 68% | 60% | 51% | 42% | 34% |
| E | 65% | 58% | 50% | 43% | 36% | 29% |
| D | 54% | 48% | 42% | 36% | 30% | 24% |
| C | 45% | 40% | 35% | 30% | 25% | 20% |
| B | 36% | 32% | 28% | 24% | 20% | 16% |
| A | 22% | 20% | 17% | 15% | 12% | 10% |

### Quench odds, and Salt

| From | Raw | F | E | D | C | B |
| --- | --- | --- | --- | --- | --- | --- |
| Chance to go up | 40% | 30% | 18% | 11% | 6.5% | 3.5% |

| Salt | Rarity | If the quench fails | Chance to go up |
| --- | --- | --- | --- |
| None | — | Drops one grade, to +0 (Raw stays Raw), and loses the last Ink stat | as the table |
| Coarse | Uncommon | Keeps its grade, at +4 | as the table |
| Fine | Epic | Keeps its grade, at +4 | × 1.25 |
| Pure | Legendary | Keeps its grade, at +9 | × 1.5 |

- **Up never passes 95%**, however good the Salt — a quench is never a certainty.
- **Where Salt drops:** a normal kill Coarse **10%**, Fine **0.5%**, Pure **0.1%**; an elite Fine **20%**, Pure
  **5%**; a shadow Fine **40%**, Pure **15%** — each only from a creature of level 15 and up (Coarse), 57 and up
  (Fine) or 71 and up (Pure). A Salt's worth is the kills it takes to find one: Coarse 10, Fine 200,
  Pure 1,000.
- **Merging:** Ink 10 of a tier → 1 of the next; Salt 20 Coarse → 1 Fine, 5 Fine → 1 Pure.
- **The cheapest Salt by climb**, worked out from what it saves against what it costs, and the average kills each
  climb takes, Salt and fees included, at the yardstick of 500 kills an hour
  ([Creature Materials](../../3-world/creature-materials.md)):

  | Climb | Cheapest Salt | Average kills |
  | --- | --- | --- |
  | Raw → F | none | 210 |
  | F → E | Coarse | 430 |
  | E → D | Coarse | 1,250 |
  | D → C | Coarse | 4,730 |
  | C → B | Fine | 19,800 |
  | B → A | Pure | 102,700 |
  | A +0 → A +10 | — | 110,200 |
  | **Raw +0 → A +10** | | **239,300 kills, about 479 hours**, of which the fees are about 116 |

### Mastery

- **Weapon Mastery:** earned from two thirds of each kill's experience (§5), times the grade's speed, on the
  character's own experience curve: mastery N to N+1 costs what base level N+1 does, so at par mastery 70 comes
  around base level 74 ([the curve](../pre-renewal-ro-port.md)). Each level adds **+0.5%** to
  each of the weapon's three stats (+35% each at 70).
- **Mastery points:** one per Weapon Mastery level, for that weapon — 70 at most. One point learns a skill, each
  further point is one more level.
- **Skill level lift:** **+5%** to each main number per level after the first (level 5 = +20%, level 10 = +45%),
  the same for actives, passives and ultimates, and only for a skill of the weapon in hand.

### Trademarks

- **Lift by the weapon's grade:** Raw **+5%** · F **+7%** · E **+10%** · D **+12%** · C **+15%** · B **+18%** ·
  A **+20%**. The server writes the finished lift on the copy; a player's machine only shows it.
- **The roll:** a class first — an ultimate's **1%**, an active's or a passive's **99%**; a weapon type with only
  one class gives that class every weapon — then a trademark of that class evenly. The sword's: **6.6%** each of
  its fifteen actives and passives, **0.33%** each of its three ultimates.

### Ink values (flat, per tier: Watery / Thin / Faint / Clear / Sharp)

| Stat | Watery | Thin | Faint | Clear | Sharp |
| --- | --- | --- | --- | --- | --- |
| Attack, Magic Attack | 2 | 4 | 6 | 8 | 10 |
| Defence, Magic Defence | 2 | 4 | 6 | 8 | 10 |
| HP | 16 | 32 | 48 | 64 | 80 |
| MP | 8 | 16 | 24 | 32 | 40 |
| Accuracy, Evasion | 2 | 4 | 6 | 8 | 10 |
| Crit chance, Crit damage | 0.6% | 1.2% | 1.8% | 2.4% | 3% |
| Attack Speed, Cast Speed | 1% | 2% | 3% | 4% | 5% |
| **Any primary** (premium 1.5×) | **3** | **6** | **9** | **12** | **15** |

- **Roll odds:** each of the twelve sub-stats **7.4%** (88.9% together); each of the six primaries **1.85%**
  (11.1% together). A stat already on the piece cannot roll again, and the odds re-spread evenly over the rest —
  with one stat written, each of the remaining sub-stats is 8% and each primary 2%.

### Sword — which mastery level opens which skill, and how far it levels

Ordered so a skill always comes after the skill it depends on, and so every grade opens something new. "Opens"
is the mastery a skill can first be bought at; it learns nothing on its own. "Needs" is the skill that must have at
least one point first.

| Sword Mastery | Opens | Needs | Max level |
| --- | --- | --- | --- |
| 1 | Piercing Gust, Bending Reed | — | 10 / 1 |
| 3 | Rising Breeze | — | 10 |
| 5 | Still Air | — | 5 |
| 7 | Crossed Branches | Piercing Gust | 5 |
| 9 | Held Breath | — | 1 |
| 11 | Eye of the Storm | Bending Reed | 5 |
| 13 | Howling Wind | — | 1 |
| 15 | Scattered Petals | — | 5 |
| 17 | Tailwind | Rising Breeze | 5 |
| 19 | Stripped Bough | — | 5 |
| 21 | Deep Roots | Still Air | 1 |
| 23 | Petal Storm | — | 10 |
| 25 | Parting Clouds | Bending Reed | 5 |
| 27 | Single Petal | — | 10 |
| **30** | **Last Petal** (ultimate) | Rising Breeze | 10 |
| 33 | Headwind | — | 5 |
| 36 | Squall | — | 5 |
| 40 | Bare Branch | Held Breath | 5 |
| **45** | **Returning Wind** (ultimate) | Held Breath | 10 |
| **60** | **Thousand Petals** (ultimate) | — | 10 |
| 65 | Second Bloom | Thousand Petals | 5 |

By grade: a Raw sword (cap 10) reaches six skills; F adds five; E adds four and the first ultimate; D three;
C the second ultimate; B the third; only an A-grade sword reaches Second Bloom. The max levels add up to **129**
(seven skills at 10, eleven at 5, four at 1) against at most **70** points, so a fully mastered swordsman still
chooses. The other thirteen weapons get their trees when their pools are authored, by the rules in the `skill-tree`
skill.

## Still open

- **Card sockets** — deferred.
- **The starter balance is checked in play.** A level 1 character in Raw +0 gear swings for 55 Attack; whether that
  is right against level 1 creatures, which wear gear by the same rules, is decided in play.
- **The equal-gear check was made at A +10 only.** It is made at the lower grades in play.
- **Skill trees for the other thirteen weapons** — written when each pool is authored. Only the Sword's 22 skills
  carry a mastery level today, and only the sword's have trademark names; the other weapons roll no trademark
  until their pools exist.
- **Which townspeople are stations** — Lastline has one of each, standing at the doors of the four houses around
  the fountain: Gunnar the Smith, Odette the Weaver (loom), Rook the Tanner (tannery), beside Fenwick the Tool Dealer's
  shop. They are rows of the townsfolk list (`WorldTownsfolk`), not objects in the scene. Which stations other towns get
  is to be planned.
- **Which weapon each creature carries, how much mastery it has in it, and which skills it buys** — to be planned.

## What V4 replaced in V3

- The rolled weapon kit and its sizes and ultimate chance.
- V3's rarity: its drop weights and its lift on skill output. (V4's rarity, §4, multiplies only a piece's base.)
- The worn ring item that switches the ring on, and its drop chance.
- V3's rule that **skills don't level**.
- The lore line that a dropped weapon's skills are "the memories that surfaced".
- The design in [Weapon Crafting & Economy](../active-skill-system-v3/weapon-crafting-economy.md) — rolled kits,
  Potential, Reforge and Essence — and the player page [Crafting & Recycling](../crafting-and-recycling.md).
- The per-copy armour roll (random defence, random stat count, random stats).

---

## Related

- [Implementation Plan](implementation-plan.md) — the six build slices, every file each touches, and the open build questions.
- [Weapon Names & Trademarks](weapon-names-and-trademarks.md) — the trademark design: the eighteen sword names, the roll, and how it is built.
- [Crafting Tables](crafting-tables.md) — the numbers only: every base, fold and quench costs and odds, rarity, Salt, Ink values, mastery caps by grade.
- [Elements, Element Levels & Converters](elements-and-converters.md) — creatures attack and defend with an element at level 1–4 on Ragnarok's chart, negative values heal, and eight level-1 converters lend players an element for 5:00.
- [Armour Roster, Materials & Words](armour-roster.md) — five armour slots in three materials; the material decides what a piece's base buys, and every armour piece rolls one of its slot's words.
- [Timed Effects Survive Logout](timed-effects-survive-logout.md) — only death ends a timed effect; logging out and changing server pause it with the time it had left.
- [Biomes — Ground Tiles and Props](../../3-world/biomes.md) — the fourteen biomes every map is generated in (each its own ground tiles, props and prop counts), and the weather a biome always has: Swamp's standing water, Snow's snow blanket, Mist's fog.
- [Biome References](../../3-world/biome-references.md) — a real photo of each biome beside its test map, with a match rate and what is still missing.

- [Active Skill System — V3](../active-skill-system-v3/README.md) — the design as it runs today.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the ring's draft and soft cap, which
  V4 keeps and moves onto the character.
- [Weapon Roster — 14 Identities](../active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen weapon
  types masteries are tracked by.
- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — mastery as the endgame of naming a Kashira.
