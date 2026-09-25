---
status: "SUPERSEDED BY V4 (agreed, not built) — the rarity output lift is built; the crafting economy is not"
owner: vince
last-updated: 2026-09-10
tags:
  - systems-combat-design
---

# Weapon Crafting & Economy (V3)

How players go from a random drop to a deliberate build. Companion to **Weapon Roster — 14 Identities** and **Active Skill System — V3**. Design converged 2026-07-28; numbers below are illustrative and still to be tuned.

> **Superseded in intent by [V4](../gameplay-system-v4/README.md)**, which removes rolled kits and rarity and makes gear crafted from materials. This page still records the rarity lift the code runs today.
>
> **Status: MOSTLY DESIGN.** Re-verified 2026-09-10: *Potential*, *Essence*, *Reforge* and *Transmog* appear in **zero** source files, and there is still no way to forge anything. What IS built: the five-tier rarity ladder driving item presentation, the refine level equipment carries and saves, the per-copy rolled rarity and rolled skill kit, and — since 2026-09-09 — the **rarity output lift**, which is live and scaling real damage. Everything else on this page is unbuilt.
>
> ⚠️ **The game has a "wardrobe", and it is NOT this one.** The in-game wardrobe recolours the character's clothing per part and saves the dye with the character. It has nothing to do with Transmog's appearance-collection below — same word, different feature. Don't read one as evidence of the other.

## Philosophy — fate → forge → commit

Three layers at three speeds, so players get surprise *and* agency without either killing the other:

- **Fate** gives you the build FIRST — the **ultimate** (the build anchor) is what a drop is chased for, and rolling one is the fast way to get it. Forging one in is also allowed, so a weapon that dropped without an anchor is a base worth feeding rather than trash.
- **Forge** refines it — a finite, per-weapon **Potential** budget lets you Reforge/Add the normals around that ult.
- **Commit** powers it — your **Ring** investment (slow, role-locked) is the deepest commitment; crafting shapes *loadout*, never *power*.

Every drop has a job, so nothing is pure trash: it's a build, a base to invest in, recycling fuel, or an appearance.

## The kit — what a drop actually rolls

**The same roll at every rarity.** Actives and passives are drawn independently, so one half never tracks the
other, and the ultimate is its own separate chance on top.

| Part | Outcome | Chance |
| --- | --- | --- |
| **Actives** | 3 | 50% |
|  | 4 | 50% |
| **Passives** | 3 | 50% |
|  | 4 | 50% |
| **Ultimate** | none | 75% |
|  | one | 25% |

Which gives this many skills on a weapon:

| Skills on the weapon | Chance |
| --- | --- |
| 6 | 18.75% |
| 7 | 43.75% |
| 8 | 31.25% |
| 9 (a full kit with its ultimate) | 6.25% |

These are the exact odds the roll produces, not a sample of it. So **7 skills is typical** and exactly **1 drop
in 16** carries the full nine — at any rarity.

**⚠️ Only the Sword can roll a kit at all today.** A kit is drawn from the PUBLISHED skills of that weapon type,
and the Sword's 22 are the only ones published — so every other weapon type currently rolls an **empty** kit, not
a short one. A dropped Bow grants no skills whatsoever until its pool is authored.

The **ultimate is the build anchor** (see the 3-builds-per-weapon concept in the roster doc). Both halves are
craftable — the normals and the ultimate alike — so a weapon that dropped without an anchor is a base to work on
rather than something to recycle.

**What stops everyone converging on one best ultimate is Potential** — finite, non-refillable, per weapon. You cannot keep rerolling a single base until it gives you what you want. It is the ONLY thing holding build diversity up, so size it deliberately rather than leaving it to be tuned later.

## Rarity changes how hard a kit hits, never what is in it

**Rarity does not decide what is in a weapon, only how hard what is in it hits.** Every drop rolls the same kit
— 3–4 actives, 3–4 passives, and a **25% chance** of an ultimate — whatever tier it is. A Common and a Legendary
can carry the identical nine skills; the Legendary's just land harder.

**What "+30%" actually means.** The lift is added to the multiplier your ring already gives that skill, not to
the finished number. With no ring investment in that skill's stats, a Legendary is +30% damage. The more your
ring already contributes, the smaller a share those same 30 points are — at double the ring multiplier the same
Legendary works out to +15%. It is a fixed **+30 points for everyone**, which is exactly why it is added rather
than multiplied: a Legendary is worth the same to a new character as to a maxed one, instead of paying veterans
more.

| Rarity    | How often it drops (**built**) | Output lift (**built**) | Kit size | Ultimate chance | Potential (rough, design) |
| --------- | ------------------------------ | ----------------------- | -------- | --------------- | ------------------------- |
| Common    | 45%                            | +0%                     | same     | same            | ~30                       |
| Uncommon  | 30%                            | +3%                     | same     | same            | ~35                       |
| Rare      | 15%                            | +9%                     | same     | same            | ~40                       |
| Epic      | 5%                             | +18%                    | same     | same            | ~45                       |
| Legendary | 5%                             | +30%                    | same     | same            | ~50                       |

**Scope of that claim:** the output lift is the only thing rarity does to a skill's POWER. Elsewhere it lifts the
weapon's own attack by the same ladder, lifts a rolled piece of armour's defence and adds a flat extra to its main
stats, and **colours an item's name and slot border**. It does not touch the kit, the
ultimate, or the refine level. The unbuilt crafting economy below still keys off rarity in three places —
Potential budget, recycle yield and salvage yield — and deliberately so: the yield rules are what stop white
drops being farmed into unlimited Essence. None of that is in code.

**Which numbers it reaches:** a skill's OUTPUT only — damage, execute power, a heal, a shield, damage-per-tick,
how far a sub-stat moves, and what one outgoing-damage charge is worth. It never touches a duration, a proc
chance, a hit count or a cleanse count. Full split: the Channels Catalog in the V3 doc.

**Why it is only that.** Every drop can roll an ultimate and a full kit, so no tier is fodder before you look at
it — a Common can anchor a build. **Which** skills you get is fate; **how hard** they hit is the tier; and neither
is the other's job.

**Rarity is rolled and never craftable.** Nothing raises a weapon's tier — that is the one axis no amount of work
can move, and it is why a drop is still worth opening when you can forge the rest yourself.

## Resources

| Resource           | Scope                                  | Role                                                                                                                                             |
| ------------------ | -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Potential**      | per-weapon, finite, **non-refillable** | gates **only** skill **Add + Reforge** (the RNG part). Spent out → that weapon's *skills* freeze; you keep using, refining, and transmogging it. |
| **Weapon Essence** | account-wide                           | universal "powder" recycled from unwanted weapons; **required by all weapon modifications** (with Gold). The thing every bad weapon becomes.     |
| **Stones**         | account-wide, per-activity             | material for the heavier mods (e.g. Refinement Stone); reserved for big mods, not every reforge.                                                 |
| **Gold**           | account-wide                           | soft sink on everything.                                                                                                                         |

## Actions & their costs

| Action              | What it does                                                                                                                                               | Potential? | Other cost                        |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------- | --------------------------------- |
| **Add**             | fill an **empty slot** with a **random** skill from the weapon's pool — the ult slot included, since an ultimate can be forged in; never player-chosen                                     | **yes**    | Essence + Gold                    |
| **Reforge**         | reroll unlocked **normal** skills; lock a skill at a rising Potential cost                                                                                 | **yes**    | Essence + Gold                    |
| **Refinement (+N)** | +Attack (weapon) / +Defense; the existing refine system                                                                                                    | **no**     | Essence + Refinement Stone + Gold |
| **Transmog**        | wear a **same-type** weapon's appearance (Sword look on a Sword only) — **appearance only**, never carries skills/stats/Potential; unlocks into a wardrobe | **no**     | Essence + Gold                    |

**Add & Reforge are stochastic** (random results, gated by finite Potential) — you steer and retry fate, you don't dictate it. That keeps naturally-good drops exciting. Deliberate agency comes from **which ult-base you choose to invest in**, not from hand-picking skills.

## Recycling — what bad weapons become

An unwanted weapon has two exits:

- **Recycle → Weapon Essence** — yield **scales with the weapon's rarity** (trash yields little), so you can't farm white drops into infinite Essence.
- **Keep as a transmog source** — collect its look into the wardrobe.

## The loop

> Drop (a rolled kit + Potential; the tier sets how hard it hits) → keep the ult you want (build) → Add/Reforge the normals with Potential+Essence+Gold → Refine & Transmog freely → recycle the rest into Essence to fuel the next weapon.

## Health guards (why it doesn't break)

- **Potential is finite and per-weapon** → you cannot reroll one base into the ultimate you want, so anchors stay varied even though they are forgeable.
- **Rarity is rolled and never craftable** → the one axis no amount of work can raise, which is what keeps a drop worth opening.
- **Potential non-refillable + per-weapon** → no immortal god-weapon; you must keep finding bases. Skills freeze when spent, but the weapon stays usable.
- **Essence yield scales with base rarity** → no white-drop Essence farming.
- **Salvage yield keys off *base* rarity, not invested Potential** → no Potential→Essence→Potential laundering.
- **Transmog is appearance-only, same-type** → no skill/stat transfer backdoor.
- **Ring untouched by crafting** → crafting can't shortcut the role-lock.

## Quality-of-life rules (locked)

To cut avoidable frustration:

- A **Reforge never re-rolls a skill into itself** (a reroll always changes something).
- **Reforge rerolls normals only.** An ultimate can be forged IN with Add but never rerolled once it is there,
  so choosing an anchor is a commitment. Open question: whether that is the intent, or Reforge should reach the
  ult slot too.
- A weapon can **never hold duplicate normals**.
- **Preview the exact Potential cost** (including any lock cost) before confirming any craft.

## Bricking — the emotional low point to design against

The failure case: a great base with a great ult, Potential spent, but the supporting normals never landed — a permanently frozen, unsatisfying weapon. It doesn't break the economy, but it's the worst *feeling* state. Mitigations (all respect non-refillable Potential):

- **Bad-luck protection** — a reforge quality/synergy floor that rises after consecutive poor rolls, so a full Potential budget can't whiff entirely.
- **Size the Potential budget** so a weapon has enough attempts to reasonably land a kit worth keeping — bricking should be rare, not a coin-flip.
- *(Optional — has a tradeoff)* **Ult Transfer** — move an ult (the anchor) onto a fresh same-type base at a high Essence/Gold cost, so a bricked-but-great-ult isn't lost forever. Tradeoff: you can keep carrying one lucky anchor onto better and better bases, which softens the reason to open the next drop.

## Open tuning items

- **Essence supply vs demand** — refinement *and* reforging both consume Essence; recycling yields must keep pace or players bottleneck.
- Exact **rarity → Potential** curve, and whether the output lift ladder (0/3/9/18/30) wants widening once it has been played.
- Reforge **lock-cost curve** (rising Potential per locked skill) — tune so perfecting a weapon can't be done inside one Potential budget (protects build diversity).
- Whether **Stones** appear on any action beyond Refinement.

---

## Related

- [Crafting & Recycling](../crafting-and-recycling.md) — the player-facing half of this same design.
- [Weapon Roster — 14 Identities](weapon-roster-12-identities.md) — the pools an Add or Reforge draws from.
- [Active Skill System — V3](README.md) — why the ult is the build anchor and the Ring is the power.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the commitment layer crafting must never shortcut.
- [Unified Unit Stats](unified-unit-stats.md) — where drops come from: a mob drops what it wears.
- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — a rolled kit as recollection, which is how this flavour must be written.
- [Roadmap to Launch](../../1-planning/roadmap-to-launch.md) — M3, the milestone waiting on this.
