---
name: rarity-lifts-skill-output
description: "Rarity lifts NOTHING. Since 2026-10-12 it is how rare an item is to come by: a forged weapon = its trademark's rarity (forge rolls a rarity 50/30/14/5/1, then a trademark of it; every ultimate Legendary), materials by kind+tier, everything else Common. Colours the name only."
metadata:
  node_type: memory
  type: project
---

**Rarity changes no number** (V4 removed V3's rarity — the 0/3/9/18/30 lift on skill output, weapon attack and
armour, the kit sizes and drop weights — on 2026-09-20). The user brought rarity back on 2026-10-12 with a different
meaning: *"we do not give rarity based on grade, we give rarity based on how rare the weapon appears when forging."*

**The ladder:** Common, Uncommon, Rare, Epic, Legendary (`Rarity` enum, numbers pinned 0–4, in `Rarity.cs`).

**Who owns what:**
- `Rarities.Of(item, copy)` — THE answer for any item: a weapon = `Rarities.OfTrademark(Trademarks.SkillOf(copy))`
  (Common with no trademark); a material = `Rarities.OfMaterial(kind, step)`; armour, consumables, quest/etc = Common.
- `Rarities.OfTrademark(skill)` — an ultimate is Legendary from its TIER (never authored); any other skill reads its
  authored `SkillDef.trademarkRarity`, guarded by the companion flag `trademarkRaritySet` (unset reads Common at
  runtime; `tools/check-skill-data.py` FAILS a published qualifying non-ultimate with it unset, or authored Legendary).
- `Rarities.OfMaterial` — metal/cloth/leather Common; Ink 1 Uncommon, 2–3 Rare, 4–5 Epic; Salt 1 Uncommon, 2 Rare,
  3 Epic. Derived, not authored, so it cannot drift from the item's own kind and step.
- `Rarities.Color` / `Label` / `Tag` — the one colour and word: Common `UiKit.TextMain`, Uncommon `UiKit.Good`, Rare
  `UiKit.RarityRare`, Epic `UiKit.RarityEpic`, Legendary `UiKit.RarityLegendary`.

**The forge roll** (`Trademarks.Roll`, server, two `CombatRng` draws): rarity by share
(`CraftingMath.TrademarkRarityAt`, shares 50/30/14/5/1 renormalised over the rarities that weapon type has), then a
trademark of that rarity evenly. Chances via `CraftingMath.TrademarkChance` (pinned in Checks.cs `TrademarkRoll`);
the sword: Common 10% each (5), Uncommon 7.5% (4), Rare 4.67% (3), Epic 1.67% (3), ultimates 0.33% (3).

**Where it shows:** item tooltip title colour + a line naming it, bag rows, the selected unit's worn list
(`WornList` carries a 4th field, the server's rarity), floor name plates (`NetGroundDrop.rarityIndex` SyncVar). A
watcher never holds the copy, so the server sends the finished rarity for the worn list and the floor.
