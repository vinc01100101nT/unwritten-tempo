# Skill Numbers & Balance Baseline (V3)

The "how much" half of the skill system — the numeric starting points for authoring skills. Companion to **Active Skill System — V3** (which owns *what* channels/effects are) and **Weapon Roster** (which owns *what* each weapon does). Every value here is a **measured starting baseline**, not a lock — tune in playtest. All anchors are pulled from the real player stats (`Character.cs`) and the skills that already exist.

## Reference frame — the numbers are SMALL

Measured player at **level 1** (`Character.cs`): **HP 20 · MP 10 · Attack 4 · Magic Attack 3 · Defense 1.** Per-level growth: **+6 HP · +3 MP · +1 Atk · +1 MAtk · +1 Def.** Stat points add roughly +1 Atk per STR, +1 MAtk per INT (Def/Evasion/crit use divisors).

So combat is **single-to-low-double-digit** for a long time. Skill numbers are *multipliers of your Attack*, never raw thousands.

## Damage model (measured — `SkillEffectApplier.RollPower`)

**Damage per hit ≈ your Attack (or Magic Attack) × the skill's `damageMult` × ring affinity**, rolled inside a min–max band. So `**damageMult` is the skill's power dial: `1` = "one basic hit."** Everything else (hits, repeats, the ult multiplier) stacks on top.

## Per-channel baselines (anchored to skills that exist today)

| Channel / knob                               | Normal band                                                              | Anchor (real skill)            |
| -------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------ |
| `damageMult` — single hit                    | 0.8–2 (fast ~1, big committed ~2)                                        | ember_bolt = 1                 |
| `damageMult` — **ultimate**                  | 6–10, usually + multi-hit                                                | **meteor = 8**                 |
| `hits` / `repeatCount` — multi-hit           | 2–4 normal; ults stack                                                   | meteor hitCount 3 × repeat 5   |
| `manaCost`                                   | spam 5–8 · standard 10–18 · heavy 18–20 · **ult ~40**                    | ember 8, mending 18, meteor 40 |
| `cooldown` (s)                               | spam 0.7–1.5 · standard 4–6 · heavy/utility 8–18 · ult ~3 (charge-gated) | ember 1.5, war_banner 18       |
| `castTime` (s)                               | instant 0 · light 0.5 · heavy 1–2                                        | meteor 1.5                     |
| `castRange`                                  | self 0 · melee 4–6 · ranged 11–13                                        | bulwark 6, meteor 13           |
| Guard `dmg_reduction`                        | 0.3–0.6 (0.5 = half), 4–6s                                               | bulwark 0.5 / 5s               |
| `shield_amount`                              | ~30–60% of player MaxHP for its window                                   | (new — Shield)                 |
| Heal `amount` / Regen `heal_per_tick`        | restore a meaningful chunk over 4–6s                                     | mending_tide 6 / 6s            |
| Buff `buff_mult`                             | 1.15–1.4, 6–8s, **FLAT (no ring scale)**                                 | war_banner 1.3 / 8s            |
| DoT `dot_damage`                             | small per-tick, ~4s window                                               | ember/hex = 3 / 4s             |
| Slow `slow_amount`                           | 0.3–0.6 (0.5 = half speed); Root ≈ 0.02                                  | —                              |
| Vulnerability `dmg_taken_frac`               | 0.15–0.3 (+15–30%), 4–6s                                                 | —                              |
| Hard-CC duration (Stun/Freeze/Silence)       | short: 1–3s                                                              | —                              |
| Soft-status duration (Slow/Bleed/Buff/Guard) | 3–8s                                                                     | —                              |
| `proc_chance`                                | 0.1–0.35                                                                 | meteor stun proc 0.25          |

## Ultimate budget & charge economy

- One ult per weapon, marked `tier = 1`. `chargeCost` ≈ **5000** (meteor) — normals leave the field at an unused 100.
- An ult should *read* as **~6–10× a normal's single hit** and/or a big multi-hit, plus a signature effect — not just "a slightly bigger skill."
- :warning: **To measure:** the charge-**gain** rate (how fast 5000 builds in a fight) isn't captured here. Tune it so an ult lands roughly **once per meaningful fight**.

## Scaling-weight DEFAULTS — LOCKED (closes V3's "Still open")

The default ring stat each channel scales off, **unless a skill deliberately overrides it (with a noted reason).** These are locked so authoring is fast and role-lock can't leak.

| Channel                                                            | Default scales off                                                                             |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| `damage`                                                           | Atk (Physical) / MAtk (Magic) `[V3]`                                                           |
| `heal_amount`, `heal_per_tick`, `shield_amount`                    | **MP** `[V3: Heal→MP]`                                                                         |
| Guard `dmg_reduction`                                              | **Def**                                                                                        |
| `dot_damage`                                                       | the DoT's **own damage type** (Physical→Atk, Magic→MAtk)                                       |
| `dmg_taken_frac`, `slow_amount`, `accuracy_loss` (offense debuffs) | the caster's **offense stat** (Atk/MAtk per family)                                            |
| Haste `move_mult`                                                  | **FLAT** — mobility isn't a stackable power axis; move-speed ≠ attack-speed; its duration → MP |
| `buff_mult`                                                        | **FLAT — no scale** `[V3]`                                                                     |
| **ALL `duration`s**                                                | **MP** — the single support/uptime axis                                                        |
| Execute base roll / `execute_scale`                                | base roll → Atk; scale raw `[V3]`                                                              |
| `shove_speed`, `leech_frac`, `reflect_frac`                        | **raw — no scale** `[V3]`                                                                       |

**The through-line:** offense magnitudes (damage, DoT, debuffs) scale off **offense stats**; sustain magnitudes (heal, shield, guard) scale off their defensive/support stat; **every duration** scales off **MP** (the support/uptime axis); a few raw utility channels (knockback speed, leech/reflect fractions, Haste speed) don't scale at all. Every channel is now defined — no undefined scaling left.

## How to use this when authoring

1. The **identity litmus test** (roster doc) decides *what* a skill is.
2. This doc decides *how much* — start from the band for its role, nudge for feel.
3. The **scaling-default table** decides *what stat grows it* — follow it unless the skill has a deliberate reason not to (and note the reason).

## Attack Speed cap — REQUIRED (locked 2026-08-01)

`Character.AttackSpeedMult` (`Character.cs:367`) is currently an **uncapped** sum (stats + passives + equip) × buff — measured, no clamp. Sword's **Momentum** (uncapped stacks, generated per basic attack) has an equilibrium of *attack rate × stack duration*, so an uncapped attack speed makes Momentum — and Deathblow's +40%/stack — scale without bound. **Add a hard Attack Speed ceiling** (pre-renewal RO-style, ~190-equivalent) so the equilibrium is bounded. This is a general combat fix, not Sword-specific. **Code TODO — not yet implemented.**

## Open / to measure before mass-tuning

- **Charge-gain rate** (sets ult cadence) — measure in a real fight.
- **Real mob HP curve** — `Health.maxHealth` defaults to 3 in code but every mob overrides it per-prefab; pull actual per-tier/biome values when tuning damage against them.
