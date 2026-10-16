---
status:
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# Active Skill System — V3 (Channels + Role-Locked Scaling)

> # :no_entry: ULTIMATE PRIORITY — HOW TO EDIT THIS DOC (read this FIRST, every time)
> ****NEVER APPEND. ALWAYS UPDATE IN PLACE.** This doc is the single source of truth — it must read as ONE current voice with **zero** outdated lines, **zero** contradictions, and **zero** duplicate lists/catalogs (each fact lives in exactly one section).
>
> When a decision changes something: **find every stale/contradictory passage and REWRITE it.** Do **not** bolt a new "Update / SETTLED (date)" section onto the end and leave the old text rotting above it — that creates exactly the contradictions and duplicate catalogs this rule exists to kill. If new intent supersedes an older passage, **edit that passage directly**; never leave both versions in the doc.
>
> Before finishing any edit: re-read the WHOLE doc and confirm nothing elsewhere still says the old thing.

> **Pillar** (2026-07-22, supersedes V2's hybrid weapon-affix + ring-voice model): give every system **one job**. A skill's numbers, a skill's effects, the ring's scaling, and enemy matchup each live in exactly one layer with no overlap. This is a **big** simplification of V2 — kept as its own doc because the changes are large; V2 stays intact as the record of where we came from.
>
> - **Weapon** = decides which skills may be learned and used (+ weapon identity: damage type, animation). *Family is a composing guide, not stamped on the weapon — see §5.*
> - **Skill** = its own numbers (the **Channels**, §1) **+** its own natural effects (the **Effects**, §2).
> - **Ring** = **numeric scaling only**, and the per-skill scaling weights are what lock roles.
> - **Every unit** = **one** stat model. A player and a mob are 1:1 — same primaries, same formulas, same point budget, no tier and no mob-only multiplier — so an effect never has to ask what kind of thing it hit. See the companion *Unified Unit Stats*.
> - **Later:** ring behavioral *voices* → v-next; **Cards** (enemy matchup) → v3-content, with mob categories.

***This doc owns the CHANNELS, the EFFECTS, the TRIGGERS and the role-lock — and those are still what the game runs.*** *[V4](../gameplay-system-v4/README.md), built 2026-09-20, replaced its rolled weapon kits, its rarity lift on skill output, the worn ring item and "skills don't level"; every passage on those points below has been rewritten to what the code now does, and V4 is where the detail lives.* *Any older doc (V2, the Fresh Brainstorm, the Anti-Stale Charter, etc.) that conflicts with V3 **loses** — V3 wins. The companions that aren't overridden are the mutable, code-facing **"Active Skill System — Implementation Notes"** and **"Unified Unit Stats"** (+ its own Implementation Notes) — they track what the code does today; on a disagreement V3 wins on intent, the Notes win on current code.*

## Why V3 (the redundancy we found)

Comparing V2's **Ring Voices** against a skill's actual outputs showed heavy overlap — a voice that adds Duration and a skill's Duration number are modifying the same thing; same for +Damage, +Cast Speed, +Radius. Two systems fighting over one value. V3 fixes this by splitting responsibilities so **nothing is edited from two places**:

- **Channels answer "how much?"** — the skill owns them (§1).
- **Ring answers "scaled by whom?"** — the ring multiplies, it doesn't add new numbers or behaviors (for now).
- **Effects answer "what happens?"** — authored on the skill (§2), not rolled as weapon affixes.

## 0. One source for skill data — the Composer writes, everything else reads (LOCKED 2026-08-24)

**The skill asset is the single owner of every skill fact** — its numbers, targets, triggers, cost, cadence and its
description. Four surfaces show that data, and **all four are readers**:

| Surface | How it gets the data |
| --- | --- |
| **Skill Composer** | Edits the asset directly. The only writer. |
| **In-game hover card** | `SkillTooltip.Build(def)` off the live asset. |
| **Player wiki** | `wiki/content/skills.generated.json` — a served copy of the export. |
| **These docs** | `data/skills.json` — the neutral export. |

**One describer, three dialects.** Every sentence about a skill comes from `SkillTooltip`, which now writes its
emphasis as Unity rich text, as markdown, or as plain text on request. The words and the numbers are identical in all
three — only the markup differs — so the wiki prints *exactly* what the player is told in game. A second describer
anywhere is the drift this removed; do not add one, in C# or in Python.

**Publishing is part of saving.** The Composer's Save regenerates `data/skills.json` and the wiki's copy
(`SkillExport.cs`, also on **Tools ▸ unwritten ▸ Skills ▸ Export Skill Data**). Neither file is ever hand-edited, and
`tools/check-skill-data.py` fails if either falls out of step with the assets.

**The plain Inspector is read-only** (`SkillDefInspector.cs`) and offers only a button back to the Composer.
Deliberately with **no unlock toggle**: an escape hatch would be used, and the moment it is, the guarantee is gone.
If the Composer cannot express something a skill needs, teach the Composer — that keeps every surface honest.

*Consequence for the wiki: a skill's card is fully generated. Hand-written player prose about a skill's numbers is no
longer a thing that can exist, because there is nowhere to type it. Prose ABOUT the weapon — its identity, how builds
fit together — stays authored, and lives around the generated cards rather than inside them.*

## 1. Channels Catalog — LOCKED (the numeric outputs, "how much?")

A channel is a **stat-agnostic numeric output** — the common language every other system references. A skill exposes **only the channels it owns**. Channels split into two tiers by who owns them. *(Live view: **Tools ▸ unwritten ▸ Catalogs ▸ Skill Channels & Effects**.)*

**Tier A — effect-output channels** — attach per-effect. **TWO things scale them, and they are ADDED together, never multiplied:** the ring (via affinity weights) and the skill's own LEVEL, bought with weapon mastery points (a weapon's trademark raises the same channels — [V4](../gameplay-system-v4/README.md)). Which channels each one moves is `EffectScaling` in `SkillEffect.cs`.

| Channel | Meaning | Unit | Ring-scales? | Level-scales? |
| --- | --- | --- | --- | --- |
| damage | Direct damage — base power × ATK/MATK × affinity | mult | yes | yes |
| hits | Strikes per landing; each is a proc trigger | int | yes (own affinity list) | no |
| heal_amount | HP restored — a fixed amount, or a range rolled per heal (lowest to "HP up to") | HP | yes | yes |
| shield_amount | Barrier HP absorbed before it breaks | HP | yes | yes |
| dot_damage | Status DoT per tick (Poison / Bleed / Burn) | HP | yes | yes |
| sub_stat_magnitude | How far ONE sub-stat moves, up or down | frac or amount (the stat's own answer) | yes | yes |
| outgoing_charge_worth | What ONE charge of Boost / Reduce Outgoing Damage is worth | frac | yes | yes |
| shove_speed | Knockback / Pull speed | u/s | no (raw) | no — movement, not power |
| charges | How many uses a charge pile holds (Parry, Brace, the two outgoing-damage piles) | int | no (`stacksPerApply`, or derived from swing rate) | no |
| cleanse_count | How many ailments one Cleanse removes | int | no (raw) | no — a whole number no fraction can move |
| modifier_seconds | What ExtendDuration hands another skill's pile | sec | yes (duration list) | no — a duration |
| modifier_stacks | What AddStacks hands another skill's pile | int | yes (affinity list) | yes — it is this skill's main number |
| execute_scale | Execute bonus multiplier vs missing HP | mult | base roll scales; this is raw | base roll scales; this is raw |
| proc_chance | Per-hit roll for a Status / Execute proc | 0..1 | yes (own affinity list) | no — clamped to 100%, so a lift saturates then does nothing |
| duration | Effect window length | sec | yes (own affinity list) | **NEVER** — a skill's level must not hold a longer stun |

**The skill-level lift, in full** (V4): every level a character has bought in a skill after the first adds **+5%** to the ring's affinity term on the channels marked above — level 5 is +20%, level 10 is +45% — the same for an active, a passive and an ultimate. It is **added** to that term, never multiplied onto the finished number: multiplying would pay out more the more ring a player had already built, so the same level would be worth far more to a veteran. **It needs no affinity weights:** an effect that authors none has a ring term of exactly 1, and the lift is added to that, so a weightless 10-level skill at level 10 is still ×1.45. Every level bought counts, whatever the grade of the weapon in hand — see [V4 §7](../gameplay-system-v4/README.md).

**Retired channels — folded into `sub_stat_magnitude` when the sub-stat collapse landed:** `dmg_reduction` (Guard), `dmg_taken_frac` (Vulnerability), `heal_per_tick` (Regen), `slow_amount`, `accuracy_loss`, `move_mult` (Haste), `leech_frac`, `reflect_frac`, `buff_mult`. Do not reintroduce any of them as its own channel — see the note under the Effects catalog.

**Tier B — skill / delivery channels** — owned once per skill or tick, not per effect:

| Channel | Meaning | Owner |
| --- | --- | --- |
| cost | Mana cost | SkillDef.manaCost |
| cooldown | Reuse delay. An Ultimate-tier skill also waits on the **shared cooldown**: casting any ultimate starts 40 s on every ultimate the unit has, on top of each one's own, for players and creatures alike | SkillDef.cooldown · SharedCooldown.UltimateSeconds |
| cast_time | Wind-up before firing (DEX shortens it) | SkillDef.castTime |
| range | Max aim distance | SkillDef.castRange |
| radius | AoE / hit width | SkillTickBinding.radius |
| projectiles / spread | Fan copies of THIS tick's carrier + cone angle | SkillTickBinding.projectileCount / projectileSpread |
| bounce | Single Target ticks only: jumps on to N more enemies (nearest not hit, within jump range of the last), each worth a share of the one before, optionally flying back; the Extra Bounce Jumps sub-stat adds whole jumps | SkillTickBinding.bounceJumps / bounceRange / bounceDamageKept / bounceReturns / bounceEffectId |
| cycle | Whole-effect repeat count + interval | SkillDef.repeatCount / repeatInterval |

**Reserved — not built yet** (author only when a skill needs them): **Interval** (DoT tick cadence — hardcoded in `StatusController`), **Limit** (max stacks / summons — no system), **Speed** (projectile travel — lives in the VFX carrier), **Resource** (non-MP gain / drain).

**On magnitude / "Strength":** V2's vague single "Strength / potency" channel is **dissolved** — every effect names the exact thing it scales (`dot_damage`, `heal_amount`, `shield_amount`, or the specific sub-stat a `sub_stat_magnitude` moves), never a floating cross-skill magnitude. Hard-CC (Stun / Freeze / Silence) names none: they're **duration-only**. *(A formal Channels-catalog **data type** with stable ids — so composer + applier + tooltip read one source — is a future serialized-schema refactor; today these ids ARE the catalog and the composer mirrors them.)*

## 2. Effects Catalog — LOCKED (the behaviors, "what happens?")

Every behavior is a **natural effect authored on the skill** (composed in the Skill Composer) — a skill that knocks back simply *is* a knockback skill. **A weapon contributes no BEHAVIOUR and no numbers to a skill; what it does is decide which skills may be learned and used** (§1), so there is still nothing to "fold": the ring and a skill's level both move the same channels and never add a verb. Each effect owns the channels in its row; **ring scales** = what affinity actually grows (verified against the applier, not just the composer label).

**This table is the `SkillEffectKind` enum, in enum order** (re-read off `SkillEffect.cs` on 2026-10-06). It is the full set — there is no effect a skill can author that is not here.

| Effect | Target | Dmg type | Channels | Ring scales | Details |
| --- | --- | --- | --- | --- | --- |
| Damage | mobs/any | yes | damage, hits, mult per point given up, mult per metre | damage, hits | Base power × ATK/MATK × affinity; each strike triggers the binding's procs. Multi-hit SPLITS one total across the strikes. Its multiplier can GROW: by a set amount per point of MP or HP the caster gave up in the same cast (to SpendAllMana / DropHealthToOne, paid once by the first tick carrying one), and by a set amount per metre to the unit struck. Both 0 = no growth |
| Heal | self/others | no | heal_amount | heal_amount | HP, fixed or a range rolled per heal; landing follows the tick's TargetMode |
| Status | mobs/self | no | ailment + dot_damage, duration, proc_chance | output + duration (not proc_chance) | The **six** ailments: Poison / Bleed / Burn (DoT) and Stun / Freeze / Silence (duration-only) |
| Taunt | mobs | no | duration | duration | Forces mob aggro onto the caster |
| Teleport | self (point) | no | — | — | Instant blink to the aimed, range-clamped point |
| **SubStatBuff** | self/others | no | sub-stat + magnitude, duration | magnitude, duration | **THE single path for every timed sub-stat RISE** — move speed, both regens, leech, reflect, accuracy, evasion, attack, defence, crit, max HP/MP, damage, damage reduction. Percent-or-amount is the stat's own answer |
| Cleanse | self | no | debuffs_removed | — | Strips up to N active debuffs |
| Execute | mobs | yes | execute_scale, proc_chance | base roll (scale raw) | Proc per hit: bonus = roll × magnitude × target's missing HP fraction; huge near death |
| Dash | self (point) | no | dash_speed, ease | — | Visible fast glide to the aimed point; blocked by geometry |
| Knockback | mobs | no | shove_speed, duration | duration only | Shoves the mob away from the caster |
| Pull | mobs | no | shove_speed, duration | duration only | Drags the mob toward the caster |
| Shield | self | no | shield_amount, duration | shield_amount, duration | Finite HP barrier; soaks hits until drained; DoT bypasses |
| **Parry** | self | no | charges, duration | duration | CHARGES: the next incoming hit is negated outright (0 damage, no defence math) and spends one. Binary — no magnitude. A spent charge fires `OnParry` |
| **Brace** | self | no | charges, duration | duration | CHARGES: each holds back one incoming hit, banking its damage instead of paying HP; the whole debt lands as one true-damage hit when the window ends. A parry ERASES, a brace POSTPONES |
| **ExtendDuration** | another SKILL's pile | no | modifier_seconds | — | Gives every stack of the named skill's pile more life. Does nothing if that pile isn't running |
| **AddStacks** | another SKILL's pile | no | modifier_stacks | — | Adds stacks to the named skill's pile, each a copy of its newest. THE way one skill feeds another's pile |
| **BoostOutgoingDamage** | self/others | no | magnitude, charges, duration | magnitude, duration | CHARGES: one charge multiplies one outgoing attack by `1 + magnitude` and is spent by it, **hit or miss**. Lands BEFORE armour. Catches basics, skill strikes, executes, mob swings — DoT excluded |
| **ReduceOutgoingDamage** | self/others | no | magnitude, charges, duration | magnitude, duration | The mirror: one charge makes one outgoing attack land at `1 − magnitude`. Authored as the CUT. Its own pile, so a debuff can never spend a boost |
| **ImmuneToDisables** | self/others | no | duration | duration | A plain window (not charges) where nothing stops the holder: flinch, stun, freeze, silence, knockback, pull, and a hit cancelling a cast. Does not touch damage or DoT |
| **SubStatDebuff** | mobs/self | no | sub-stat + magnitude, duration | magnitude, duration | The exact mirror of SubStatBuff, same stat list and pile. Authored as a positive number, applied as a subtraction |
| Summon | self | no | — | — | Tops the caster's summons back up to its kind's maximum. Who and how many belong to the caster, never to the effect |
| **ElementConverter** | self/others | no | converts (Weapon/Armor), element, duration | duration | For the window, the holder's ORDINARY ATTACKS land as the element (Weapon), or the holder is HIT AS the element at level 1 (Armor). A new one on the same slot replaces the old — never stacks. Skills keep their own element. What the converter consumables run |
| **SpendAllMana** | self (always) | no | — | — | The caster spends every point of MP held, on top of the skill's MP cost. Paid once per cast, by the first tick carrying it; what it took is read by every Damage effect after it in the cast. Never rolls a chance. The Fist's all-in blow |
| **DropHealthToOne** | self (always) | no | — | — | The caster's HP drops to 1 — never 0. Not a hit: nothing mitigates it and nothing that answers being hurt fires. What it took is read the same way. The Shuriken's all-in blow |
| **DrainMana** | mobs/any | no | mp_drained, proc_chance | mp_drained | Takes up to N MP from the unit it lands on (never more than it holds); the caster gains exactly what was taken. The Scythe's mana drain |
| **ReturnToSavePoint** | self/others | no | — | — | Sends the unit it lands on back to where it returns to: a person to their save point (the default one when none is set), on whichever map it is; a creature to the home it spawned at. Server only; a dead unit goes nowhere. Never rolls a chance. What a Butterfly Wing runs ([The Keeper and the Wings](../keeper-and-wings.md)) |
| **TeleportToRandomSpot** | self/others | no | — | — | Sets the unit it lands on down on a random walkable spot of the same connected stretch of ground it stands on, so it can always walk back. Nowhere to land = nothing happens, and an item running it is not spent. Never rolls a chance. What a Fly Wing runs |

**Collapsed into the two sub-stat effects — they are NOT separate kinds any more:** Haste, Guard, Regen, Buff, Vulnerability, ManaLeech and Reflect. Each is now one row of the sub-stat table plus one lift at that stat's read site. Adding any of them back as its own verb is the DRY violation the collapse removed. **Slow and Blind are likewise not ailments** — slow is the MovementSpeed sub-stat, and accuracy loss is the Accuracy sub-stat.

**Rejected — out of scope for unwritten:** **Transform** (a whole rig/stance/stat-swap feature).

**Still PLANNED — not in the catalog yet** (V2 vocabulary with no verb today; add when a skill needs them): **targeting** (Penetrate / Homing — Bounce is built, as a tick setting above), **persistence** (Lingering / Trail).

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

## 6. Skill triggers — active vs passive — LOCKED

A skill's **trigger** (`SkillDef.trigger`) decides what makes it run — one authored property:

- **OnCast** — a normal active the player presses (default), from whichever hotbar slot they put it in; uses the cost / cadence / aim of §Tier B.
- **Passive** (any other trigger) — never casts; it runs its authored effects when a combat moment fires. It never goes on the hotbar — the Skills window will not drag one and the server refuses one in a slot — and nothing switches it on: a passive is live the moment a mastery point learns it, and fires while a weapon of its type is in hand (2026-09-21).

Passive triggers come in **two shapes, each matched to the space it names:**

- `OnSkillCastComplete<skill>` **— parameterized** (pick the target active from a dropdown): fires when that active finishes casting **uninterrupted**. The skill set is **open-ended**, so a parameter — not one enum value per skill — is the only clean fit. This is how a passive rides another skill (e.g. Crossed Branches off Piercing Gust).
- **Natural events — flat** (`OnBasicAttackDealt`, `OnKill`, `OnLowHP`, `OnDamageDealt`, `OnParry`, `OnBasicAttackStart`, `OnDamageReceived`): a **small, closed** set, listed directly with no wrapper — the whole of `SkillTrigger` besides `OnCast` and the parameterized one above. `OnParry` **is the deliberate exception** to the skill-parameterized scheme — a parry's success is a *game* event (you blocked a hit), not "you cast a skill."

All triggers route through **one dispatch handler** (`PassiveEvents.Fire`) — the single place a trigger's rule is applied, so no fire-site can forget one. A passive **fires as a full skill** — the same VFX, ticks, and effects an active plays; only the trigger differs. Its aim is auto-resolved from the trigger: an aimed skill (single-target / AoE / projectile) lands on the triggering entity or its location; a **self** skill always casts on the caster (never the victim).

The trigger is only **when** a passive runs. **What** it sets or reads (stacks, marks, owner) is the **State System** doc's job — kept there as the single source so the two don't duplicate.

## Resulting separation (the whole point)

| System | Single responsibility |
| --- | --- |
| Weapon | Decides which skills may be learned and used (see **[Gameplay System V4](../gameplay-system-v4/README.md)**); defines weapon identity |
| Channels (§1) | The skill's numeric outputs ("how much?") |
| Effects (§2) | The skill's natural behaviors ("what happens?") |
| Trigger (§6) | WHEN a skill runs — an active press vs a passive's combat-moment (dispatched via one handler) |
| Ring | Numeric scaling of channels; scaling weights lock roles |
| Cards *(later)* | Matchup specialization vs. enemy categories |
| Ring Voices *(later)* | New behaviors added by ring investment |

## Still open

- Ring **voice** design when it returns (v-next); **card** list + mob categories (v3-content).
- Mobs have the full stat model and their roster is reset onto it (98 creatures, levels 1–170, all on point budget), but there is almost no gear for them to wear yet — two equipment items exist. See *Unified Unit Stats — Implementation Notes*.

---

## Related

- [Gameplay System — V4](../gameplay-system-v4/README.md) — built: crafted gear, quality grades, weapon mastery, and skill levels bought with mastery points.
- [Implementation Notes](implementation-notes.md) — how this design is actually wired today (this doc wins on intent, that one on code).
- [Unified Unit Stats](unified-unit-stats.md) — the unit half: one stat model, so an effect never asks what it hit.
- [State System — Buffs, Debuffs & Combo Marks](state-system-buffs-debuffs-combo-marks.md) — what a state IS, and the stacks/charges/gate rules.
- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the "how much" half, and the locked scaling defaults.
- [Weapon Roster — 14 Identities](weapon-roster-12-identities.md) — the fourteen identity sentences every skill must reinforce.
- [Weapon Crafting & Economy](weapon-crafting-economy.md) — the rolled-kit economy design; the game does not use it (V4 replaced it).
- [Sword — Skill Roster](sword-skill-roster.md) — the one fully-authored pool.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the scaling layer §3 hands the numbers to.
- [Pre-Renewal RO Port — Phase Plan & Formulas](../pre-renewal-ro-port.md) — the combat maths under the channels.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — why the server cannot resolve these effects yet.
- [The Three-Brain Design Process](../../0-process/design-process.md) — the flow that locks anything on this page.
