# Handoff — creature economy, loot-owner points, and what a kill pays (all built, nothing played)

**Task:** rebuild how hunting pays (no gold drops; each creature's own materials; who owns a kill; saved-character
reward; a bigger purse), then make a creature's reward follow how hard it is ("toughness"), then gear every creature.
Session of 2026-10-13.

## Status

**IN PROGRESS. Everything agreed so far is BUILT (2026-10-13 evening); nothing has been played.**

The user's ruling: there is no measured difficulty number. Level sets a creature's body; the grade and folds of its
gear are the ONLY difficulty dial, chosen per creature and not tied to level; grade and folds are two separate
variables. Pay = what level and kind give × grade multiplier (25% more per grade above Raw) × fold multiplier
(1 + 5% per fold), on experience, material prices and saved-character gold. The 25% and the 5% are Claude's picks,
accepted with "okay go with that". Design page: `docs/3-world/what-a-kill-pays.md`.

Everything below compiles and passes the offline checks. **Nothing has been played. Don't trust prior "it works"
claims** — "works" here only ever meant "compiles, pinned numbers hold, Unity reports nothing stale".

## What's done

### 1. Creature materials (built, agreed)
- 126 material items in `unity/Assets/3_Data/Resources/Items/` (category Etc, weight 10, `materialKind: 6`,
  `materialStep` = rarity 1–5). One ordinary per normal creature and elite (84), a rare one on 24 normals and all 18
  elites. Names are drafts.
- `CreatureMaterials.cs` — chances, prices, an elite's piles. `MonsterDef.material` / `rareMaterial`.
- `LootDropper.GiveLoot` rolls them (`RollOwnMaterial`); a rebel rolls them again at the creature's own chances; an
  elite's ordinary material falls as 3–5 piles of 4–7 (`Rewards.PayLoot` gained `pileItems`/`pileCounts`); nothing an
  elite drops is auto-looted (chances passed as null).
- `CreatureDrops.cs` lists them for `/mobinfo` and the wiki. `Rarity.cs`, `MaterialCatalog.cs`, `GmCommands.cs`,
  `Item.cs` (new `MaterialKind.Creature`).
- Design: `docs/3-world/creature-materials.md`.

### 2. Loot-owner pointing system (built, agreed)
- `KillLedger.cs` (new): `KillPointsMath`, `KillLedger` (on `Health.Ledger`), `HurtBy` (on `Character.Hurts`).
- Points: damage 60, **time the creature spent chasing you 15** (from the moment it gives chase — the user rejected
  any "only after first damage" condition), healing others 15, first hit 5, last hit 5. Top scorer owns the loot;
  experience is split by points (`Rewards.PayKill`, `Rewards.WhoEarned`).
- Hooks: `Health.SubtractHealth` / `Regained` / `Revive` / `Die`; `CombatAuthority.NoteHurtBy`; `MonsterAI3D` (chase
  time, and the pay site); `SkillEffectApplier` (heal credit).
- Design: `docs/2-systems/loot-owner-pointing.md`.

### 3. Saved-character reward (built, agreed)
- Gold worth 4 / 8 / 12 kills of the creature plus 3 / 6 / 9 of its ordinary material (into the bag, overflow falls).
  `SavedCharacterRules.RescueKills` / `RescueGold` / `RescueMaterials`, `SavedCharacters.cs`.
- `MonsterDef.goldMin` / `goldMax` and the ×20/×40/×60 tiers are gone (code, `MobBiomeImporter`, `MobTableWindow`,
  all 98 creature files).

### 4. Purse is 64-bit (built)
- `Character.Gold`, `BagWire.gold`, `SaveData.gold`, `SaveData.SoldItem.paid`, `Wallet`, `ShopMath.SalePay` /
  `PaidTogether`, `BuybackList`, `NetPlayer` (casts removed), `PlaySession`, `KeeperPanel`, `StatusBox`,
  `ProgressionWindows`, `QuestMath.CanPay`. The character server and database needed no change (progress is one
  opaque document). NOT verified against the real database.

### 5. What a kill pays (built, agreed)
- `CreaturePay.cs` (the rule, plain arithmetic), fed to `CreatureKinds.KillExperience`, `CreatureMaterials.SellValue`
  and `SavedCharacterRules.RescueGold`, each taking the creature's `gearQuality` and `gearFolds`.
- The measured toughness is gone: its class, `MonsterDef.toughnessPercent` (removed from all 98 creature files), the
  stat-body code in `MonsterDefStats`, the server-start log and the build guard.
- `Editor/CreatureMaterialPriceBake.cs` writes each material's saved price (`BakeAll(true)`; no menu item).
  `tools/check-creature-materials.py` (+ prover) guards prices, fold caps and a returning toughness field.
- Every normal creature's and elite's grade, folds and Ink are written from the rule in
  `docs/3-world/creature-gear.md`: grade by its rare material's tier, folds = the grade below's cap plus 0 to 5 for
  how few are alive (normals) or how few maps (elites). 122 material prices rewritten. Shadows stay A +30.

### 6. Gear and stats on every creature (built on the user's instruction)
- All 84 normals and elites now have `startingGear` = a weapon + head, body, shoes, garment, and their six primaries
  were RE-SPENT for their weapon's build. Table: `docs/3-world/creature-gear.md`.
- **Every weapon now has an elite and a shadow (2026-10-13 late, uncommitted):** Toadmaw holds the Shuriken, and each of
  the 13 unarmed shadows was handed the weapon the roster names for it, with its stats re-spent for that build. Their
  grade, folds, Ink and ring ranks are unchanged. Every shadow now wears a full set of one armour material, chosen by
  what its weapon's mastery raises (they wore a cap, a vest and a cloak and no shoes).
- **The re-spent stats only reach a spawned creature through `MobSpawner.Spawn`**, which now copies level and
  stat points from the creature file on the server (added 2026-10-13 19:31). Measured before that: 84 of 98 baked
  mob prefabs held the OLD stat points, so every spawn and `/summon` came out with them. The server builds the user
  made at 19:23 and 19:28 predate this and need remaking.
- Armour by what the creature is (metal / leather / cloth, from its portrait). Weapon by a town it lives around,
  spread so weapons are even; Katar given to Viper, Emberwyrm, Hornscale, Golden Bandit (Hazemere has no fields).
- Builds (weights STR:AGI:VIT:INT:DEX:LUK) are in that page; Fist and Shuriken have two builds, the second for metal.

### 7. Consumables: prices and where each one comes from (built 2026-10-13 evening, agreed, uncommitted)
- **Ten potions** — five sizes of Healing Potion and Mana Draught with fixed amounts, prices and weights, all in the
  Tool Dealer's stock. Page: `docs/2-systems/healing-and-mana-potions.md`.
- **Town supplies** (potions, Fly Wing 50, Antidote 100, Butterfly Wing 300) are bought and never dropped; a shop buys
  one back for half, rounded down.
- **Hunting finds** — the 30% random consumable roll is gone. Each creature gives up one find by its element
  (`CreatureFinds.cs`): Fire / Water / Earth / Wind one of its two converters, the other six elements a food, at 5% a
  kill for a normal, 50% elite, 100% shadow. No shop buys a find (sell value 0). Foods last 5 minutes. A sixth food,
  Sage Stew (+15% magic attack), is Ghost's. Page: `docs/2-systems/town-supplies-and-hunting-finds.md`.
- Measured in the editor without Play: all 98 creatures have a find and each element's find is the agreed one.

### 8. Gear drops, rarity lift, Temper Dust and Quench Oil (built 2026-10-13 night, agreed, uncommitted)
- **Forge is gone; gear drops.** A kill has a 10% chance to drop one random piece of what the creature wears, as a new
  Raw +0 copy. Stations keep Fold, Quench, Ink and Recycle.
- **Rarity on gear adds to the grade's lift on fold values** (0 / 10 / 20 / 35 / 50%), rolled only on the dropped piece
  by the creature's kind (normal Common to Rare, elite Epic, shadow Legendary). What a creature wears stays Common, so
  no creature number moved. This reverses the 2026-10-12 rule that rarity never changes a number, for gear only.
- **Trademark is separate from rarity:** 1% an ultimate's, 99% an active's or passive's. Skills lost their trademark
  rarity field.
- **The 21 band materials are deleted.** Fold costs the grade's step in Temper Dust, quench the grade's step in Quench
  Oil; the amount a kill gives follows what the kill pays. Recycling returns half; folds made at Raw return nothing.
- **A new character starts wearing** a Raw Common Sword, Chainmail and Iron Sabatons (no trademark, no word), and the
  first tutorial is talk, fold, wear. A piece the first tutorial still needs cannot be recycled, sold or dropped.
- **Prices:** every gear piece sells for 5, Temper Dust 2, Quench Oil 10, Inks 2/4/8/16/32, Salts 2/6/18.
- Save version 19; the servers' shared build string is `unwritten-character-v25`.
- Built by a builder agent from a planner's map, after a red team's 11 findings were folded in. All checkers pass and
  the editor loads it. **Every existing character and stored item was deleted by the user on 2026-10-13**, so no old
  save is converted.
- Every weapon has a normal, an elite and a shadow wielding it; all 14 shadows wear a full armour set chosen by their
  weapon (Asterion in metal).

## Uncommitted working-tree changes

The pay rule, creature gear and everything in sections 1 to 6 were committed and pushed to `main` on 2026-10-13 at
about 20:15 (GMT+8). **Sections 7 and 8 are uncommitted** — run `git status` for the real state. Three files were
already dirty before section 7 and are not this task's: `unity/Assets/Scripts/Editor/MobExport.cs`,
`website/index.html` and `wiki/content/monsters.generated.json` (the last was regenerated by the wiki export while
building section 7).

## What's left

1. **Low-level creatures in heavy gear.** A fold gives the same amount at every level, so Flutterby (level 2, F +8)
   and Gloop King (level 8, C +23) are far harder for their level than a level 90 creature in the same gear. Not
   measured. The user's stance: that is a fault in what gear gives, to be fixed there, never in the pay rule.
2. Decide the open items still undecided:
   - Low-level elites are over-geared: elites start at grade D at level 6, players at level 24 have grade F.
   - A Sniper's folds raise physical attack, but its shot uses magic attack (`CraftingMath.IsCasterWeapon` excludes
     Firearm). Affects players too. Reported, not changed.
3. Hat quests (15 hats across the towns, each asking for several creatures' materials) and repeatable material
   quests. Both need a quest step that TAKES items; today a quest can only check you hold one.
4. **Prices are done** (sections 7 and 8). Nothing is priced by measurement yet: how fast a player kills and how
   much health a kill costs are both unmeasured.
5. One play-test checklist at the very end. It must say: game server REBUILD and map server RESTART required (the bag
   message changed shape and all the runtime code changed); character server and account service — no change made.

## Dead ends — do not rebuild any of these

Five versions of a MEASURED toughness (a simulated attacker against each creature) were built and rejected. The
user's final word: a third variable should not exist at all — "if we still need a third variable, then there is
something wrong with our sub stats balancing". Also rejected, in the session that settled it: pay by level and kind
only, and grade folded into the fold count as a small bonus (grade is its own term: it also opens ink slots and a
higher weapon mastery limit).

**Measured 2026-10-13, and the reason sub-stat balance is the next real problem:** creatures with identical grade
and folds scored 70% to 415% on the old measure (grade F +10: Iris 70, Gnasher 415), and five folds on a metal body
piece add 36 health — 70% of a bare level 5 creature's 51, 2.5% of a bare level 99 creature's 1,423.

## Next single step

**Hand the user the one play-test checklist for everything in this file**, once nothing else is queued. It must open
with: game server REBUILD, map server RESTART and character server RESTART all required (the shared build string
changed, so restarting only one makes them refuse each other); account service — no change. Still unanswered: which
"stat builds column" the user expected in `docs/3-world/creature-gear.md`.

## How to resume

```text
Read handoffs/creature-economy-and-toughness.md. Everything in it is built and unplayed; sections 7 and 8 are
uncommitted. Next is the play-test checklist, then whatever the play-test finds.
```
