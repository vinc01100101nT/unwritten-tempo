# Active Skill System — Implementation Notes

> **This is the MUTABLE, code-facing companion** to the design doc *"Active Skill System — V3 (Channels + Role-Locked Scaling)"* — that doc is the **design source of truth**. This doc tracks how the design is actually wired in code and is expected to churn every build. If the two disagree: **V3 wins on intent, this doc wins on what the code does today.** *(Rewritten 2026-07-22 after the V3 teardown — the old V2 "weapon-affix + ring-voice fold" account it used to hold is gone, matching the code.)*

## The model in one paragraph

A **weapon** grants skills (`Item.grantedSkillIds`). A **skill** is a `SkillDef` asset (authored in the Skill Composer) that binds VFX ticks to a list of `SkillEffect`s. Each `SkillEffect` owns its **numbers** (the V3 *channels*: `damageMult`, `amount`, `seconds`, `magnitude`, `hitCount`, `chance`, binding `radius`, plus `SkillDef` cost/cooldown/castTime/range) **and** its **behavior** (the `SkillEffectKind`). The **ring** does exactly one thing to all of this: **scale it**, via per-effect **affinity** weight-lists. There is no separate mutation/affix/voice engine — every behavior is a `SkillEffect` running through the one `SkillEffectApplier`.

## How V3 maps to live code (all already present)

| V3 concept                               | Code                                                                                                                                                                                                                                |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Channels** (the numbers)               | fields on `SkillEffect` + `SkillDef`                                                                                                                                                                                                |
| **Ring = numeric scaling only**          | `affinity` / `durationAffinity` / `hitCountAffinity` (`List<StatWeight>`) → `SkillEffectApplier.AffinityMult` reads `Character.RingBonus(StatId)` (0 when no ring worn)                                                             |
| **Role-lock** (Heal→MP, Damage→Atk/MAtk) | the `affinity` weights authored on each effect — data, per skill. per-channel scaling **defaults** locked in *Skill Numbers & Balance Baseline*                                                                                     |
| **Damage type as data**                  | `DamageType {Physical, Magic, Crit}` on each `SkillEffect` (Physical=ATK vs armour, Magic=MATK/unmissable/no-crit, Crit=forced-crit). *Crit-pierce / Magic-glance math is still a stubbed combat-pass TODO — pre-existing, not V3.* |
| **Effects authored on skills**           | `SkillEffectKind` + the `SkillEffectApplier` handlers                                                                                                                                                                               |
| **Family / Archetype = guides**          | `SkillFamily` is an **author-time default** for an effect's `DamageType` (runtime never reads it for balance); `SkillArchetype` is a tooltip identity chip. Neither is a stat-lock.                                                 |

## V3 teardown — DONE (2026-07-22)

The leftover **V2 reshaper layer** (weapon affixes baked on the weapon + ring "voices" folded per cast through a `CastMods` struct) was **removed** — V3 makes the ring numbers-only and authors every behavior on the skill. Deleted: `RingVoices.cs`, `WeaponAffixes.cs`, `CastMods.cs` (incl. `SkillReshape`), `Editor/AffixVoiceTableWindow.cs`, `Item.weaponAffixIds` (+ the Iron Sword seed), and the weapon equip-passives (`weaponManaLeech/Refund/Reflect`). The `SkillEffectApplier.ApplyBurst` `in CastMods` param and its `ApplyOutgoing`/`EchoDamage`/`ExecuteFinisher` post-pass are gone; `SkillRunner.OnTick` no longer folds anything.

**Nothing was lost — every affix behavior was rehomed or was already reachable:**

| Old affix / voice                                            | Where it lives now                                                                                                                                                                 |
| ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Slow, Stun                                                   | already authorable as a `Status` effect (`Ailment.Slow` — magnitude = speed factor; `Ailment.Stun`)                                                                                |
| Root                                                         | a `Status` `Ailment.Slow` with a ~0.02 factor (a hard stop) — no separate kind (DRY)                                                                                               |
| Push (knockback)                                             | **new** `SkillEffectKind.Knockback` → `MonsterAI3D.Knockback`, shoved away from the caster (magnitude = speed)                                                                     |
| Pull                                                         | **new** `SkillEffectKind.Pull` → `MonsterAI3D.Knockback` from a mirrored origin (toward the caster)                                                                                |
| Mana-Leech                                                   | **new** self `SkillEffectKind.ManaLeech` → `Character.BeginManaLeech(sec, frac)`; the player's damage-dealt choke (`Health.ApplyLeech`) reads `Character.ActiveManaLeechPct`       |
| Reflect                                                      | **new** self `SkillEffectKind.Reflect` → `Character.BeginReflect(sec, frac)`; `Character.Reflect` adds `ActiveReflectBonus` on top of the permanent thorns-card `reflectPct`       |
| Raw damage                                                   | **dropped** — a skill authors its own `damageMult` (the Damage channel). Redundant.                                                                                                |
| Refund                                                       | **dropped** — a skill authors its own `manaCost` (the Cost channel). Redundant.                                                                                                    |
| Ring voices (Fortify/Leech/Accelerate/Execute/Expand/Repeat) | **removed** (V3 defers behavioral voices to v-next). Their effects still exist as authorable `SkillEffect` kinds (Guard/Regen/Execute/…), just no longer auto-fired by ring stats. |

The two new mob kinds (`Knockback`/`Pull`) use the caster's position as the shove origin, so no cast-point threading through the hit loop is needed. The two new self kinds (`ManaLeech`/`Reflect`) are timed windows on `Character` — **lazy** (checked via `Time.time`, no `Update` tick, no allocation), latest-call-replaces (no stacking).

**Files touched:** `SkillEffect.cs` (+4 kinds), `SkillEffectApplier.cs` (−reshaper post-pass, +Knockback/Pull/ManaLeech/Reflect handlers), `Character.cs` (−weapon passives, +`BeginManaLeech`/`BeginReflect` + `ActiveManaLeechPct`/`ActiveReflectBonus`; reflect reads the timed window), `Health.cs` (leech read), `SkillRunner.cs` (−voice cast-hook, −2 refund blocks, plain `ApplyBurst`), `SkillTooltip.cs` (−Ring&Weapon section, +describe lines), `Item.cs` (−`weaponAffixIds`), `Iron Sword.asset` (−seed), `SkillMod.cs` (comment), `Editor/TOOLS.md` (−Affix table row).

## Shield effect + catalog lock — DONE in code (2026-07-24, roster prep)

Added the **Shield** effect (the one new verb the roster forced) and locked the effect/channel catalog with GPT. Shield = a timed absorbing **barrier**: a finite HP pool that soaks incoming HITS until drained or its window lapses — distinct from Guard's % damage *cut*.

**Files touched:**

- `SkillEffect.cs` — **appended** `SkillEffectKind.Shield` (MUST stay last: Unity serializes the kind by int, so inserting mid-enum would corrupt existing skill `.asset` ints). Added to `IsSelfVerb` (self-only, like Guard/Regen). Reuses existing fields — `amount` = barrier HP, `seconds` = duration; no new serialized field.
- `Character.cs` — `BeginShield(int amount, float seconds)` + `ShieldRemaining` (for a future HUD pip) + a private `ApplyHitToHP(int)` choke that drains the barrier before HP. `TakeDamage` / `TakeMagicDamage` route through it; `TakeTrueDamage` does **not** (DoT bypasses the shield, matching Guard). Cleared in `Revive()`. Soaked damage pops a pale-cyan number (`ShieldAbsorbColor`) via the pooled `DamagePopup` — no per-hit allocation.
- `SkillEffectApplier.cs` — `case Shield` in `ApplyOneToPlayer` → `BeginShield(ScaledAmount, ScaledSeconds)` (barrier HP scales off the effect's `affinity`; duration off `durationAffinity`).
- `Editor/SkillComposerWindow.cs` — field-drawing case (barrier HP + seconds), `AffinityOutputLabel` → "boosts BARRIER HP (sustain: HP/Def/MP)", `HasDurationChannel` includes Shield, effect-summary line.
- `SkillTooltip.cs` — `DescribeEffect` line ("Absorbs N damage for Ns").

**Rejected (design call):** Summon (inseparable from the deferred `Limit`/cap system — uncapped = perf hazard) and Transform (a whole feature). Out of scope.

**Measured finding — the "split strength into channel-ids" clarity win is ALREADY present in the composer UI.** `SkillComposerWindow` already draws per-kind named magnitude labels (`damage cut`, `HP / tick`, `shove speed`, `MP leech`, `slow amount`, …) and already **hides** magnitude for Stun/Freeze/Silence (the dead-knob removal). What does NOT yet exist is a formal **Channels-catalog data type** with stable ids: the labels live in a per-kind `switch` (`AffinityOutputLabel`) and the value storage is still one reused `magnitude` float. Turning that into a real catalog (effects declare owned channel-ids; ring-scaling + composer + tooltip read one source) is a **serialized-schema refactor** that would migrate every existing skill `.asset` — **deferred**, not done here, to avoid an unrequested data migration.

**Status:** not yet compiled/verified in Unity (the user recompiles + watches the Console). Playtest: author a skill with a Shield effect on a Player-binding → cast → take a hit → a pale-cyan absorbed number pops and HP is spared until the barrier drains.

## Standing (unchanged, still true)

- `Item.grantedSkillIds` — weapons carry their kit; equip → `HotbarBindings.SetWeaponKit` fills the bar, unequip clears it.
- Skills don't level — power comes from the ring (affinity). Ring = passive draft + stat bonuses; skills come from the equipped weapon (+ Grimoire **H** for testing).
- Multi-hit cascade, procs (per-hit `chance`), and self-vs-mob effect routing are unchanged.

## Playtest (after recompile)

- **Knockback / Pull:** author a skill with a `Knockback` (or `Pull`) effect on a Mobs-binding → hit a mob → it's shoved away from (or dragged toward) you.
- **ManaLeech / Reflect:** author on a Player-binding → cast → for the window, damage you deal returns MP (watch the MP bar) / damage you take bounces back (orange number on the attacker).
- **Slow / Stun / Root:** author a `Status` effect (Slow factor, Stun, or Slow ~0.02 for root) — the same path as before.
- **Ring scaling still works:** invest a stat that an effect's `affinity` weights → that effect's output grows; no ring worn → ×1.

## Status / caveats

- **Not yet compiled/verified in Unity** as of writing (the user recompiles + watches the Console).
- **Buff / Shield / Curse scaling stat** — defaulted in *Skill Numbers & Balance Baseline* (all durations → MP; Guard → Def; offense debuffs → the caster's offense stat; Haste speed flat). Composer starter-weights not yet updated to match those defaults.
- **Damage-type combat math** (Crit armour-pierce, Magic glance-vs-Evasion) is still the pre-existing stubbed TODO — Physical/Magic behave as before, Crit forces a crit; the full pierce/glance pass touches `Health` and is separate from V3.
- **Ring behavioral voices** and **Cards** are deferred by design (v-next / v3-content) — not in code.
- **Weapon skill-pool + crafting / economy** (each weapon type has a 12-skill pool → rarity-scaled drop of up to 1 ult + 3 normals; Potential / Weapon Essence / refine / transmog / recycle) — DESIGNED (see *Weapon Roster — 12 Identities* + *Weapon Crafting & Economy*), **not in code yet**; today `Item.grantedSkillIds` is hand-authored per weapon.
