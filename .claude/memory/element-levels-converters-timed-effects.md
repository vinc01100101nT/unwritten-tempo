---
name: element-levels-converters-timed-effects
description: 2026-09-23 decisions — creature element levels 1-4 (RO chart, negatives heal), 8 converters, timed effects survive logout (paused), armour roster 5 slots x 3 materials with rolled words
metadata:
  type: project
---

Decided by the user on 2026-09-23, built the same day (not play-tested):

- **Creatures own their element at a level 1–4 and use it BOTH ways** — ordinary attacks land as it, and they are hit as it. This deliberately differs from real Ragnarok (monster melee there is non-elemental). Levels by kind (user, 2026-09-23): normal 1, elite 2, shadow 3. The shadow creatures' elements now cover all ten (the table is in docs/3-world/mob-roster.md). The element once called Shadow is **Dark** since 2026-09-26 (user: Holy and Dark counter each other). A NEUTRAL held weapon never overrides a creature's own element (every weapon is Neutral today).
- **The chart is rAthena's four pre-renewal tables copied cell for cell** (`db/pre-re/attr_fix.yml`, sha256 f785222b…a7a6); a **negative value HEALS the defender** (damage-after-defence × |value|, floor 1), skipping everything a landed hit sets off. Attacks have no level. Our old softer level-1 chart is gone.
- **Elemental forging was REJECTED.** Players get **converters** instead: 8 consumables, level 1 only, 5:00, weapon (ordinary attacks) or armour (what you're hit as). Names: Flame/Frost/Seismic/Lightning + "Elemental Converter" (RO) / "Armor Converter". Only water/earth/fire/wind — Ghost is deliberately excluded (neutral hits at 25%). Drop from creatures of their own element via the 30% consumable roll. A person has no element of their own (Neutral L1); the old unused player armour-element field was deleted.
- **Timed effects end ONLY on death.** Logout and cross-server travel save every state with seconds left; the clock PAUSES while away. Taunt is never saved (its taunter won't exist). Save v15, build string `unwritten-character-v15`.
- **Armour roster BUILT 2026-09-23:** slots Head/Body/Garment/Shoes (enum Head/Armor/Garment/Boots; the off-hand slot 4 was removed 2026-10-06), each in metal/leather/cloth = 12 designs; material decides what a fold buys (metal DEF+HP, leather ASPD+EVA, cloth MDEF+MP, head/shoes half); every armour piece rolls ONE word from its slot, each word on one slot only (Ironblood, Steadfast, Wellspring / Bulwark, Sanguine, Fierce / four element wards / Tireless, Anchored, Wayfarer — Anchored and Wayfarer built 2026-10-06). Worn words reach stats through ONE lift: UnitStates folds the unit's worn EquipBonuses into the sub-stat table. Gear evasion/accuracy now reach the hit roll. Leather Buckler keeps id item.wooden-buckler.
- **The full truth lives in the v4 docs**, not here: docs/2-systems/gameplay-system-v4/elements-and-converters.md, timed-effects-survive-logout.md, armour-roster.md. See [[the-five-anti-invention]].
