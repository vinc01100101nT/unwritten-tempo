---
status: Locked 2026-07-27; Fist and Kick added and the weapon groups set 2026-09-25; Shield and Kick replaced by Katar and Shuriken, Firearm renamed Sniper, Fist made the two-path weapon, 2026-10-06
owner: vince
last-updated: 2026-10-06
tags:
  - systems-combat-design
---

# Weapon Roster — 14 Identities (V3)

This is the **canonical weapon lineup** for the V3 skill system. The one-sentence identity per weapon is the **litmus test for every authored skill**: *if a proposed skill doesn't reinforce its weapon's sentence, it belongs on another weapon.* Companion to the **Active Skill System — V3** design doc (that owns the channels/effects/scaling rules; this owns the weapon identities).

## 1. The litmus test — one sentence per weapon

| Weapon | Identity (every skill must reinforce this) | Kashira (the wielder the identity comes from) |
| --- | --- | --- |
| **Sword** | Win duels through precise single-target bursts and timed defence. | Veyra, the Last Petal |
| **Greatsword** | Delete groups with huge cleaves. | Mordane, King Beneath the Waves |
| **Axe** | Snowball through relentless aggression. | Varkhaz, the Ever-Burning |
| **Warhammer** | Control enemies by denying their actions. | Tharos, the Starbreaker |
| **Spear** | Control enemy positioning. | Orion, the River That Remembers |
| **Fist** | Overwhelm with physical bursts, or stand in front and take the hits — or both. | Asterion, the Light Beyond Night |
| **Bow** | Win through mobility and spacing. | Celestia, the Wandering Sky |
| **Dagger** | Kill slowly with poison from the blade. | Velmora, the Poisoned Rose |
| **Katar** | Finish targets with chains of critical hits. | Nysara, the Face Without a Name |
| **Shuriken** | Many blades: thrown, they bounce through a crowd; swung, one swing lands two or three times. | Riven, the Red Autumn |
| **Sniper** | Land one painful shot from as far away as possible. | Vael, the Moonless Eye |
| **Staff** | Dominate areas with destructive magic. | Kaelen, the Ember Beneath Snow |
| **Wand** | Keep allies alive. | Seraphine, Before the Dawn |
| **Scythe** | Slowly overwhelm enemies with curses. | Morwen, the Last Bloom |

Each weapon is what the artist kept of one of **the Fourteen Kashira** — the champions he imagined to fight Chaos, from before his paranoia turned them into the fourteen shadows. Their names, styles, themes and legends, and the whole story of the battle, are in `../../3-world/story-setting-canon.md`. The identity sentence is the litmus test for skills; the Kashira's legend and style are the flavour every skill must sound like. A skill that doesn't fit the Kashira doesn't fit the weapon.

**Learning is diegetic:** a weapon remembers its Kashira in fragments, and the skills a player buys with its mastery are *which memories they chose to draw out*. Write that flavour in those terms.

## 2. Full identity map

*Effects are named in the current vocabulary: every timed stat change is a **sub-stat buff or debuff**, so "self-Guard" is a damage-reduction buff, "Slow" is a movement-speed debuff, and "Vulnerability" is the damage-reduction pile carrying a negative. Only the ailments (Poison / Bleed / Burn / Stun / Freeze / Silence) and the standalone kinds (Shield, Taunt, Parry, Brace, Execute, Dash, Knockback, Pull, Cleanse) are verbs of their own.*

| Weapon | Group | Specialty | Mastery raises | Attack |
| --- | --- | --- | --- | --- |
| Sword | Might + Finesse | Fast strike combos that burst one enemy down; parries and braces at the right moment to win the exchange. | Attack Speed, Attack, HP | melee, reach 0.5 |
| Greatsword | Might | Wide cleaves that hit every enemy in a broad arc; built to wipe out packs, not to duel. | Attack, Critical, HP | melee, reach 0.5 |
| Axe | Might + Finesse | Stacks Bleed; gains speed and a damage buff that keeps rising while it keeps attacking; gives up all defence to do it. | Attack Speed, Attack, Critical | melee, reach 0.5 |
| Warhammer | Might | Stuns and knocks back, interrupting what enemies were about to do; never taunts. | Attack, HP, Accuracy | melee, reach 0.5 |
| Spear | Might | Pulls enemies in, dashes through their line, and cuts the defence of whatever it moves. | Accuracy, Attack, HP | melee, reach 1.0 |
| Fist | Might | Two paths, chosen by which skills a player learns. **Offence:** builds energy, then spends all MP in one enormous physical blow (in the spirit of Asura Strike); energy blasts that still deal physical damage. **Defence:** taunts, cuts damage taken, blocks, reflects damage back. A player can build one path or mix both. | Attack, HP, MP | melee, reach 0.5 |
| Bow | Finesse | Slows enemies and dashes away to keep its distance; fights on the move. | Evasion, Accuracy, Attack Speed | ranged, reach 5 |
| Dagger | Finesse | Stacks poison on one target with every hit; the poison does its damage over time. | Attack Speed, Evasion, Attack | melee, reach 0.5 |
| Katar | Finesse | Raises its crit chance and crit damage as the fight goes on; Execute finishes low-HP targets. | Critical, Evasion, Attack Speed | melee, reach 0.5 |
| Shuriken | Might + Finesse | **Thrown (ranged):** normal attacks, and some skills, bounce to nearby enemies. **Swung (melee):** each swing can hit two or three times, and never bounces; its signature blow drops the wielder's HP to 1 for one huge physical hit. | Attack Speed, Accuracy, HP | melee (STR) or ranged (DEX), whichever stat is higher; ranged reach 3, melee reach 0.5 |
| Sniper | Finesse + Arcane | One magic shot that can crit — the only magic in the game that can; the further the target, the more it hurts; the longest reach in the game. | Critical, Accuracy, Magic Attack | ranged, reach 7 |
| Staff | Arcane | Area nukes, Burn, and Freeze and slows to lock an area down. | Magic Attack, Cast Speed, MP | melee, reach 0.5 |
| Wand | Arcane | Heals, regeneration, damage shields, buffs, and Cleanse. | MP, Cast Speed, HP | melee, reach 0.5 |
| Scythe | Might + Arcane | Silence, defence cut and mana drain that wear an enemy down over a long fight. | Magic Attack, MP, HP | melee, reach 0.5 |

Warhammer and Spear raise the same three stats; every other weapon's three are a set of its own. Each mastery level adds +0.5% to each of the three (see V4); the table lives in code, `WeaponMasteryStats.Raises`.

**The weapon groups (decided 2026-09-25, revised 2026-10-06).** Every weapon sits in one of four groups:

| Group | Weapons |
| --- | --- |
| Might | Greatsword, Warhammer, Spear, Fist |
| Finesse | Bow, Dagger, Katar |
| Arcane | Staff, Wand |
| Hybrid | Might + Finesse: Sword, Axe, Shuriken · Might + Arcane: Scythe · Finesse + Arcane: Sniper |

A weapon's group says which families its skills come from; a Hybrid weapon's pool mixes two. **Each skill still carries exactly one family** — Might, Arcane or Finesse — and that family sets its damage type, so Hybrid is a grouping of weapons, never a fourth family on a skill. The Sword already mixes: 16 of its 22 skills are Might and 6 are Finesse. **The Fist is pure Might:** its MP-fuelled blow and its energy blasts are physical damage.

**The Sniper's magic (decided 2026-09-25 and 2026-10-06, built 2026-10-06).** The Sniper is the only weapon whose magic can crit: every other unit's magic never crits, so no spell pierces defence the way a crit does. In a Sniper's hands a magic crit is a physical crit's twin — the crit chance and the target's LUK shield work the same way, the hit is full magic attack × the crit multiplier, and it ignores magic defence. Since magic also never misses, it is the strongest kind of hit in the game. **Its ordinary attack is a magic shot:** magic attack against magic defence, it cannot miss, it can crit, and it grows by **2% for every metre** to its target. Any skill strike can also grow with distance through the Damage effect's per-metre setting in the Skill Composer; the Sniper's own skills are not authored yet.

**The two all-in blows (decided 2026-10-06).** The Fist's offence path spends all MP on one blow; the Shuriken's melee path drops HP to 1 for one. The effects exist: **Spend All Mana** and **Drop Health To One** empty the caster, and every strike after them in the same cast grows its damage multiplier by a set amount per point given up. The two skills themselves are not authored yet. **The Shuriken's bounce (decided and built 2026-10-06):** thrown, its ordinary attack leaves the hand, hits its target, jumps to up to 3 more enemies — each the nearest one not hit yet within 2.5 of the last, each worth 75% of the hit before — and flies back to the hand; the server decides every jump when the blade arrives. Any Single Target skill tick can bounce the same way (the bounce settings in the Skill Composer), and the Extra Bounce Jumps sub-stat lets a skill or passive add jumps for a while. Thrown and swung each have their own attack pose. **Its multi-hit (decided 2026-10-06):** a swung one's two-or-three hits per swing need no new code — they are authored in the Skill Composer as a Shuriken skill that fires on a hit trigger; that skill is not authored yet.

## 3. The three control weapons — each owns a distinct battlefield question

- **Fist (defence path) → "Hit me."** — draws fire via Taunt, blocks, absorbs and reflects.
- **Warhammer → "You don't get to act."** — Stun + Knockback, no Taunt (Taunt is the Fist's; keeping them apart is what makes the split clean).
- **Spear → "Stand over there."** — Pull + Dash-through + a damage-reduction debuff; rearranges, doesn't disable.

## 4. Magic AoE is split three ways (no Staff monopoly)

- **Staff = burst AoE** (delete the pack now).
- **Scythe = attrition AoE** — a curse-field that wears a pack down.
- **Wand = support AoE** — party heal.

Three completely different reasons to bring magic AoE.

## 5. Weapon-type & engine mapping (kept honest)

Real `WeaponType` enum today: **Sword, TwoHandSword, Dagger, Axe, Mace, Spear, Staff, Bow, Katar, Knuckle, Firearm, Wand, Scythe, Shuriken**. Players read four of them by their roster names (`WeaponTypes.Label`): Greatsword, Warhammer, Fist and Sniper.

- **Maps to a type:** Sword→Sword, Greatsword→TwoHandSword, Axe→Axe (Berserker; *dual-wielded like the Katar: one item fills both hands, never a separate type or slot, and every basic swing lands a second hit from the other hand at half power, 90% of the way through*), Warhammer→Mace, Spear→Spear, Fist→Knuckle, Dagger→Dagger, Katar→Katar, Bow→Bow, Sniper→Firearm, Staff→Staff, Wand→Wand, Scythe→Scythe and Shuriken→Shuriken. Every weapon has its own attack animation, cloned from a Ragnarok Online sprite attack (2026-10-09).
- **Melee or ranged** is one rule, `WeaponTypes.SwingsRanged`: a bow and a sniper always swing ranged; a shuriken swings ranged when its holder's total DEX is above their total STR (a tie is melee), and then reaches its own 3 instead of the bare-hand 0.5. The server decides it; a player's machine shows the reach the server sent.
- **No off-hand (built 2026-10-06).** Shields are gone, and with them the off-hand slot. Block chance is a sub-stat only a skill grants, for the Fist's defence path; reflect already was one.
- **Attack animations:** only the sword swing, the bow draw and the bare-hands punch are real clips. The Fist plays the punch, the Sniper the bow draw, and the thrown Shuriken (its own pose, separate from its swing) the single-target spell release; every other weapon's attack slot plays the sword swing as a stand-in until its own clip exists. Several weapons still share one attack slot — Greatsword, Axe and Warhammer share the Sword's, Dagger and Katar share one, Staff and Wand share one, and the Fist shares the bare hands' — so they cannot get separate clips until each has its own. Every weapon's model comes from one low-poly pack (mounted 2026-10-08, every shape built in blue): Sword, Greatsword, Axe, Warhammer, Dagger, Spear, Staff and Bow have their own shape; the Katar holds a second dagger, the Wand a second staff, the Scythe a long axe, the Shuriken a third dagger and the Sniper a second bow until models of their own arrive; the Fist holds nothing and fights bare-handed.
- **Sniper:** the "reload rhythm" is approximated by a **long per-skill cooldown** for v1 (already a channel); a true ammo/reload state machine is deferred.

## 6. Authoring plan & open items

- **Grid = 14 weapons, each with its own skill POOL** — the pool holds 3 ultimates + however many normals that weapon's identity naturally supports (pool size is **not fixed** — a weapon can have as few or as many normals as make sense; author the ones that reinforce §1, no filler to hit a quota). **Since [V4](../gameplay-system-v4/README.md), a weapon carries no skills at all:** every skill in a pool names the **mastery level** in that weapon at which a character can buy it with that weapon's mastery points, the skill it needs first (only when it literally uses that skill), and its max level. The tree rules are the `skill-tree` skill. A learned skill may only be used while holding that weapon.
- **Scaling role-lock is load-bearing** (Heal→MP, Damage→Atk/MAtk) — self-check affinity weights on every skill.
- **Universal-but-specialized rule (authoring heuristic, from the Sword pass)** — *every ult should have at least one normal that **specializes** in it while still benefiting the other two ults*, building a shared spine so ult-builds aren't siloed. The specialization must be **asymmetric** (one ult gains most, or it's filler); cap at **\~1 per ult**; keep the other normals **sharply single-purpose**; and **not every normal must be build-optimal** (deliberate "trash-ish" slots are fine — players discover their build). It's a **heuristic, not a law** — don't force the resource→specialist→ult shape onto weapons whose identity resists it. See the **Sword — Skill Roster** doc for the worked example.
- **Sword is authored** — see **Sword — Skill Roster**. The other 13 pools start fresh; author each straight from its one-sentence identity (§1) + the channels/effects/scaling rules in the V3 docs.

---

## Related

- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — the Fourteen Kashira these identities are the mechanical half of.
- [Sword — Skill Roster](sword-skill-roster.md) — the one identity taken all the way to a composed pool.
- [Active Skill System — V3](README.md) — the channels, effects and scaling every pool is built from.
- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the bands to author the other thirteen pools within.
- [Gameplay System — V4](../gameplay-system-v4/README.md) — how a weapon is dropped, folded and quenched, and how its skills are learned.
- [The Three-Brain Design Process](../../0-process/design-process.md) — the flow to run each remaining pool through.
- [GPT Concept Prompts — Paste-Me](../../4-art/gpt-concept-prompts.md) — the Part 2 prompts that give these fourteen a look.
