# Handoff — creature economy, loot-owner points, and what a kill pays (all built, nothing played)

**Task:** rebuild how hunting pays (no gold drops; each creature's own materials; who owns a kill; saved-character
reward; a bigger purse), then make a creature's reward follow how hard it is ("toughness"), then gear every creature.
Session of 2026-10-13.

## Status

**IN PROGRESS. Everything agreed is BUILT (2026-10-13 evening); nothing has been played.**

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
  were RE-SPENT for their weapon's build. Shadows untouched. Table: `docs/3-world/creature-gear.md`.
- **The re-spent stats only reach a spawned creature through `MobSpawner.Spawn`**, which now copies level and
  stat points from the creature file on the server (added 2026-10-13 19:31). Measured before that: 84 of 98 baked
  mob prefabs held the OLD stat points, so every spawn and `/summon` came out with them. The server builds the user
  made at 19:23 and 19:28 predate this and need remaking.
- Armour by what the creature is (metal / leather / cloth, from its portrait). Weapon by a town it lives around,
  spread so weapons are even; Katar given to Viper, Emberwyrm, Hornscale, Golden Bandit (Hazemere has no fields).
- Builds (weights STR:AGI:VIT:INT:DEX:LUK) are in that page; Fist and Shuriken have two builds, the second for metal.

## Uncommitted working-tree changes

None from this task. Everything was committed and pushed to `main` on 2026-10-13 at about 20:15 (GMT+8), together
with three files that were already dirty before the session and are not this task's: `Fonts/DejaVuSansCondensed
SDF.asset`, `ProjectSettings.asset`, `tools/hook-analytics/hook-analytics.jsonl`. Run `git status` for the real
state; the patch notes for the push are the top entry of `launcher/news.json`.

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
4. **Rework every other price** (the user said "let's do it now" on 2026-10-13, then went to sleep before the rule
   was settled). No forge material, ink, salt, consumable or equipment price has been touched. Measured facts:
   - Every normal kill also drops its level stretch's metal, cloth and leather (20% each), ink, salt and a 30%
     consumable, all sellable. At today's placeholder prices that adds about 58 gold to a level 7 kill meant to be
     worth 70, and about 635 to a level 92 kill meant to be worth 920.
   - Current prices: forge materials 20 / 80 / 180 / 320 / 500 / 720 / 980 by band; Inks 40 to 400,000; Salts 50 to
     5,000; weapons sell 5,000, armour 800 or 1,500; potions buy 80 / 300 / 1,200; Fly Wing 60, Butterfly Wing 300.
   - Proposed and NOT agreed: sell prices sized so all other drops together add a quarter of a kill; buy prices and
     station costs counted in kills (10 gold a level) at "the level the thing is meant for". The user asked how
     that level would be decided; the honest answer was that only forge materials, folding, quenching and forging
     have one, and potions, wings, the antidote, converters and food have none, so the rule is a guess for them.
     The user has not answered. Forge materials dropping by level stretch is itself a level ladder, which clashes
     with the sandbox ruling; raised, not decided.
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

**Ask the user how flat-priced things should feel** (cheap for everyone, or a real cost for a beginner) and whether
a quarter is the right share for the other drops, then write the price page in `docs/` first, as was done for the
pay rule. Also still unanswered: which "stat builds column" they expected in `docs/3-world/creature-gear.md` (the
page has a Build column and a six-stat column on every row). The play-test checklist comes only after all of that,
and must open with: game server REBUILD and map server RESTART required; character server and account service — no
change.

## How to resume

```text
Read handoffs/creature-economy-and-toughness.md. The pay rule and creature gear are built, committed and unplayed.
Next is the price rework for forge materials, ink, salt, consumables and equipment: ask me the two open questions
in "Next single step" before writing anything.
```
