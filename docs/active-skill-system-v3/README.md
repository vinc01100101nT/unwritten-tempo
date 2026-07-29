# Active Skill System — V3 (Channels + Role-Locked Scaling)

> # :no_entry: ULTIMATE PRIORITY — HOW TO EDIT THIS DOC (read this FIRST, every time)
>
> **NEVER APPEND. ALWAYS UPDATE IN PLACE.** This doc is the single source of truth — it must read as ONE current voice with **zero** outdated lines, **zero** contradictions, and **zero** duplicate lists/catalogs (each fact lives in exactly one section).
>
> When a decision changes something: **find every stale/contradictory passage and REWRITE it.** Do **not** bolt a new "Update / SETTLED (date)" section onto the end and leave the old text rotting above it — that creates exactly the contradictions and duplicate catalogs this rule exists to kill. If new intent supersedes an older passage, **edit that passage directly**; never leave both versions in the doc.
>
> Before finishing any edit: re-read the WHOLE doc and confirm nothing elsewhere still says the old thing.

> **Pillar** (2026-07-22, supersedes V2's hybrid weapon-affix + ring-voice model): give every system **one job**. A skill's numbers, a skill's effects, the ring's scaling, and enemy matchup each live in exactly one layer with no overlap. This is a **big** simplification of V2 — kept as its own doc because the changes are large; V2 stays intact as the record of where we came from.
>
> - **Weapon** = grants skills (+ weapon identity: damage type, animation). *Family is a composing guide, not stamped on the weapon — see §5.*
> - **Skill** = its own numbers (the **Channels**, §1) **+** its own natural effects (the **Effects**, §2).
> - **Ring** = **numeric scaling only**, and the per-skill scaling weights are what lock roles.
> - **Later:** ring behavioral *voices* → v-next; **Cards** (enemy matchup) → v3-content, with mob categories.

***This doc is the single source of truth for the skill / weapon / ring / card design.** Any older doc (V2, the Fresh Brainstorm, the Anti-Stale Charter, etc.) that conflicts with V3 **loses** — V3 wins. The only companion that isn't overridden is the mutable, code-facing **"Active Skill System — Implementation Notes"** — it tracks what the code does today; on a disagreement V3 wins on intent, the Notes win on current code.*

## Why V3 (the redundancy we found)

Comparing V2's **Ring Voices** against a skill's actual outputs showed heavy overlap — a voice that adds Duration and a skill's Duration number are modifying the same thing; same for +Damage, +Cast Speed, +Radius. Two systems fighting over one value. V3 fixes this by splitting responsibilities so **nothing is edited from two places**:

- **Channels answer "how much?"** — the skill owns them (§1).
- **Ring answers "scaled by whom?"** — the ring multiplies, it doesn't add new numbers or behaviors (for now).
- **Effects answer "what happens?"** — authored on the skill (§2), not rolled as weapon affixes.

## 1. Channels Catalog — LOCKED (the numeric outputs, "how much?")

A channel is a **stat-agnostic numeric output** — the common language every other system references. A skill exposes **only the channels it owns**. Channels split into two tiers by who owns them. *(Live view: **Tools ▸ unwritten ▸ Catalogs ▸ Skill Channels & Effects**.)*

**Tier A — effect-output channels** — attach per-effect; these are what the ring scales (via affinity weights):

| Channel        | Meaning                                          | Unit | Ring-scales?                                     |
| -------------- | ------------------------------------------------ | ---- | ------------------------------------------------ |
| damage         | Direct damage — base power × ATK/MATK × affinity | mult | yes                                              |
| hits           | Strikes per landing; each is a proc trigger      | int  | yes                                              |
| heal_amount    | Flat HP restored                                 | HP   | yes                                              |
| heal_per_tick  | Regen HP every 0.8s                              | HP   | yes                                              |
| shield_amount  | Barrier HP absorbed before it breaks             | HP   | yes                                              |
| dmg_reduction  | Guard incoming-damage cut (clamped to 1)         | 0..1 | yes                                              |
| dmg_taken_frac | Vulnerability extra damage on a mob              | frac | yes                                              |
| dot_damage     | Status DoT per tick (Poison / Bleed / Burn)      | HP   | yes                                              |
| slow_amount    | Status Slow speed factor                         | 0..1 | yes                                              |
| accuracy_loss  | Status Blind HIT loss                            | HIT  | yes                                              |
| move_mult      | Haste move-speed (mobs: <1 = slow)               | mult | yes                                              |
| shove_speed    | Knockback / Pull speed                           | u/s  | no (raw)                                         |
| leech_frac     | ManaLeech — damage dealt → MP                    | frac | no (raw)                                         |
| reflect_frac   | Reflect — damage taken bounced back              | frac | no (raw)                                         |
| execute_scale  | Execute bonus multiplier vs missing HP           | mult | base roll scales; this is raw                    |
| buff_mult      | Buff amplify amount                              | mult | **NO — flat by design** (support value = uptime) |
| proc_chance    | Per-hit roll for a Status / Execute proc         | 0..1 | no                                               |
| duration       | Effect window length                             | sec  | yes (own affinity list)                          |

**Tier B — skill / delivery channels** — owned once per skill or tick, not per effect:

| Channel              | Meaning                                 | Owner                                       |
| -------------------- | --------------------------------------- | ------------------------------------------- |
| cost                 | Mana cost                               | SkillDef.manaCost                           |
| cooldown             | Reuse delay                             | SkillDef.cooldown                           |
| cast_time            | Wind-up before firing (DEX shortens it) | SkillDef.castTime                           |
| range                | Max aim distance                        | SkillDef.castRange                          |
| radius               | AoE / hit width                         | SkillTickBinding.radius                     |
| projectiles / spread | Fan copies + cone angle                 | SkillDef.projectileCount / projectileSpread |
| cycle                | Whole-effect repeat count + interval    | SkillDef.repeatCount / repeatInterval       |
| charge_cost          | Ult charge to cast (Ultimate tier only) | SkillDef.chargeCost                         |

**Reserved — not built yet** (author only when a skill needs them): **Interval** (DoT tick cadence — hardcoded in `StatusController`), **Limit** (max stacks / summons — no system), **Speed** (projectile travel — lives in the VFX carrier), **Resource** (non-MP gain / drain).

**On magnitude / "Strength":** V2's vague single "Strength / potency" channel is **dissolved** — every effect names the exact thing it scales (`slow_amount`, `dot_damage`, `dmg_reduction`, …), never a floating cross-skill magnitude. Hard-CC (Stun / Freeze / Silence) names none: they're **duration-only**. *(A formal Channels-catalog **data type** with stable ids — so composer + applier + tooltip read one source — is a future serialized-schema refactor; today these ids ARE the catalog and the composer mirrors them.)*

## 2. Effects Catalog — LOCKED (the behaviors, "what happens?")

Every behavior is a **natural effect authored on the skill** (composed in the Skill Composer), not rolled onto a weapon — a skill that knocks back simply *is* a knockback skill. The **ring does numbers only**, so there's nothing to "fold" between weapon and ring. Each effect owns the channels in its row; **ring scales** = what affinity actually grows (verified against the applier, not just the composer label).

| Effect        | Target       | Dmg type | Channels                                                                  | Ring scales                         | Details                                                                    |
| ------------- | ------------ | -------- | ------------------------------------------------------------------------- | ----------------------------------- | -------------------------------------------------------------------------- |
| Damage        | mobs/any     | yes      | damage, hits                                                              | damage, hits                        | Base power × ATK/MATK × affinity; each hit triggers the binding's procs    |
| Execute       | mobs         | yes      | execute_scale, proc_chance                                                | base roll (scale raw)               | Proc per hit: bonus = roll × power × target's missing HP; huge near death  |
| Heal          | self/others  | no       | heal_amount                                                               | heal_amount                         | Flat HP; landing follows the tick's TargetMode                             |
| Status        | mobs/self    | no       | ailment + dot_damage / slow_amount / accuracy_loss, duration, proc_chance | output + duration (not proc_chance) | Ailment picks the channel; Stun/Freeze/Silence = duration-only             |
| Haste         | self/mobs    | no       | move_mult, duration                                                       | move_mult, duration                 | Move-speed ×; on mobs a <1 value is a slow-debuff                          |
| Guard         | self         | no       | dmg_reduction, duration                                                   | dmg_reduction, duration             | % incoming-damage cut (clamped to 1)                                       |
| Shield        | self         | no       | shield_amount, duration                                                   | shield_amount, duration             | Finite HP barrier; soaks hits until drained; DoT bypasses (like Guard)     |
| Regen         | self         | no       | heal_per_tick, duration                                                   | heal_per_tick, duration             | Heal-over-time, HP every 0.8s                                              |
| Buff          | self         | no       | buff_mult, duration, ramp                                                 | duration only                       | buffStat = Damage/AttackSpeed/CastSpeed; amount FLAT; Damage buff can ramp |
| Cleanse       | self         | no       | debuffs_removed                                                           | —                                   | Strips up to N active debuffs                                              |
| Vulnerability | mobs         | no       | dmg_taken_frac, duration                                                  | dmg_taken_frac, duration            | Marks a mob to take +frac more damage                                      |
| Taunt         | mobs         | no       | duration                                                                  | duration                            | Forces mob aggro onto the caster                                           |
| Knockback     | mobs         | no       | shove_speed, duration                                                     | duration only                       | Shoves the mob away from the caster                                        |
| Pull          | mobs         | no       | shove_speed, duration                                                     | duration only                       | Drags the mob toward the caster                                            |
| ManaLeech     | self         | no       | leech_frac, duration                                                      | duration only                       | Timed window: fraction of damage dealt returns as MP                       |
| Reflect       | self         | no       | reflect_frac, duration                                                    | duration only                       | Timed window: reflect a fraction of damage taken                           |
| Teleport      | self (point) | no       | —                                                                         | —                                   | Instant blink to the aimed, range-clamped point                            |
| Dash          | self (point) | no       | dash_speed, ease                                                          | —                                   | Visible fast glide to the aimed point; blocked by geometry                 |

**Rejected — out of scope for unwritten:** **Summon** (needs the deferred `Limit` cap — uncapped = perf hazard) and **Transform** (a whole rig/stance/stat-swap feature).

**Still PLANNED — not in the catalog yet** (V2 vocabulary with no verb today; add when a skill needs them): **targeting** (Penetrate / Bounce / Homing), **element-convert**, **persistence** (Lingering / Trail).

## 3. Ring = numeric scaling only, and scaling locks roles — LOCKED (behaviors deferred)

For now the ring does **one** thing: **multiply channels** based on per-stat investment (the existing **affinity** path — `StatWeight` per effect → `AffinityMult` reads `RingBonus`). No behavioral "voices" yet.

**Roles are locked by *which ring stat scales which channel*, per skill** — not by the weapon, and not by banning channels:

- **Heal** channel scales **only off MP**.
- **Damage** channel scales **off Atk / MAtk**.
- Support skills never scale off offense stats; offense skills never scale off MP.

**Why this makes DPS = DPS and SUPPORT = SUPPORT:** the thing that commits your role is your **ring investment**, which is slow to change — not your weapon, which you can hot-swap. A support who poured points into MP and grabs a DPS weapon still hits like wet paper (damage scales off Atk/MAtk they never invested); a DPS holding a staff heals for crumbs (heal scales off MP they never invested). Swapping weapons swaps your *skills*, but not your *power* — that stays bound to the stats you chose.

**The cost to stay honest:** the guard now lives entirely in **per-skill scaling weights**. One heal accidentally weighted to MAtk reopens the exact hole the V2 ban was protecting against. So authoring discipline in Skill Composer is load-bearing — a self-check pass on every skill's weights is required, and the weights are the single source of truth for role identity. Per-channel **scaling defaults** — which ring stat each non-damage channel grows from — are locked in **Skill Numbers & Balance Baseline**; a skill overrides a default only with a noted reason.

### Deferred — Ring behavioral Voices → v-next

V2's behavioral voices (explode / spread / blink / leech-→-regen-→-barrier hierarchies) are **too complex to compose cleanly right now** and are **postponed to a later version**. They are not cancelled — just not in this slice. When they return they must obey V3's split: a voice adds a **new behavior**, never a channel number (that's scaling's job).

## 4. Cards (enemy matchup) → v3-content, with mob categories

An independent progression axis, Ragnarok-style: bonuses/resists **vs. enemy category or damage type** (e.g. +20% dmg to Demon, +15% to Undead, +10% to Boss, +20% resist vs Water). **Cards never touch skills** — only combat outcomes against tagged enemies. Deferred until we implement **mob categories/tags**; lands together with them.

Watch-items for when we build it: needs the mob-tag system first; guard against the RO "mandatory anti-X card per map" chore; keep cards as *matchup flavor*, not raw power, so they don't become a second power-accumulation track.

## 5. Families & Archetype Verbs = composing guides, NOT tags or stat-locks — LOCKED

Two authoring lenses help us compose balanced, distinct skills. **Neither is a label on a weapon or skill, and neither maps to stats.** They are guidance for the designer at the Skill Composer, nothing the runtime reads:

- **Families** — Might › Arcane › Finesse. A balance guide for *what kind of offense* a skill leans on and how the roster stays in rock-paper-scissors. Not stamped on weapons or skills.
- **Archetype verbs** — Burst / Tempo / Fortress / Buff / Curse (the five in the `SkillArchetype` enum). A coherence guide for *what battlefield role* a skill plays, so 300 skills don't blur into "strike the enemy." Not stamped on skills as a stat cluster (it is a tooltip identity chip).

**They guide; they do not bind.** When composing a "Burst" skill you'll naturally weight its Damage channel toward Atk/MAtk — but that's your authoring call (§3), not a rule the archetype enforces. There is **no Archetype→stat table and no Family→stat table** — those were removed to kill the single-source-of-truth clash with §3 scaling. The only real stat mapping is §3's per-skill channel weights.

### The combat rules families exist to protect ARE strict (runtime, not a guide)

Families are a soft authoring lens, but the RPS combat behaviors they're meant to preserve are **hard, always-on runtime rules**:

- **Crit ignores Defense.**
- **Magic attacks never miss.**
- **Tanks are stronger against mages** (high Def/HP resists the magic arc).

For these to fire, each skill still carries a **damage type as real data** — **physical / magic / crit** — because the armor / accuracy / crit math reads it every hit. **Damage type is data on the skill; family is only the guide that helped pick it.** Keep the two distinct: dropping family-as-a-tag must never drop damage typing.

## Resulting separation (the whole point)

| System                | Single responsibility                                                                                                             |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Weapon                | Grants skills — a rarity-scaled roll from the weapon's 12-skill pool (see **Weapon Crafting & Economy**); defines weapon identity |
| Channels (§1)         | The skill's numeric outputs ("how much?")                                                                                         |
| Effects (§2)          | The skill's natural behaviors ("what happens?")                                                                                   |
| Ring                  | Numeric scaling of channels; scaling weights lock roles                                                                           |
| Cards *(later)*       | Matchup specialization vs. enemy categories                                                                                       |
| Ring Voices *(later)* | New behaviors added by ring investment                                                                                            |

## Still open

- Ring **voice** design when it returns (v-next); **card** list + mob categories (v3-content).
