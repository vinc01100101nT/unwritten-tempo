---
status: built; the roster's re-tune is done by playing it
owner: vince
last-updated: 2026-10-31
tags:
  - systems-combat-design
---

# Unified Unit Stats — one stat block for players and mobs

## The principle

Every combat unit — the player, every mob, every future NPC — carries the **same stats and substats**, produced by
the **same formulas**. No unit has a stat another lacks, and no system asks "is this a player or a mob?" before
reading a number.

This is the data-shaped half of the caster/target work in [Active Skill System V3](README.md): once an effect can
land on anybody, the thing it lands on has to *have* the fields the effect reads. A damage-reduction buff needs a
damage cut on a mob, a mana-leech buff needs MP on a mob, and a shared stat block is what makes "no exceptions" true
rather than aspirational.

**A mob is strong for the reasons a player is strong — its level, its primaries, its gear, its ring — plus ONE
typed dial: its multiplier.** There is no tier and no mob-only growth rate. The multiplier (`MonsterDef.multiplier`,
1 or more, typed per creature) multiplies its health, its attack by the cube root (×30 hits about 3.1× as hard), and
everything its kill pays — experience, Temper Dust, Quench Oil, and the chance of its Salts, Inks and own material
([Mob Roster](../../3-world/mob-roster.md)). There is no global dial: retuning means editing a mob's level, its
primaries, its gear or its multiplier. Decided deliberately: emergent and honest over quick-to-dial.

**Where it lives.** `UnitStatMath.cs` owns every formula, constant and the stat-point economy; `Character` computes
nothing itself — every derived getter delegates there. The formulas were *moved*, not copied: a new stat is added
there, or the two sides drift. `UnitStatBlock` is a mob's live block, `UnitEquipment` the worn set for every unit
(the player's included), `UnitMitigation` the damage cut and Shield absorb every unit shares.

## The stat-point budget

A unit's primaries are an ALLOCATION, not free-typed numbers. `PointsEarnedByLevel(L)` = 48 at level 1 plus
`⌊L/5⌋+3` per level after — and a player's 48 are spent at character creation, on the stat hexagon: STR/INT,
AGI/LUK and VIT/DEX are opposite pairs that always add to 10, each stat 1–9, and every such spread costs exactly 48
(`UnitStatMath.IsCreationSpread`, pinned by the maths checks), so a new character starts with none left.
`PointsSpent` charges the same rising `StatCost` curve the player pays (`⌊V/10⌋ + 2` a point). A mob at level L may
carry exactly the stats a player who reached L could have bought — which is what keeps the two sides comparable.
`UnitStatMath.Allocate` spends a level's whole budget across a weighted spread; 0–4 points can remain unspendable
when the next point costs more than what is left.

**Authoring maths is replayed in a throwaway script against the real constants before it is trusted** — that is how
a budget guard that crushed untouched stats was caught before it shipped.

## The stat reference

Read off `UnitStatMath` and its callers on 2026-10-31. The code wins any disagreement.

### Primary stats — the only six you allocate

STR, AGI, VIT, INT, DEX, LUK. Each starts at 1, caps at 99, and has exactly **two** sources: the points you spend
and what your gear grants. **No skill, buff, debuff, passive, ailment or state may move a primary** — that rule is
the reason the sub-stat table below exists.

### What the primaries drive

Every row also takes gear, its ring node, and a timed change of that sub-stat; the ring's percentage scales the flat
sum unless the row says otherwise.

| Sub-stat | Scaled by |
| --- | --- |
| Melee attack | base + 1/level, STR ×1 + (STR÷10)², DEX÷5, LUK÷5. Every weapon that is not ranged, and bare hands |
| Ranged attack | the same with STR and DEX swapped: DEX ×1 + (DEX÷10)², STR÷5, LUK÷5, and the **same** Atk ring node. Used while holding a Bow or a Sniper, or a Shuriken with DEX above STR (`WeaponTypes.SwingsRanged`). A Sniper's own power is magic attack (§2 of [V4](../gameplay-system-v4/README.md)) |
| Magic attack | base + 1/level, INT ×1 + (INT÷5)² |
| Soft defence (flat) | base + 1/level, VIT÷2 + the larger of VIT×0.3 and VIT²÷150 − 1 |
| Soft magic defence (flat) | base + 1/level, INT ×1 |
| Hard defence / hard magic defence (a percentage) | the Def ring node (+1 point a rank), what is worn (armour, Ink, the Bulwark word), timed Defence changes; never below 0, and a blow uses at most 90% |
| Max health | the level's health curve × (1 + VIT×1%), plus gear; a creature's then × its multiplier |
| Max mana | the level curve × (1 + INT×1%), plus gear |
| Accuracy | level + DEX + worn accuracy |
| Evasion | level + AGI + worn evasion (Ink, leather); with more than two attackers on it, 10% off for each past the second, and 12 or more always hit |
| Attack speed | AGI 0.4%/pt + DEX at a quarter of that, gear, ring, × the attack-speed buff; the swing is timed by Ragnarok's ASPD, capped at 190 |
| Cast speed | `1 − DEX/150` (instant at 150 DEX), divided by the CastSpeed ring node and a timed CastSpeed change; a fixed cast time is never shortened |
| Crit chance | 1% + LUK 0.3%/pt, gear, ring; the defender's LUK takes 0.2%/pt off it, and the result is held inside 0–95% |
| Crit damage | ×2 base + LUK 0.2%/pt + LUK's jump (⌊LUK/10⌋² + 3×⌊LUK/10⌋ percentage points), gear, the same Critical node; never past ×3 |
| Perfect dodge | the defender's LUK, 0.1%/pt of ordinary attacks dodged outright, a crit included |
| HP regen | **Ragnarok's: VIT/5 + MaxHP/200 (at least 1) every 6 s**, plus a timed HPRegen change |
| Mana regen | **Ragnarok's: 1 + INT/6 + MaxMP/100 every 8 s, plus (INT−120)/2 + 4 from 120 INT**, plus a timed MPRegen change |
| Minimum damage | physical: 55% of max, closed toward max by DEX at 1.2%/pt; magic: max less ((INT÷5)² − (INT÷7)²). A crit ignores the spread |

Both regen lines are Ragnarok pre-renewal's formula (rAthena `status_calc_regen`), with whole-number division.

### Misc — nothing here reads a primary

| Stat | Where it comes from |
| --- | --- |
| Movement speed | `CharacterSpec`'s shared pace — the run gear is the base, the walk gear 0.4 of it, the sprint gear 2 times it — × timed movement-speed effects, held inside 30%–250%. No gear gives movement speed. A creature also multiplies by its own movement speed, 0 to 2 (`MonsterDef.moveSpeed`) |
| Attack reach | `CharacterSpec.unarmedReach` bare-handed, or the held weapon's `attackRange`; blended by the pair's body sizes |
| Base swing interval | `UnitOffense.SwingSeconds(weapon, weight)` for every unit — 0.8 s bare-handed plus 0.005 s per point of weight a hand carries (a pair weapon's two hands carry half its weight each) — before attack speed |
| Mana leech | a timed sub-stat change only |
| Life steal | worn words + a timed LifeSteal change; at most 25% of a blow |
| Damage reflect | worn words + a timed Reflect change; at most 50% of a blow |
| Damage dealt | **not a character stat** — a per-hit multiplier applied while the hit is built (`UnitOffense.Dealt`) |
| Damage reduction | **not a character stat** — the Guard damage cut, a percentage of the incoming hit after defence, then the Shield absorb soaks a flat pool of what is left (`UnitMitigation`). Both are hit-only: damage over time bypasses them |
| Max stamina | flat 100 on `Character`, player-only |
| Block chance | the block-chance sub-stat, which only a skill grants |
| Extra bounce jumps | the extra-bounce-jumps sub-stat, which only a skill grants (`SubStat.ExtraBounceJumps`): whole jumps added to every bounce chain the unit starts — a thrown shuriken or a bouncing skill — never making one bounce that does not |
| Element (what the unit is hit as) | a person: Neutral level 1, or an Armor Converter's element at level 1 while one runs; a mob: its authored element and level (`MonsterDef.element` / `elementLevel`), or a converter's |

**`CritShieldPerLuk` is symmetric.** It reads the *target's* LUK to lower the attacker's crit chance, for a mob's
attack exactly as for a player's — a real balance consequence of the shared path, not a refactor.

## Regeneration is one ticker for everyone

Both pools refill in `StatusController` — the one per-unit ticker — each carrying its sub-1-point remainder so a
tiny rate is paid exactly rather than rounded up. Rate = the unit's own natural rate (doubled while its weapon is put
away and it has neither dealt nor taken a blow for 5 s — see [Combat and Peace Mode](../combat-and-peace-mode.md)) +
the timed sub-stat change, which peace never doubles; **a rate at or below zero pays nothing**, so a regen debuff can
stop healing but never drain. Moving never stops either pool.

## Every HP gain announces itself the same way

`CombatAuthority.NoteHPAdded` is the ONE door: it draws the green digits, fires the `OnHPAdded` cue and raises the
`OnHPAdded` event, which carries only WHO and HOW MUCH, so any healing source added later is covered for free. A cue
names the moment, never the source. **One deliberate exception:** `Character.LeechHeal` adds HP silently, so
multi-hit life steal does not spam numbers.

## Mobs wear equipment and rings

A mob carries the same worn set the player does (`UnitEquipment`), aggregated through the same `EquipBonuses` —
folds and Inks included. A mob is dangerous because it is a high level in good gear — the same sentence you would
write about a player.

**The ring is part of the unit.** There is no ring item and no ring slot, for a creature or for a player. A creature
authors its ring's invested ranks (`ringRankStats` / `ringRankValues`); they always count, and resolve through
`PassiveRanks` → `SkillRing.Percent`, the player's own soft-cap curve (10 ranks = +16%, 20 = +26.7%, 40 = +40%,
asymptotic to +80%). No mob-side copy of the maths, and no drafting: a player's ring comes from the level-up draft,
a mob's is authored.

**Worn gear is serialized on the component, not the dictionary.** `UnitEquipment.startingGear` is the serialized
half; the live dictionary is runtime-only, equipped **lazily on first access**, not in `Awake`, so the wake order of
`UnitEquipment` and `UnitStatBlock` cannot matter.

### What state a mob's gear is in

A creature wears exactly the pieces authored on its catalog entry and nothing else; an empty slot stays empty —
unless it `rollsGearEachLife`, when every life it picks a weapon type at random and one armour material for its
body, shoes and garment instead. **Every piece comes to life at the grade its LEVEL gives** (`CreatureLoot.WornGrade`:
levels 1–99 in seven even stretches, Raw to A — the gear a player of that level is expected to have), never typed.
Its folds are rolled each life by its kind (normal from 0, elite from 4, shadow from 7, each fold rarer than the one
before); its sheet shows the fewest. Its Inks are written by its grade, and which stats they land on, a weapon's
trademark and an armour piece's word are re-rolled every life. The server decides it; a watching machine is told
what each creature wears.

### Mobs never lose what they wear

**A creature keeps what it wears, and 1 kill in 10 drops a copy of one piece.** Nothing is taken off the creature:
on 10% of kills a new copy of one random piece it wears falls — the same grade, folds, Inks, trademark or word,
base roll and card slots, with its own id and no cards (`EquipmentInstance.CloneAsNewCopy`,
[V4 §1](../gameplay-system-v4/README.md)). A kill also pays materials and consumables (no death pays Unsigned — see
[Saved Characters](../../3-world/saved-characters.md)); each kill also has a chance of the creature's own find, a
food or an element converter ([Town Supplies and Hunting Finds](../town-supplies-and-hunting-finds.md)). Everything
lands on the ground where it fell, held for the killer (see
[Networking — Implementation Notes](../networking/implementation-notes.md)).

**What a kill pays is not authored on the creature.** `CreatureKinds.KillExperience` works it out from the level in
the creature's file and its multiplier; no per-creature pay number is saved.

### There is no such thing as mob-only gear

**One item catalog, shared.** Anything a mob wears is a real equipment Item the player can wear too — same slot,
same bonuses, same 3D model, same drop. No mob-exclusive equipment tier, no stat-bearing "natural weapon" that only
exists to give a mob numbers. The code enforces the line: `ItemType` is DERIVED from `slot` + `category`, so an item
is Weapon/Armor *because* it has an equip slot; a stat-carrying item with no slot would be data the equip pipeline
cannot read.

- **Equipment** — has an equip slot and bonuses; a weapon also has a model. Worn by anyone — see the
  [Armour Roster](../gameplay-system-v4/armour-roster.md).
- **Materials** — `ItemCategory.Material`: an icon, a name, a crafting input, **no stats and no model**.

**A mob with no equipment is valid.** Its stats come from level + primaries; `EquipBonuses` aggregates to zeros and
every formula still resolves.

## What stays on `MonsterDef`

Only behaviour and inputs: identity, kind, size, flight, movement speed, aggression, biome, level, the six
primaries, the multiplier, worn gear (or `rollsGearEachLife`), ring ranks, weapon mastery, aggro and wander radii,
`preferredRange`, `fleeHpFraction`, element and its level, on-hit ailment, its own material, loot, art.

**No sub-stat is typed onto a unit, with one exception: a creature's own movement speed** (decided 2026-10-14) — a
number from 0 to 2 that multiplies the shared pace; 1 is a person's pace and 0 never moves. The movement pace and
bare-handed reach are one value each on `CharacterSpec`, and the swing interval is worked out from the held weapon's
weight, read by players and creatures alike. The combat numbers on a creature's body (`Health`, `MonsterAI3D`) are
written only by `UnitStatBlock` and are never saved.

## Load-bearing decisions — don't undo these by accident

- **A creature never gains a level in a fight.** Its level is what it spawned at (or what `/summon` re-statted it
  to), and its numbers are re-derived from that level, never scaled.
- **Tune the CAUSE, never the result.** Anything that changes a mob's numbers — `/summon` with a level included —
  sets level + the six primaries and re-derives. A derived stat written directly is silently overwritten by the
  block on its next derive.
- **The mob builder does NOT stamp derived stats.** `UnitStatBlock` is the single writer of a mob's health,
  defences, evasion, attack, accuracy and damage spread; `MonsterArchetype` carries the stat block, not a snapshot of
  its outputs, so there is no second copy to drift.
- **A respawn is a fresh creature.** `MobRespawn.Reactivate` — the one choke every respawn goes through — seats fresh
  gear, re-derives and refills mana before `Health.Revive()`.
- **The training dummy opts OUT of the model.** `TrainingDummy.MakeOne` (the `/dummy` GM command) strips the
  `UnitStatBlock` after building, because the block would re-derive over its hand-set health and attack and turn the
  punching bag into an ordinary mob. It is a measuring instrument, not a unit.
- **The equip-hover preview never mutates the worn set.** It is worked out on the server from a pure aggregate of
  the hypothetical set, and the server sends back the finished "If equipped" lines (`EquipPreview`).
- **A mob's skills come from one place only: the levels it has bought with mastery points**, in the same store a
  player's live in (`Health.Mastery`, a `MasteryProgress`), decided by the user 2026-10-04. It may use a bought skill
  by the player's own rule — never what an item runs, and a weapon's skill only with that weapon in its hands — at
  the level bought. Its weapon mastery opens nothing on its own, and nothing is drawn at random. **Nothing spends a
  mob's points yet, so no mob casts a weapon skill** (every elite and shadow holds the summon skill from birth —
  [Mob Roster](../../3-world/mob-roster.md)). A mob that has bought skills casts an active or the ultimate in place of
  a swing when one is off cooldown, affordable and in reach (the summon first, then the ultimate, otherwise one at
  random), and its passives fire on their own triggers. It pays MP and waits out cooldowns exactly as a player does,
  and its skills land on people, never on other mobs. Every cooldown starts ready on a new life.

## The Mob Table is the authoring surface

`Tools ▸ unwritten ▸ Catalogs ▸ Mob Table` edits every creature inline — level and the six primaries with a live
`spent/budget` points column (green on budget, red over, amber when a real chunk is unspent), and read-only derived
readouts computed live through `UnitStatMath`, so you edit the cause and see the effect. Its `▸` expander holds
per-mob equipment (an item is rejected if its slot does not match), the ring's ranks with the % each is worth, and on
a shadow its appearance.

**IMGUI gotcha:** the expander toggle is queued in `_pendingToggle` and applied *after* the draw loop. Flipping it
mid-draw changes the layout-group count between the Layout and Repaint passes of one frame → "Mismatched
LayoutGroup" spam.

## Still open

- **Roster re-tuning by playing it** — the numbers are consistent, not necessarily *fun* yet.

---

## Related

- [Active Skill System — V3](README.md) — why one stat model is the data-shaped half of "one applier, no special cases".
- [Pre-Renewal RO Port — Phase Plan & Formulas](../pre-renewal-ro-port.md) — where these formulas came from.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the percentage curve mobs share with the player.
- [State System — Buffs, Debuffs & Combo Marks](state-system-buffs-debuffs-combo-marks.md) — the sub-stat piles the table above is read through.
- [Mob Roster](../../3-world/mob-roster.md) — the creature kinds, the level ladder and the multiplier.
- [Creature Gear](../../3-world/creature-gear.md) — what each creature wears.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — the per-connection fighter the server builds from this block.
