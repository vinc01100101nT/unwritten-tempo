---
status: "BUILT 2026-09-21 — not play-tested. The shape, the main-numbers rule and the numbers below were agreed with the user on 2026-09-21; since 2026-09-26 a weapon is named by its trademark skill's own form name; weapons are dropped, never forged, and the trademark roll is a class first (an ultimate's 1%). Gear has no rarity."
owner: vince
last-updated: 2026-10-31
tags:
  - systems-combat-design
---

# Weapon Names & Trademark Skills

A weapon rolls one of its weapon's **trademark skills** as it comes into the world, is **named after that skill**, and **empowers it**.
Every skill is a numbered form of its weapon's style (see [Story & Setting](../../3-world/story-setting-canon.md)), so
a sword that rolled Petal Storm is called Petal Storm. The name is the piece's identity and never changes; the grade
and the folds are its quality and ride alongside it.

**The format:** `+7 Petal Storm [C]` — folds, name, grade. Any weapon can be folded, quenched
and recycled exactly as now, whatever its name.

This replaces the earlier idea of a name-per-grade, which could not work: a name tied to the grade would be
destroyed by the very quench that improved the weapon, and nobody would ever climb past their favourite name.

## 1. What a name is

- **Rolled ONCE, as the weapon comes into the world**, together with its base roll; nothing rolls either again. A
  creature's weapon comes into the world with each of its lives, and a weapon it drops is an exact copy of the one it
  wore — trademark, grade, folds and all.
- **It never changes.** A quench moves the grade, a fold moves the number, a failed fold or quench takes some back —
  none of them touch the name. **This is the point:** you find the weapon you want, and then spend the rest of
  the crafting ladder climbing that one weapon.
- **It belongs to one weapon type.** *Petal Storm* is a form of Blossom Wind, the sword's style, so it is only ever
  a sword. The name already says what the thing is, and the item's type does not need repeating in the title.
- **It names a form, not a person.** The canon reserves real names for the Fourteen — naming a Kashira is the
  mastery endgame. A weapon carries the one form of its style it does especially well.

## 2. What "empowers" means

**A percentage added to that one skill's output, and nothing else.** No new mechanism: the game already has a
per-skill lift hook that the skill's level uses — `Character.SkillOutputLift(skillId)`, resolved once per burst and
**added** to the ring's term rather than multiplied (`SkillEffectApplier.OutputMult`). A weapon's trademark adds
one more term there.

That covers every shape of skill without a special case, because "output" already means the right thing for
each: a damage skill hits harder, a heal heals more, a shield absorbs more, and **Squall's attack-speed buff
is bigger** — the user's own example.

### Main numbers only — decided 2026-09-21

**A skill's level and a trademark lift an effect's MAIN number and nothing else — every main number, no
exception.** Every other channel — chance, duration, number of hits, seconds added — is moved by the ring alone.
There is ONE list of which effects have a main number (`EffectScaling.Boostable`); the ring's **boosts …** row,
the Skill Composer's green **raised by the skill's level** line and the fight all read it, and a trademark rides the
same line.

**Every effect of the skill that has a main number is lifted — never just one.** The lift is resolved once per
cast and handed to every effect that cast applies (`SkillEffectApplier.ApplyBurst`), and each main-number effect
reads it. Thousand Petals carries a strike and a damage buff: its level and a Thousand Petals sword raise both. Squall carries a +30%
Attack Speed buff and a take-double-damage self-debuff: its level and a Squall sword raise both, as the rule says.

| The main number | Takes the lift? |
| --- | --- |
| A strike's damage, an execute's damage | **Yes** |
| A heal's amount, a shield's pool | **Yes** |
| A sub-stat buff or debuff's size (Squall's attack speed) | **Yes** |
| An outgoing-damage boost or cut | **Yes** |
| An ailment's damage per tick (Poison, Bleed, Burn) | **Yes** |
| Stacks added to another skill's pile (Tailwind, Bare Branch, Second Bloom) | **Yes** |

| Every other channel | Takes the lift? |
| --- | --- |
| **boosts HITS COUNT** — how many times it strikes | **No** — ring only |
| **boosts DURATION** / **SECONDS ADDED** — how long it lasts | **No** — ring only |
| **boosts CHANCE** — a proc chance | **No** — ring only |

**An EMPTY channel changes nothing about this.** Clearing every stat out of *boosts DAMAGE* means "the ring does
not scale this skill" — it does not mean "nothing scales it". An empty affinity list answers **×1**, and the
level and trademark lifts are ADDED to that, so a Thousand Petals with no stats on its damage channel still gets
×1.45 at its max level 10 and ×1.65 with an A-grade Thousand Petals sword beside it. The one thing to know is the
consequence: with the channel empty, the skill's level and the weapon become the *only* things that scale that skill, so
it lands the same for every build.

**So a Thousand Petals sword lifts Thousand Petals' damage, not its number of hits.** That is deliberate, and worth keeping:

- **A hit is worth far more than its damage.** Every extra strike carries its own crit roll, its own on-hit
  passive triggers and its own charge. A trademark that granted hits would be worth several times one that
  granted damage, so the trademarks would not be interchangeable and everyone would chase the same three.
- **A count is a whole number.** +5% of five hits is 5.25, which rounds to five — nothing at all — while +15%
  of five is 5.75, which rounds to six, a 20% jump at one threshold. A Raw trademark would read as broken and a
  C one as a windfall, with no honest reading in between.

Duration is excluded for the same reason it is excluded from a skill's level: a longer buff is a different kind of
power from a bigger one, and the design pays for one of them only.

**It scales with the weapon's grade**, so the name and the grade pull in the same direction:

| Grade | Trademark lift |
| --- | --- |
| Raw | +5% |
| F | +7% |
| E | +10% |
| D | +12% |
| C | +15% |
| B | +18% |
| A | **+20%** |

Deliberately below a 10-level skill's own +45% at its max level: a trademark should be a reason to want a weapon,
never a reason a skill is unusable without one. The two stack additively, so a 10-level skill at level 10 on its
A-grade trademark weapon is +65% at the top — a real build, reached by two different kinds of work.

**Efficiency — a critical hit for a whole cast (agreed with the user on 2026-09-30).** Each time a **trademark
skill** (§3 — any skill with a main number) fires — pressed or on its own trigger, active or passive, ultimate or not,
whatever weapon the caster holds — the server rolls once: a flat **5%** chance, the same for every player, creature
and skill, scaled by nothing. An efficient cast adds a further **+20 points** to that same lift for every main number
it runs, every tick and repeat of that one cast included, so the top build above reads +85% on an efficient cast. It
also makes the full style call ("Blossom Wind style, 21st form, Thousand Petals!!"). A skill that is not a trademark
skill has no main number to raise and never rolls.

## 3. Which skills are trademarks

**Every skill with a main number on any of its effects is a trademark skill, automatically, no exception.**
A new weapon rolls among the PUBLISHED ones of its type. For the sword that is **seventeen**:
fourteen actives and passives and all three ultimates, in two classes the roll treats apart (§4).

### Why a rule beats a hand-picked list

Including every eligible skill is better than choosing favourites, for a reason that has nothing to do with
variety:

- **It needs no arbitration and no bench.** "Eligible" is a fact about a skill's own data, not an opinion. There
  is nothing to argue about and no list that can be wrong.
- **It maintains itself.** Author a new sword skill with real output and it *has* a trademark, the same day,
  with nobody remembering to add it. A chosen list is a second list to keep in sync with the pool — exactly the
  kind of parallel copy this project bans everywhere else.
- **A weak trademark is not a dead one.** A weapon that lifts Eye of the Storm's MP return is a poor prize for most
  players and the right one for a mana-hungry parry build. That is variety doing its job.

### The one test

**Does any of its effects have a main number?** That is: **Damage · Execute · Heal · Shield · a Poison, Bleed or
Burn Status · SubStatBuff · SubStatDebuff · BoostOutgoingDamage · ReduceOutgoingDamage · AddStacks** — the same
list the lift reads (§2). A skill with none of them — only Parry, Brace, Taunt, Teleport, Dash, Knockback, Pull,
Cleanse, ImmuneToDisables, ExtendDuration, or a Stun, Freeze or Silence — has nothing to lift.

### The sword's seventeen

Read off the saved skill data, every effect decoded.

| Trademark — and the weapon's name | Mastery | What the lift moves |
| --- | --- | --- |
| **Piercing Gust** | 1 | its strike — and every auto-lunge Crossed Branches fires, since those ARE Piercing Gust |
| **Rising Breeze** | 3 | its +3%-damage stack |
| **Still Air** | 5 | its 50% damage reduction |
| **Eye of the Storm** | 11 | its 2/sec MP return |
| **Scattered Petals** | 15 | its strike, and the 30% attack-speed cut it puts on them |
| **Tailwind** | 17 | the Rising Breeze it adds per basic attack |
| **Stripped Bough** | 19 | its strike, and the 40% weakening of their next attack |
| **Petal Storm** | 23 | its stack buff and its extra swings — measured: those swings are handed the lifted multiplier |
| **Parting Clouds** | 25 | the +200% it puts on your next attack after a parry |
| **Single Petal** | 27 | its 200% Attack blow |
| **Headwind** | 33 | how far every hit cuts their HP regen |
| **Squall** | 36 | its +30% Attack Speed, and its take-double-damage drawback |
| **Bare Branch** | 40 | the 5 Held Breath charges it arms at low HP |
| **Second Bloom** | 65 | the 7 dance charges a kill adds |
| **Last Petal** (ultimate) | 30 | its 600% blow |
| **Returning Wind** (ultimate) | 45 | its damage |
| **Thousand Petals** (ultimate) | 60 | its damage and its stack buff |

### The four that are not trademarks

| Skill | What it carries |
| --- | --- |
| **Bending Reed** | the parry itself — no main number |
| **Held Breath** | the brace itself — no main number |
| **Howling Wind** | a taunt — no main number |
| **Deep Roots** | disable immunity — no main number |

That leaves a permanent asymmetry: a player built around parrying and bracing has fewer weapons to chase than
a damage build. It is softened by **Parting Clouds and Eye of the Storm, which are both parry-gated and both pass**, so the
parry routine does have weapons of its own.

## 4. The roll: random — a class first, then a name

**The trademark is rolled in two steps:** first a **class**, then **one trademark of that class**, every one equally
likely. Gear has no rarity ([V4](README.md) §1), so nothing else about the weapon bends the roll.

| Class | Share of all new weapons of a type | Sword trademarks | Each, on a sword |
| --- | --- | --- | --- |
| An active's or a passive's | **99%** | 15 | **6.6%** |
| An ultimate's | **1%** | 3 | **0.33%** |

- **A weapon type with trademarks of only one class** gives that class every weapon.
- **A skill carries no trademark rarity.** Adding a skill to a class makes the others of that class rarer and leaves
  the other class's share alone.
- **A weapon type with no trademark skills rolls none.** A new character's starting Sword is the one sword made with
  none, so making a character again can never reroll it.

**Every dropped weapon empowers something.** No blank outcome: the question a player asks is "which one did I get",
never "did I get anything".

**Why random beats a stone.** A skill stone would make the name a purchase, and a purchased identity is not a
hunt — it also needs a whole new material, a drop table for it, and a second door to the same room.

## 5. How a wanted trademark is found — by hunting

A weapon comes only from a kill: every kill has a **10%** chance to drop an exact copy of one random piece of what the
creature wears, and a creature's weapon rolls its trademark afresh every life, so a wanted trademark is hunted from
creatures that carry that weapon type. Of the swords that drop:

| Chasing | Chance on each dropped sword | Dropped swords it takes, on average |
| --- | --- | --- |
| A specific active's or passive's trademark | 6.6% | ~15 |
| *Any* ultimate's | 1% | ~100 |
| A specific ultimate's | 0.33% | ~300 |

A dropped piece nobody wants sells to a shop for 5u; recycling an untouched Raw piece returns 1 Temper Dust.

## 6. How it is built

- **The name IS the skill's name** (`SkillDef.displayName`) — there is no separate weapon-name field and no table of
  names to keep in step with the skills. Rename a skill and every weapon carrying it is renamed with it.
- **Everything else is read from the skill as it is now** (`Trademarks`): whether it can be a trademark
  (`Trademarks.Qualifies` — it belongs to a weapon and `EffectScaling.HasMainNumber`), which of its effects the
  weapon raises (`EffectScaling.Boostable`, the same list the skill's level and the ring read), and what the weapon's card
  lists. Change a skill's effects and every weapon carrying it follows at once.
- **The roll takes only PUBLISHED skills** of the weapon's type (`Trademarks.Rollable`): publishing is what lets a
  skill be rolled onto a weapon. The roll is server-side (`Trademarks.Roll`): two `CombatRng` draws, the class by
  `CraftingMath.TrademarkIsUltimate` (`CraftingMath.UltimateTrademarkPercent` = 1), then a skill of it evenly; the
  chance of each is `CraftingMath.TrademarkChance`, pinned in `tools/game-math-tests/Checks.cs`, and
  `Trademarks.ChanceOf` hands it to the wiki's trademark table. Each weapon type's candidates are gathered once and
  forgotten whenever the skill catalog reloads (`Trademarks.Forget`). Every new weapon comes through that same roll
  (`EquipmentInstance.CreateFresh`) — a GM's `/weapon`, a creature's gear at any grade (`CreateWorn`); a dropped
  piece is a copy of the creature's (`CloneAsNewCopy`) and rolls nothing. The one weapon made without it is a new character's (`EquipmentInstance.CreateStarter`).
- **The copy stores two things:** the trademark's **skill id**, and the **finished lift** the server works out from
  the grade (`CraftingMath.TrademarkLift`) — a player's machine never works out the percentage, it shows what it was
  sent. Both ride the text the grade and the Ink lines already travel in, so there is no new array in the save or on
  the wire (`EquipmentInstance.EncodeRoll`). The save today is **v21**, and both servers' build string is
  `unwritten-character-v30`.
- **In a fight** the lift is added to the per-cast skill-level lift (`Character.SkillOutputLift` = skill level + trademark,
  and the same on a creature in `Health`), which every main-number effect of the cast reads. An efficient cast adds
  `SkillEfficiency.OutputBonus` on top (`SkillEfficiency.OutputLift`); the roll — for any skill `Trademarks.Qualifies`
  — happens once at the cast's release in `SkillRunner.PlayCycles` and is handed to every burst of that cast, and
  `SkillShout.Words` picks the call from it.
- **The title** is `+7 Petal Storm [C]` (`ItemTooltip.CopyTitle`); a weapon with no trademark keeps its old title.
  Gear has no rarity, so it is written in the ordinary (Common) text colour everywhere it shows.
- **The weapon's card** reads "Trademark Skill: Thousand Petals" and one line per raised effect, each naming the skill
  it is for ("+20% to Thousand Petals' damage", "+20% to Thousand Petals' damage buff"); the wiki's trademark table,
  which names the skill on the row, keeps the short form ("+20% damage"). **The skill's card** shows each effect's base number, then "Trademark skill bonus damage:
  +20%" on its own line, only when the weapon in hand empowers that skill.
- **The skill-data check** fails when a skill file still carries the retired separate weapon name (`trademarkName`).

## 7. Armour

Armour has no skills, so it has no trademark; it rolls a **word** of its own instead, on the same terms — once, as
the piece comes into the world, part of its name — see [Armour Roster, Materials & Words](armour-roster.md).
