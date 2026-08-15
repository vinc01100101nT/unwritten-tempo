# Active Skill System — Implementation Notes

> **This is the MUTABLE, code-facing companion** to the design doc *"Active Skill System — V3 (Channels + Role-Locked Scaling)"* — that doc is the **design source of truth**. This doc tracks how the design is actually wired in code and is expected to churn every build. If the two disagree: **V3 wins on intent, this doc wins on what the code does today.** *(Rewritten 2026-07-22 after the V3 teardown — the old V2 "weapon-affix + ring-voice fold" account it used to hold is gone, matching the code.)*
>
> **Sibling:** [Unified Unit Stats — Implementation Notes](unified-unit-stats-implementation-notes.md) covers the UNIT side (one stat model for players and mobs, the point budget, mob gear/rings/drops). This doc covers the SKILL side. An effect landing on anybody at all depends on both.


## The model in one paragraph

A **weapon** grants skills (`Item.grantedSkillIds`). A **skill** is a `SkillDef` asset (authored in the Skill Composer) that binds VFX ticks to a list of `SkillEffect`s. Each `SkillEffect` owns its **numbers** (the V3 *channels*: `damageMult`, `amount`, `seconds`, `magnitude`, `hitCount`, `chance`, binding `radius`, plus `SkillDef` cost/cooldown/castTime/range) **and** its **behavior** (the `SkillEffectKind`). The **ring** does exactly one thing to all of this: **scale it**, via per-effect **affinity** weight-lists. There is no separate mutation/affix/voice engine — every behavior is a `SkillEffect` running through the one `SkillEffectApplier`.

## How V3 maps to live code (all already present)

| V3 concept | Code |
| --- | --- |
| **Channels** (the numbers) | fields on `SkillEffect` + `SkillDef` |
| **Ring = numeric scaling only** | `affinity` / `durationAffinity` / `hitCountAffinity` (`List<StatWeight>`) → `SkillEffectApplier.AffinityMult` reads `Character.RingBonus(StatId)` (0 when no ring worn) |
| **Role-lock** (Heal→MP, Damage→Atk/MAtk) | the `affinity` weights authored on each effect — data, per skill. per-channel scaling **defaults** locked in *Skill Numbers & Balance Baseline* |
| **Damage type as data** | `DamageType {Physical, Magic, Crit}` on each `SkillEffect` (Physical=ATK vs armour, Magic=MATK/unmissable/no-crit, Crit=forced-crit). *Crit-pierce / Magic-glance math is still a stubbed combat-pass TODO — pre-existing, not V3.* |
| **Effects authored on skills** | `SkillEffectKind` + the `SkillEffectApplier` handlers |
| **Family / Archetype = guides** | `SkillFamily` is an **author-time default** for an effect's `DamageType` (runtime never reads it for balance); `SkillArchetype` is a tooltip identity chip. Neither is a stat-lock. |

## V3 teardown — DONE (2026-07-22)

The leftover **V2 reshaper layer** (weapon affixes baked on the weapon + ring "voices" folded per cast through a `CastMods` struct) was **removed** — V3 makes the ring numbers-only and authors every behavior on the skill. Deleted: `RingVoices.cs`, `WeaponAffixes.cs`, `CastMods.cs` (incl. `SkillReshape`), `Editor/AffixVoiceTableWindow.cs`, `Item.weaponAffixIds` (+ the Iron Sword seed), and the weapon equip-passives (`weaponManaLeech/Refund/Reflect`). The `SkillEffectApplier.ApplyBurst` `in CastMods` param and its `ApplyOutgoing`/`EchoDamage`/`ExecuteFinisher` post-pass are gone; `SkillRunner.OnTick` no longer folds anything.

**Nothing was lost — every affix behavior was rehomed or was already reachable:**

| Old affix / voice | Where it lives now |
| --- | --- |
| Slow, Stun | already authorable as a `Status` effect (`Ailment.Slow` — magnitude = speed factor; `Ailment.Stun`) |
| Root | a `Status` `Ailment.Slow` with a \~0.02 factor (a hard stop) — no separate kind (DRY) |
| Push (knockback) | **new** `SkillEffectKind.Knockback` → `MonsterAI3D.Knockback`, shoved away from the caster (magnitude = speed) |
| Pull | **new** `SkillEffectKind.Pull` → `MonsterAI3D.Knockback` from a mirrored origin (toward the caster) |
| Mana-Leech | **new** self `SkillEffectKind.ManaLeech` → `Character.BeginManaLeech(sec, frac)`; the player's damage-dealt choke (`Health.ApplyLeech`) reads `Character.ActiveManaLeechPct` |
| Reflect | **new** self `SkillEffectKind.Reflect` → `Character.BeginReflect(sec, frac)`; `Character.Reflect` adds `ActiveReflectBonus` on top of the permanent thorns-card `reflectPct` |
| Raw damage | **dropped** — a skill authors its own `damageMult` (the Damage channel). Redundant. |
| Refund | **dropped** — a skill authors its own `manaCost` (the Cost channel). Redundant. |
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

## Passive trigger system — DONE (2026-08-04, code foundation)

Rebuilt the passive-skill trigger foundation clean (the reverted sword work's core, redone). A skill now carries a `SkillTrigger`: `OnCast` (a normal active — default, serialized int **0** so every existing `.asset` stays valid) or one of the passive triggers. A passive is **authored exactly like an active** (same VFX, ticks, effects) — **only the trigger differs**. It takes no hotbar slot and isn't pressed; when its trigger fires it **fires as a full skill cast** (`SkillRunner.FireAsPassive` → the same `PlayCycles` path an active uses: VFX + ticks + `SkillEffectApplier`), with **no mana / cooldown / wind-up** (the trigger is the gesture).

**Aim is auto-resolved from the trigger** (via `def.Reticle`, so it matches how the skill was authored):

- **Single-target** (`CastReticle.Unit`) → lands on the triggering entity (the `victim`); skipped if the moment has no entity.
- **Ground / AoE / projectile** (`CastReticle.Ground`) → casts AT the victim's location (or the caster's spot if the moment has no victim, e.g. OnLowHP). So *meteor on OnBasicAttackDealt* rains on the mob you just hit, exactly as if hand-cast there.
- **Self** (`CastReticle.Instant`) → **ALWAYS** casts on the caster; the victim is ignored (a self-buff is never flung onto the mob).

**Re-entrancy:** ✅ **SOLVED 2026-08-07** by the **no-self-feeding brake** (see the stacking section below) — a passive never fires off a chain it's already in, at any delay. The old one-level `dispatching` bool is **deleted**: it only caught synchronous re-entry and missed the real hazard, a passive's damage landing frames later via deferred VFX ticks. Damage payloads on `OnDamageDealt`/`OnKill` passives are now safe.

**Two trigger shapes, matched to the space they name:**

- `OnSkillCastComplete` **— parameterized** by `SkillDef.triggerSkillId` (a target active picked in the composer dropdown): fires when THAT active finishes casting **uninterrupted**. One value replaces the old per-skill events (`OnLunge`, `OnFeintSuccess`) because the skill set is open-ended. **Renamed** from the earlier `OnSkillSuccess` — "cast complete" states plainly that the cast wasn't interrupted, where "success" was context-vague.
- **Natural events — flat** (a small, closed set, so no wrapper): `OnBasicAttackDealt`, `OnKill`, `OnLowHP`, `OnDamageDealt`, `OnParry`. `OnParry` **is the deliberate exception** to the skill-parameterized scheme — a parry's "success" is a natural game event (you blocked an attack), not "you cast the Parry skill."

**One dispatch handler:** every fire-site routes through `PassiveEvents.Fire(trigger, skillId, victim)` — the single choke point, so a new trigger's rule lives in exactly one place and no fire-site can forget it. Passives are `SkillRunner` components that self-register in `OnEnable` / unregister in `OnDisable`, so **registered ⇔ the player owns it** (no separate owned-set to hand-sync). `Fire` runs every passive whose `trigger` matches (and, for `OnSkillCastComplete`, whose `triggerSkillId` == the completed skill's id).

**Files:** `SkillDef.cs` (+`SkillTrigger` enum, `trigger`/`triggerSkillId` fields, `IsPassive`), `PassiveEvents.cs` (new — the handler + registry), `SkillRunner.cs` (`Activate` rejects passives; `OnEnable`/`OnDisable` register/unregister; new `FireAsPassive(victim)` — resolves aim from `def.Reticle` then runs `PlayCycles`; emits `OnSkillCastComplete` at the top of `PlayCycles` **only for actives**, `if (!def.IsPassive)`, so passives don't chain off one another), `Editor/SkillComposerWindow.cs` (new **Trigger** section — dropdown + `DrawSkillPicker` for the target active + a passive help box).

**Wired now:** `OnSkillCastComplete` (from `PlayCycles`, the uninterrupted-cast site). **Natural events wired (2026-08-04):**

- `OnBasicAttackDealt` — `Player3DAttacker.LandBasicHit`, only on a landed (non-whiff, `dealt > 0`) basic; victim = the mob. Covers melee + arrow basics (both funnel through `LandBasicHit`), NOT skill strikes.
- `OnDamageDealt` — `Health.ApplyLeech(Health victim, int dmgDealt)`, the code's own "one player-dealt-damage choke," so basics **and** skills both fire it once per damage instance; victim = the mob hit. (Signature gained the `victim`; its 4 call sites pass `this`.)
- `OnKill` — `Health.Die`, fired first (before pool/destroy so `victim.position` is still valid); victim = the dying mob. Mobs only die to the player here, so this reads as "player kill."
- `OnLowHP` — `Character.ApplyHitToHP`, a **local before/after crossing** of `LowHpFrac` (0.3 · MaxHP): fires the hit that crosses you into low HP while still alive. Re-arms after any heal automatically (no persisted flag). DoT-only crossings bypass this line and aren't covered — fine until OnLowHP becomes a queried state (its consumer Last Stand is a "while low" condition that wants the state store anyway).

**Still NOT wired:** `OnParry` — no parry mechanic exists yet (the Parry skill was reverted); its fire-site lands when that's built.

**Marks / stacks / charge counts:** ✅ **BUILT 2026-08-07** on the unified state store — see the stacking section below. (Owner Private/Shared remains deferred until multiplayer exists.)

**Status:** not yet compiled/verified in Unity (the user recompiles + watches the Console). Playtest: author a passive (trigger = OnSkillCastComplete, pick an active) with a **Self** Haste/Guard effect → cast that active → the passive's buff lands and its icon shows on the tray (via the shared applier's auto-register).

## Skill Composer — effect picker reorg: State ▸ Buffs / Debuffs — DONE (2026-08-04)

Realized the State-System doc's `STATE → BUFF | DEBUFF` hierarchy in the composer's effect picker. The old flat \~18-value `SkillEffectKind` dropdown is now a cascade: **instant effects sit at top level**, and every **durational** effect collapses under a single **State ▸** entry → **Type** (Buffs / Debuffs) → the specific effect. **Purely presentational** — it maps to/from the flat `SkillEffectKind` (+ `Ailment`), so **no enum/serialization change** and existing `.asset`s are untouched.

Classification (author's rules): **State = has any duration** (even \~0s); **Buff = self-castable**; **Debuff = never self**. It's organizational only — **targeting stays on the affects checkboxes**, not enforced by the category.

- **Top-level (instant + ACTION):** Damage, Heal, Execute, Teleport, Cleanse, **Dash, Knockback, Pull**. A "state" is an ongoing CONDITION; a movement action (dash glide, shove) is NOT a state even though it takes time — its timer is movement time, not a condition. So these carry `seconds = 0` and never show an icon. Their movement time lives in dedicated fields (Dash: dash fields; **Knockback/Pull:** `shoveSeconds`, split out of `seconds` 2026-08-04).
- **State ▸ Buffs:** Stat Buff (`Buff`), Haste, Guard, Regen, Shield, ManaLeech, Reflect.
- **State ▸ Debuffs:** Vulnerability, Taunt, + the 8 ailments (Poison/Bleed/Burn/Slow/Stun/Freeze/Silence/Blind). The ailments map to `kind = Status` + `ailment`; the rest are their own kinds. (Taunt IS a state — the mob is forced to target you for its duration.)

`seconds` **now means one thing: ongoing-CONDITION duration.** Instant/action kinds keep it 0 (default 0 + composer forces it via `!HasDurationChannel`), so the tray/nameplate "duration &gt; 0 ⇒ show icon" rule is exact — no kind checks.

Nice cleanup: choosing "Burn" IS the choice, so the **separate** `ailment` **dropdown was removed** from the Status field block (no more two-step "Status → then ailment field").

**Files:** `Editor/SkillComposerWindow.cs` only (`InstantKinds`/`BuffKinds`/`DebuffOptions` tables + `DrawEffectKindTop`/`DrawEffectStateBranch` + the removed ailment popup).

## Buff-tray auto-register: duration-driven — DONE (2026-08-04)

The on-screen buff tray populates from **one rule**: in `SkillEffectApplier`'s player-apply path, **any effect with a duration** (`e.seconds > 0`) tags the applied state with the source skill's icon (with the affinity-scaled seconds + tooltip); instant effects tag nothing. This **replaced the hand-maintained** `IsOngoingSelfBuff` **kind list** (Haste/Guard/Buff/Regen/ManaLeech/Reflect/Shield) — that list is deleted. It falls straight out of the State classification: *has a duration ⇒ it's a State ⇒ it's on the tray*, so no list can drift and a new durational effect shows up for free.

**Scope:** debuffs the player puts **on a mob** belong on the mob (nameplate), not the player tray; a "your active debuffs on enemies" tracker would be a separate feature if ever wanted. **File:** `SkillEffectApplier.cs`.

> ⚠️ **Superseded 2026-08-06:** the separate `PlayerBuffs` display mirror this section originally described is **deleted**. The tray now reads the unified store directly (`Character.States.Active`), and the icon rides the state itself via the applier's apply-context. The duration-driven rule above is unchanged — only its storage moved.

## Unified state store (`UnitStates`) — COMPLETE in code (2026-08-04 → 08-06), NOT Play-tested

The State-System doc's unified per-unit state store: ONE list of active buffs/debuffs per unit, the single source for BOTH gameplay queries AND display (player buff tray + mob nameplate icons). It absorbed the scattered `StatusController` ailment arrays, `Character`'s 8 buff windows, and `Health.VulnMult` — all of which are now deleted.

**Optimized (as agreed — this is a cost, not a speed-up; goal is to keep it perf-neutral):** the list is the source of truth, but the hot scalars (`MoveMult`, `GuardCut`, `VulnMult`, `DamageBuffMult`, `AttackSpeedBuffMult`, `CastSpeedBuffMult`, `ManaLeechPct`, `ReflectBonus`, `ShieldPool`, `CannotAct`, `Silenced`, `HitPenalty`) are **cached** and recomputed only on a set change → readers stay **O(1)**; entries are **pooled** → apply/remove is **zero-alloc**. (N = active states on one unit, \~0–8, hard-capped by the effect kinds; compute cost is microseconds even naive — the only lag risk was GC, designed out.)

**The store itself:** `UnitStates.cs` — the store (`StateId` enum, pooled `State` entries, `Apply`/`Remove`/`Prune`/`Clear`, `Active` display list, `OnChanged`) + the cached query getters + `Recompute`. Two locked decisions: **overwrite = newest-wins** (refresh replaces window+magnitude — the State-System doc's rule), and **lazy self-expiry** (`EnsureFresh` prunes only when an expiry is due → no per-frame `Update`, cached scalars stay correct at the old lazy cost).

**Migration progress (family-by-family, each behaviour-preserving + testable):**

- ✅ **Vulnerability** (mob-side) — `Health.BeginVulnerable`/`VulnMult` → `Health.States`; old fields deleted. Each mob lazily gets a `UnitStates`.
- ✅ **All player-side buff families** (`Character.States`, lazy on the player): **Guard** (`GuardCut`/`Guarded`), **AttackSpeed** & **CastSpeed** buffs (feed `AttackSpeedMult`/`CastSpeedMult` unchanged), **DamageBuff incl. the War-Banner ramp** (ramp params live on the state entry; refresh-keeps / lapse-resets parity comes free from `Apply`'s `EnsureFresh`), **Regen** (ticks off the entry's `nextTick`), **ManaLeech**, **Reflect**, **Shield** (drains via `States.DrainShield`, drops its icon on break). `Revive()` now clears via `States.Clear()`. **All old** `Character` **buff fields deleted; every public property signature kept, so external readers are untouched.** Verified: zero dangling references.
  - Helpers added to `UnitStates` for these: `Remaining(id)`, `Get(id)` (owner reads/mutates the entry for ramp/regen/shield), `DrainShield`, ramp fields on `State`, and `EnsureFresh` inside `Apply` (lapsed re-cast restarts).
- ✅ **Ailments + Haste** (`StatusController`) — all ailment state (timers + magnitudes) and Haste moved into `UnitStates`; the `until[]`/`nextTick[]`/`mag[]` arrays and `hasteUntil`/`hasteMult` are deleted. `StatusController` keeps the VFX auras, the mob-AI delegation (`ai.Stun`/`Slow`/`Stagger`), DoT ticking (off the entry's `nextTick`/`magnitude`), `Cleanse`, and the **action-lock** (`recoverUntil` — a cast/aftercast root, NOT a state, stays). `MoveMult`/`CannotAct`/`Silenced`/`HitPenalty`/`HasteMult` now read the store (`Ailment i` ↔ `StateId i`, matching enum order). **Behaviour change (intended):** ailment refresh is now **newest-wins** (was longer-wins `Max`) — the State-System doc's locked policy.

**➡ Every unit shares ONE** `UnitStates` — buffs, ailments, and vulnerability in one store. **The entire gameplay read/write path is migrated.**

- ⚠️ **The PLAYER needed a fix to make that true.** The player is **TWO GameObjects** (the persistent `Character` data object + the spawned body carrying `Player3DController`/`StatusController`), so the find-or-create lookup silently gave it **two** stores — gameplay read one, the tray the other (symptom: Haste sped the player but showed no icon). `StatusController` now resolves the player's store **lazily** from `Character.Instance.States`. Mobs are one GameObject and were always fine. See memory `player-two-gameobjects-one-store`.

- ✅ **DISPLAY — DONE (2026-08-06).** `BuffBarPanel` (player tray) and `MobNameplate` (pooled icon row above the HP bar) both read `UnitStates.Active`; the legacy `PlayerBuffs` mirror and `RegisterBuff` are **deleted**. Icons ride the state itself, populated by the applier's apply-context latch. *Nameplate limitation:* icons only show once a mob has been damaged (the plate hides at full HP), so a slowed-but-untouched mob shows none.

- ✅ **Mob move speed folded in too.** The mob mover read a private `slowFactor` (clamped 0..1, so it could only ever SLOW) and ignored the store — mob Haste did nothing. It now reads `Health.States.MoveMult` (Slow × Haste); `MonsterAI3D.Slow` is deleted and its callers write the store's Slow state.

⚠️ **NOT Play-tested.** This is the balance-critical rewrite — combat-test guard %, vuln, slow/haste move-speed, stun/silence gating, DoT ticks, shield absorb/break, the damage-buff ramp, and death-clear before trusting it.

## Stacking · charges · the combo gate · the no-self-feeding brake — BUILT (2026-08-07)

The State-System doc's stacking slice, built in one pass. **Compile-verified** — the C# compiler bundled inside the Unity install (`…/DotNetSdk/…/Roslyn/bincore/csc.dll`, the same one Unity itself uses) was run directly over all runtime + editor scripts with the project's real assemblies: zero errors in every touched file. That proves the code is VALID, not that it BEHAVES — **NOT Play-tested.**

**Stacks and charges are ONE mechanism.** Each `UnitStates.State` now holds a list of `Stack` structs, each with its OWN `endTime`, `magnitude` and `intAmount`. A "charge" is simply N stacks granted together sharing a window; a Momentum-style pile is one stack per event, each on its own timer. `State.magnitude`/`intAmount` are now the AGGREGATE (`Refold()` owns that sum), so **every existing reader is untouched** — a non-stacking state is one stack whose aggregate IS its value.

- **Expiry** is per stack (`Prune` thins a pile one stack at a time); `nextExpiry` is the min across all STACK ends, so the lazy no-`Update` design still wakes at the right moment. **Overflow** and **consumption** both take the OLDEST first.
- `MaxStacks = 99` is a hard safety ceiling even for "uncapped" design (Momentum) — bounded memory, design never reaches it.
- **Magnitude encoding — the one trap.** A STACKING state stores per-stack **deltas** (10 × 0.03 ⇒ 0.30); a non-stacking one stores the absolute value (1.5 ⇒ ×1.5). Summing multipliers would be nonsense. `State.Mult` **owns this rule** — `Recompute`, `Character.DamageBuffMult` (which reads the raw entry for the War-Banner ramp) and `StatusController.HasteMult` all route through it, so it can't be re-derived wrongly. Stacking Slow reads `1 − sum`. Additive channels (DoT, guard cut, vuln, shield, leech, reflect, blind) sum correctly with no branch.

**Apply context replaces the display latch.** `SetApplyContext(icon, name, desc, stacking, stacksPerApply, stackLimit)` / `ClearApplyContext()` — one latch carrying BOTH the source icon and the stacking rules, so no gameplay method (`BeginGuard`/`BuffDamage`/`StatusController.Apply`/…) needs a stacking parameter. Set for **every** hit, not just the first, so a Status **proc** landing on hit 3 stacks exactly as it would on hit 0.

**The combo gate is SKILL-level** (`SkillDef.requiresState` / `requireState` / `requireMinStacks` / `consumeAllStacks` / `consumeStacks`), checked and spent through the ONE `StateGate` (new file) from both entry points — `SkillRunner.Spend` (pressed active, before any cost is paid) and `PassiveEvents.Fire` (passive). Unmet ⇒ **nothing happens at all**: no VFX, no cast, no cost.

- ⚠️ `StateId` **is now SERIALIZED** (on `SkillDef.requireState`) ⇒ the enum is **append-only** from here. Its old comment claiming the order was free to change was corrected. Same discipline as `SkillEffectKind`.
- **COMPUTE BEFORE CONSUME** — locked, no snapshots anywhere. Effects run reading the LIVE store; the spend happens in `SkillRunner.OnTick` immediately after `ApplyBurst` returns, once per cast (`consumedThisCast`). So Deathblow sees the whole Momentum pile while rolling damage and spends it the instant that finishes. *(Corollary: a skill with a requirement but no effect bindings never reaches a tick, so it never consumes — a malformed authoring case.)*

**The no-self-feeding brake replaces the old** `dispatching` **bool.** Every effect carries the set of passives that caused it (`chain`, one bit per registered passive, a single `ulong` — zero-alloc). `PassiveEvents.Fire` skips a passive whose bit is already in the chain; unrelated passives still run. Because no passive can repeat within a chain, **every chain is finite by construction** — no depth counter. `SkillRunner.OnTick` pushes its cast's chain around `ApplyBurst` (OnDamageDealt/OnKill fire synchronously inside it), and the multi-hit cascade captures it too, since a flurry's later strikes land on later frames. The old guard only caught synchronous re-entry and missed the real hazard: damage landing frames later via deferred ticks.

- **Only damage a passive's own effects deal stamps its bit** — a passive that merely buffs/extends doesn't mark an active's damage. That's what keeps Encore (extends Bladedance) firing on each kill.

**Display:** stack-count badges on both readers — the player tray (gold "×3", top-left, opposite the timer; pre-built `"x1".."x99"` label table so a count change never allocates) and the mob nameplate (count on the icon). Blank at 1 stack.

**Authoring (Skill Composer):** a **stacking** toggle on any durational effect revealing *max stacks* + *stacks per use* (the charges knob), with an inline note about the bonus-vs-multiplier encoding; and a **requires a state** block in the Trigger section (state · min stacks · consume ALL / consume N) that hides whichever consume field is irrelevant.

**Files:** `UnitStates.cs`, `StateGate.cs` **(new)**, `PassiveEvents.cs`, `SkillRunner.cs`, `SkillEffectApplier.cs`, `SkillEffect.cs`, `SkillDef.cs`, `Character.cs`, `StatusController.cs`, `BuffBarPanel.cs`, `MobNameplate.cs`, `Editor/SkillComposerWindow.cs`.

**Still content-side (the user authors these):** Lock Blades + Momentum. Lock Blades additionally needs the roster's open question answered — "auto-pursue" must resolve to ONE engine-wide mechanism (attack-lunge / pathfind / forced facing).

## Character window — full stat readout + live state deltas — BUILT (2026-08-07)

The Character sheet (**C**) now lists **every** player-facing number and shows what states are doing to each, live. Compile-verified, not Play-tested.

**The rule: the number shown is the character's OWN value; the parentheses after it are the state contribution** — green for a gain, red for a loss, nothing at all when unmodified. This fixed a real inconsistency: ASPD already included its buff while ATK / M.ATK / HIT did **not** (those multipliers land at swing time, not in the stat), so the sheet was silently mixing buffed and unbuffed lines.

**Implemented as two views diffed, not a hand-written bonus table** — `Character.BaseDerived` (an `ignoreStates` flag makes the buff multipliers read neutral, so the baseline runs through the SAME formulas) and `Character.DerivedWithStates` (applies each state where it really lands: the damage buff scales the attack rolls exactly as `RollDamage` does, Blind subtracts HIT, Slow×Haste scales move speed). No formula is duplicated, so the window can't drift from combat.

`DerivedSnapshot` **gained** `castSpeedMult`, `hardDefPct`, `hardMdefPct`, `guardCutPct`, `lifeStealPct`, `reflectPct`, `manaLeechPct`, `barrierHP` — twelve numbers that existed in the game but were never displayed (crit damage, cast speed, cast wind-up, hard DEF/M.DEF %, guard cut, barrier, life steal, mana leech, reflect, stamina, ult charge, run speed). State-sourced ones read 0 on the baseline so they display as `0 (+25)` while active.

**Live refresh** runs only while the window is open and rebuilds text **only when a displayed value actually changes** (field compare on the struct; resources compared at their ROUNDED value so a draining stamina bar doesn't rewrite every frame). Per-frame cost is a struct build + compares — no string work, no allocation.

- Window grew 360×500 → **360×660** (the block went from 5 lines to 11; a fixed height would have clipped the tail).
- Run speed is read from the player's BODY (a different GameObject — see the two-GameObject split) and cached.

**Files:** `DerivedSnapshot.cs`, `Character.cs`, `ProgressionWindows.cs`.

## Standing (unchanged, still true)

- `Item.grantedSkillIds` — weapons carry their kit; equip → `HotbarBindings.SetWeaponKit` fills the bar, unequip clears it.
- Skills don't level — power comes from the ring (affinity). Ring = passive draft + stat bonuses; skills come from the equipped weapon (+ Grimoire **H** for testing).
- Multi-hit cascade, procs (per-hit `chance`), and self-vs-mob effect routing are unchanged.

## Playtest (after recompile)

**⚠️ PRIORITY — the 2026-08-06/07 work is untested and four layers deep. Test BOTTOM-UP or a failure won't localise:**

1. **State store (gameplay)** — guard %, vulnerability, slow/haste move speed (player AND mob), stun/freeze gating, silence, blind, DoT ticks, shield absorb+break, regen, damage-buff ramp, death clears states.
2. **Display** — buff tray + mob nameplate icons appear with the source skill's icon and clear on expiry.
3. **Stacking** — author a stacking effect: stacks climb, show a count badge, expire one at a time. Then a charge-granting skill + a consumer (gate blocks with 0 charges; damage reads the full pile before spending).
4. **Character sheet (C)** — every stat listed, green/red parentheses while buffed, nothing clipped at the window's bottom.

- **Knockback / Pull:** author a skill with a `Knockback` (or `Pull`) effect on a Mobs-binding → hit a mob → it's shoved away from (or dragged toward) you.
- **ManaLeech / Reflect:** author on a Player-binding → cast → for the window, damage you deal returns MP (watch the MP bar) / damage you take bounces back (orange number on the attacker).
- **Slow / Stun / Root:** author a `Status` effect (Slow factor, Stun, or Slow \~0.02 for root) — the same path as before.
- **Ring scaling still works:** invest a stat that an effect's `affinity` weights → that effect's output grows; no ring worn → ×1.

## Status / caveats

- **Compile status:** the 2026-08-06/07 work (state store, display, stacking, combo gate, brake, character sheet) was **compile-verified offline** — the C# compiler bundled inside the Unity install (the same one Unity uses when it recompiles) run directly over the scripts with the project's real assemblies, so nothing in Unity had to be touched or refreshed. Zero errors in every touched file. Earlier sections' code predates that check. **Compiling is not behaving** — a wrong formula compiles perfectly; none of it is Play-tested.
- **Buff / Shield / Curse scaling stat** — defaulted in *Skill Numbers & Balance Baseline* (all durations → MP; Guard → Def; offense debuffs → the caster's offense stat; Haste speed flat). Composer starter-weights not yet updated to match those defaults.
- **Damage-type combat math** (Crit armour-pierce, Magic glance-vs-Evasion) is still the pre-existing stubbed TODO — Physical/Magic behave as before, Crit forces a crit; the full pierce/glance pass touches `Health` and is separate from V3.
- **Ring behavioral voices** and **Cards** are deferred by design (v-next / v3-content) — not in code.
- **Weapon skill-pool + crafting / economy** (each weapon type has a 12-skill pool → rarity-scaled drop of up to 1 ult + 3 normals; Potential / Weapon Essence / refine / transmog / recycle) — DESIGNED (see *Weapon Roster — 12 Identities* + *Weapon Crafting & Economy*), **not in code yet**; today `Item.grantedSkillIds` is hand-authored per weapon.