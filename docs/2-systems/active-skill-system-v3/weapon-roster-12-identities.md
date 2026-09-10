---
status: Locked 2026-07-27
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# Weapon Roster — 12 Identities (V3)

Locked 2026-07-27 after a diversity pass. This is the **canonical weapon lineup** for the V3 skill system. The one-sentence identity per weapon is the **litmus test for every authored skill**: *if a proposed skill doesn't reinforce its weapon's sentence, it belongs on another weapon.* Companion to the **Active Skill System — V3** design doc (that owns the channels/effects/scaling rules; this owns the weapon identities).

## 1. The litmus test — one sentence per weapon

| Weapon | Identity (every skill must reinforce this) | Unwritten (the wielder the identity comes from) |
| --- | --- | --- |
| **Sword** | Win duels through precise single-target bursts and timed defense. | The One Who Answered |
| **Greatsword** | Delete groups with huge cleaves. | The One Who Cleared the Road |
| **Axe** | Snowball through relentless aggression. | The One Who Never Stopped |
| **Warhammer** | Control enemies by denying their actions. | The One Who Said No |
| **Spear** | Control enemy positioning. | The One Who Moved the Line |
| **Shield** | Protect allies by absorbing attention. | The One Who Stood |
| **Bow** | Win through mobility and spacing. | The One Who Kept Distance |
| **Dagger** | Eliminate weakened targets. | The Late Arrival |
| **Firearm** | Land devastating single shots. | The One Who Fired Once |
| **Staff** | Dominate areas with destructive magic. | The One Who Burned the Field |
| **Wand** | Keep allies alive. | The One Who Brought Them Back |
| **Scythe** | Slowly overwhelm enemies with curses. | The One Who Waited |

Each weapon is the only surviving trace of one of **the Twelve Unwritten** — the people who fought the thing that silenced the scribe, after the last recorded line, so none of it was written down. The epithet and the identity sentence are the same statement in two registers: the identity is the litmus test for skills, the epithet is the flavor it must sound like. A skill that doesn't fit the epithet doesn't fit the weapon. Full lore in `../story-setting-canon.md`.

**The rolled kit is diegetic:** a dropped weapon remembers its wielder in fragments, so whichever skills it carries are *which memories surfaced*. Write drop/kit-roll flavor text in those terms, never as "randomized stats."

## 2. Full identity map

| Weapon | Family / scales | Archetype | Uniquely owns | Feel |
| --- | --- | --- | --- | --- |
*Effects are named in the current vocabulary: every timed stat change is a **sub-stat buff or debuff**, so "self-Guard" is a damage-reduction buff, "Slow" is a movement-speed debuff, and "Vulnerability" is the damage-reduction pile carrying a negative. Only the ailments (Poison / Bleed / Burn / Stun / Freeze / Silence) and the standalone kinds (Shield, Taunt, Parry, Brace, Execute, Dash, Knockback, Pull, Cleanse) are verbs of their own.*

| Weapon | Family / scales | Archetype | Uniquely owns | Feel |
| --- | --- | --- | --- | --- |
| Sword | Might / Atk | Burst | single-target spike + self damage-reduction | duelist |
| Greatsword | Might / Atk | Burst (AoE) | melee AoE cleave | crowd-clearer |
| Axe | Might / Atk | Buff (self) | Bleed + self move-speed + a ramping damage buff, no defense | berserker |
| Warhammer | Might / Atk | Tempo (CC) | hard Stun + Knockback stagger | disabler |
| Spear | Might / Atk | Tempo | Pull + Dash-through + a damage-reduction debuff | wrangler |
| Shield | Might / Def | Fortress | Taunt + damage reduction + Shield + reflect, \~no damage | wall |
| Bow | Finesse / Atk-crit | Tempo | mobile kite: move-speed debuff + Dash spacing | skirmisher |
| Dagger | Finesse / Atk-crit | Burst | Execute finisher + blink out | assassin |
| Firearm | Finesse / Atk-crit | Burst | biggest single crit spike on a reload rhythm, immobile | sniper |
| Staff | Arcane / MAtk | Burst (AoE) | ranged magic AoE nuke + Burn + Freeze / move-speed lockdown | artillery |
| Wand | Arcane / MP | Fortress / Buff | Heal / HP-regen / Shield / sub-stat buffs / Cleanse — ally uptime | lifeline |
| Scythe | Arcane / MAtk | Curse | stacked Poison + a damage-reduction debuff + Silence attrition + mana leech | reaper |

## 3. The three control weapons — each owns a distinct battlefield question

- **Shield → "Hit me."** — draws fire via Taunt, absorbs and reflects.
- **Warhammer → "You don't get to act."** — Stun + Knockback, no Taunt (Taunt is the Shield's; keeping them apart is what makes the split clean).
- **Spear → "Stand over there."** — Pull + Dash-through + a damage-reduction debuff; rearranges, doesn't disable.

## 4. Magic AoE is split three ways (no Staff monopoly)

- **Staff = burst AoE** (delete the pack now).
- **Scythe = attrition AoE** — a DoT curse-field (the existing `hex_field` skill is exactly this shape).
- **Wand = support AoE** — party heal.

Three completely different reasons to bring magic AoE.

## 5. Weapon-type & engine mapping (kept honest)

Real `WeaponType` enum today: **Sword, TwoHandSword, Dagger, Axe, TwoHandAxe, Mace, Spear, Staff, Bow, Katar, Knuckle, Firearm**.

- **Maps to an existing type + attack clips we already have (KayKit):** Sword→Sword, Greatsword→TwoHandSword, Axe→Axe (Berserker; *dual-wield is animation only, not a separate type*), Spear→Spear, Dagger→Dagger/Katar, Bow→Bow, Firearm→Firearm, Staff→Staff, Wand→short Staff.
- **Needs a NEW** `WeaponType` **+ a new attack-animation bucket:** **Scythe**; **Shield** (also **off-hand / left-hand** — opens Sword+Shield, Mace+Shield dual setups later).
- **Firearm:** the "reload rhythm" is approximated by a **long per-skill cooldown** for v1 (already a channel); a true ammo/reload state machine is deferred — the sniper feel survives without it.

## 6. Authoring plan & open items

- **Grid = 12 weapons, each with its own skill POOL** — the pool holds 3 ultimates + however many normals that weapon's identity naturally supports (pool size is **not fixed** — a weapon can have as few or as many normals as make sense; author the ones that reinforce §1, no filler to hit a quota). A dropped weapon grants a kit drawn from that pool: **3–4 actives, 3–4 passives, and a 25% chance of an ultimate — the same at every rarity.** Rarity changes only how hard that kit hits, never what is in it. This resolves V3's "loot surprise source" open item — the surprise IS the rolled kit.
- **Scaling role-lock is load-bearing** (Heal→MP, Damage→Atk/MAtk) — self-check affinity weights on every skill.
- **Universal-but-specialized rule (authoring heuristic, from the Sword pass)** — *every ult should have at least one normal that **specializes** in it while still benefiting the other two ults*, building a shared spine so ult-builds aren't siloed. The specialization must be **asymmetric** (one ult gains most, or it's filler); cap at **\~1 per ult**; keep the other normals **sharply single-purpose**; and **not every normal must be build-optimal** (deliberate "trash-ish" slots are fine — players discover their build). It's a **heuristic, not a law** — don't force the resource→specialist→ult shape onto weapons whose identity resists it. See the **Sword — Skill Roster** doc for the worked example.
- **Sword is authored** — see **Sword — Skill Roster**. The other 11 pools start fresh; author each straight from its one-sentence identity (§1) + the channels/effects/scaling rules in the V3 docs.

---

## Related

- [Story & Setting (Canon)](../../3-world/story-setting-canon.md) — the Twelve Unwritten these identities are the mechanical half of.
- [Sword — Skill Roster](sword-skill-roster.md) — the one identity taken all the way to a composed pool.
- [Active Skill System — V3](README.md) — the channels, effects and scaling every pool is built from.
- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the bands to author the other eleven pools within.
- [Weapon Crafting & Economy](weapon-crafting-economy.md) — the rarity table a kit is rolled against, and what rarity does to a skill's power.
- [The Three-Brain Design Process](../../0-process/design-process.md) — the flow to run each remaining pool through.
- [GPT Concept Prompts — Paste-Me](../../4-art/gpt-concept-prompts.md) — the Part 2 prompts that give these twelve a look.
