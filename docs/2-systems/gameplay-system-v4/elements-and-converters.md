---
status: "BUILT 2026-09-23 — not play-tested. Decided with the user on 2026-09-23; every creature's element re-set 2026-09-26."
owner: vince
last-updated: 2026-09-26
tags:
  - systems-combat-design
---

# Elements, Element Levels & Converters

Every creature has an **element** and an **element level**, and uses both ways: its ordinary attacks land as that
element, and it is hit as that element. Players have no element of their own; they borrow one for a while with a
**converter**. The chart is Ragnarok's.

## 1. The chart

- **Ragnarok's four pre-renewal element tables, copied cell for cell** from rAthena's
  `db/pre-re/attr_fix.yml` (sha256 `f785222b…a7a6`, 400 cells, verified against the file). Rows are the attacking
  element, columns the defending element, with rAthena's element names. The tables live in `Element.cs`, in whole
  percent, and are pinned by the game-maths tests.
- **Only the defending side has a level, 1 to 4.** An attack never does. The higher the level, the more the defender
  shrugs off what it resists and the harder it falls to what it is weak to.
- **A value below zero HEALS the defender.** It gains what the hit would have dealt after its defence, times how far
  below zero the chart goes, at least 1 (`Elements.ElementHeal`). A heal sets off nothing a landed hit does — no
  reflect, life steal, mana leech, passive, flinch or damage number — and shows as green "+N". Crits heal more,
  because a crit ignores defence. A beat on a creature already dead shows the faded "0" an immune hit shows.
  **Damage over time, reflect and delayed Brace damage never go through the chart**, so they never heal.
- **Exactly 0 is immune**, exactly as before: a faded "0" on a creature, nothing on a person.

A few cells, to read the shape:

| Attack → defender | Lv 1 | Lv 2 | Lv 3 | Lv 4 |
| --- | --- | --- | --- | --- |
| Fire → Fire | 25% | 0% | −25% | −50% |
| Water → Fire | 150% | 175% | 200% | 200% |
| Earth → Fire | 50% | 25% | 0% | 0% |
| Neutral → Ghost | 25% | 25% | 0% | 0% |

## 2. Who has which element

- **A creature's element and level are authored on its creature data** (`element`, `elementLevel`), edited in the
  Mob Table's **elem** and **Element Lv** columns, and copied onto the creature at the moment it spawns — on the
  server and on every watching machine — so the two can never disagree.
- **Levels by kind:** every **normal** creature is **level 1**, every **elite level 2**, every **shadow level 3**.
- **Normals and elites** take their element from where they live, following the [World Map](../../3-world/world-map.md)'s
  biome table and the level tiers: a creature of levels 1–33 is only Neutral, Earth or Water; 34–66 adds Wind and
  Fire; 67–99 adds Poison, Holy, Dark, Ghost and Undead. Set 2026-09-26; each creature's value lives in its
  creature data only.
- **The shadows' elements** cover all ten, as decided in the [World Map](../../3-world/world-map.md): Neutral — Asterion
  (Fist); Holy — Seraphine (Wand); Earth — Tharos (Warhammer); Water — Orion (Spear), Mordane (Greatsword); Fire — Varkhaz (Axe),
  Kaelen (Staff); Dark — Vael (Sniper); Undead — Morwen (Scythe); Poison — Velmora (Dagger); Ghost — Nysara (Katar); Wind — Celestia (Bow), Riven (Shuriken), Veyra (Sword). A shadow shows its Kashira's name. Only four of them — the Water, Earth, Fire and Wind ones —
  can be answered with a converter today. (Full table in [Mob Roster](../../3-world/mob-roster.md).)
- **A creature's ordinary attack** lands as: a weapon converter's element while one runs; otherwise its held
  weapon's element if that weapon HAS one; otherwise its own element. A weapon with no element (every weapon today)
  never overrides a creature's own.
- **A person** is hit as Neutral at level 1, and attacks as their weapon's element (Neutral today), unless a
  converter says otherwise.
- **A skill keeps its own authored element** — a fire bolt is Fire whoever casts it. Only ordinary attacks, and
  skill strikes that resolve as one, take the attacker's element.
- **Nothing on screen shows a target's element.**

## 3. Converters

Eight consumables, each running one skill with the **Element Converter** effect:

| Item | Element | Converts |
| --- | --- | --- |
| Flame Elemental Converter | Fire | Weapon — ordinary attacks land as it |
| Frost Elemental Converter | Water | Weapon |
| Seismic Elemental Converter | Earth | Weapon |
| Lightning Elemental Converter | Wind | Weapon |
| Flame Armor Converter | Fire | Armor — you are hit as it, at level 1 |
| Frost Armor Converter | Water | Armor |
| Seismic Armor Converter | Earth | Armor |
| Lightning Armor Converter | Wind | Armor |

- **Level 1 only, for 5:00.** A second converter on the same slot **replaces** the first; one weapon and one armour
  converter run side by side. It never stacks — one element on one slot has no "twice".
- **Ghost is deliberately absent:** a level-1 Ghost armour takes 25% from every neutral hit.
- **The effect works on creatures too** — any creature skill may carry it, with any of the ten elements.
- **Drops:** a creature of Fire, Water, Earth or Wind gives up one of its own element's two converters as its find
  — 5% a kill for a normal, 50% for an elite, 100% for a shadow ([Town Supplies and Hunting Finds](../town-supplies-and-hunting-finds.md)); a
  creature whose element has no converter (Neutral, Poison, Holy, Dark, Ghost, Undead) never drops one. The element lives once, on the converter's skill; the item carries no copy.
- **Weight 8. No shop sells one and no shop buys one.**

## 4. Where it lives in the code

- The chart, the heal and the converter level: `Element.cs` (`Elements.Modifier(attack, defense, level)`,
  `ElementHeal`, `ConverterLevel`).
- The two converter states `WeaponElement` / `ArmorElement` and the one answer both kinds of unit read:
  `UnitStates.BeginElement`, `AttackElementOr`, `DefendingElementOr`.
- The heal branch: `Health.HealedByElement` (creatures) and `Character.HealedByElement` (people); the log reason
  "healed by its element".
- The effect kind: `SkillEffectKind.ElementConverter`, with `SkillEffect.converts` (Weapon or Armor).
- The drop split: `ItemCatalog.RandomConsumable(creatureElement)`.

## Related

- [Timed Effects Survive Logout](timed-effects-survive-logout.md) — a converter, like every timed effect, ends only
  on death.
- [Mob Roster](../../3-world/mob-roster.md) — every creature's kind, and the shadows' elements.
- [Gameplay System — V4](README.md)
