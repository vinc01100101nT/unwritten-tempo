---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - systems-combat-design
---

# Skill Numbers & Balance Baseline (V3)

The "how much" half of the skill system — the numeric starting points for authoring skills. Companion to **Active Skill System — V3** (which owns *what* channels/effects are) and **Weapon Roster** (which owns *what* each weapon does). Every value here is a **measured starting baseline**, not a lock — tune in playtest. All anchors are pulled from the real player stats (`Character.cs`) and the skills that already exist.

## Reference frame — the numbers are SMALL

Measured player at **level 1** (`Character.cs`): **HP 20 · MP 10 · Attack 4 · Magic Attack 3 · Defense 1.** Per-level growth: **+6 HP · +3 MP · +1 Atk · +1 MAtk · +1 Def.** Stat points add roughly +1 Atk per STR, +1 MAtk per INT (Def/Evasion/crit use divisors).

So combat is **single-to-low-double-digit** for a long time. Skill numbers are *multipliers of your Attack*, never raw thousands.

## Damage model (measured — `SkillEffectApplier.RollPower`)

**Damage per hit ≈ your Attack (or Magic Attack) × the skill's** `damageMult` **× ring affinity**, rolled inside a min–max band. So `damageMult` **is the skill's power dial:** `1` **= "one basic hit."** Everything else (hits, repeats, the ult multiplier) stacks on top.

## Per-channel baselines (anchored to skills that exist today)

| Channel / knob | Normal band | Anchor (real skill) |
| --- | --- | --- |
| `damageMult` — single hit | 0.8–2 (fast \~1, big committed \~2) | ember_bolt = 1 |
| `damageMult` — **ultimate** | 6–10, usually + multi-hit | **meteor = 8** |
| `hits` / `repeatCount` — multi-hit | 2–4 normal; ults stack | meteor hitCount 3 × repeat 5 |
| `manaCost` | spam 5–8 · standard 10–18 · heavy 18–20 · **ult \~40** | ember 8, mending 18, meteor 40 |
| `cooldown` (s) | spam 0.7–1.5 · standard 4–6 · heavy/utility 8–18 · **ult 45–60, a long cooldown and NOT charge-gated (decided 2026-08-23)** | ember 1.5, war_banner 18 |
| `castTime` (s) | instant 0 · light 0.5 · heavy 1–2 | meteor 1.5 |
| `castRange` | self 0 · melee 4–6 · ranged 11–13 | bulwark 6, meteor 13 |
| `shield_amount` | \~30–60% of player MaxHP for its window | bulwark |
| Heal `amount` | restore a meaningful chunk over 4–6s | mending_tide 6 / 6s |
| DoT `dot_damage` | small per-tick, \~4s window | ember/hex = 3 / 4s |
| Hard-CC duration (Stun/Freeze/Silence) | short: 1–3s | — |
| `proc_chance` | 0.1–0.35 | meteor stun proc 0.25 |

**Every timed stat change is one channel —** `sub_stat_magnitude` — so its band depends on which sub-stat it moves, not on a separate verb per effect. The bands below all read as a **bonus fraction** (0.3 = ±30%), authored positive and applied as a subtraction on the debuff side:

| Sub-stat moved | Normal band | Anchor (measured off the asset, 2026-08-24) |
| --- | --- | --- |
| Damage reduction | 0.3–0.6 (0.5 = half damage taken), 4–6s | guard_stance **0.5 / 6s**, bulwark **0.5 / 5s** |
| Damage reduction, as a DEBUFF | the same pile carrying a negative — this is how a skill makes a target take MORE | overclock **1.0 / 3s** (double damage taken, deliberate) |
| Attack speed | 0.15–0.4, 6–8s | overclock **0.3 / 8s** |
| Damage, stacking | small per stack — it is meant to pile up | momentum **0.03 / 5s per stack** (+3%) |
| HP regen | a meaningful chunk over its window | mending_tide **7.5 / 6s** |
| Movement speed · accuracy · the rest | no authored anchor yet — pick from the neighbouring bands | — |
| Soft window duration (any sub-stat, Bleed) | 3–8s | — |

⚠️ **One value has not been migrated to this convention: war_banner is authored** `1.3`**.** Under the "0.3 = +30%" rule that reads as **+130% damage**, not the +30% the old multiplier-style number meant. It is the only skill still carrying the pre-collapse encoding. *Left as-is deliberately — changing it is a balance decision, not a doc fix.*

## Ultimate budget & charge economy

- An ult is marked `tier = 1`. `chargeCost` ≈ **5000** (meteor) — normals leave the field at an unused 100. Sword carries **three** (Deathblow, Retribution, Blade Dance), so "one per weapon" is a guideline the composed pool has already outgrown, not a rule.
- An ult should *read* as **\~6–10× a normal's single hit** and/or a big multi-hit, plus a signature effect — not just "a slightly bigger skill."
- :warning: **To measure:** the charge-**gain** rate (how fast 5000 builds in a fight) isn't captured here. Tune it so an ult lands roughly **once per meaningful fight**.

## Scaling-weight DEFAULTS — LOCKED (closes V3's "Still open")

The default ring stat each channel scales off, **unless a skill deliberately overrides it (with a noted reason).** These are locked so authoring is fast and role-lock can't leak.

| Channel | Default scales off |
| --- | --- |
| `damage` | Atk (Physical) / MAtk (Magic) `[V3]` |
| `heal_amount`, `shield_amount` | **MP** `[V3: Heal→MP]` |
| `dot_damage` | the DoT's **own damage type** (Physical→Atk, Magic→MAtk) |
| `sub_stat_magnitude` — EVERY sub-stat buff or debuff (damage reduction, attack speed, regen, move speed, leech, reflect, …), and an outgoing-damage boost or cut | **none by default** — every saved skill leaves this row empty. Its "boosts …" row is offered like any other, so a skill MAY name ring stats for it. **Always raised by Skill Mastery** (a passive: by Weapon Mastery), up to +30%. Both since 2026-09-21. Its duration scales off MP. |
| **ALL** `duration`**s** | **MP** — the single support/uptime axis |
| Execute base roll / `execute_scale` | base roll → Atk; scale raw `[V3]` |
| `shove_speed` | **raw — no scale** `[V3]` |
| `charges` | **raw** — a count of uses is not a number the ring grows |

**The through-line:** offense magnitudes (damage, DoT) scale off **offense stats**; sustain magnitudes (heal, shield) scale off their support stat; **every duration** scales off **MP** (the support/uptime axis); the size of a sub-stat change or an outgoing-damage boost is raised by practice, and by the ring only where a skill names stats for it; a few raw utility numbers (shove speed, charge counts) don't scale at all.

**Where this lives in the game:** one set of functions, `EffectScaling` (in `SkillEffect.cs`), answers "can this number be boosted, which ring stats scale it, and does practice raise it" for every effect — ONE list of boostable numbers, and every one of them is offered as a "boosts …" row. The fight, the skill tooltip and the Skill Composer's "boosts …" rows all ask it, so the composer shows exactly what a fight does — including a *raised by Skill Mastery* line wherever practice applies.

## How to use this when authoring

1. The **identity litmus test** (roster doc) decides *what* a skill is.
2. This doc decides *how much* — start from the band for its role, nudge for feel.
3. The **scaling-default table** decides *what stat grows it* — follow it unless the skill has a deliberate reason not to (and note the reason).

## Attack Speed cap — DONE (implemented 2026-08-04)

`Character.AttackSpeedMult` (`Character.cs:370`) is clamped by `MaxAttackSpeedMult = 3f` (`Character.cs:88`). Sword's **Momentum** (uncapped stacks, generated per basic attack) has an equilibrium of *attack rate × stack duration*; the clamp bounds that ceiling so Momentum — and Deathblow's +40%/stack — can't scale without bound. General combat fix, not Sword-specific. Retune the `3f` in playtest if needed.

## Cost & cooldown — the RULES (decided 2026-08-23, applied and verified on all 22 sword skills)

The bands above say what's normal; they can't say why a skill is 10 and not 14. Before these rules the sword pool was full of defaults nobody revisited — a single repeated cooldown and a single repeated mana cost standing in for a dozen separate decisions. The rules replace that guesswork: still starting points, but every number now has a reason attached.

### Rule 1 — passives are FREE

**Every passive costs 0 MP and has no cooldown.** What a passive costs you is the build slot it occupies; charging mana on top prices it twice, and its trigger already limits how often it fires. All nine sword passives comply, verified off the assets.

### Rule 2 — mana is the price of POWER

One number, four steps. A skill sits in the step matching what it delivers, and moving a skill between steps is a real decision to write down:

| Step | MP | What belongs here |
| --- | --- | --- |
| Reaction | 8 | short, small, meant to be used often — a gap-closer, a single parry |
| Standard | 12 | the ordinary active: a normal buff, a burst, a guard |
| Heavy | 16 | the biggest non-ultimate — a long wind-up strike, or a skill arming two piles at once |
| Ultimate | 40 | one per weapon |

### Rule 3 — cooldown is set by the DURATION, never by feel

**A skill's cooldown must be at least 1.5× its own duration**, so its effect can never be permanently up. An effect that outlives its cooldown isn't a decision the player makes, it is a state they simply live in.

- Instant skills (no duration) keep the band cooldown for their step: reaction \~5s, standard \~6s, heavy \~8s.
- **Ultimates: 45–60s.** A long cooldown, deliberately not a charge gate — decided 2026-08-23, and the `cooldown` band above is corrected to match.
- Brace already obeys this (4s duration, 7s cooldown) and is the reference.

### Rule 4 — a WINDOW and a COUNT OF CHARGES are different things, and never mix

A duration is **seconds**; charges are **a number of uses**. A skill may carry both — Brace holds 5 charges for 4s — but they are authored and tuned as separate numbers, and neither is ever expressed in terms of the other. In particular, never write a duration as "however many attacks that is": attack speed varies per character, so the same authored number would mean different things to two players.

### What the rules produce for the sword pool

**The whole sword pool, read back off the assets 2026-08-24** — all 22 skills, 13 actives and 9 passives. Every active obeys Rules 1–3.

| Active | Duration | MP | Cooldown | Why |
| --- | --- | --- | --- | --- |
| Deathblow | — | 40 | 60 | ultimate |
| Retribution | — | 40 | 45 | ultimate |
| Blade Dance | 8s | 40 | 45 | authored `tier = Ultimate`, so it pays an ultimate's price |
| Committed Strike | — | 16 | 8 | heavy, instant |
| Overclock | 8s / 3s | 12 | 12 | standard; cooldown raised past its own buff, which used to be permanently up |
| Guard Stance | 6s | 12 | 9 | 6s × 1.5 |
| Disarm | 6s | 12 | 9 | 6s × 1.5 |
| Brace | 4s | 12 | 7 | the reference case |
| Onslaught | 5s | 12 | 8 | 5s × 1.5 |
| Challenge | 5s | 12 | 8 | 5s × 1.5 |
| Offbeat | 5s | 12 | 8 | 5s × 1.5 |
| Lunge | — | 8 | 5 | reaction, instant |
| Parry | 3s | 8 | 5 | 3s × 1.5; a shorter cooldown would be permanent parry |

| Passive | Duration | MP | Cooldown |
| --- | --- | --- | --- |
| Momentum · Unwavering · Lock Blades · Opening · Pressure · Composure | 4–6s | 0 | 0 |
| Flow · Encore · Last Stand | — | 0 | 0 |

**Sword has THREE ultimates** — Deathblow, Retribution and Blade Dance, all authored `tier = Ultimate`. The pricing follows the assets, since the composed skill is the source of truth for what a skill IS; the roster and the wiki both describe three.

**Rule 3 is a rule about ACTIVES.** Its whole purpose is to keep a *press* meaningful — an effect that outlives its cooldown stops being a decision the player makes. A passive is never pressed, so there is no decision for the rule to protect and nothing for it to govern: a passive that grants a 4–6s state with no cooldown is the design working, not a gap in it. A passive's limiter is its trigger and the build slot it occupies.

**Resolved 2026-08-24 — Opening rolls 50%.** The asset's chance is the authority; the roster doc and the wiki were the stale side and now say a successful parry has a **50% chance** to grant the +200% charge.

**Found and FIXED 2026-08-24 — Guard Stance was authored backwards.** It was a `SubStatDebuff` on `DamageReduction`, and `SkillEffectApplier` negates a debuff's magnitude, so the live effect was **+50% damage taken** rather than −50%. Now a `SubStatBuff`, verified off the asset. Overclock still uses the debuff kind on purpose — that is how it doubles incoming damage. **Watch for this shape on any new defensive skill:** the two kinds differ by one word and the wrong one is silently a buff for the enemy.

## Open / to measure before mass-tuning

- **Charge-gain rate** (sets ult cadence) — measure in a real fight.
- **Real mob HP curve** — `Health.maxHealth` defaults to 3 in code but every mob overrides it per-prefab; pull actual per-tier/biome values when tuning damage against them.

---

## Related

- [Active Skill System — V3](README.md) — the channels and effects these numbers are bands for.
- [Sword — Skill Roster](sword-skill-roster.md) — the pool these rules were applied and verified against.
- [State System — Buffs, Debuffs & Combo Marks](state-system-buffs-debuffs-combo-marks.md) — the window-vs-charges distinction Rule 4 protects.
- [Weapon Roster — 14 Identities](weapon-roster-12-identities.md) — the identity test that decides *what* before this decides *how much*.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the ring stats the scaling defaults point at.
- [Pre-Renewal RO Port — Phase Plan & Formulas](../pre-renewal-ro-port.md) — where the player anchors in the reference frame come from.
- [Unified Unit Stats](unified-unit-stats.md) — the real mob numbers to tune damage against.
