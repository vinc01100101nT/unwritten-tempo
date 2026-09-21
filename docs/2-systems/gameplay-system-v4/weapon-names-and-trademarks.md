---
status: "BUILT 2026-09-21 — not play-tested. The shape, the main-numbers rule, the eighteen sword names and the numbers below were agreed with the user on 2026-09-21."
owner: vince
last-updated: 2026-09-21
tags:
  - systems-combat-design
---

# Weapon Names & Trademark Skills

A forged weapon rolls a **name**, and that name **empowers one of its weapon's trademark skills**. The name is
the piece's identity and never changes; the grade and the folds are its quality and ride alongside it.

**The format, decided 2026-09-21:** `+15 Hadori [C]` — folds, name, grade. Any weapon can be folded, quenched
and recycled exactly as now, whatever its name.

This replaces the earlier idea of a name-per-grade, which could not work: a name tied to the grade would be
destroyed by the very quench that improved the weapon, and nobody would ever climb past their favourite name.

## 1. What a name is

- **Rolled at the FORGE, once.** It is the only random thing in a weapon's whole life.
- **It never changes.** A quench moves the grade, a fold moves the number, a failed quench drops the grade —
  none of them touch the name. **This is the point:** you roll the weapon you want at Raw, and then spend the
  whole crafting ladder climbing that one weapon.
- **It belongs to one weapon type.** *Hadori* is only ever a sword; *Greenheart* is only ever a bow. So the name
  already says what the thing is, and the item's type does not need repeating in the title.
- **It names a skill, not a person.** The canon reserves real names for the Twelve — naming an Unwritten is the
  mastery endgame. A weapon name is a smith's word for a blade that does one thing especially well.

## 2. What "empowers" means

**A percentage added to that one skill's output, and nothing else.** No new mechanism: the game already has a
per-skill lift hook that Skill Mastery uses — `Character.SkillOutputLift(skillId)`, resolved once per burst and
**added** to the ring's term rather than multiplied (`SkillEffectApplier.OutputMult`). A weapon's trademark adds
one more term there.

That covers every shape of skill without a special case, because "output" already means the right thing for
each: a damage skill hits harder, a heal heals more, a shield absorbs more, and **Overclock's attack-speed buff
is bigger** — the user's own example.

### Main numbers only — decided 2026-09-21

**Skill Mastery and a trademark lift an effect's MAIN number and nothing else — every main number, no
exception.** Every other channel — chance, duration, number of hits, seconds added — is moved by the ring alone.
There is ONE list of which effects have a main number (`EffectScaling.Boostable`); the ring's **boosts …** row,
the Skill Composer's green **raised by Skill Mastery** line and the fight all read it, and a trademark rides the
same line.

**Every effect of the skill that has a main number is lifted — never just one.** The lift is resolved once per
cast and handed to every effect that cast applies (`SkillEffectApplier.ApplyBurst`), and each main-number effect
reads it. Blade Dance carries a strike and a damage buff: mastery and a Ranbu raise both. Overclock carries a +30%
Attack Speed buff and a take-double-damage self-debuff: mastery and a Hadori raise both, as the rule says.

| The main number | Takes the lift? |
| --- | --- |
| A strike's damage, an execute's damage | **Yes** |
| A heal's amount, a shield's pool | **Yes** |
| A sub-stat buff or debuff's size (Overclock's attack speed) | **Yes** |
| An outgoing-damage boost or cut | **Yes** |
| An ailment's damage per tick (Poison, Bleed, Burn) | **Yes** |
| Stacks added to another skill's pile (Flow, Last Stand, Encore) | **Yes** |

| Every other channel | Takes the lift? |
| --- | --- |
| **boosts HITS COUNT** — how many times it strikes | **No** — ring only |
| **boosts DURATION** / **SECONDS ADDED** — how long it lasts | **No** — ring only |
| **boosts CHANCE** — a proc chance | **No** — ring only |

**An EMPTY channel changes nothing about this.** Clearing every stat out of *boosts DAMAGE* means "the ring does
not scale this skill" — it does not mean "nothing scales it". An empty affinity list answers **×1**, and the
mastery and trademark lifts are ADDED to that, so a Blade Dance with no stats on its damage channel still gets
×1.30 from a maxed Skill Mastery and ×1.50 with an A-grade Ranbu beside it. The one thing to know is the
consequence: with the channel empty, mastery and the weapon become the *only* things that scale that skill, so
it lands the same for every build.

**So a Ranbu lifts Blade Dance's damage, not its number of hits.** That is deliberate, and worth keeping:

- **A hit is worth far more than its damage.** Every extra strike carries its own crit roll, its own on-hit
  passive triggers and its own charge. A trademark that granted hits would be worth several times one that
  granted damage, so the trademarks would not be interchangeable and everyone would chase the same three.
- **A count is a whole number.** +5% of five hits is 5.25, which rounds to five — nothing at all — while +15%
  of five is 5.75, which rounds to six, a 20% jump at one threshold. A Raw trademark would read as broken and a
  C one as a windfall, with no honest reading in between.

Duration is excluded for the same reason it is excluded from Skill Mastery: a longer buff is a different kind of
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

Deliberately below Skill Mastery's own +30% ceiling: a trademark should be a reason to want a weapon, never a
reason a skill is unusable without one. The two stack additively, so a mastered skill on its trademark weapon is
+50% at the top — a real build, reached by two different kinds of work.

## 3. Which skills are trademarks

**Every skill with a main number on any of its effects is a trademark skill, automatically, no exception.**
Decided 2026-09-21. The forge rolls the PUBLISHED ones of the weapon's type. For the sword that is **eighteen**:
fifteen ordinary skills and all three ultimates.

### Why a rule beats a hand-picked list

Including every eligible skill is better than choosing favourites, for a reason that has nothing to do with
variety:

- **It needs no arbitration and no bench.** "Eligible" is a fact about a skill's own data, not an opinion. There
  is nothing to argue about and no list that can be wrong.
- **It maintains itself.** Author a new sword skill with real output and it *has* a trademark, the same day,
  with nobody remembering to add it. A chosen list is a second list to keep in sync with the pool — exactly the
  kind of parallel copy this project bans everywhere else.
- **A weak trademark is not a dead one.** A weapon that lifts Composure's MP return is a poor prize for most
  players and the right one for a mana-hungry parry build. That is variety doing its job, and recycling means a
  roll nobody wants costs half of one forge.

### The one test

**Does any of its effects have a main number?** That is: **Damage · Execute · Heal · Shield · a Poison, Bleed or
Burn Status · SubStatBuff · SubStatDebuff · BoostOutgoingDamage · ReduceOutgoingDamage · AddStacks** — the same
list the lift reads (§2). A skill with none of them — only Parry, Brace, Taunt, Teleport, Dash, Knockback, Pull,
Cleanse, ImmuneToDisables, ExtendDuration, or a Stun, Freeze or Silence — has nothing to lift.

### The sword's eighteen

Read off the saved skill data on 2026-09-21, every effect decoded.

| Trademark | Mastery | What the lift moves | Name | Why that word |
| --- | --- | --- | --- | --- |
| **Lunge** | 1 | its strike — and every auto-lunge Lock Blades fires, since those ARE Lunge | **Tsuki** | the thrust itself |
| **Momentum** | 3 | its +3%-damage stack | **Hyōshi** | rhythm — the beat you build and keep |
| **Guard Stance** | 5 | its 50% damage reduction | **Kamae** | *kamae* IS the stance |
| **Lock Blades** | 7 | its +2 basic attack range per charge | **Tsubazeriai** | the kendo word for two blades locked together |
| **Composure** | 11 | its 2/sec MP return | **Heijō** | from *heijōshin*, presence of mind |
| **Offbeat** | 15 | its strike, and the 30% attack-speed cut it puts on them | **Kuzushi** | breaking their balance and rhythm |
| **Flow** | 17 | the Momentum it adds per basic attack | **Nagare** | "flow" |
| **Disarm** | 19 | its strike, and the 40% weakening of their next attack | **Kote** | the wrist strike that takes the weapon |
| **Onslaught** | 23 | its stack buff and its extra swings — measured: those swings are handed the lifted multiplier | **Renzoku** | "continuous", one attack into the next |
| **Opening** | 25 | the +200% it puts on your next attack after a parry | **Suki** | the gap in their guard — the kendo word for an opening |
| **Committed Strike** | 27 | its 200% Attack blow | **Ichigeki** | "a single blow", everything in it |
| **Pressure** | 33 | how far every hit cuts their HP regen | **Seme** | pressure — the kendo word for pressing an opponent |
| **Overclock** | 36 | its +30% Attack Speed, and its take-double-damage drawback | **Hadori** | the user's own example, kept |
| **Last Stand** | 40 | the 5 Brace charges it arms at low HP | **Haisui** | from *haisui no jin*, the stand with your back to the water |
| **Encore** | 65 | the 7 dance charges a kill adds | **Futatabi** | "once more" |
| **Deathblow** | 30 | its 600% blow | **Todome** | **ULT** — the finishing blow |
| **Retribution** | 45 | its damage | **Kaeshi** | **ULT** — the counter |
| **Blade Dance** | 60 | its damage and its stack buff | **Ranbu** | **ULT** — "wild dance" |

### The four that are not trademarks

| Skill | What it carries |
| --- | --- |
| **Parry** | the parry itself — no main number |
| **Brace** | the brace itself — no main number |
| **Challenge** | a taunt — no main number |
| **Unwavering** | disable immunity — no main number |

That leaves a permanent asymmetry: a player built around parrying and bracing has fewer weapons to chase than
a damage build. It is softened by **Opening and Composure, which are both parry-gated and both pass**, so the
parry routine does have weapons of its own.

## 4. Forging: random, as asked

The forge rolls the name, with these weights:

| Outcome | Each | Together |
| --- | --- | --- |
| One of the fifteen ordinary trademarks | **5.7%** | 85% |
| One of the three ultimate trademarks | **5%** | 15% |

Each ultimate is a fixed 5%, and the ordinary skills share the rest evenly, so adding an ordinary skill to the
pool never makes an ultimate rarer.

**Every weapon empowers something.** No blank outcome: a forge is then always a result rather than sometimes a
nothing, and the question a player asks is "which one did I get", never "did I get anything".

**Why random beats a stone.** A skill stone would make the name a purchase, and a purchased identity is not a
hunt — it also needs a whole new material, a drop table for it, and a second forge path, which is a second door
to the same room. The gamble already has its safety net, below.

## 5. What a roll actually costs — recycling is the pity mechanism

A forge is **8 Copper + 800 gold**; recycling a Raw +0 piece returns half, **4 Copper + 400 gold**. So one more
attempt costs a **net 4 Copper and 400 gold**.

| Chasing | Expected attempts | Net cost |
| --- | --- | --- |
| A specific ordinary trademark (5.7%) | ~18 | ~71 Copper, ~7,100 gold |
| *Any* ultimate (15%) | ~7 | ~28 Copper, ~2,800 gold |
| A specific ultimate (5%) | ~20 | ~80 Copper, ~8,000 gold |

Band-1 material and early gold, for a weapon carried to the end of the ladder. That is the number that makes
pure random defensible — and it only works because recycle exists, which is why the two should ship together.

## 6. How it is built

- **The name lives on the skill** — a **trademark name** field in the Skill Composer, shown only on a skill that
  can be a trademark (`SkillDef.trademarkName`). There is no separate table of names to keep in step with the skills.
- **Everything else is read from the skill as it is now** (`Trademarks`): whether it can be a trademark
  (`Trademarks.Qualifies` — it belongs to a weapon and `EffectScaling.HasMainNumber`), which of its effects the
  weapon raises (`EffectScaling.Boostable`, the same list mastery and the ring read), and what the weapon's card
  lists. Change a skill's effects and every weapon carrying it follows at once.
- **The forge rolls only PUBLISHED skills** of the weapon's type (`Trademarks.Rollable`): publishing is what lets a
  skill be rolled onto a weapon. The roll is server-side, through `CombatRng`, weighted by
  `CraftingMath.TrademarkWeight`. A weapon handed out any other way (a GM grant, a creature's gear) has no trademark.
- **The copy stores two things:** the trademark's **skill id**, and the **finished lift** the server works out from
  the grade (`CraftingMath.TrademarkLift`) — a player's machine never works out the percentage, it shows what it was
  sent. Both ride the text the grade and the Ink lines already travel in, so there is no new array in the save or on
  the wire. The save is **v14**, and both servers' build string is `unwritten-character-v14`.
- **In a fight** the lift is added to the per-cast practice lift (`Character.SkillOutputLift` = mastery + trademark,
  and the same on a creature in `Health`), which every main-number effect of the cast reads.
- **The title** is `+15 Hadori [C]` (`ItemTooltip.CopyTitle`); a weapon with no trademark keeps its old title.
- **The weapon's card** reads "Trademark Skill: Blade Dance" and one line per raised effect ("+20% damage",
  "+20% damage buff"). **The skill's card** shows each effect's base number, then "Trademark skill bonus damage:
  +20%" and "Skill mastery: +12%" (a passive: "Weapon mastery") on their own lines, each only when it is above zero.
- **The skill-data check** fails when a published weapon skill with a main number has no trademark name.

## 7. Open, for the user

1. **Armour.** It has no skills, so it has no trademarks. Either it keeps a plain name and only weapons play
   this game, or armour rolls something of its own — a separate decision, not made here.
