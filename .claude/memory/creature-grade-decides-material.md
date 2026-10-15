---
name: creature-grade-decides-material
description: "2026-10-16 redesign — a creature's grade decides its ONE material's tier; newbie maps Raw +0 only; base HP 160; tags are the sell/drop/store/trade rule"
metadata:
  node_type: memory
  type: project
  originSessionId: 83019dbf-fb4f-4716-9bf2-815f8aa8394c
  modified: 2026-10-15T18:14:54.009Z
---

Built 2026-10-16 (not play-tested), agreed over a long session with five red-team rounds:

- **Grade and folds are the only creature difficulty levers.** No level-gap rule, no level-scaled gear (user ruled out a "third lever").
- **Grade decides the material** (flipped from "grade follows the rarest drop"): one material per creature; normal Raw..C = Common..Legendary, elite only D/C/B = Rare/Epic/Legendary (elites never below D — party content). Tables live in `CreatureMaterials.NormalTierByGrade/EliteTierByGrade`; the materials check reads them.
- **Every tier averages 10 × level per normal kill** (prices 20/100/500/2,000/10,000 per level); an elite kill = 100 normal kills (D: 2 Rare in two piles, C: 50% Epic, B: 10% Legendary).
- **Newbie maps** lst_fild01 + prl_fild01: Raw +0 normal creatures only (checker enforces). F +0 kills a level 1 player in 2 hits — F is ×3 Raw's weapon, not a small step.
- **Base HP 160** (was 20) for every unit, so the lowest-VIT level 1 player in the starter set takes ≥5 blows. Cutting weapon base instead was rejected: defence comes off every blow, so it wrecks fast/pair weapons.
- Starter kit: Raw +0 Sword, Raw +5 Chainmail/Iron Sabatons/Chain Mantle, 100 Newbie Potion (heals 30, all four tags). Iron Helm/Leather Cap/Padded Hood deleted; no creature wears headgear.
- **Item tags Undroppable/Unsellable/Unstorable/Untradeable are THE rule**; price 0 no longer means unsellable.
- Deploy step: `tools/pay-out-deleted-items.py --apply` with all servers stopped pays holders of the 43 deleted items.

Related: [[mobs-get-everything-players-get]], [[ground-items-visible-to-everyone]].
