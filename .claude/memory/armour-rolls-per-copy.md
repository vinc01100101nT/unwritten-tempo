---
name: armour-rolls-per-copy
description: "A Head/Armor/Garment/Shield copy rolls rarity, DEF/M.DEF and 0–6 distinct main stats inside Item-authored ranges as it enters the world (ArmourRoll); the roll is saved (v11) and travels with the copy; creatures spawn wearing rolled copies and drop the exact one. Built 2026-09-13, unplayed."
metadata:
  node_type: memory
  type: project
---

**Built 2026-09-13 to the user's decisions (unplayed — `docs/1-planning/play-test-checklist/`).**

- **What rolls, and when:** `EquipmentInstance.CreateRolled` — the one door for anything entering the world — rolls
  rarity (the weapon odds 45/30/15/5/5) for weapons AND for the four armour places, then `ArmourRoll.Roll`: DEF and
  M.DEF inside `Item.defenseMin/Max` / `magicDefenseMin/Max`, a count of main stats inside `mainStatCountMin/Max`,
  WHICH stats by swap-remove (never the same twice), each value inside `mainStatValueMin/Max` floored at 1.
  Raw numbers only; rarity is applied at the sum.
- **The one sum:** `EquipmentInstance.AddPieceInto` — rolled armour adds `Rarities.Lifted(def/mdef)` (×1+0/3/9/18/30%)
  and each rolled stat + `Rarities.StatExtra` (+0/1/2/3/5); a WEAPON adds its flat fields with ATK/M.ATK lifted by
  the same ladder; anything unrolled adds its flat fields as authored. Refine and cards are added after, unlifted.
  The tooltip (`AppendCopyStats`) and the stat sheet read this.
- **Persistence:** `EncodeRoll`/`DecodeRoll` = "def,mdef,6 stats"; anything not exactly 8 ints loads UNROLLED (never a
  fake zero). `SaveData` v11 `equipRolls`/`bagRolls`; `BagWire.rolls`/`wornRolls`; `Character.CaptureWornInto` /
  `RestoreWornFrom` gained a `rolls` parameter. Older saves load every piece unrolled with flat bonuses.
- **Creatures:** on the server, `UnitEquipment.SeatStartingGear` seats `CreateRolled` copies (a watching player's
  machine seats plain `new EquipmentInstance(item)` copies and rolls nothing) — so a creature's WEAPON also rolls a
  random rarity + kit per life (only the Axolot wears one today; the user has NOT yet been asked whether that is
  wanted). `MobRespawn.Reactivate` calls `ReseatForNewLife` (fresh objects — a dropped copy may be in a bag) then
  re-derives. `LootDropper` hands over the worn INSTANCE (`wonWorn`) via `Rewards.PayLoot(winner, items, worn, gold)`.
- **Decisions made by the session, flagged in the plan:** head's DEF and M.DEF roll independently; block chance
  stays authored and unlifted; refine unlifted; no starter armour (`SeedBag` skips rolled slots); Garment's bag
  column shows M.DEF.
- **Gotchas:** the first armour items were authored 2026-09-15 (`item.leather-cap`, `item.padded-vest`,
  `item.travellers-cloak`, `item.wooden-buckler`) and none has been rolled in play yet; all-zero ranges roll DEF 0
  with `rolled = true`. A watched creature copy on a client rolls its OWN local numbers — harmless until creatures
  wear rolled armour, then a hit's on-screen number uses the client's DEF roll. The Armor tab of the item table
  edits the RANGES (`DrawRange`, red when max < min) and hides rarity.
