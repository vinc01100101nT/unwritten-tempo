---
status: "BUILT 2026-09-20 (slices 1-6), weapon trademarks and the main-number rule BUILT 2026-09-21, creature drops by level BUILT 2026-09-22, element levels, converters, timed effects surviving logout and the armour roster BUILT 2026-09-23, the world's fourteen biomes in the map generator and the weather a biome always has BUILT 2026-09-25, skills bought and levelled with weapon mastery points BUILT 2026-10-03; not yet play-tested. The numbers in §9 are live in the code."
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# Gameplay System — V4 (Forging, Mastery, Trademarks & Ink)

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else. When a decision lands, rewrite the passage it
> changes and delete it from Still open.

**What V4 is.** V3 made the weapon the source of a character's skills: a dropped weapon rolled a random kit, and
its rarity lifted how hard that kit hit. V4 moves progression into the character's own hands. **Gear is made,
not dropped. Skills are bought with weapon mastery points, not rolled. Power over a skill comes from the level
bought in it, and from the trademark of the forged weapon in hand.**

**How it relates to V3.** This is now what the code does. [V3](../active-skill-system-v3/README.md) still
describes the channels, effects and triggers, which V4 keeps; where the two disagree — rolled
weapon kits, rarity, equipment drops, the worn ring item, "skills don't level" — V4 is what runs and V3's
passages on those points are dead. **Not yet play-tested.**

## 1. Gear is crafted, not dropped

- **Creatures stop dropping equipment.** They may still wear it, but when they die they drop only **materials,
  consumables and gold**.
- **Players make their gear from materials.** Each kind of material has its own craft and its own materials:
  **metal is forged, cloth is woven, leather is tanned**, and so on. The three actions below are the same for
  every craft; the words are the forge's.
- **Crafting happens at a station in town**: a smith for metal, a loom for cloth, a tannery for leather. Never
  from the bag.
- **There is no level requirement to wear gear.** Anyone can equip anything.
- **Weapons and armour scale the same way**: what a piece is worth comes from its **folds** and its **quality**,
  nothing else — attack on a weapon; on armour, what its **material** buys (metal defence and HP, leather attack
  speed and evasion, cloth magic defence and MP). There is no Level on gear. Armour has five slots — Head, Body,
  Off-hand, Garment, Shoes — each in all three materials, and every armour piece rolls one **word** from its slot
  ([Armour Roster, Materials & Words](armour-roster.md)).

## 2. Forge, Fold, Quench

**Forge (make it).** Choose a design, pay base materials and gold. A **Raw** piece is born: **+0, no quality**.
A weapon rolls ONE thing at forging — its **trademark** (§6) — and nothing else about any piece is rolled.

**What a piece is called.** A weapon with a trademark goes by its trademark skill's own name: its folds, the name,
then its grade — **+15 Thousand Petals [C]**. Any other piece: its grade, its folds, then the item — **B +12 Sword**. That one title
is used everywhere a piece is named: the bag list (which sorts and searches by it), its card, the station window
and the chat replies.

**Fold (raise it).** Each fold costs materials and gold and raises the piece by one: **a fold IS the existing
refine (+N)**. A sword folded twelve times is a +12 sword. Folding is deterministic work with no luck.

**The material is decided by the piece's GRADE, never by its fold count.** Each craft's base material comes in
**seven bands, one per grade** (Raw, F, E, D, C, B, A), and a piece folds only with its current grade's band.
Each band drops from one slice of creature levels, low to high, so the quench ladder walks the player across the
world: an A-grade piece is folded only with what the strongest creatures drop. After a quench resets a piece to
+0, it folds with the NEW grade's band. Base bands never merge into one another.

| Band | Metal (forged) | Cloth (woven) | Leather (tanned) |
| --- | --- | --- | --- |
| 1 · Raw | Copper | Burlap | Rawhide |
| 2 · F | Bronze | Hemp | Hide |
| 3 · E | Iron | Linen | Buckskin |
| 4 · D | Steel | Wool | Leather |
| 5 · C | Silver | Cotton | Cordovan |
| 6 · B | Mithril | Silk | Shagreen |
| 7 · A | Adamant | Brocade | Wyrmhide |

**Quench (roll its quality).** **Quality** — the one label on every crafted piece, weapon or armour — is a grade
**F (poorest) to A (highest)**, climbed **one rung at a time**:

- A quench moves the grade **up one, down one, or leaves it** — never a jump, never a roll to "any" grade.
- **Every quench consumes all the folds**, success or not: the piece is **+0** afterwards. Folding is what a
  quench spends.
- **F is the floor.** A failed F stays F. Raw is left once and never returned to.
- **A piece must be at its grade's fold cap to be quenched at all** — the cap is the gate, and the folds are what
  the quench spends. Enforced in the code from 2026-09-21; before that any piece could be quenched at +0.
- **A quench costs a larger amount of the piece's current band material, plus gold**, and optionally **one Salt**.
- **Salt is a one-use booster**: it shifts that quench's odds toward "up" and is consumed
  either way. It never guarantees success, never removes the "down" outcome, and never adds power. It is **one
  item shared by all three crafts**: **Coarse Salt → Fine Salt → Pure Salt**, each its own item,
  the better dropping less often; **one per quench**, any tier.

**Each grade has a fold cap, and the cap is its quench gate.** A piece cannot be folded past its grade; the only
way further is to quench. *(Example gates, to be tuned:)*

| Grade | Fold cap = quench gate |
| --- | --- |
| Raw | +5 |
| F | +10 |
| E | +15 |
| D | +20 |
| C | +25 |
| B | +30 |
| A | **+35 — the equipment cap** |

So a B +30 that quenches becomes **A +0** or **C +0**, and the decision to try is the whole game.

**What quality does:** it lifts what the piece's folds buy (as rarity used to), and the size of an armour piece's word, it caps and speeds
weapon mastery (§5), it limits how many of a skill's levels count (§7), and it sets **how many stat slots** the piece has (§3). A Raw piece caps mastery **below F**.

**No Potential.** The folds are the budget, and they refill through work.

**Recycle (take it apart).** At the station of its craft, any piece can be recycled: it is destroyed, and
**half** of what it provably consumed comes back — the forge bill; for each rung it climbed, that grade's full
fold cap and one quench (priced at the grade it was made from, which the gate above makes provable); the folds
standing on it now (at its own band); and the Inks whose stats are written on it. Nothing records a failed quench or the folds it spent, so those come back as nothing;
Salt is never returned. Each material is halved and rounded down on its own, so a single unit never comes back
whole. Decided 2026-09-21.

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

- **Rarity labels are removed from the game**: no Common, Uncommon, Rare, Epic or Legendary, anywhere. Gear has
  its quality grade; a material's rarity shows only in how often it drops.
- **No rarity or tier colours anywhere.** The only colour theme kept is on **skills**: each skill is coloured by
  the weapon it requires, as today (red for sword skills, and so on).
- **Rare materials are the surprise drop**, and they drop from creatures **of every level**. Only the base
  forging, weaving and tanning materials are level-gated.
- **Which band a creature drops is its level's answer** (decided 2026-09-22): the seven bands split creature levels
  1–99 evenly — 1–14 Raw, 15–28 F, 29–42 E, 43–56 D, 57–70 C, 71–84 B, 85–99 A — and a shadow (105–170) drops band
  A. A creature drops its band's metal, cloth and leather, and a chance of an Ink or a Salt; how much, and which
  tiers, is its kind's answer (normal, elite, shadow). The table is in [Mob Roster](../../3-world/mob-roster.md).
  Every kill also has a 30% chance of one random consumable; materials are never in that roll.
- **Tiered materials merge upward, deterministically**: a fixed number of one tier (e.g. ten) always becomes one
  of the next tier, no failure. This applies to **Ink** and to **Salt**, so surplus low tiers
  are never trash and the top tier is reachable by work as well as by luck. The merge ratio is the dial that
  keeps the top tier a jackpot. **Base band materials do not merge.**

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
  | Shield | Absorb attention | Defense, HP, MP |
  | Bow | Mobility and spacing | Evasion, Accuracy, Attack Speed |
  | Dagger | Finish weakened targets | Critical, Evasion, Attack Speed |
  | Firearm | Devastating single shots | Critical, Accuracy, Attack |
  | Staff | Destructive area magic | Magic Attack, Cast Speed, MP |
  | Wand | Keep allies alive | MP, Cast Speed, Defense |
  | Scythe | Overwhelm with curses | Magic Attack, MP, HP |

  The engine's weapon types do not all match that list, and the code maps them this way (decided while
  building, 2026-09-20; kept as is on 2026-09-21): **Greatsword** is the two-handed sword type; **Warhammer** is
  the mace type; a **two-handed axe** uses the Axe row; **Wand** uses the Staff row until it has a type of its
  own; **Katar** uses the Dagger row (the roster already maps Dagger to both). **Knuckle** has no roster identity
  yet and raises **Attack Speed, Attack, Evasion** — a placeholder row, to be replaced when the fist gets its
  sentence. **Shield** and **Scythe** have no type yet and so no mastery.

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

### A forged weapon's trademark

A weapon does not grant a skill; it **empowers** one. Full design: [Weapon Names & Trademarks](weapon-names-and-trademarks.md).

- **Rolled once, at the forge, and never changed.** Folds, quenches and a failed quench leave it alone. A weapon
  handed out any other way (a GM grant, a creature's gear) has none.
- **Which skills can come up:** every **published** skill of the weapon's type with a **main number** (§7) on
  any of its effects — decided from the skill's own effects every time, so a skill that gains a main number joins
  the roll, and one that loses its last leaves it. For the sword that is 18 skills; Bending Reed, Held Breath, Howling Wind and
  Deep Roots have no main number. **Each ultimate is 5%; the ordinary skills share the rest evenly.**
- **Its name lives on the skill** — the **trademark name** field in the Skill Composer, shown only while the
  skill can be rolled. The skill-data check fails on a rollable skill with no name.
- **What it does:** while the weapon is held, **every main number of that skill** rises by the grade's percentage
  (§9) — added to the skill-level lift (§7), never multiplied onto it. Creatures follow the same rule.
- **What the cards say.** The weapon's card: "Trademark Skill: Thousand Petals", then one line per raised effect
  ("+20% damage", "+20% damage buff"). The skill's card: each effect's base number, then "Trademark skill bonus
  damage: +20%" on its own line, only when the weapon in hand empowers that skill.

## 7. Skill levels

**Every skill has a level, bought with its weapon's mastery points** (§5). The first point **learns** the skill
(level 1); each further point raises it one level. The server decides every spend. Pressing a skill does not
train it.

- **It powers the skill.** It takes over the job rarity used to have: every level after the first adds **+5%** to
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
- **The equipped weapon's grade limits how many levels COUNT** (the table in §9). The levels bought are kept;
  a better weapon lets more of them count again. Level 1 of a learned skill always counts, at every grade.
- **What the Skills window shows.** One weapon at a time, as a horizontal timeline of mastery 0 → 70: each skill's
  left edge sits at the mastery level that opens it, a skill and the skills that need it share rows joined by
  wires, a skill out of reach is faint, one in reach with no point is solid grey, and a bought one wears the
  weapon's colour; a **+** button on each skill spends a point. The skill's card shows **PASSIVE / ACTIVE /
  ULTIMATE** beside the name, its requirements (green when met, red when not), "Skill level N / max", and each
  growing number now → at the next level. Those numbers are what an ordinary cast lands: they include the
  character's ring investment and, for a skill of the weapon in hand, its grade limit and its trademark; a skill of
  a weapon not in hand shows every level bought.
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

### Folds and quality

| Grade | Fold cap = quench gate | Quality lift on the fold value | Ink slots | Weapon Mastery cap | Skill levels that count (10-level / 5-level skill) | Weapon Mastery speed |
| --- | --- | --- | --- | --- | --- | --- |
| Raw | +5 | +0% | 0 | 10 | 2 / 1 | ×0.5 |
| F | +10 | +0% | 1 | 20 | 3 / 2 | ×0.75 |
| E | +15 | +5% | 2 | 30 | 4 / 2 | ×1.0 |
| D | +20 | +10% | 3 | 40 | 6 / 3 | ×1.1 |
| C | +25 | +15% | 4 | 50 | 7 / 4 | ×1.2 |
| B | +30 | +22% | 5 | 60 | 9 / 5 | ×1.35 |
| A | +35 | +30% | 6 | 70 | 10 / 5 | ×1.5 |

- **Skill levels that count:** the grade's share of a 30-step scale (Raw 4 · F 8 · E 12 · D 16 · C 20 · B 25 ·
  A 30), applied to the skill's own max level and rounded up — so a 1-level skill always counts and only
  an A-grade weapon lets every level of a 10-level skill count.
- **Per fold:** a weapon gains **+1 attack** (or +1 magic attack for a Staff / Wand / Scythe). An armour piece's
  **material** decides what a fold buys and its **slot** how much: metal **+0.5 defence and +4 HP**, leather
  **+0.25% attack speed and +0.25 evasion**, cloth **+0.5 magic defence and +2 MP** on body, off-hand and garment;
  head and shoes take half. Quality lifts that fold total by the column above. The full table is in
  [Crafting Tables](crafting-tables.md).
- **Fold cost:** 1 unit of the grade's band material + 50 gold × band number. **Quench cost:** 10 units of the
  band + 500 gold × band number, and optionally one Salt.

### Quench odds (up / same / down), and Salt

| From | Up | Same | Down |
| --- | --- | --- | --- |
| Raw → F | 80% | 20% | — |
| F → E | 60% | 40% | — (F is the floor) |
| E → D | 50% | 30% | 20% |
| D → C | 40% | 35% | 25% |
| C → B | 35% | 35% | 30% |
| B → A | 25% | 40% | 35% |

- **Salt adds to Up:** Coarse **+10**, Fine **+20**, Pure **+30** points. The points are taken from **Same**
  first, then from **Down**, and **Down never goes below 5%** where it exists.
- **Up never passes 95%**, however much Salt goes in — a quench is never a certainty. Any points that would take
  it past 95 stay with "unchanged".
- **Merging:** 10 of a tier → 1 of the next, for Ink and Salt.

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
- **Roll weights:** each ultimate **5%**; the ordinary skills share the remaining 85% evenly (**5.7%** each for
  the sword's fifteen).

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
- **Skill trees for the other thirteen weapons** — written when each pool is authored. Only the Sword's 22 skills
  carry a mastery level today, and only the sword's have trademark names; the other weapons roll no trademark
  until their pools exist.
- **Which townspeople are stations** — Lastline has one of each, standing at the doors of the four houses around
  the fountain: Gunnar the Smith, Odette the Weaver (loom), Rook the Tanner (tannery), beside Fenwick the Tool Dealer's
  shop. They are rows of the townsfolk list (`WorldTownsfolk`), not objects in the scene. `/station` still makes the
  nearest villager one for a play-test. Which stations other towns get is to be planned.
- **Which weapon each creature carries, how much mastery it has in it, and which skills it buys** — to be planned.

## What V4 replaced in V3

- The rolled weapon kit and its sizes and ultimate chance.
- Rarity: its drop weights, its lift on skill output, weapon attack and armour, its colours and labels.
- Creatures dropping what they wear, including the ring's own drop chance.
- The worn ring item that switches the ring on.
- V3's rule that **skills don't level**.
- The lore line that a dropped weapon's skills are "the memories that surfaced".
- The design in [Weapon Crafting & Economy](../active-skill-system-v3/weapon-crafting-economy.md) — rolled kits,
  Potential, Reforge and Essence — and the player page [Crafting & Recycling](../crafting-and-recycling.md).
- The per-copy armour roll (random defence, random stat count, random stats).

---

## Related

- [Implementation Plan](implementation-plan.md) — the six build slices, every file each touches, and the open build questions.
- [Weapon Names & Trademarks](weapon-names-and-trademarks.md) — the trademark design: the eighteen sword names, the roll, and how it is built.
- [Crafting Tables](crafting-tables.md) — the numbers only: bands per station, forge bills, fold and quench costs and odds, Salt, Ink values, mastery caps by grade.
- [Elements, Element Levels & Converters](elements-and-converters.md) — creatures attack and defend with an element at level 1–4 on Ragnarok's chart, negative values heal, and eight level-1 converters lend players an element for 5:00.
- [Armour Roster, Materials & Words](armour-roster.md) — five armour slots in three materials; the material decides what a fold buys, and every armour piece rolls one of its slot's words.
- [Timed Effects Survive Logout](timed-effects-survive-logout.md) — only death ends a timed effect; logging out and changing server pause it with the time it had left.
- [Biomes — Ground Tiles and Props](../../3-world/biomes.md) — the fourteen biomes every map is generated in (each its own ground tiles, props and prop counts), and the weather a biome always has: Swamp's standing water, Snow's snow blanket, Mist's fog.
- [Biome References](../../3-world/biome-references.md) — a real photo of each biome beside its test map, with a match rate and what is still missing.

- [Active Skill System — V3](../active-skill-system-v3/README.md) — the design as it runs today.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the ring's draft and soft cap, which
  V4 keeps and moves onto the character.
- [Weapon Roster — 14 Identities](../active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen weapon
  types masteries are tracked by.
- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — mastery as the endgame of naming a Kashira.
