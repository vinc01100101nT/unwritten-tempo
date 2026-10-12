---
status: "BUILD PLAN — all six slices landed 2026-09-20; slice 6 was rebuilt as skill levels bought with mastery points on 2026-10-03. Kept as the record of what each slice touched and what was decided."
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# Gameplay System V4 — Implementation Plan

Companion to [the V4 design](README.md), which is the contract. This page says HOW it was built: six slices,
smallest risk first. **All six landed on 2026-09-20 (slice 6 rebuilt as skill levels on 2026-10-03) and none has been play-tested.**

**Where the build differed from this plan**, decided while building:
- The weapon kit's removal (slice 3) and learned skills (slice 5) shipped together, so no session ever ran with
  the kit gone and nothing to replace it.
- One save version bump covered both slices: **v14** carries a forged weapon's trademark ([Weapon Names &
  Trademarks](weapon-names-and-trademarks.md)). The save today is **v18** (skill levels bought with mastery points ride
  it since v17; since v18 the hotbar lives in a row of its own and every piece of gear carries its own id) and both
  sides' build string is **unwritten-character-v24** (v19 carried quests; v20 the world events and the
  sign-in flag on an arrival; v24 the auto-combat settings row).
- Open question 1 was taken as recommended — the server writes a copy's finished attack, magic attack, defence
  and magic defence onto it, and a player's machine only adds them.
- Open question 2 was NOT taken as written: instead of the bag wire carrying costs for every copy, a station asks
  for ONE piece's costs and odds when the player picks it (`CmdAskOffer` → `TargetOffer`), which keeps the bag
  push the size it was.
- Open question 4 was taken as recommended: a "design" is an item carrying its own forge bill
  (`forgeMaterialCost`, `forgeGoldCost`) plus what it is `madeOf`, which also decides which station takes it.
- Open question 3 was taken further than recommended: `EquipBonuses` gained `accuracy`, `evasion` and
  `castSpeedPercent`, and `critPercent`/`aspdPercent` became fractions, so Ink can write the stats it rolls.
- The item's own flat bonus fields and the armour roll ranges were DELETED rather than left unread: a crafted
  piece is its folds, its grade and its Ink, and a number on the design that nothing reads is a lie.
- A testing door was added that the plan did not ask for: `/mastery [weapon] <n>` sets a weapon mastery level (it learns
  nothing — the points it gives are spent in the Skills window), because a level is thousands of kills wide and a
  play-test cannot fight them. `/resetskills [weapon|all]` takes back the points spent on a weapon's skills.
- Ten defects found in the 2026-09-20 play-test and by review were fixed on 2026-09-21: the HUD prefab rebuilt with
  the station window; a save's bar held to what is learned and a refused press said in chat; `/item` taking a
  quoted two-word name; Magic Defence and Crit Damage inks no longer dropped on decode; worn gear keeping its fold
  count over the wire; a starter kit no longer one of every material; a forge answering with the new piece picked; one Ink
  button per slot so the player picks; the Knuckle/Katar mastery rows written into the design; the dead Alt-key
  weapon-skills panel path removed from the tooltip.
- Added 2026-09-21 at the user's asking, after the first play-test of the stations:
  - **Every station press answers in one sentence**, to the chat and to a line in the window — red when refused,
    and carrying the numbers ("Needs 8 Copper — you have 3"). Forge rows name the band-1 material instead of the
    word "material". The bag's Merge answers the same way.
  - **Recycle**: at the station of its craft a piece can be taken apart for HALF of what it provably consumed —
    `CraftingMath.RecycleYield` counts the forge bill, one successful quench per rung climbed, the folds standing
    on it, and one Ink per written stat. Failed quenches and the folds they spent are recorded nowhere, so they
    return nothing; Salt never returns. Halved and floored per material.
  - **A station is an interaction like a conversation**: the same side-on camera, the townsperson halts and turns,
    the player is held, and Esc or the window's X ends it. `PlayerInteractor` owns both kinds so they cannot
    disagree.
  - **The window opens on a menu** — Forge, Fold, Quench, Ink, Recycle — and each opens its own page in the same
    window with a Back row; the picked piece carries across pages. Its list **scrolls**: one shared
    `UiKit.ScrollColumn` now serves the station, the Skills window and the skill draft, which each carried their own
    copy of the same twenty lines.
  - **One rule for what raises a skill's numbers** (the user's decision, 2026-09-21): the composer and the fight
    had separate lists and disagreed — the composer called a sub-stat change and an outgoing-damage boost or cut
    fixed while the fight raised them. `EffectScaling.Boostable` is now the only list: every
    number on it gets a "boosts …" ring row in the composer AND is raised by the skill's level, no exception; the
    fight, the tooltip and the composer all read it. Replayed against all 57 saved effects: no combat number moved,
    because no saved skill names ring stats on the newly offered rows.
  - **The fold cap became a real quench gate** (the user's decision, 2026-09-21, after watching a +0 sword
    quench): `CraftingMath.ReadyToQuench` refuses a quench below the grade's cap, and `RecycleYield` now counts
    each climbed rung's full fold cap as well as its quench, since the gate is what makes those folds provable.
  Every "today it does X" claim below carries
the file and line it was read at on 2026-09-20; anything not opened is marked "not verified". Update it in place
as slices land and as the open questions at the bottom are answered.

## Part 1 — What the player is getting, slice by slice

**Slice 1 — the Ring is part of you.** The Ring stops being a worn item. The ring row disappears from the equipment list, and the ring's percentage bonuses always count. Nothing else about the ring changes: it still levels from the same kills, still banks a point per ring level, still has its draft window and its soft cap.
*Check:* open the equipment window — there is no Ring row. Open the character sheet — the attack/defence/HP numbers are exactly what they were before the change (measure them on the same character before and after). Kill a creature — the ring still gains experience and still banks a point on a ring level. *Failure:* any sheet number differs, or the ring stops levelling.

**Slice 2 — materials exist and a GM can hand them out.** Twenty-one base band materials (Copper, Bronze, Iron, Steel, Silver, Mithril, Adamant; Burlap, Hemp, Linen, Wool, Cotton, Silk, Brocade; Rawhide, Hide, Buckskin, Leather, Cordovan, Shagreen, Wyrmhide), three Salts (Coarse, Fine, Pure) and five Inks (Watery, Thin, Faint, Clear, Sharp) become real items. Ten of one Ink or Salt tier can be merged into one of the next.
*Check:* type `/item copper 10` — ten Copper appear in the bag's Material tab. Type `/item "watery ink" 10`, then merge — you have one Thin Ink and no Watery Ink. *Failure:* the item is unknown, lands in the wrong tab, or merging with fewer than ten does something.

**Slice 3 — gear has a grade and folds instead of a rarity and a refine.** Every weapon and armour piece shows one label: its Quality (Raw, or F to A) and its fold count (+N). Rarity words and rarity colours are gone everywhere — bag rows, tooltips, floor drops, chat lines, the creature's health-bar gear list. A piece's worth — attack on a weapon; on armour, what its material buys ([Armour Roster](armour-roster.md)) — is exactly its folds lifted by its quality, per the design's §9 table; a Raw +0 piece adds 0. Every piece the player already owns becomes Raw +0 with no stats written on it.
*Check:* hover a sword in the bag — the tooltip reads "Raw +0" (no Common/Rare, no colour). Hover an armour piece — same. Kill a creature that wears gear — nothing worn drops; only the items in its table and gold do. *Failure:* the word Common/Uncommon/Rare/Epic/Legendary, or a rarity colour, appears anywhere; a piece shows a defence that is not its fold total lifted by its grade.

**Slice 4 — Forge, Fold, Quench, Ink at a station in town.** Standing next to the smith (metal), loom (cloth) or tannery (leather) and pressing F opens the station's window. Forge: pick a design, pay the base materials and gold, receive a Raw +0 piece. Fold: pay 1 unit of the grade's band material + 50 gold × band number; the piece goes up one fold, never past its grade's cap. Quench: pay 10 units of the band + 500 gold × band number, optionally one Salt; the grade goes up one, stays, or (from E and above) drops one, using the §9 odds; folds go to +0 either way; a drop removes the last Ink stat. Ink: choose an empty or occupied stat slot on the piece; one random stat from the eighteen lands with the tier's number; the Ink is spent.
*Check:* with 12 Copper and 1,200 gold, Forge a sword (Raw +0), Fold it five times (Raw +5, sixth fold refused), Quench it (F +0 with 80% odds, else Raw +0), Ink it with a Watery Ink (one stat, +2 attack or +16 HP etc., or +3 of a primary), hover it: the tooltip lists the grade, the folds and the stat. Walk away from the station and press F where no station is: nothing crafting-related opens. *Failure:* any of it works from the bag or away from the station; a fold goes past the cap; a quench jumps two grades; an Ink lands the same stat twice.

**Slice 5 — Weapon Mastery.** Each weapon type has a mastery. Fighting with a sword earns Sword Mastery exactly the way the character's level and the ring are earned: two thirds of the experience every kill pays also goes to the mastery of the weapon in hand, times the grade's training speed, never past the grade's cap, on the character's own curve ([the curve](../pre-renewal-ro-port.md)). Every level adds +0.5% to the three stats in the design's §5 table, added to the ring's percentage. Each mastery level gives one mastery point for that weapon, spent in the Skills window (K): a skill can be bought once the mastery has reached its level (the §9 table) and the skill it needs, if any, has a point. A learned skill is usable only while holding that weapon type. Rolled weapon kits are gone: a weapon carries no skills.
*Check:* a fresh character holding a Raw sword kills creatures worth 5 experience each — each kill adds 2.5 mastery experience (5 × 0.5 Raw speed); the eighth kill reaches 20 and Sword Mastery 1, and the Skills window shows one point to spend, which can buy Piercing Gust or Bending Reed; the sheet's Attack Speed, Attack and HP each rise by +0.5%. A training dummy gives 0. With Sword Mastery 10 and a Raw sword, kills earn nothing more until an F sword is held. *Failure:* a kill with a bow raises Sword Mastery; the sheet number does not move; a skill can be bought before its level or before the skill it needs.

**Slice 6 — Skill levels.** Each further point spent on a learned skill raises it one level, up to its max level (10, 5 or 1 — the design's §7). Every level after the first adds +5% to each of that skill's main numbers, the same for actives, passives and ultimates, added to the ring's term the way rarity's lift used to be. Every level bought counts, whatever the grade of the weapon held. Pressing a skill does not train it.
*Check:* with Sword Mastery 3 and a Raw sword, spend three points on Piercing Gust: its card reads "Skill level 3 / 10", and its damage at the next level is higher than its damage now. Press it twenty times: its level does not move. *Failure:* a point is spent without the mastery or the needed skill; pressing raises the level; a level bought does not raise the number.

## Part 2 — How it gets built

### The spine every slice rests on

**A character is one JSON document** in the `character.progress` column (accounts/sql/001_init.sql:35), replaced whole on each server write (charserver/Program.cs:138-176), stored without interpretation (charserver/Progress.cs:8-11). The map server writes it from `Character.CaptureSave()` (unity/Assets/Scripts/NetPlayer.cs:1036-1077). A player's machine only reads (SaveManager.cs:86-93, 164-171). So **every saved-data upgrade is a version branch in `Character.RestoreSave`** (Character.cs:2045) keyed on `SaveData.version` (SaveData.cs:21-25), rewritten on the next server save. When the save's SHAPE changes, `SaveData.CurrentVersion` (SaveData.cs:23) AND the shared build string `CharacterBuild = "unwritten-character-v14"` on both sides (MapServerLink.cs:51, charserver/MapServers.cs:61) must be bumped in the same edit — the two servers refuse each other otherwise (MapServerLink.cs:42-50). The save size cap is 256 KB (charserver/Config.cs:48); nothing here comes near it.

**Equipment copies today** are `EquipmentInstance` (EquipmentInstance.cs:12-38): `def`, `upgrade`, `cards`, `rarity`, `skillIds`, and the armour roll (`rolled`, `rolledDefense`, `rolledMagicDefense`, `rolledStats[6]`). They travel as parallel arrays in three places that deliberately share one shape: the save (SaveData.cs:74-94: `equipRefines/equipRarities/equipSkills/equipRolls`, `bagRefines/bagRarities/bagSkills/bagRolls`), the bag wire (BagWire.cs:19-51, built at BagWire.cs:75-134, applied at 140-185), and the worn-text SyncVar for watchers (WornList.cs:29-49 encodes `id, rarity, drop chance`). Roll numbers are encoded as one comma string (EquipmentInstance.cs:79-114). **V4's copy shape** replaces `rarity` + the roll with: grade, folds, and the Ink stat list — encoded the same way (one string per copy, in the slot `equipRolls`/`bagRolls`/`rolls` already occupy) so no new parallel array is needed.

**Where every gear number enters the formulas**: `EquipmentInstance.AddPieceInto` (EquipmentInstance.cs:174-208) → `EquipBonuses` (221-251) → `UnitStatMath.*(u, eb, ringPct)` (UnitStatMath.cs:240-305). `EquipBonuses` has hp, mp, atk, def, matk, mdef, six primaries, `critPercent`, `aspdPercent` — **and no accuracy, evasion or cast-speed field** (EquipmentInstance.cs:223-226); `Hit`, `Flee` and `CastTimeMult` take no `EquipBonuses` at all (UnitStatMath.cs:277, 280; Character.cs:579). Ink can roll Accuracy, Evasion and Cast Speed (V4 §9), so those three gain a field in `EquipBonuses` and a parameter in those three formulas. `critPercent` is a whole-percent `int` (EquipmentInstance.cs:226); Ink Critical is 0.6%–3% (V4 §9) — see open question 3.

**Where the ring percentage enters**: `Character.PassivePercent` gated on `RingEquipped` (Character.cs:281-290) feeding `Lift` (Character.cs:309) and every getter (Character.cs:400-579); mob side `UnitStatBlock.RingPercent` gated on `Gear.RingEquipped` (UnitStatBlock.cs:58-70). Skills read it through `ICombatUnit.RingBonus` (ICombatUnit.cs:170) into `AffinityMult` (SkillEffectApplier.cs:920-934). **Weapon Mastery adds at exactly these two `Lift` sites and nowhere else** (V4 §5: "added to the ring's bonus, never multiplied").

**Where rarity's skill lift enters**: `ICombatUnit.SkillRarityLift(skillId)` (ICombatUnit.cs:174) — `Character.cs:300` and `Health.cs:148-153` both call `Rarities.LiftFor` (Rarity.cs:91-99); the applier resolves it once per burst (SkillEffectApplier.cs:44, 61-68) and adds it in `OutputMult = AffinityMult + rarityLift` (SkillEffectApplier.cs:1025-1026); the tooltip reads the same hook (SkillTooltip.cs:232). **The hook is now `ICombatUnit.SkillOutputLift`** — the skill-level lift plus the trademark — same signature shape, same add.

**The server decides everything**: casts are owned/validated at `GameNetwork.Owns` (GameNetwork.cs:2320-2331 — bar, ult, or weapon kit); the bar and passives are set by `CmdSetHotbar`/`CmdSetPassives` with no learned-set check (NetPlayer.cs:1157-1185); the sheet is derived server-side and pushed (NetPlayer.cs:589-624 → Character.SetSheetFromServer, Character.cs:121-134); GM commands with `changesTheWorld` run on the server (GmCommand.cs:24-27; GameNetwork.cs:2469-2479). Kill credit is `Health.LastAttacker` written at the damage door (CombatAuthority.cs:73-77 for blows, 248-251 for ticks); **there is no per-attacker damage tally today** (Rewards.cs:51-58 says so in its own comment).

**NOCLIENTMATH gap carried forward that V4 must not repeat:** the tooltip's per-copy line runs `AddPieceInto` on the player's machine (ItemTooltip.cs:299-317), which multiplies and rounds (`Rarities.Lifted`, Rarity.cs:86). In V4 the copy's finished attack/defence and its Ink lines are numbers; the server works them out and the wire/save carries the finished ints (recommendation in open question 1).

### Build order — six slices, smallest risk first

Every slice: run `tools/check-unity-code.sh` after each C# edit; read the editor console before reporting; **the game-server build must be rebuilt for every slice** (all of them change C# the server runs); the character server restarts only where the save shape/build string moves (marked); the account service never (it no longer owns characters — accounts/src/routes has only accounts.ts).

#### Slice 1 — The Ring is part of the character (V4 §8)

**Deletes:** `Character.EnsureRingEquipped`/`LoadRingDef` and its runtime-built fallback item (Character.cs:851, 856-881, 1981, 2072); the gate in `RingEquipped`/`PassivePercent` (Character.cs:281-285) — `PassivePercent` becomes `Ring.Passives.Percent(stat)` unconditionally; the ring-XP gate `if (RingEquipped)` (Character.cs:1634); `UnitStatBlock.RingPercent`'s gate (UnitStatBlock.cs:58-59) and `UnitEquipment.RingEquipped` (UnitEquipment.cs:100); `EquipSlotType.Ring` and `EquipSlots.Label/Order` rows (Item.cs:23, 46, 65) — see open question 5; `ItemType.Ring` and its `Apply` case (ItemType.cs:7, 20, 41) and the "Rings" menu + column set (Editor/ItemTableWindow.cs:78-80, 98); `MonsterDef.Ring` and the ring words in its tooltips (MonsterDef.cs:60-61, 117-129); `LootDropper.RingDropChance` and its branch (LootDropper.cs:46-62); ring exclusions in `CaptureBag/SeedBag/CaptureEquipment/RestoreEquipment` (Character.cs:1865-1867, 1935-1937, 1995, 2024), `BagWire` (BagWire.cs:42-43, 92, 108-111), `InventoryStore.cs:164`, `NetPlayer.cs:1356`, the ring rows in `ItemActionMenu` (ItemActionMenu.cs:198-202, 225), `ProgressionWindows.WornOrder`'s Ring entry (ProgressionWindows.cs:18-20), `CreatureWardrobe`'s "ring is not one" note (CreatureWardrobe.cs:16), the ring checks in `Editor/MobTableWindow.cs:355, 398, 436`, and the `/ringxp` comment "regardless of whether it's currently worn" (Character.cs:1682-1684; GmCommands.cs:151).

**Reuses unchanged:** `RingProgress` (RingProgress.cs, whole file), `PassiveRanks`, `SkillRing.Percent` and its caps (SkillRing.cs:57-88), the ring level/XP save fields (SaveData.cs:50-59), the draft window, `TellOwnerTheyEarned` (GameNetwork.cs:1281).

**Saved data upgrade:** none — the ring was never serialized (SaveData.cs:72-73); no version bump, no character-server restart.

**Touches but does not alter:** the ring's XP curve (today the character's own curve, paid a third of each kill — [the curve](../pre-renewal-ro-port.md)); one draft point per ring level (RingProgress.cs:53); the ten ring stats and their order (SkillRing.cs:42-53); the +80%/+50%/+60% caps and K=40 (SkillRing.cs:63-76); the level-up draft UI; ring XP still coming from the same kills as character XP (Character.cs:1632-1634). The rule "ring XP only accrues while worn" (Character.cs:1633) simply becomes "always" — that is what §8 says.

**Rebuild:** game server yes; char server no; accounts no.

#### Slice 2 — Materials as items, merging, GM grant (V4 §2 table, §3, §4)

**Touches:** 29 new `Item` assets under `unity/Assets/3_Data/Resources/Items/` with `category: Material` (Item.cs:76, 119), `slot: None`, one per name in V4 §2's table + Coarse/Fine/Pure Salt + Watery/Thin/Faint/Clear/Sharp Ink — a `data-author` job (ids follow the existing `item.<kebab>` shape, Item.cs:85-86). Each needs a band number (1–7) and craft (metal/cloth/leather) so Fold/Quench know which material a grade wants — **a new authored field on `Item`** (band + craft, or one "material tier" field), written on all 29 in the same change and left at zero on the 17 existing items, where zero must mean "not a crafting material". Merge: one new server command (`CmdMergeMaterial(bagIndex, expectedItemId)` beside the bag commands at NetPlayer.cs:1281-1419, guarded by `StillTheSameStack` as they are) that takes 10 of a tier and gives 1 of the next through `TakeIntoBag` (Character.cs:1717-1734); the "next tier" lookup is data on the Ink/Salt items (a `mergesInto` item reference) — one new field. The bag window's action menu gets a Merge row (ItemActionMenu.cs:181-215 builds rows per kind). Materials can already be granted with `/item <id|name> [count]` (GmCommands.cs:163, 299-317 → `GiveTo` → `TakeIntoBag`, 393-401), which runs on the server (GmCommand.cs:24-27) — **no new GM command**.

**Reuses:** `ItemCatalog.Resolve` (ItemCatalog.cs:56-66), the Material tab (ItemType.cs:26), `TakeIntoBag`, `PushBagToOwner`/`SaveSoon` (NetPlayer.cs:1211-1222; GmCommands.cs:405-410).

**Deletes:** nothing. **Saved data upgrade:** none (items are `id + count` in the bag, SaveData.cs:88-89).

**Touches but does not alter:** the `/item` command's name matching; the bag's stacking rule for non-equipment (Character.cs:1729-1731); the 30%-per-kill random-item roll (now `ItemCatalog.RandomConsumable`, consumables only since 2026-09-22 — see open question 6).

**Rebuild:** game server yes (new command); char server no.

#### Slice 3 — Grade and folds replace rarity, refine and the armour roll; worn gear stops dropping (V4 §1, §2, §4, §9)

**The copy:** `EquipmentInstance` loses `rarity`, `skillIds`, `rolled/rolledDefense/rolledMagicDefense/rolledStats` (EquipmentInstance.cs:20-38) and `AtkPerRefine/DefPerRefine` (42-43); `upgrade` stays and IS the fold count (V4 §2). It gains: grade (Raw/F/E/D/C/B/A — a new enum), the Ink stat list (up to 6 entries of stat id + value, the slot count from the §9 table), and **the finished numbers the server worked out** (piece attack / magic attack / defence / magic defence — open question 1). `EncodeRoll/DecodeRoll` (79-114) become the encode/decode of grade + Ink list, reusing the `rolls` arrays in save and wire. `CreateRolled` (58-69) becomes "create Raw +0" and stops calling `WeaponSkillRoll`/`ArmourRoll`. `AddPieceInto` (174-208) reads the copy's finished piece numbers + Ink lines; for a weapon, `def.bonusAttack/bonusMAtk` are no longer read — today `Sword.asset` carries `bonusAttack: 3`, so that authored 3 goes away; for armour, the eight range fields (Item.cs:154-170) stop being read. `AccumulateInto`'s refine lines (144-146) are deleted — the fold value now lives in the piece numbers.

**Arithmetic in plain functions:** one new static class holding the §9 tables — fold cap per grade, quality lift per grade, Ink slots per grade, mastery caps and speed per grade, per-fold gains by slot (weapon +1 atk or +1 matk for Staff/Wand/Scythe; Armor/Shield +1 def; Garment +1 mdef; Head +1 of each per 2 folds), quench odds per grade + Salt shift (Same first, then Down, Down floor 5%), fold/quench costs, Ink values per stat per tier, Ink roll odds (8.8% × 10, 2% × 6, re-spread over unrolled stats). All take numbers and return numbers; the die comes in as a parameter.

**Deletes (V4 "What V4 replaces"):** `Rarity.cs` whole; `WeaponSkillRoll.cs` whole; `ArmourRoll.cs` whole (its `FillMidpoints` is read by Editor/MobTableWindow.cs — line not verified); `Item.rarity` (Item.cs:115-117) and the eight roll-range fields (Item.cs:154-170); `SaveData.equipRarities/equipSkills/bagRarities/bagSkills` (SaveData.cs:79-80, 92-93) and their capture/restore lines (Character.cs:1874-1875, 1878, 1884-1885, 1918-1919, 2001-2002, 2009-2010, 2033-2034); `BagWire.rarities/skills/wornRarities/wornSkills` (BagWire.cs:23-24, 49-50, 98-99, 119-120, 127-128, 159-160); the rarity index in `WornList` (WornList.cs:16, 45, 67-68), `GroundDrop.rarity` + its label colour and sparkle tint (GroundDrop.cs:47, 218, 238, 374, 398), `NetGroundDrop.rarityIndex` (NetGroundDrop.cs:46, 100), `TellOwnerTheyGot`/`TargetGotItem`/`ChatController.ItemArrived`'s rarity parameter and colour (Character.cs:1766-1773; GameNetwork.cs:1385; NetPlayer.cs:1244-1256; ChatController.cs:67-74); every rarity read in `InventoryList` (309, 338-339, 416-425), `ItemActionMenu.cs:244-247`, `ItemTooltip` (77, 90, 104, 113, 259, 279-287), `UnitStatusPanel.cs:243`, `AgentLink.cs:1060, 1166, 1185`, `SkillTooltip.cs:232` (this one becomes the mastery lift in Slice 6 — leave the hook, change its source), the "Rarity" column and armour-range columns of `ItemTableWindow` (Editor/ItemTableWindow.cs:28, 45, 71-89, 285); the worn-gear drop: `LootDropper.WornDropChances/WornDropChance/RollWornGear/wonWorn` (LootDropper.cs:52-63, 86-88, 121, 128-144) and `Rewards.PayLoot`'s `worn` parameter and loop (Rewards.cs:68-69, 90-93, 142-149); `CreatureWardrobe.WearChances/WearChance` keyed by rarity (CreatureWardrobe.cs:20-24, 38 — open question 7); the `/weapon [type] [rarity]` command's rarity half and its kit report (GmCommands.cs:164-167, 319-373) — the command becomes `/weapon [type] [trademark] [folds] [grade]`, a forged weapon with its trademark; `GiveEquipment(Item, Rarity?)` (InventoryStore.cs:94) and `TakeIntoBag(Item, int, Rarity?)`'s third parameter (Character.cs:1717); `SeedBag`'s "never seed weapons/armour" branches (Character.cs:1938-1943) — whether starter gear is seeded at all is open question 8.

**Also removed with the kit:** `HotbarBindings.SetWeaponKit` and its call on equip (HotbarBindings.cs:146-175; Character.cs:972-988), `PassiveLoadout.GrantedByWeapon` and the weapon branch in `IsActive/Set/CollectActive` (PassiveLoadout.cs:49-58, 63, 70, 99-105), the weapon-kit source in `UsableSkills` (Character.cs:1150-1154), the kit half of `GameNetwork.Owns` (GameNetwork.cs:2326-2330), the creature's `KitOf` kit half (Health.cs:287-303), the kit icons/lines in the tooltip (ItemTooltip.cs:92, 131, 179, 262), `InventoryList.cs:341`, `WeaponSkillDetailsPanel.cs:306` (not read beyond that line), `ExtraSkillRoll.Roll`'s "skip what the kit carries" argument (ExtraSkillRoll.cs:22, 31; Health.cs:268), `BagWire`'s `alsoTheBar` push on weapon change (BagWire.cs:53-70, 83-87; NetPlayer.cs:1293-1304) — the bar no longer changes on equip, but what is castable does (weapon type gate, Slice 5), so `RefreshUsableSkills` on equip stays (Character.cs:987).

**Saved data upgrade (needs `CurrentVersion` 12 and `CharacterBuild` v12 on both sides → character-server restart):** in `RestoreSave`/`RestoreEquipment`/`RestoreBag` (Character.cs:2016-2040, 1903-1924), a save below 12 loads every equipment copy as **Raw, folds = 0, no Ink, finished numbers 0** (open question 9 on keeping old `upgrade` values). The old `equipRarities/equipSkills/equipRolls` keys are ignored on load and vanish on the next write; no SQL migration is needed because the document is replaced whole (charserver/Program.cs:160). Verify by reading a row back: `psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -A -t -c "select name, progress->'equipRolls' from character where deleted_at is null order by updated_at desc limit 3;"`.

**Touches but does not alter:** cards (`cards`, `cardSlots`, `Card.cs`) — untouched, deferred; the bag's position-based commands and `StillTheSameStack` (NetPlayer.cs:1281-1419); the drop's killer-only window and lifetime (Rewards.cs:110-117); XP payment (Rewards.cs:27-49); the `RandomItemChance = 0.30` roll (LootDropper.cs:69-71) — stays exactly as is; mob authored `itemDropNames/itemDropChances` (MonsterDef.cs:109-115); gold drops (LootDropper.cs:101-105); the skill colour by weapon type (`WeaponColors.cs`, not read — V4 keeps it); the tooltip's structure; the "+N" title prefix (ItemTooltip.cs:89, 103, 114) which now reads as folds.

**Rebuild:** game server yes; **character server yes** (build string v12); accounts no.

#### Slice 4 — Stations: Forge, Fold, Quench, Ink (V4 §1, §2, §3)

**Server commands** (new, beside the bag commands in NetPlayer.cs, all `[Command]`, all guarded by `Leaving`/`StillTheSameStack` as NetPlayer.cs:1283-1287 shows, all ending in `PushBagToOwner(); SaveSoon();`): Forge(designItemId), Fold(bagIndex, expectedItemId), Quench(bagIndex, expectedItemId, saltBagIndex or −1), Ink(bagIndex, expectedItemId, inkBagIndex, slotIndex). Each first checks the asker is within reach of a station of the piece's craft — the same shape as `CmdTalkToNpc` (NetPlayer.cs:1509-1514), which passes the asker's own `transform`. Each computes with the Slice-3 static functions, spends materials/gold through the existing bag list and `AddGold` (Character.cs:1783-1787), refuses silently on any shortfall, and logs one line (GameLog — pattern at Rewards.cs:40-45). The quench die and the Ink stat die go through `CombatRng` on the server only (`CombatAuthority.cs:27-32` is the precedent).

**The station in town:** every townsperson is a `TownNPC` with a name from the townsfolk list (`WorldTownsfolk`) and random barks, reached by F through `PlayerInteractor.ReachForTheNearestThing` (PlayerInteractor.cs:64-82) which opens `DialogBox` (PlayerInteractor.cs:99-108). A station needs a role on the NPC (smith/loom/tannery — a new serialized field on `TownNPC`, and on `NetNpc`'s SyncVars so watchers know, NetNpc.cs:58 shows the pattern) and a branch in `StartTalk`: a role opens the crafting window instead of the dialog. The window is a new `UiKit.Window` (the pattern SkillBookPanel.cs:77 uses) showing: the design list (Forge), the held piece's grade/folds/slots/Ink lines and the three buttons with their cost — numbers the server sent. No townsperson lives in a scene: they are rows of the townsfolk list, spawned by the server when it loads their map.

**The costs on screen are numbers the server sent.** Cost = 1 unit + 50 g × band is arithmetic; the window must not do it (open question 2).

**Designs:** a "design" is an Item the station can forge and its base-material bill — new authored data (open question 4).

**Saved data upgrade:** none beyond Slice 3.

**Touches but does not alter:** the F key's nearest-thing rule (PlayerInteractor.cs:64-82); the camera two-shot and HUD hide (PlayerInteractor.cs:101-107, 127-136) — a station window should NOT enter the cinematic (a window needs the HUD's canvas); `TownNPC` wander/bark behaviour; the drop-at-feet flow.

**Rebuild:** game server yes; char server no.

#### Slice 5 — Weapon Mastery, learned skills, the character skill window (V4 §5, §6, §9)

**Storage:** on `Character`, one mastery-experience total per `WeaponType` (to two decimals — each kill's share is kept by the same rule as the character's and the ring's) and the bought skill levels. Level is DERIVED from that experience by a static function on the character's own curve (`MasteryMath.WeaponLevelFrom`; [the curve](../pre-renewal-ro-port.md)) — one owner. Save: two new parallel arrays (`masteryWeapons[]`, and `masteryPoints[]` — the mastery experience, not the points a player spends) → `CurrentVersion` 13, `CharacterBuild` v13 (folded into Slice 3's bump); the bought skill levels are Slice 6's arrays. Wire to the owner: a new `TargetMastery(...)` RPC in the shape of `TargetStatsChanged` (NetPlayer.cs:1149-1155), carrying finished values (per weapon type: level, experience into the level, the level's size, capped-by-held-weapon; the learned ids). The sheet already carries the finished stat numbers (NetPlayer.cs:589-624).

**Earning (server only):** `Character.AddXP` — the one door every kill's experience (and `/xp`) comes through — pays the same amount to the held weapon's mastery (`MasteryProgress.AddXP`), times the grade's speed (`MasteryMath.WeaponXpFromKill`), refused once the grade's cap is reached. Only whoever is paid the experience earns it. Creatures do not earn.

**Applying to stats:** `Character.Lift`/`PassivePercent` (Character.cs:285, 309) and `UnitStatBlock.Lift`/`RingPercent` (UnitStatBlock.cs:58-70) each add `mastery percent for (held weapon type, stat)` — a static function over the §5 table (weapon type → three `StatId`s) and the usable level (min(earned level, cap for held grade)). Applies to mobs from the same store a player's lives in (`Health.Mastery`, read by `UnitStatBlock.MasteryPercent`), with mastery in one weapon only: `MonsterDef.weaponMastery` on the weapon in its gear, put into the store by `MobSpawner` when the creature is made, and set in the Mob Table (decided 2026-10-04; 0 on every creature today). Creatures do not EARN (a respawn is a fresh creature, MobRespawn.cs:111-116).

**Learning and the gate:** `SkillDef` carries the mastery level that opens it (`masteryLevel`), the skill it needs first (`needsSkillId`) and how far it levels (`maxLevel`) (V4 §9 sword table), written for all 22 sword assets. `weaponType` already says which mastery. A skill is learned only when the player spends a point on it: `NetPlayer.CmdSpendSkillPoint` → `MasteryProgress.TrySpend`, which only the server runs (open question 10). `GameNetwork.Owns` (GameNetwork.cs:2320-2331) becomes: on the bar/ult AND learned AND `weaponType` matches the held weapon (or `None`). `CmdSetHotbar`/`CmdSetPassives` (NetPlayer.cs:1157-1185) refuse ids not learned. `UsableSkills` (Character.cs:1139-1177) drops the kit source; `PassiveLoadout.IsActive` (PassiveLoadout.cs:62-63) adds the learned + held-type test; `PassiveEvents.Fire` keeps reading `PassiveIsOn` (PassiveEvents.cs:114). Creatures: `Health.PassiveIsOn` is "everything" (Health.cs:307) — their list becomes every skill of their held weapon type up to their authored mastery level plus the extra slot (open question 11).

**The window:** the Skills window (SkillsWindow.cs, key K) is the character skill window: one weapon at a time as a timeline of mastery 0–70, a + button on each skill to spend a point, learned skills to drag onto the bar. Its tree rules are the `skill-tree` skill. The Grimoire (J), the old list of every skill in the game, was deleted on 2026-09-21. `published` (SkillDef.cs:465) loses its kit reader but keeps `ExtraSkillRoll` (ExtraSkillRoll.cs:30) and the composer dimming (SkillComposerWindow.cs:242-261); its tooltip text ("eligible to be rolled onto a dropped weapon") must be rewritten.

**Touches but does not alter (none of these was ever agreed):** the hotbar's layout was not part of V4 — the user set it on 2026-10-03 as three rows of nine, any slot holding a skill, an item or a piece of gear, with no separate ultimate seat ([HUD/UI System](../hud-ui-system.md)); passives are never ticked — every learned passive is live while its weapon is in hand (decided 2026-09-21); the training dummy earns nothing because it never loses health it keeps; the challenge scale uses the CHARACTER's level — for a creature killing a creature nothing is paid (Rewards.cs:33-35 already pays only a `Character`).

**Rebuild:** game server yes; **character server yes** (save shape).

#### Slice 6 — Skill levels (V4 §7, §9)

**Storage:** per learned skill id, the level bought. Save: two parallel arrays (`skillLevelIds[]`, `skillLevels[]`, SaveData.cs:84-85), save **v17**; an older save's automatically learned skills are not read, so every point its masteries earned is there to spend again.

**Spending (server only):** `CmdSpendSkillPoint` → `MasteryProgress.TrySpend` refuses a skill of no weapon, a skill at its max level, a skill whose mastery level is not reached, a skill whose needed skill has no point, and a spend with no point left. Nothing is earned per press.

**Applying:** `Character.MasteryLift(skillId)` = `MasteryMath.SkillLevelLift(Mastery.LevelOf(skillId))` — +5% a level after the first, every level bought counting, and only while the skill's weapon is in hand. A creature spends no points: `Health` lifts every skill it knows at `CreatureSkillLevel` = 1, which is no lift. `OutputMult` and the tooltip are untouched in shape.

**Deletes:** the `Rarities.LiftFor` call sites named above; the "+0% Common … +30% Legendary" sentences in V3 README §1 and implementation-notes ("Skills don't level"), and ring-system-trio-ring.md:15.

**Touches but does not alter:** which channels take the lift (output only, never duration/proc chance/hit count/cleanse count/shove speed — `EffectScaling.Boostable`); the lift is ADDED to the ring term; the tooltip's describer reads the local player's finished values.

**Rebuild:** game server yes; character server yes (save shape).

### Docs and wiki that state the old rules (RULE NOSTALE — rewrite in place in the slice that changes each)

docs/2-systems/active-skill-system-v3/README.md (rarity lift §1, "Weapon = grants skills"), implementation-notes.md (:25-28 kit, :233-235 "skills don't level"), unified-unit-stats(-implementation-notes).md, weapon-crafting-economy.md and crafting-and-recycling.md (both replaced), ring-system-trio-ring.md:15, hud-ui-system.md, pre-renewal-ro-port.md, roadmap/action-plan, docs/README.md; wiki/content/weapons.json, cards.json, skills.json, crafting.json (grep hits for rarity words). Not read in full; listed from `grep -rli "legendary\|rarity"`.

## Part 3 — What could break

**Every caller of what changes** (found by grep 2026-09-20):
- `Rarity`/`Rarities`: 27 files — every one is named in Slice 3.
- `EquipmentInstance.skillIds`: Rarity.cs:94, Character.cs:981, 1151, PassiveLoadout.cs:54, 99, Health.cs:268, 291, GameNetwork.cs:2326, InventoryList.cs:341, ItemTooltip.cs:92/131/179/262, GmCommands.cs:360-370, WeaponSkillDetailsPanel.cs:306.
- `EquipmentInstance.upgrade`: GroundDrop.cs:239, BagWire.cs:117, ItemTooltip.cs:89/103/114/263, ItemActionMenu.cs:177/245, AgentLink.cs:1163/1183, NetGroundDrop.cs:46, InventoryList.cs:419, Character.cs:1767/1872/1999, Editor/MobTableWindow.cs:342 — all keep working (folds = upgrade).
- `EquipSlotType.Ring`: 20 sites (listed in Slice 1).
- `RingBonus`/`PassivePercent`/`Lift`: Character.cs:400-579 (every getter), UnitStatBlock.cs:58-70, 161, 227, SkillEffectApplier.cs:930.
- `ICombatUnit.SkillRarityLift`: Character.cs:300, Health.cs:148, SkillEffectApplier.cs:62, SkillTooltip.cs:232.
- `Rewards.PayLoot(winner, items, worn, gold, victim)`: LootDropper.cs:123 only.
- `EquipmentInstance.CreateRolled`: EquipmentInstance.cs:66-67 (self), Rewards.cs:87, Character.cs:1725, UnitEquipment.cs:53, CreatureWardrobe.cs:37, GmCommands.cs:352.

**Modes to walk for every slice:**
- **Server vs player's machine vs watched copy.** `UnitEquipment.SeatStartingGear` rolls only on the owner (UnitEquipment.cs:49-53); `Health.RollExtraSkillsForNewLife` bails on a watched copy (Health.cs:252); `PassiveEvents.Fire` bails on a watched copy (PassiveEvents.cs:100); `CombatAuthority.Apply/ApplyTrue` return 0 off the server (CombatAuthority.cs:27-32, 240). Mastery earning and every craft roll must sit behind the same `GameServer.IsServer` line; the player's machine must only receive finished mastery numbers.
- **Editor tools:** `ItemTableWindow` (rarity/range columns), `MobTableWindow` (ring checks, `FillMidpoints`, `pieceScratch.upgrade`), `SkillComposerWindow` (published dimming; new unlock fields need composer UI because it is the only writer — V3 README §0), `SkillExport` (writes `weapon`/`published` at SkillExport.cs:103-108; new fields must be exported or `tools/check-skill-data.py` may flag drift — not verified what it compares).
- **Alive / dead / pooled / respawned:** the damage tally must clear at `MobRespawn.Reactivate` (MobRespawn.cs:91-124) and at pool return (`PooledMob.OnFadedOut`, Health.cs:745 — not read); a mob's worn set is re-seated fresh each life (UnitEquipment.cs:82-86) so a creature's Raw +0 gear re-seats too.
- **First run / repeat / teardown:** a fresh character (progress `{}` → `version 0`, SaveData.cs:22-27) must start with no mastery, no learned skills, Raw +0 seeded gear (or none — Q8); a v11 save must load with every copy Raw +0 (Q9); `SaveManager.BeginLoad` re-runs on a rebuilt character (SaveManager.cs:58-65) so the upgrade must be idempotent; the pending-cooldown pattern (Character.cs:2118-2147) shows how a value that arrives before its consumer exists is parked — the mastery RPC arriving before `Character.Instance` must park the same way `TargetSheet` does (NetPlayer.cs:632-643).
- **Two players in one world:** `PassiveLoadout`'s owner rule (PassiveLoadout.cs:24-29) — every new check must read the OWNER character, never `Character.Instance`.
- **Perf:** the tally write is per hit (CombatAuthority.Land is the per-hit path per PERFORMANCE.md's checker, :88-90) — fixed-size, no dictionary, no string; the mastery percent lookup runs inside `Lift`, which runs inside every stat getter the server derives twice a second per player (NetPlayer.cs:582-583) — a table index, no search.

**Behaviours that stay exactly as they are (carried forward, never agreed — the user should see them):** the hotbar's layout (set separately on 2026-10-03: three rows of nine, no ultimate seat); every learned passive live, no limit (2026-09-21); `RandomItemChance = 30%` of one random non-worn item per kill; killer = last unit to cost health (Rewards.cs:13-14, 58) for XP and loot; killer-only pickup window; creatures dress themselves at random in every empty place (UnitEquipment.cs:57); the ring's caps and draft (its curve is now the character's, paid a third of each kill); a level-up's full heal.

## Part 4 — Open questions, each with the planner's recommendation

1. **Where the copy's finished attack/defence lives.** The tooltip sums on the player's machine today (ItemTooltip.cs:299-317 → `Rarities.Lifted`). *Recommend:* the server writes the finished piece attack / magic attack / defence / magic defence onto the copy whenever it forges, folds or quenches, and both the save and the wire carry those ints beside grade and folds; the player's machine and `AddPieceInto` only add them.
2. **How the station window shows costs and odds.** *Recommend:* the bag wire carries, per equipment copy, next-fold cost (material id, units, gold), quench cost, quench odds (three ints) as finished numbers; no separate reply message.
3. **Ink Critical is 0.6%–3% but `EquipBonuses.critPercent` is a whole-percent int** (EquipmentInstance.cs:226; UnitStatMath.cs:290 divides by 100). *Recommend:* widen `critPercent` to a float (same name, same unit), and add three new `EquipBonuses` fields — accuracy, evasion, castSpeedPercent — with `Hit`, `Flee`, `CastTimeMult` taking `EquipBonuses`.
4. **What a "design" is.** *Recommend:* a base-cost list on the `Item` itself (which band-1 material, how many, how much gold), so the existing gear items are the designs; empty list = not forgeable.
5. **Whether `EquipSlotType.Ring = 12` is deleted or left unread.** Its integer is pinned (Item.cs:4-10) and no asset holds it. *Recommend:* delete the member and the `ItemType.Ring` bucket.
6. **Materials in the random-item roll.** Settled 2026-09-22: the 30%-per-kill roll (`ItemCatalog.RandomConsumable`) hands out consumables only, and materials come from the creature's level and kind (`CreatureLoot`, [Mob Roster](../../3-world/mob-roster.md)).
7. **What creatures wear.** Settled 2026-09-22: no random wardrobe — a creature wears exactly its authored pieces, at the grade, folds and Ink authored on its own catalog entry ([Mob Roster](../../3-world/mob-roster.md), Gear state).
8. **Starter gear.** Settled 2026-10-07: a new character starts with nothing — no items, no gold. Gear is made, never seeded: the crafting tutorial's first run forges a Sword, a Chainmail and Iron Sabatons and folds each to +5 free, and the healing tutorial's first run gives the first potions free (`StarterKit.cs`, [Quests and Tutorials](../quests-and-tutorials.md)).
9. **Existing copies on old saves.** *Recommend:* every pre-V4 copy loads as Raw, folds 0, no Ink — not keeping `upgrade` as folds, because those refines were bought under the +3 attack rule.
10. **Learning: automatic or an action.** Settled 2026-10-03: an action — each mastery level gives one point, and the player spends it in the Skills window; nothing is learned automatically.
11. **What skills a creature has.** Settled 2026-10-04: only the skills it has bought with mastery points, in the same store and by the same rule as a player's. Nothing spends a creature's points yet, so no creature casts a skill.
12. **The window's name.** Settled: it is the **Skills** window, on **K**; the Grimoire is deleted (2026-09-21).
13. **Passives on weapon swap.** Settled 2026-09-21: nothing is ticked any more — every learned passive is live, and one whose weapon type is not held simply does not fire.
14. **Rounding of the quality lift** (e.g. an A +25 weapon: 25 × 1.8 × 1.30 = 58.5). *Recommend:* round half up in the static function — never `Mathf.RoundToInt`, which rounds .5 to even.
15. **Weapon types for masteries.** `WeaponType` has Sword, TwoHandSword, Dagger, Axe, TwoHandAxe, Mace, Spear, Staff, Bow, Katar, Knuckle, Firearm, Wand, Scythe, Shuriken. Masteries are keyed on the enum.

## New user-visible words this plan would need that the V4 design does not already name

- A "Merge" action label in the bag menu.
- The station window's title(s) and the Ink action's button word.
- Labels for the three new gear stat lines (Accuracy, Evasion, Cast Speed) only if a shorter word than those is wanted.
- The GM command keeps the name `/weapon`: `/weapon [type] [trademark] [folds] [grade]`, anything left out random.

Everything else uses the design's own words.
