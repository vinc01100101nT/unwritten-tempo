# Skill Numbers & Balance Baseline (V3)

The "how much" half of the skill system — the numeric starting points for authoring skills. Companion to **Active Skill System — V3** (which owns *what* channels/effects are) and **Weapon Roster** (which owns *what* each weapon does). Every value here is a **measured starting baseline**, not a lock — tune in playtest. All anchors are pulled from the real player stats (`Character.cs`) and the skills that already exist.

## Reference frame — the numbers are SMALL

Measured player at **level 1** (`Character.cs`): **HP 20 · MP 10 · Attack 4 · Magic Attack 3 · Defense 1.** Per-level growth: **+6 HP · +3 MP · +1 Atk · +1 MAtk · +1 Def.** Stat points add roughly +1 Atk per STR, +1 MAtk per INT (Def/Evasion/crit use divisors).

So combat is **single-to-low-double-digit** for a long time. Skill numbers are *multipliers of your Attack*, never raw thousands.

## Damage model (measured — `SkillEffectApplier.RollPower`)

**Damage per hit ≈ your Attack (or Magic Attack) × the skill's** `damageMult` **× ring affinity**, rolled inside a min–max band. So `**damageMult` is the skill's power dial: `1` = "one basic hit."\*\* Everything else (hits, repeats, the ult multiplier) stacks on top.

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
| Guard `dmg_reduction` | 0.3–0.6 (0.5 = half), 4–6s | bulwark 0.5 / 5s |
| `shield_amount` | \~30–60% of player MaxHP for its window | (new — Shield) |
| Heal `amount` / Regen `heal_per_tick` | restore a meaningful chunk over 4–6s | mending_tide 6 / 6s |
| Buff `buff_mult` | 1.15–1.4, 6–8s, **FLAT (no ring scale)** | war_banner 1.3 / 8s |
| DoT `dot_damage` | small per-tick, \~4s window | ember/hex = 3 / 4s |
| Slow `slow_amount` | 0.3–0.6 (0.5 = half speed); Root ≈ 0.02 | — |
| Vulnerability `dmg_taken_frac` | 0.15–0.3 (+15–30%), 4–6s | — |
| Hard-CC duration (Stun/Freeze/Silence) | short: 1–3s | — |
| Soft-status duration (Slow/Bleed/Buff/Guard) | 3–8s | — |
| `proc_chance` | 0.1–0.35 | meteor stun proc 0.25 |

## Ultimate budget & charge economy

- One ult per weapon, marked `tier = 1`. `chargeCost` ≈ **5000** (meteor) — normals leave the field at an unused 100.
- An ult should *read* as **\~6–10× a normal's single hit** and/or a big multi-hit, plus a signature effect — not just "a slightly bigger skill."
- :warning: **To measure:** the charge-**gain** rate (how fast 5000 builds in a fight) isn't captured here. Tune it so an ult lands roughly **once per meaningful fight**.

## Scaling-weight DEFAULTS — LOCKED (closes V3's "Still open")

The default ring stat each channel scales off, **unless a skill deliberately overrides it (with a noted reason).** These are locked so authoring is fast and role-lock can't leak.

| Channel | Default scales off |
| --- | --- |
| `damage` | Atk (Physical) / MAtk (Magic) `[V3]` |
| `heal_amount`, `heal_per_tick`, `shield_amount` | **MP** `[V3: Heal→MP]` |
| Guard `dmg_reduction` | **Def** |
| `dot_damage` | the DoT's **own damage type** (Physical→Atk, Magic→MAtk) |
| `dmg_taken_frac`, `slow_amount`, `accuracy_loss` (offense debuffs) | the caster's **offense stat** (Atk/MAtk per family) |
| Haste `move_mult` | **FLAT** — mobility isn't a stackable power axis; move-speed ≠ attack-speed; its duration → MP |
| `buff_mult` | **FLAT — no scale** `[V3]` |
| **ALL** `duration`**s** | **MP** — the single support/uptime axis |
| Execute base roll / `execute_scale` | base roll → Atk; scale raw `[V3]` |
| `shove_speed`, `leech_frac`, `reflect_frac` | **raw — no scale** `[V3]` |

**The through-line:** offense magnitudes (damage, DoT, debuffs) scale off **offense stats**; sustain magnitudes (heal, shield, guard) scale off their defensive/support stat; **every duration** scales off **MP** (the support/uptime axis); a few raw utility channels (knockback speed, leech/reflect fractions, Haste speed) don't scale at all. Every channel is now defined — no undefined scaling left.

## How to use this when authoring

1. The **identity litmus test** (roster doc) decides *what* a skill is.
2. This doc decides *how much* — start from the band for its role, nudge for feel.
3. The **scaling-default table** decides *what stat grows it* — follow it unless the skill has a deliberate reason not to (and note the reason).

## Attack Speed cap — DONE (implemented 2026-08-04)

`Character.AttackSpeedMult` (`Character.cs:370`) is clamped by `MaxAttackSpeedMult = 3f` (`Character.cs:88`). Sword's **Momentum** (uncapped stacks, generated per basic attack) has an equilibrium of *attack rate × stack duration*; the clamp bounds that ceiling so Momentum — and Deathblow's +40%/stack — can't scale without bound. General combat fix, not Sword-specific. Retune the `3f` in playtest if needed.

## Cost & cooldown — the RULES (planned 2026-08-23, not yet applied)

The bands above say what's normal; they can't say why a skill is 10 and not 14. Measured on the sword pool, that gap shows: **7 of 17 sword skills carry the identical 5s cooldown and 5 carry the identical 10 MP.** That is a default nobody revisited, not seventeen decisions. These rules replace the guesswork — they are still starting points, but every number now has a reason attached.

### Rule 1 — passives are FREE (decided 2026-08-23)

**Every passive costs 0 MP and has no cooldown.** What a passive costs you is the build slot it occupies; charging mana on top prices it twice, and its trigger already limits how often it fires. Today the sword pool is split — Encore, Unwavering and Opening pay 10 MP, Momentum pays 1, while Flow, Lock Blades and Pressure pay nothing — with no rule saying which is right. There is one now.

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

- Instant skills (no duration) keep the band cooldown for their step: reaction ~5s, standard ~6s, heavy ~8s.
- **Ultimates: 45–60s.** A long cooldown, deliberately not a charge gate — decided 2026-08-23, and the `cooldown` band above is corrected to match.
- Brace already obeys this (4s duration, 7s cooldown) and is the reference.

### Rule 4 — a WINDOW and a COUNT OF CHARGES are different things, and never mix

A duration is **seconds**; charges are **a number of uses**. A skill may carry both — Brace holds 5 charges for 4s — but they are authored and tuned as separate numbers, and neither is ever expressed in terms of the other. In particular, never write a duration as "however many attacks that is": attack speed varies per character, so the same authored number would mean different things to two players.

### What the rules produce for the sword pool

**APPLIED to the assets 2026-08-23** and read back from disk. Values marked → changed; the rest already complied.

| Skill | Duration | MP | Cooldown | Why |
| --- | --- | --- | --- | --- |
| Deathblow | — | 40 | 60 | ultimate |
| Retribution | — | 40 | 45 | ultimate |
| Blade Dance | 8s | 10 → **40** | 5 → **45** | **it is authored `tier = Ultimate`**, so it pays an ultimate's price |
| Committed Strike | — | 14 → **16** | 6 → **8** | heavy, instant |
| Overclock | 8s / 3s | 12 | 6 → **12** | its buff outlived its cooldown — permanent uptime |
| Guard Stance | 9s → **6s** | 12 | 10 → **9** | 9s was outside the 4–6s Guard band; 6s × 1.5 |
| Brace | 4s | 10 → **12** | 7 | already the reference |
| Onslaught | 5s | 10 → **12** | 5 → **8** | 5s × 1.5 |
| Lunge | — | 8 | 5 | reaction, instant |
| Parry | 3s | 8 | 1 → **5** | 3s × 1.5; a 1s cooldown on a 3s effect is permanent parry |
| Unwavering | 9s → **6s** | 10 → **0** | 5 → **0** | passive; its 6s deliberately **matches Guard Stance**, which it exists to cover |
| Momentum · Flow · Encore · Opening · Lock Blades · Pressure | unchanged | → **0** | → **0** | passives are free |

**Sword now has THREE ultimates** — Deathblow, Retribution and Blade Dance are all authored `tier = Ultimate`. The pricing above follows the assets, since the composed skill is the source of truth for what a skill IS. **Adopted 2026-08-24:** the sword roster and the wiki now both describe three, so the "one per weapon" line no longer contradicts them.

**The two that were actually broken, not merely inconsistent:** Overclock and Parry both had cooldowns *shorter* than their own durations, so their effects could be held up permanently.

**Resolved 2026-08-24 — Opening rolls 50%.** The asset's chance is the authority; the roster doc and the wiki were the stale side and now say a successful parry has a **50% chance** to grant the +200% charge.

**Found 2026-08-24, NOT a number and NOT fixed — Guard Stance is authored backwards.** It is a `SubStatDebuff` on `DamageReduction`, and `SkillEffectApplier` negates a debuff's magnitude, so the live effect is **+50% damage taken** rather than −50%. Overclock uses the same kind on purpose (that is how it doubles incoming damage). Guard Stance needs `SubStatBuff`. Left for the owner to change.

## Open / to measure before mass-tuning

- **Charge-gain rate** (sets ult cadence) — measure in a real fight.
- **Real mob HP curve** — `Health.maxHealth` defaults to 3 in code but every mob overrides it per-prefab; pull actual per-tier/biome values when tuning damage against them.