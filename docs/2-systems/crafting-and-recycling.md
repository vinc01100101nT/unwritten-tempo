---
status: "the loop below is built and not yet play-tested; its costs and drop rates are first numbers, to be tuned in play"
owner: vince
last-updated: 2026-10-13
tags:
  - systems-combat-design
---

# Crafting & Recycling

*Gear is dropped Raw, and made worth more by work.*

> **Superseded.** Everything this page used to describe — a weapon arriving with a rolled set of skills, a
> Potential budget, Reforge, Add, Weapon Essence and Transmog — belonged to the rarity-and-kits design that
> [Gameplay System V4](gameplay-system-v4/README.md) replaced on 2026-09-20. The loop that took its place
> is written there, and this page is kept only so a link to it does not lead nowhere.

## What replaced it, in one paragraph

Gear is dropped, never made: a kill has a 10% chance to drop one piece of what the creature wears, always Raw
and unfolded, its base rolled, at a rarity the creature's kind decides. At a station in town you **fold** it with
Temper Dust to make it worth more, up to +10 — from +4 a fold can fail and take one fold back — and at +10 you
**quench** it with Quench Oil to move its grade one rung along the F-to-A ladder, to +0, or fail. Grades glow in one
sky blue, Flyff's, for every grade and rarity (`GradeGlow`); Raw shows nothing. A graded piece's picture glows at its
edge wherever it is drawn — bag, storage, station, hotbar, shop — but not on its card, and a graded piece on the floor
has a glowing sphere behind it. A graded weapon streams its weapon's enchant off its own model, drawn or put away —
Fire for Axe, Knuckle and Staff; Water for Greatsword and Spear; Ice for Bow, Sword and Katar; Nature for Wand, Mace
and Shuriken; Dark for Sniper, Dagger and Scythe — at Lvl 1 for F and E, Lvl 2 for D and C, Lvl 3 for B and A
(`GradeGlow.MakeWeaponEnchant`). An armour set — Body, Garment and Shoes, the hat not counting — raises a lightning
aura around the body at the LOWEST grade of the three (a missing piece is Raw, so nothing shows): a cloud that grows
from F and is full from D, then lightning from C, full at A (`GradeGlow.MakeSetAura`). Every body gets both, a
person's or a creature's. Neither effect fades when ground or a body sits close behind it, so both show from the top
camera, and neither is drawn into the blue see-through body shown behind a wall — only the body's own models are. **Recycling** a
piece returns half of what its folds and quenches took, and never less than 1 Temper Dust. **Ink** writes a stat onto it, one roll of one stat, and
better Ink writes a bigger number. **Salt** decides what a failed quench costs — and the better ones raise the
chance — and is spent either way.
Skills are not on the weapon at all: you buy and level them with the mastery points a weapon's mastery earns, and
the grade of what you hold caps that mastery; every skill level you bought counts at any grade.

---

## Related

- [Gameplay System — V4](gameplay-system-v4/README.md) — dropped gear, folding, the grades, Ink, Salt and mastery.
- [Weapon Roster — 14 Identities](active-skill-system-v3/weapon-roster-12-identities.md) — what each weapon is for.
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — the growth the ring owns.
