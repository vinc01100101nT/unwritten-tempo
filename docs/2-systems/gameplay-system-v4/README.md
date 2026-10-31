---
status: "BUILT, not yet play-tested. The numbers below are first guesses the user owns, to be tuned in play; the code holds them (CraftingMath, CreatureLoot, MasteryMath, the item and skill assets) and wins any disagreement."
owner: vince
last-updated: 2026-10-31
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

**How it relates to V3.** This is what the code does. [V3](../active-skill-system-v3/README.md) still
describes the channels, effects and triggers, which V4 keeps; where the two disagree — rolled weapon kits, V3's
rarity (a rolled tier that lifted a weapon's skill output), the worn ring item, "skills don't level" — V4 is what
runs and V3's passages on those points are dead. **Not yet play-tested.**

**Where the numbers live.** Every base, cost, chance and per-grade rule is in `unity/Assets/Scripts/CraftingMath.cs`;
what a creature wears and drops in `CreatureLoot.cs`; mastery in `MasteryMath.cs` and `WeaponMasteryStats.cs`; prices
on the item assets under `unity/Assets/3_Data/Resources/Items/`; each skill's mastery level, parent and max level on
its skill file. The tables below were read from those on 2026-10-31. Change the code or the asset, then this page.

## 1. Gear is dropped Raw, and worked at a station

- **Gear is dropped, never made.** Every kill, of every kind of creature, has a **10%** chance to drop **one**
  random piece of what the creature wears. The drop is a new copy of that item — nothing leaves the creature — and
  it is always **Raw, +0**, with its base roll (§2) and its trademark (a weapon) or word (an armour piece) rolled.
  There is no way to make a piece from materials.
- **A new character starts wearing a Sword, a Chainmail and Iron Sabatons** — Raw, +0, unrolled (§2), with no
  trademark and no word.
- **Gear has no rarity** (decided 2026-10-18). Two pieces of the same item differ by their roll, grade, folds,
  trademark or word and Inks — never by a colour.
- **Players make their gear worth more with materials.** Every piece — **metal, cloth or leather** — is worked at
  the smith. The station does five things: **Fold, Quench, Ink, Slot, Recycle**. The work happens at a station in
  town, never from the bag.
- **There is no level requirement to wear gear.** Anyone can equip anything.
- **Weapons and armour scale the same way**: what a piece is worth is its **base**, times its **roll**, times what
  its **folds** and **quenches** add (§2) — attack on a weapon; on armour, what its **material** buys (metal hard
  defence and HP, leather attack speed and evasion, cloth hard magic defence and MP). There is no Level on gear.
  Every armour piece rolls one **word** from its slot ([Armour Roster, Materials & Words](armour-roster.md)).

## 2. Fold, Quench, Slot, Recycle

**What the player gets.** A higher grade is always stronger: **A +0 is more than eight times F +10**, and a grade at
+0 is always worth more than the grade below it at +10. A quench is the big moment — a successful one doubles what
the last grade's folds gave — and a fold is the small one. Every piece is worth wearing from the moment it drops,
because it has a base under its folds, and two dropped pieces are rarely the same, because each rolls its base.
The climb from Raw +0 to A +10 is meant to be long: the user's target is about two months of eight hours a day.

**How a piece arrives.** A dropped piece is **Raw: +0, no quality**. Three things are rolled as it comes into the
world and never again: its **base roll** (below), and a weapon's **trademark** (§6) or an armour piece's **word**.

**What a piece is called.** A weapon with a trademark goes by its trademark skill's own name: its folds, the name,
then its grade — **+7 Thousand Petals [C]**. Any other piece: its grade, its folds, then the item — **B +6 Sword**. That one title
is used everywhere a piece is named: the bag list (which sorts and searches by it), its card, the station window
and the chat replies.

### What a piece is worth

**Worth = base × roll × (1 + every fold and quench percentage, added together)**, rounded half up once, at the end
(attack speed to one decimal). The percentages add; they never multiply each other. The server writes the finished
numbers onto the copy.

- **A weapon's base is 50 × its swing time in seconds**, divided by 1 + the off hand's power (0.5) for a pair weapon
  (Katar and Axe: one item in both hands, the second hand at half power), so every weapon deals the same damage a
  second at equal gear. A swing time is 0.8 s plus 0.005 s per point of weight a hand carries; a pair weapon's two
  hands carry half its weight each. **Staff, Wand, Scythe and Sniper give Magic Attack**; every other weapon gives
  Attack.

  | Weapon (weight) | Swing time | Base |
  | --- | --- | --- |
  | Katar (a pair, 80) | 1.0 s | 33 |
  | Axe (a pair, 120) | 1.1 s | 37 |
  | Fist (30) | 0.95 s | 48 |
  | Dagger, Shuriken (40) | 1.0 s | 50 |
  | Wand (50) | 1.05 s | 53 |
  | Bow (60) | 1.1 s | 55 |
  | Staff (80) | 1.2 s | 60 |
  | Sword (90) | 1.25 s | 63 |
  | Spear, Sniper (110) | 1.35 s | 68 |
  | Warhammer (130) | 1.45 s | 73 |
  | Scythe (140) | 1.5 s | 75 |
  | Greatsword (160) | 1.6 s | 80 |

- **An armour piece's MATERIAL decides what its base buys, and its SLOT how much.** Head and shoes give half of what
  a body or a garment does, except hard defence, which is the same on every piece: **7.0% at A +10 on a 115% roll**
  (6.1% at 100%), scaled by grade, folds and roll like every other number. No piece gives movement speed — a person
  moves at the shared pace, and only a timed effect changes it.

  | Material | Body or garment, Raw +0 | Head or shoes, Raw +0 | Body or garment, A +10 |
  | --- | --- | --- | --- |
  | Metal | 33 HP, hard defence | 17 HP, hard defence | 1,667 HP, 6.1% hard defence |
  | Leather | 2.1% Attack Speed, 0 Evasion | 1% Attack Speed, 0 Evasion | 104.2% Attack Speed, 21 Evasion |
  | Cloth | 17 MP, hard magic defence | 8 MP, hard magic defence | 833 MP, 6.1% hard magic defence |

  **Leather's evasion is a fifth of its attack speed's share** (decided 2026-10-18): at the old share a full grade E
  +10 leather set already held a same-level creature to the 5% floor on the chance to hit; now that floor takes
  level 99, AGI 99, a best-roll A +10 set and a full ring.
- **The roll.** Only a piece a creature DROPS rolls its base, at **85% to 115%** in whole percents, evenly — on the
  server, when it drops, saved on the copy and never changed. Every other piece — a new character's, a shop's, a
  reward, a GM grant, and what a creature wears — is unrolled and counts as 100%. A piece that was never rolled can
  be told apart from one that rolled 100%.
- **Folds and quenches add percentages of the base**, by grade (§9): each fold adds 10% at Raw up to 70% at A, and
  the quench that reached a grade adds 100% at F up to 600% at A.
- **The tooltip shows the base:** **Base 54 (108%)** — base × roll; an armour piece shows its first stat. An
  unrolled piece shows **Base 50**, with no percent. A piece shown with no copy behind it — a shop's row of gear for
  sale — shows no Base line.

### Fold (raise it)

Each fold costs materials and Unsigned and raises the piece by one: **a fold IS the existing refine (+N)**. A sword
folded seven times is a +7 sword.

- **Every grade's fold cap is +10**, and the cap is the quench gate: a piece can be quenched only at +10, and cannot
  be folded past it.
- **+0 to +4 always succeed.** From +4 a fold can fail, at a chance set by the grade and the folds already on the
  piece (§9). **A failed fold takes off one fold and never goes below +4.** Every try is paid for, whether it
  succeeds or not.
- **Every fold is paid in Temper Dust, and how much is decided by the piece's GRADE, never by its fold count** — the
  grade's step in units, 1 at Raw to 7 at A, plus a fee in Unsigned (§9). Temper Dust is one material shared by all
  three crafts. After a quench resets a piece to +0, it folds at the NEW grade's price.
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
- **What a failure does is the Salt's answer.** One Salt may go in, chosen in the window, and it is spent whatever
  happens (the table in §9). **The chance to go up never passes 95%**, however good the Salt — a quench is never a
  certainty.
- **Salt is one item shared by all three crafts**: **Coarse Salt → Fine Salt → Pure Salt**, each its own item, the
  better dropping less often. Nobody is told which Salt to use; each is meant to be the cheapest at some grades,
  worked out from what it saves against what it costs.
- **The station window says what a failure does:** "Chance to rise: X% · If it fails: drops to E +0", or "… stays
  F, back to +4".

**What quality does:** it sets how much each fold adds and what the quench that reached it added, the size of an
armour piece's word, how big an Ink's stat is (§3), it caps and speeds weapon mastery (§5), and it sets **how many
stat slots** the piece has (§3). A Raw piece caps mastery **below F**.

**No Potential.** The folds are the budget, and they refill through work.

### Slot (open a card slot)

**Slotting adds one card slot, or fails and keeps the slots it had.** A weapon can hold up to **4**, a body, a
garment or shoes up to **2**, a head none. Each press costs Quench Oil and Unsigned — the piece's quench price,
tripled for every slot it already has — paid whether it lands or not. Its chance falls with each slot already there
(`CraftingMath.SlotChancePct`). What a card does once it is in a slot is still open (below).

### Recycle (take it apart)

At the smith, any piece can be recycled: it is destroyed, and **half** of what it provably consumed comes back —
for each rung it climbed, that grade's ten folds and one quench (priced at the grade it was made from, which the
gate above makes provable); the folds standing on it now (at its own grade's price); and one Ink per written stat, of
the tier that wrote it. Each material is halved and rounded down on its own, so a single unit never comes back
whole. Nothing records a failed try, fold or quench, so those come back as nothing; Salt is never returned.

- **Every recycle gives at least 1 Temper Dust**, so an untouched Raw piece gives exactly that — and a piece with
  even one fold gives that fold's Unsigned on top, so an untouched piece always gives the least.
- **Raw folds count like every other grade's.** The first tutorial's up to 5 free Raw folds pay back only 2 Temper
  Dust and 105u once per character, less than four kills earn, so they are not guarded.
- A piece the first tutorial still needs cannot be recycled, sold or dropped
  ([Quests & Tutorials](../quests-and-tutorials.md)).
- **What a shop pays for a piece follows the same sum**: a piece is worth its recycle (its Unsigned, plus its Temper
  Dust and Quench Oil at their values) plus its Inks at theirs — so a brand-new piece is worth one Temper Dust.

| Piece | Comes back |
| --- | --- |
| Raw +0 Sword | 1 Temper Dust |
| Raw +10 Sword (ready to quench) | 5 Temper Dust, 210u |
| F +0 Sword | 5 Temper Dust, 420u |
| F +4 Sword | 9 Temper Dust, 672u |
| E +0 Sword | 15 Temper Dust, 1 Quench Oil, 1,680u |
| A +10 Sword | 140 Temper Dust, 10 Quench Oil, 18,060u |
| … with 2 Watery Ink stats written | + 1 Watery Ink |

## 3. Stats are written on with Ink

- **One material, Ink, in five tiers**, each its own item, dropping less often the better it is:
  **Watery → Thin → Faint → Clear → Sharp** Ink.
- **Using Ink on a piece rolls ONE stat** from a pool of eighteen: **twelve sub-stats** and the **six primaries**
  (STR, AGI, VIT, INT, DEX, LUK). **Primaries roll far less often and are worth far more** — the jackpot.
- **One Ink writes one stat, never two.** Two of the ring's ten nodes each move a pair — its Defense node moves
  defence AND magic defence, its Critical node moves crit chance AND crit damage — so those four are separate
  here: defence, magic defence, crit chance and crit damage. With HP, Attack, Accuracy, Cast Speed, Magic Attack,
  MP, Evasion and Attack Speed, that is the twelve.
- **How big the stat is: the stat's top value × the piece's grade share × the Ink's tier share**, rounded once when
  the server writes it. The top is a Sharp Ink on an A piece; the primary's top is **1.5×** the flat sub-stats'
  (the primary premium). Defence and magic defence write **hard** defence, a percentage.
- **The player picks the slot.** An empty slot is filled; an occupied one is replaced. A stat already on the
  piece cannot roll again.
- **There is no failure outcome.** A bad roll is the failure, and the Ink is always spent.
- **Quality sets the number of slots** (§9; Raw has none). When a quench lowers the grade, **the last stat on the
  list is removed** and its Ink is gone; if the grade comes back, that slot is simply empty again.
- The ring and the masteries stay the percentages on top of what gear gives.

## 4. Materials and rarity

- **Five rarities: Common, Uncommon, Rare, Epic, Legendary — a material's, set by how hard it is to find.** Temper
  Dust and Quench Oil are Common; Watery Ink and Coarse Salt Uncommon; Thin Ink and Faint Ink Rare; Clear Ink,
  Sharp Ink and Fine Salt Epic; Pure Salt Legendary. A creature's own material is the tier its step names
  ([Creature Materials](../../3-world/creature-materials.md)). Read off the material's kind and tier
  (`Rarities.OfMaterial`), so it can never drift from the item. Gear, consumables, quest and other items are Common.
- **An item's name is written in its rarity's colour** — Common the ordinary text colour, Uncommon green, Rare blue,
  Epic purple, Legendary orange-gold — in the tooltip (which also names the rarity), the bag, the worn list of a unit
  you select, and on the floor. One function owns the answer and the colour (`Rarities`). Skills keep their own
  colour theme: each skill is coloured by the weapon it requires (red for sword skills, and so on).
- **What a kill drops is decided by the creature's multiplier alone**, never by its level or its gear (decided
  2026-10-20; [Mob Roster](../../3-world/mob-roster.md)). Every kill gives **0.6 Temper Dust and 0.02 Quench Oil
  times the multiplier** — whole units, and the part left over is the chance of one more. Quench Oil is a thirtieth
  of the Dust, the share a fold-and-quench climb uses. Both come from creatures only.
- **Ink and Salt are the surprise drop**, each a chance a ×1 kill would get, times the multiplier — and **the rarer
  ones open only from a high enough multiplier**: Watery Ink and Coarse Salt from ×1, Thin Ink and Fine Salt from ×3,
  Faint Ink from ×10, Clear Ink and Pure Salt from ×30, Sharp Ink from ×90 (the chances: `CreatureLoot.Lines`).
  Every kill also has a chance of the creature's own find — a food or an element converter, by its element
  ([Town Supplies and Hunting Finds](../town-supplies-and-hunting-finds.md)); materials are never in that roll.
- **A Salt's worth is the ×1 kills it takes to find one** — Coarse 10, Fine 200, Pure 1,000 — and its merge follows
  that worth.
- **Tiered materials merge upward, deterministically**: a fixed number of one tier always becomes one of the next
  tier, no failure — **Ink 10 → 1** of the next tier, **Salt 20 Coarse → 1 Fine, 5 Fine → 1 Pure** — so surplus low
  tiers are never trash and the top tier is reachable by work as well as by luck. Temper Dust and Quench Oil have no
  tiers, so there is nothing of theirs to merge.
- **What a shop pays for one** (an item's value less the shop's Tax, a third):

  | Material | Unsigned |
  | --- | --- |
  | Temper Dust | 104 |
  | Quench Oil | 1,042 |
  | Watery / Thin / Faint / Clear / Sharp Ink | 2,625 / 25,000 / 10,666 / 26,000 / 92,000 |
  | Coarse / Fine / Pure Salt | 1,750 / 64,000 / 390,000 |

  No shop sells any of these.

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
  | Warhammer | Deny enemy actions | Attack, HP, Accuracy |
  | Spear | Control positioning | Accuracy, Attack, HP |
  | Fist | Physical bursts, or stand in front and take the hits | Attack, HP, MP |
  | Bow | Mobility and spacing | Evasion, Accuracy, Attack Speed |
  | Dagger | Poison from the blade | Attack Speed, Evasion, Attack |
  | Katar | Chains of critical hits | Critical, Evasion, Attack Speed |
  | Shuriken | Bouncing throws, or many-bladed swings | Attack Speed, Accuracy, HP |
  | Sniper | One painful shot from far away | Critical, Accuracy, Magic Attack |
  | Staff | Destructive area magic | Magic Attack, Cast Speed, MP |
  | Wand | Keep allies alive | MP, Cast Speed, HP |
  | Scythe | Overwhelm with curses | Magic Attack, MP, HP |

  The engine's weapon types do not all match that list, and the code maps them this way: **Greatsword** is the
  two-handed sword type; **Warhammer** is the mace type; **Fist** is the knuckle type; **Sniper** is the firearm
  type.

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
- **The equipped weapon's quality caps it.** Each quality grade allows mastery up to a limit (§9). A master
  swordsman holding a poor sword uses only the mastery that sword allows. The earned mastery is not lost; it counts
  again the moment a better sword is equipped.
- **Quality also sets how fast mastery grows.** A better weapon trains faster; a poor one trains slower.
- **While capped, no mastery is earned.** If a character's mastery has reached what the weapon allows, fighting
  with it teaches them nothing more.
- **Creatures follow the same mastery rules, in the same store as a player's** (decided 2026-10-04). A creature has
  mastery in ONE weapon only and knows only the skills it has bought with that mastery's points; nothing is opened
  for free. Its weapon and its mastery level are set on its catalog entry (the Mob Table: the weapon with its gear,
  the mastery in its own column) and put on it when it is made. **Every creature's mastery is 0 today, and nothing
  spends a creature's points, so no creature casts a weapon skill** (which weapon, mastery and skills each gets:
  Still open, below).

### How mastery is earned

**Exactly the way your level and your ring are earned.** Two thirds of the experience every kill pays you also goes
to the mastery of the weapon in your hands — the same kill, paid to whoever the experience is paid to. Two things
adjust it, both already above: the weapon's **grade sets how fast it trains** (the speed column in §9), and
**nothing is earned while the grade's cap is reached**. Weapon mastery levels on **the character's own curve**: mastery
N to N+1 costs what base level N+1 does, so at par mastery 70 comes around base level 74
([the curve](../pre-renewal-ro-port.md)), and it keeps levelling after the character reaches 99. Each level adds
**+0.5%** to each of the weapon's three stats (+35% each at 70). A creature that cannot die (a training dummy) pays
no experience, so it teaches nothing.

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
  the roll, and one that loses its last leaves it. **The roll is two draws: a class first — an ultimate's on 1% of
  weapons, an active's or a passive's on the other 99%; a weapon type with only one class gives that class every
  weapon — then a skill of that class evenly.**
- **Its name is the skill's own name** — there is no separate weapon-name field, and the skill-data check fails on a
  skill file that still carries one. A skill carries no trademark rarity.
- **What it does:** while the weapon is held, **every main number of that skill** rises by the grade's percentage
  (§9) — added to the skill-level lift (§7), never multiplied onto it. The server writes the finished lift on the
  copy; a player's machine only shows it. Creatures follow the same rule.
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
  a defensive or utility percentage or a charge count, **1** when nothing on it grows.
- **Main numbers, and only main numbers — no exception.** A main number is: a strike's or an execute's damage, a
  heal, a shield, an ailment's damage per tick (Poison, Bleed, Burn), the size of a sub-stat buff or debuff
  (drawbacks included — Squall's double damage taken grows too), an outgoing-damage boost or cut, and the
  stacks a skill adds to another skill's pile. A skill with several (Thousand Petals' strike AND its damage buff)
  has all of them raised. Chance, duration, number of hits and seconds added are raised by the ring alone. The
  weapon trademark (§6) raises exactly the same numbers.
- **Every level bought COUNTS, with any weapon of the skill's type.** The weapon's grade does not limit skill
  levels, and nothing else does: a skill can be raised to its max level and every level raises its numbers.
- **A weapon's tree is laid out so a skill always comes after the skill it depends on, and every grade opens
  something new.** "Opens" is the mastery a skill can first be bought at; it learns nothing on its own. A skill's
  opening mastery, the skill it needs and its max level are on its skill file (`masteryLevel`, `needsSkillId`,
  `maxLevel`) and drawn in the Skills window. A weapon's max levels add up to more than the 70 points its mastery
  can earn, so a fully mastered fighter still chooses.
- **What the Skills window shows.** One weapon at a time, as a horizontal timeline of mastery 0 → 70: each skill's
  left edge sits at the mastery level that opens it, a skill and the skills that need it share rows joined by
  wires, a skill out of reach is faint, one in reach with no point is solid grey, and a bought one wears the
  weapon's colour; a **+** button on each skill spends a point. The skill's card shows **PASSIVE / ACTIVE /
  ULTIMATE** beside the name, its requirements (green when met, red when not), "Skill level N / max", and each
  growing number now → at the next level. Those numbers are what an ordinary cast lands: they include the
  character's ring investment and, for a skill of the weapon in hand, its trademark.
- **There is no refund for players.** The GM command `/resetskills [weapon|all]` takes points back for testing.

## 8. The Ring becomes part of the character

- **The ring is no longer an item.** Its growth (the ten stats, the level-up draft, the soft cap) belongs to the
  character and always counts. Nothing has to be worn to switch it on.
- **The ring equipment slot is removed.**
- **It keeps the name "Ring".**

## 9. First numbers — tuned in play

Every value here is a **first guess the user asked for and owns**; it will move once it is played. Change it in the
code, then here, in place.

### Grades, folds and quenches

| Grade | Each fold adds | The quench that reached it adds | Total at +0 | Total at +10 | A Sword at +10, 100% roll | Ink slots | Weapon Mastery cap | Weapon Mastery speed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Raw | 10% | — | 0% | 100% | 125 | 0 | 10 | ×0.5 |
| F | 20% | 100% | 200% | 400% | 313 | 1 | 20 | ×0.75 |
| E | 30% | 200% | 600% | 900% | 625 | 2 | 30 | ×1.0 |
| D | 40% | 300% | 1,200% | 1,600% | 1,063 | 3 | 40 | ×1.1 |
| C | 50% | 400% | 2,000% | 2,500% | 1,625 | 4 | 50 | ×1.2 |
| B | 60% | 500% | 3,000% | 3,600% | 2,313 | 5 | 60 | ×1.35 |
| A | 70% | 600% | 4,200% | 4,900% | **3,125** | 6 | 70 | ×1.5 |

Every grade's fold cap — and so its quench gate — is +10.

### What each try costs

A station's fee is priced in **kills of income at the grade's level** (decided 2026-10-14): a ×1 kill is worth 10u ×
the creature's level, and a grade's level is the middle of its seventh of levels 1–99. **A fold try costs 0.6 kills
of income, a quench try 6.** Each try also costs the grade's step in **Temper Dust** (a fold) or **Quench Oil** (a
quench).

| Grade | Raw | F | E | D | C | B | A |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Level it is priced at | 7 | 21 | 35 | 50 | 64 | 78 | 92 |
| Temper Dust per fold | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| Unsigned per fold | 42 | 126 | 210 | 300 | 384 | 468 | 552 |
| Quench Oil per quench | 1 | 2 | 3 | 4 | 5 | 6 | — |
| Unsigned per quench | 420 | 1,260 | 2,100 | 3,000 | 3,840 | 4,680 | — |

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

### Ink values

What one Ink writes = the stat's top × the piece's grade share × the Ink's tier share.

| Stat | Top (Sharp Ink on an A piece) |
| --- | --- |
| Attack, Magic Attack, Accuracy, Evasion | 10 |
| Defence, Magic Defence (hard) | 6.0% |
| HP | 80 |
| MP | 40 |
| Crit chance, Crit damage | 3% |
| Attack Speed, Cast Speed | 5% |
| **Any primary** (premium 1.5×) | **15** |

| Grade share | F 50% · E 60% · D 70% · C 80% · B 90% · A 100% (Raw has no slots) |
| --- | --- |
| **Tier share** | **Watery 60% · Thin 70% · Faint 80% · Clear 90% · Sharp 100%** |

- **Roll odds:** each of the twelve sub-stats **7.4%** (88.9% together); each of the six primaries **1.85%**
  (11.1% together) — weights 80 and 20. A stat already on the piece cannot roll again, and the odds re-spread
  evenly over the rest — with one sub-stat written, each of the remaining sub-stats is 8% and each primary 2%.

### Slots

| Slots already on the piece | 0 | 1 | 2 | 3 |
| --- | --- | --- | --- | --- |
| Chance a weapon gains one | 50% | 25% | 10% | 3% |
| Chance a body, garment or shoes gains one | 25% | 5% | — | — |

### Mastery and trademarks

- **Mastery points:** one per Weapon Mastery level, for that weapon — 70 at most. One point learns a skill, each
  further point is one more level.
- **Skill level lift:** **+5%** to each main number per level after the first (level 5 = +20%, level 10 = +45%).
- **Trademark lift by the weapon's grade:** Raw **+5%** · F **+7%** · E **+10%** · D **+12%** · C **+15%** · B
  **+18%** · A **+20%**.

## Still open

- **Cards** — what a card in a slot does. Slots can already be opened (§2); the cards themselves wait for the
  creature-category tagging they depend on.
- **The starter balance is checked in play** — whether a level 1 character in Raw +0 gear is right against level 1
  creatures, which wear gear by the same rules.
- **The equal-gear check was made at A +10 only.** It is made at the lower grades in play.
- **Whether the climb meets its target** — about two months of eight hours a day from Raw +0 to A +10, Salt and
  fees included, at the drop rates the multiplier now sets. Measured in play.
- **Skill trees for the other weapons** — written when each pool is authored, by the rules in the `skill-tree`
  skill. A weapon with no trademark-able skills rolls no trademark until its pool exists.
- **Which townspeople are stations** — Lastline has one, Gunnar the Smith, standing at a house door by the fountain
  near Fenwick the Tool Dealer's shop. They are rows of the townsfolk list (`WorldTownsfolk`), not objects in the
  scene. Which stations other towns get is to be planned.
- **Which weapon each creature carries, how much mastery it has in it, and which skills it buys** — to be planned.

## What V4 replaced in V3

- The rolled weapon kit and its sizes and ultimate chance.
- V3's rarity: its drop weights and its lift on skill output.
- The worn ring item that switches the ring on, and its drop chance.
- V3's rule that **skills don't level**.
- The lore line that a dropped weapon's skills are "the memories that surfaced".
- The design in [Weapon Crafting & Economy](../active-skill-system-v3/weapon-crafting-economy.md) — rolled kits,
  Potential, Reforge and Essence — and the player page [Crafting & Recycling](../crafting-and-recycling.md).
- The per-copy armour roll (random defence, random stat count, random stats).

---

## Related

- [Weapon Names & Trademarks](weapon-names-and-trademarks.md) — the trademark design: the sword names, the roll, and how it is built.
- [Elements, Element Levels & Converters](elements-and-converters.md) — creatures attack and defend with an element at level 1–4 on Ragnarok's chart, negative values heal, and level-1 converters lend players an element for 5:00.
- [Armour Roster, Materials & Words](armour-roster.md) — the armour slots in three materials; the material decides what a piece's base buys, and every armour piece rolls one of its slot's words.
- [Timed Effects Survive Logout](timed-effects-survive-logout.md) — only death ends a timed effect; logging out and changing server pause it with the time it had left.
- [Station Window](../shop-cart-and-station-window.md) — how the station window is laid out on screen.
- [Mob Roster](../../3-world/mob-roster.md) — the creature kinds, what they wear and what they drop.
- [Creature Materials](../../3-world/creature-materials.md) — the material only one creature drops, and what a kill is worth.
- [Biomes — Ground Tiles and Props](../../3-world/biomes.md) — the biomes every map is generated in, and the weather a biome always has.
- [Biome References](../../3-world/biome-references.md) — a real photo of each biome beside its test map, with a match rate and what is still missing.
- [Active Skill System — V3](../active-skill-system-v3/README.md) — the channels, effects and triggers V4 keeps.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the ring's draft and soft cap, which
  V4 keeps and moves onto the character.
- [Weapon Roster — 14 Identities](../active-skill-system-v3/weapon-roster-12-identities.md) — the weapon
  types masteries are tracked by.
- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — mastery as the endgame of naming a Kashira.
