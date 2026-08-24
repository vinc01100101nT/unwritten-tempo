# Weapon Crafting & Economy (V3)

How players go from a random drop to a deliberate build. Companion to **Weapon Roster — 12 Identities** and **Active Skill System — V3**. Design converged 2026-07-28; numbers below are illustrative and still to be tuned.

> **Status: DESIGN ONLY — none of this is in code.** Verified 2026-08-24: nothing in the codebase mentions Potential, Essence, Reforge, Add or Transmog. What exists today is the rarity ladder below (the five tiers are real and drive item presentation) and the refine level, which equipment already carries and saves. Everything else on this page is unbuilt.

## Philosophy — fate → forge → commit

Three layers at three speeds, so players get surprise *and* agency without either killing the other:

- **Fate** gives you the build — the **ultimate** you roll (the build anchor) is **drop-only, never craftable**.
- **Forge** refines it — a finite, per-weapon **Potential** budget lets you Reforge/Add the 3 normal skills around that ult.
- **Commit** powers it — your **Ring** investment (slow, role-locked) is the deepest commitment; crafting shapes *loadout*, never *power*.

Every drop has a job, so nothing is pure trash: it's a build, a base to invest in, recycling fuel, or an appearance.

## The kit — max 4 slots

A weapon holds up to **1 ultimate + 3 normals**. The **ultimate is the build anchor** (see the 3-builds-per-weapon concept in the roster doc) and is **only ever set by the drop roll** — you can never craft one in. Normals are craftable.

## Rarity scales the drop (three axes at once)

A drop's rarity determines how many slots come pre-filled, its Potential budget, and whether an ult appears. *(Values illustrative — tune later.)*

| Rarity    | Skills pre-filled | Ultimate chance             | Potential (rough) |
| --------- | ----------------- | --------------------------- | ----------------- |
| Common    | 1                 | very low                    | ~30               |
| Uncommon  | 1–2               | low                         | ~35               |
| Rare      | 2                 | moderate                    | ~40               |
| Epic      | 2–3               | high                        | ~45               |
| Legendary | 3–4               | very high / near-guaranteed | ~50               |

So a Common is a sparse, low-Potential base you'll likely craft up or recycle; a Legendary often drops near-complete with a build anchor already in it. **Chasing an ult means chasing rarity.**

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
| **Add**             | fill an **empty normal slot** with a **random** skill from the weapon's pool (never the ult slot; never player-chosen)                                     | **yes**    | Essence + Gold                    |
| **Reforge**         | reroll unlocked **normal** skills; lock a skill at a rising Potential cost                                                                                 | **yes**    | Essence + Gold                    |
| **Refinement (+N)** | +Attack (weapon) / +Defense; the existing refine system                                                                                                    | **no**     | Essence + Refinement Stone + Gold |
| **Transmog**        | wear a **same-type** weapon's appearance (Sword look on a Sword only) — **appearance only**, never carries skills/stats/Potential; unlocks into a wardrobe | **no**     | Essence + Gold                    |

**Add & Reforge are stochastic** (random results, gated by finite Potential) — you steer and retry fate, you don't dictate it. That keeps naturally-good drops exciting. Deliberate agency comes from **which ult-base you choose to invest in**, not from hand-picking skills.

## Recycling — what bad weapons become

An unwanted weapon has two exits:

- **Recycle → Weapon Essence** — yield **scales with the weapon's rarity** (trash yields little), so you can't farm white drops into infinite Essence.
- **Keep as a transmog source** — collect its look into the wardrobe.

## The loop

> Drop (rarity-scaled kit + Potential) → keep the ult you want (build) → Add/Reforge the normals with Potential+Essence+Gold → Refine & Transmog freely → recycle the rest into Essence to fuel the next weapon.

## Health guards (why it doesn't break)

- **Ult is drop-only** → build anchors stay a hunt; no convergence onto one crafted best-ult.
- **Potential non-refillable + per-weapon** → no immortal god-weapon; you must keep finding bases. Skills freeze when spent, but the weapon stays usable.
- **Essence yield scales with base rarity** → no white-drop Essence farming.
- **Salvage yield keys off *base* rarity, not invested Potential** → no Potential→Essence→Potential laundering.
- **Transmog is appearance-only, same-type** → no skill/stat transfer backdoor.
- **Ring untouched by crafting** → crafting can't shortcut the role-lock.

## Quality-of-life rules (locked)

To cut avoidable frustration:

- A **Reforge never re-rolls a skill into itself** (a reroll always changes something).
- A weapon can **never hold duplicate normals**.
- **Preview the exact Potential cost** (including any lock cost) before confirming any craft.

## Bricking — the emotional low point to design against

The failure case: a great base with a great ult, Potential spent, but the supporting normals never landed — a permanently frozen, unsatisfying weapon. It doesn't break the economy, but it's the worst *feeling* state. Mitigations (all respect non-refillable Potential):

- **Bad-luck protection** — a reforge quality/synergy floor that rises after consecutive poor rolls, so a full Potential budget can't whiff entirely.
- **Size the Potential budget** so a high-rarity weapon has enough attempts to reasonably land 3 keepers — bricking should be rare, not a coin-flip.
- *(Optional — has a tradeoff)* **Ult Transfer** — move just the drop-only ult (the anchor) onto a fresh same-type base at a high Essence/Gold cost, so a bricked-but-great-ult isn't lost forever. Tradeoff: it weakens the ult loot-chase (you can keep upgrading one lucky ult's base), so it's only worth it if bad-luck protection alone doesn't cure the feel-bad.

## Open tuning items

- **Essence supply vs demand** — refinement *and* reforging both consume Essence; recycling yields must keep pace or players bottleneck.
- Exact **rarity → (skill count, ult chance, Potential)** curves.
- Reforge **lock-cost curve** (rising Potential per locked skill) — tune so perfecting a weapon can't be done inside one Potential budget (protects build diversity).
- Whether **Stones** appear on any action beyond Refinement.
