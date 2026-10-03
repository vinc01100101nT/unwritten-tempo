---
status:
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# Active Skill System — Implementation Notes

> **This is the MUTABLE, code-facing companion** to the design doc *"Active Skill System — V3 (Channels + Role-Locked
> Scaling)"* — that doc is the **design source of truth**. This one records how the design is actually wired **today**.
> If the two disagree: **V3 wins on intent, this doc wins on what the code does.**
>
> # :no_entry: NEVER APPEND. ALWAYS UPDATE IN PLACE.
> This doc states the CURRENT wiring, not the order things were built in — git holds the history. When something
> changes, **rewrite the passage that described the old wiring**; never add a dated section and leave the old text
> above it. Before finishing any edit, re-read the whole doc and confirm nothing elsewhere still describes the old
> behaviour.
>
> **Sibling:** [Unified Unit Stats — Implementation Notes](unified-unit-stats-implementation-notes.md) covers the UNIT
> side (one stat model for players and mobs, the point budget, mob gear/rings/drops). This doc covers the SKILL side.
> An effect landing on anybody at all depends on both.

## The model in one paragraph

A **weapon** carries no skills: a character buys each skill with that weapon's mastery points and may use it only
while holding that weapon type (`MasteryProgress`, [V4 §5–§7](../gameplay-system-v4/README.md)). A **skill** is a `SkillDef` asset (authored in the Skill Composer)
that binds VFX ticks to a list of `SkillEffect`s. Each `SkillEffect` owns its **numbers** (the V3 *channels*:
`damageMult`, `amount`, `seconds`, `magnitude`, `hitCount`, `chance`, binding `radius`, plus `SkillDef`
cost/cooldown/castTime/range) **and** its **behavior** (the `SkillEffectKind`). The **ring** does exactly one thing to
all of this: **scale it**, via per-effect **affinity** weight-lists; the skill's own level adds a lift to that term
(`Character.MasteryLift`). There is no separate mutation/affix/voice engine —
every behavior is a `SkillEffect` running through the one `SkillEffectApplier`.

## How V3 maps to live code

| V3 concept | Code |
| --- | --- |
| **Channels** (the numbers) | fields on `SkillEffect` + `SkillDef`, each a `ScaledNumber` |
| **Ring = numeric scaling only** | `affinity` / `durationAffinity` / `hitCountAffinity` / `chanceAffinity` (`List<StatWeight>`) → `SkillEffectApplier.AffinityMult` reads `Character.RingBonus(StatId)` (the ring is part of the character and always counts) |
| **Role-lock** (Heal→MP, Damage→Atk/MAtk) | the `affinity` weights authored on each effect — data, per skill. Per-channel scaling **defaults** locked in *Skill Numbers & Balance Baseline* |
| **Damage type as data** | `DamageType {Physical, Magic, Crit}` on each `SkillEffect` (Physical = ATK vs armour, Magic = MATK/unmissable/no-crit, Crit = forced-crit) |
| **Effects authored on skills** | `SkillEffectKind` + the `SkillEffectApplier` handlers |
| **Which side an effect lands on** | `SkillEffect.side` (`EffectSide`) — every effect states whether it hits the caster or the target; `SkillEffect.LandsOnCaster` is the one reader the runtime and the composer both partition on |
| **Family / Archetype = guides** | `SkillFamily` is an author-time default for an effect's `DamageType` (runtime never reads it for balance); `SkillArchetype` is a tooltip identity chip. Neither is a stat-lock. |

## The effect catalog as the composer groups it

Twenty kinds, split by what they act on. The composer offers each group filtered by side, so a section only lists kinds
it can actually run:

- **Health** — Damage, Execute, Heal.
- **Movement** — Teleport, Dash, Knockback, Pull. Every verb that MOVES a body; who gets moved is the targeting
  question, not this one. Their seconds are **travel time, not an ongoing condition**, which is exactly why they are not
  States and never show an icon (`shoveSeconds` holds a shove's travel time, kept out of `seconds` for that reason).
- **State ▸ Buffs** — Sub Stat Buff, Boost Outgoing Damage, Parry, Brace, Immune To Disables, Shield, Element Converter.
- **State ▸ Debuffs** — Sub Stat Debuff, Reduce Outgoing Damage, Taunt, and the **six** ailments (Poison, Bleed, Burn,
  Stun, Freeze, Silence). Ailments map to `kind = Status` + `ailment`; the rest are their own kinds. Choosing "Burn" IS
  the choice, so there is no second ailment dropdown.
- **Cleanse** — the one verb that REMOVES states, so it sits beside the two branches rather than inside either.
- **Modifiers** — Extend Duration, Add Stacks. These target **a skill's pile**, not a body, and are labelled as the whole
  sentence they read as ("extend duration of &lt;skill&gt;") because that IS the effect.

**Every timed sub-stat change is ONE kind.** Move speed, HP/MP regen, mana leech, reflect, life steal, attack, magic
attack, defence, magic defence, accuracy, evasion, crit chance, crit damage, max HP/MP, damage, damage reduction, attack
speed and cast speed are all picked from the `subStat` dropdown rather than being a verb each. Shield, Parry, Brace,
Immune To Disables and the outgoing-damage charges stayed as their own kinds because none of them moves a sub-stat —
they are windows, pools and charges. **Do not re-add a per-stat verb**; that is the DRY violation this collapse removed.

`seconds` **means one thing: ongoing-CONDITION duration.** Instant and movement kinds keep it 0, so the tray/nameplate
rule "duration &gt; 0 ⇒ show an icon" is exact and needs no kind checks.

## The unified state store

ONE `UnitStates` per unit — player and mob alike — holding every buff, debuff and ailment, and serving as the single
source for BOTH gameplay queries AND display (player buff tray + mob nameplate icons). It absorbed the old scattered
ailment arrays, the eight hand-written buff windows on `Character`, and the separate mob vulnerability field; all of
those are deleted.

- **Overwrite = newest-wins.** A refresh replaces window AND magnitude, even if weaker. Ailments follow the same rule.
- **Lazy self-expiry** — `EnsureFresh` prunes only when an expiry is due, so there is no per-frame `Update` and the
  cached scalars stay correct at the old lazy cost.
- **Perf shape (a cost, not a speed-up — the goal was perf-neutral):** the stack list is the source of truth, but the
  hot scalars are **cached** and recomputed only on a set change, so readers stay O(1); entries are **pooled**, so
  apply/remove is zero-alloc. N is ~0–8 per unit; the only real lag risk was GC, and it is designed out.
- ⚠️ **`StateId` is SERIALIZED** on the gate and on modifier effects ⇒ the enum is **append-only**. A member inserted in
  the middle silently re-points saved skills at a different pile.

⚠️ **The player is TWO GameObjects** (the persistent `Character` data object plus the spawned body carrying
`Player3DController`/`StatusController`), so a find-or-create lookup once gave it **two** stores — gameplay read one and
the tray the other, so a move-speed buff sped the player up while showing no icon. `StatusController` resolves the
player's store **lazily from `Character.Instance.States`** for this reason. Mobs are one GameObject and were always
fine. See memory `player-two-gameobjects-one-store`.

*Where a mob's icons show:* on the plate over the unit you are pointing at, damaged or not. No unit draws a plate
under its feet — every creature, elite, shadow, person and villager reads through that one hover plate.

## Stacks, charges and the gate

**Stacks and charges are ONE mechanism.** Each state holds a list of `Stack` structs, each with its OWN end time,
magnitude and integer amount. A "charge" is N stacks granted together sharing a window; a Rising Breeze-style pile is one
stack per event, each on its own timer. The state's aggregate is a fold over its stacks, so **every existing reader is
untouched** — a non-stacking state is one stack whose aggregate IS its value.

- **Expiry is per stack**; the wake-up time is the minimum across all stack ends, so the lazy no-`Update` design still
  fires at the right moment. **Overflow and consumption both take the OLDEST first.**
- `MaxStacks = 99` is a hard safety ceiling even for "uncapped" design — bounded memory, and the design never reaches it.
- ⚠️ **Magnitude encoding — the one trap.** A STACKING state stores per-stack **deltas** (10 × 0.03 ⇒ 0.30); a
  non-stacking one stores the absolute value. Summing multipliers would be nonsense. `State.Mult` **owns this rule** and
  every reader routes through it, so it cannot be re-derived wrongly. Additive channels (DoT, damage reduction, shield,
  leech, reflect) sum correctly with no branch.
- **Apply context, not a per-method parameter** — one latch carries the source icon AND the stacking rules, so no
  gameplay method needs a stacking argument. It is set for **every** hit, not just the first, so a Status proc landing on
  hit 3 stacks exactly as it would on hit 0.

**The gate is per TRIGGER** (`SkillTriggerEntry.requiresState` / `requireSkillId` / `requireStateId` /
`requireMinStacks` / `consumeAllStacks` / `consumeStacks`), checked and spent through the ONE `StateGate` from both
entry points — a pressed active (before any cost is paid) and a passive fire. Unmet ⇒ **nothing happens at all**: no VFX,
no cast, no cost.

- ⚠️ **The gate names a PILE, not just a skill.** Keying by skill alone summed every state that skill granted and spent
  them in list order, so a skill with two stacking effects had its piles merged — a Thousand Petals beat could have eaten a
  Parry charge. The composer's "which pile" dropdown lists only what the chosen granter actually stacks. Changing what a
  skill stacks re-points or drops its consumers on Save, and a gate that names no pile is reported as an authoring error.
- **COMPUTE BEFORE CONSUME** — no snapshots anywhere. Effects run reading the LIVE store; the spend happens immediately
  after the burst returns, once per cast. Last Petal sees the whole Rising Breeze pile while rolling damage and spends it the
  instant that finishes. *(A skill with a requirement but no effect bindings never reaches a tick, so it never
  consumes — a malformed authoring case.)*
- **The run of hits** is a gate source that no skill grants: one per fight, never spent, ended by the fight itself. It
  has no "which pile" half, and it is only a live number at the moments one of your hits just landed.

### Stack counts earned from attack speed

`stacksFromAttackSpeed` + `stackShare` make the stack count *how many times the caster would swing inside this effect's
own window*. Faster attack speed = more stacks in the SAME window, never a longer one. **Deliberately uncapped** (the
stack limit is bypassed) — clamping an earned count to a hand-sized ceiling would delete charges the caster paid for. Two
effects sharing a duration stay in exact proportion: a `0.5` share is always exactly half, because halving a window and
halving that window's count round the same way. Verified offline across 7,839 duration × attack-speed combinations.

### Numbers that answer to the pile

`SkillEffect.useStacks` lets an effect's numbers read the pile the cast is cashing, each number carrying its own
operation and reading, so one effect can have a damage multiplier that builds on the pile beside a duration that
ignores it. This is the **single** rule for stack-sized numbers — damage does not get a private version of it.

## Passives and triggers

A skill carries a list of `SkillTriggerEntry`. `OnCast` (serialized 0, so every existing asset stays valid) is a normal
pressed active; anything else is a passive moment. A passive is **authored exactly like an active** — same VFX, ticks and
effects — and when its trigger fires it **runs as a full skill cast**, with no mana, cooldown or wind-up, because the
trigger is the gesture. It may sit on the hotbar as a tile to watch, but pressing it does nothing and it runs whether or not it is there.

**Aim is auto-resolved from the trigger**, matching how the skill was authored: a single-target reticle lands on the
triggering entity; a ground/AoE/projectile reticle casts at the victim's location (or the caster's spot when the moment
has no victim); a **self** reticle ALWAYS casts on the caster, so a self-buff is never flung onto the mob.

**The trigger set:** `OnSkillCastComplete` is **parameterized** by a target skill id picked in the composer, because the
skill set is open-ended; the rest are a small closed set of natural events — `OnBasicAttackDealt`, `OnKill`, `OnLowHP`,
`OnDamageDealt`, `OnParry`, `OnBasicAttackStart`, `OnDamageReceived`. `OnParry` is the deliberate exception to the
skill-parameterized scheme: a parry's success is a *game* event, not "you cast a skill."

**One dispatch handler** — every fire-site routes through `PassiveEvents.Fire`, so a trigger's rule lives in exactly one
place and no fire-site can forget it. Passives self-register when enabled and unregister when disabled, so
**registered ⇔ the player owns it**, with no separate owned-set to hand-sync.

### The no-self-feeding brake

Every effect carries the set of passives that caused it — one bit per registered passive in a single machine word, so
zero-alloc. A passive whose bit is already in the chain is skipped; unrelated passives still run. Because no passive can
repeat within a chain, **every chain is finite by construction**, with no depth counter to tune. Only damage a passive's
own effects deal stamps its bit, which is what keeps Second Bloom (which merely extends Thousand Petals) firing on each kill.

⚠️ **A passive's slot is fixed and never compacted.** A chain is stamped now but consumed frames later, so shifting a
slot makes a passive stop recognising itself and re-fire off its own damage. This replaced an earlier one-level guard
that only caught synchronous re-entry and missed the real hazard — damage landing frames later via deferred ticks.

### ⚠️ STANDING RULE — never add an `OnRepeat` trigger

A beat of a repeating cast is **not a moment**. Every trigger member is something that happened in the fight; a repeat is
the *same cast still running*, which is why a beat spends its charge directly instead of announcing anything. Give a beat
a trigger and a skill can answer its own beat → which re-enters the skill → which beats again: an unbounded loop that
also pays nothing, because the pile is only spent by the run that owns it. This is the same trap `OnSkillCastComplete`
avoids by announcing **only on a player press** — the brake that stops a granter/consumer pair (Crossed Branches ⇄ Piercing Gust)
refilling each other forever. **A repeating cast reads its pile; it must never raise a moment.** The rule is restated on
the trigger enum in code.

It is also why a skill may name **itself** as its stack source: granting your own charges and spending them over your own
repeats is self-contained, because the repeats replay the cast and never start a new one. Only the "which skill FIRES
this" question excludes self.

## Repeating casts

- **`repeatCostsCharge`** — each repeat spends one charge of the state the trigger requires, and the run continues *while
  the pile holds*, ignoring the authored repeat count. Because the loop reads the LIVE pile, a passive granting charges
  mid-run genuinely lengthens it — **Second Bloom needs no special case.**
- **`repeatReaim` + `repeatReaimRange`** — each repeat re-aims at a RANDOM living mob within range of the **current
  target** (not the caster, not the original aim), so the run walks target to target; an empty search ends it. One beat =
  one target: this is single-target, never AoE.
- **`repeatScaleWithAttackSpeed` + `repeatBeatsPerSwing`** — the beat follows the caster's swing time divided by a whole
  multiple, **read per beat**, so a speed buff landing mid-run quickens the rest of it. At one beat per swing the run
  lands on its authored window at every attack speed, because count and beat come from the same swing time.
- ⚠️ **Grants happen on the OPENING play only.** A repeat replays the whole body, so the effects that created the pile
  re-ran every beat and refilled it faster than the beats drained it — observed live at 98 stacks against the 99 ceiling.
  A repeat now runs everything except the effects that hand out a stacking state.
- **A tick carries the aim of the play that raised it.** Without that, a re-aiming repeat whose previous beat is still
  playing resolves the older beat's effects against the NEWER aim.
- **`starCorners`** — the relocation figure is authored, not fixed at five: the step is `180 + 180/corners`. Odd counts
  only, since an even one never closes, so it is nudged up.

## Afterimages

`afterimageSeconds` + `afterimageTint` drive a pooled, shared snapshot service: each skinned renderer bakes its current
deformed shape into a plain mesh, drawn once and faded, never animated. ⚠️ **Copy, never re-derive** — position and
rotation come verbatim from the source and NO scale is applied, because the bake already delivers world size.
Re-applying the source's scale made every ghost ten times too large, the third time this project has hit that mistake.

## The Character window

The Character sheet (**C**) lists **every** player-facing number and shows what states are doing to each, live.

**The rule: the number shown is the character's OWN value; the parentheses after it are the state contribution** — green
for a gain, red for a loss, nothing at all when unmodified. This fixed a real inconsistency where attack speed already
included its buff while ATK / M.ATK / HIT did not, so the sheet silently mixed buffed and unbuffed lines.

**Implemented as two views diffed, not a hand-written bonus table** — a baseline that runs through the SAME formulas with
the state multipliers reading neutral, and a with-states view that applies each state where it really lands. No formula
is duplicated, so the window cannot drift from combat. Live refresh runs only while the window is open and rebuilds text
only when a displayed value actually changes, so the per-frame cost is a struct build and some compares — no string work,
no allocation.

## Standing facts

- **A weapon carries no skills.** A character learns a skill by spending one of that weapon's mastery points on it in
  the Skills window (K), and raises it a level with each further point; the server decides every spend
  (`NetPlayer.CmdSpendSkillPoint` → `MasteryProgress.TrySpend`).
- **Skills level.** Power comes from the ring (affinity) plus +5% a level after the first
  (`MasteryMath.SkillLevelLift`), counting only the levels the held weapon's grade allows.
- Multi-hit cascade, per-hit proc chance, and self-vs-mob effect routing all run through the one applier.
- The composer is the ONLY writer of skill data; the tooltip, the wiki and the docs export are all readers. See §0 of the
  V3 design doc.

## Verifying a change here

`tools/check-unity-code.sh` compiles three passes (the game, the editor tools, and the game as a shipped PC build sees
it) in about eight seconds without opening Unity, and
`tools/check-skill-data.py` proves the authored skill data is sane. Neither touches the running editor. **Compiling is
not behaving** — a wrong formula compiles perfectly, so neither replaces a play-test.

## Known gaps

- **Damage-type combat math** — Crit armour-pierce is still stubbed. Evasion has no effect on magic at all. Physical and Magic
  behave as before and Crit forces a crit; the full pass touches `Health` and is separate from V3.
- **Composer starter weights** don't yet match the scaling defaults locked in *Skill Numbers & Balance Baseline*.
- **A formal Channels-catalog data type** with stable ids does not exist: the per-kind labels live in a switch and the
  value storage is one reused magnitude field. Turning that into a real catalog would migrate every existing skill
  asset — deferred deliberately.
- **Ring behavioral voices** and **Cards** are deferred by design (v-next / v3-content) — not in code.
- **Weapon skill-pools + crafting/economy** are DESIGNED (see *Weapon Roster — 14 Identities* and *Weapon Crafting &
  Economy*) but **not in code**; today the granted skill list is hand-authored per weapon.
- **`StateId.Knockback` and `StateId.Pull` are declared but never written** — they are instant shoves with no window to
  hold, so they are excluded from the composer's pile dropdown.

---

## Related

- [Active Skill System — V3](README.md) — the design source of truth this tracks.
- [Unified Unit Stats — Implementation Notes](unified-unit-stats-implementation-notes.md) — the unit-side sibling.
- [State System — Buffs, Debuffs & Combo Marks](state-system-buffs-debuffs-combo-marks.md) — the design behind the store, the stacks and the gate.
- [Sword — Skill Roster](sword-skill-roster.md) — the pool composed on top of all of it.
- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the bands the composer's starter weights still don't match.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — what `AffinityMult` is reading.
- [HUD/UI System — Design & Relay](../hud-ui-system.md) — the windows these numbers surface in.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — the applier that does not yet run on the server.
