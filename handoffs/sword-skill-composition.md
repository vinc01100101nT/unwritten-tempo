# Handoff — Sword skill composition

**Task** — Author the Sword weapon's full skill roster (9 actives + 9 passives + 3 ultimates) as Skill Composer assets, and build whatever engine channels the design needs. Design authority: `docs/active-skill-system-v3/sword-skill-roster.md` (what each skill is) + `skill-numbers-balance-baseline.md` (how much) + `state-system-buffs-debuffs-combo-marks.md` (stacks/charges rules). No Tempo issue. **Read this file and you have everything — no other prompt needed.**

**Status — IN PROGRESS. Repo is CLEAN; everything below is COMMITTED and COMPILES.** 9 of 21 roster entries authored. **Nothing was play-tested this session** — every claim below is either compile-verified or proven by offline replay, never observed in Play. Treat "works" as "should work".

---

## ⛔ DECISION 2026-08-13 — NO "effect on next act" (1:any). We stay 1:1. Do not build it.

**The rule going forward:** a stack pile is granted by one skill and consumed by ONE NAMED skill that says `requireSkillId: <granter>`. That is the only stack relationship the engine has, and the only one we want for now.

**What was rejected:** a general "1:any" system where a pile is spent by *whatever next eligible action happens* (next hit / next cast), with an authored spend-moment and an authored payoff.

**Why (decided with the user after a full design pass — do NOT re-litigate without new information):**

1. **A framework wouldn't generalise the expensive part.** Every payoff hooks a *different, unrelated* line: damage → `UnitOffense.Dealt`; free cast → `SkillRunner` MP spend at **two** sites (~lines 210 and 383); no-cooldown → `SkillRunner` `nextUse` (~391); instant-cast → the castTime fork (~222). The only shared part is ~20 lines of bookkeeping (hold a stack, spend it at a moment). The framework buys those 20 lines and nothing else.
2. **Most payoff × moment combinations are meaningless and fail SILENTLY.** A damage payoff spent at `OnBasicAttackStart` removes the stack *before* the damage is rolled, so it does nothing, with no error. 6 payoffs × 8 triggers = 48 pairings, only a handful valid. Building validation UI for that violates the project's own "no dead knobs" rule.
3. **Nothing gets harder by waiting.** Adding a payoff later costs exactly what it costs today — same 20 lines, same single hook. No migration, no lock-in. So buy play-test information first.

**Context (not the reason):** this repo has twice built and deleted a layer that sits between a skill and its cast and rewrites it (`RingVoices` / `WeaponAffixes` / `CastMods`, removed 2026-07-20 and 2026-07-22). The V3 rule it violated is in `docs/active-skill-system-v3/README.md`: *"Every behavior is a natural effect authored on the skill… The ring does numbers only, so there's nothing to fold."* `SkillContext` (in `SkillMod.cs`) is the leftover shell — ~20 per-strike knobs, **all unused**, every strike passes `Default`. Do not revive it.

### Roster entries that MUST BE REWORKED because they assumed effect-on-act

These three are written in `sword-skill-roster.md` as one-shot "your next X is modified" effects. The engine will not do that. **They need a DESIGN rework before they can be authored — this is the user's call, not a code task.**

| Skill | Doc text today | Why it can't be authored | Rework direction (needs user's decision) |
| --- | --- | --- | --- |
| **Composure** | OnParrySuccess → *your next skill costs 0 MP* | Needs a one-shot COST payoff spent at the next cast. No cost channel exists at all. | Re-spec as 1:1 or durational — e.g. "MP refund on parry", or a timed MP-cost-reduction window. |
| **Opening** | OnFeintSuccess → chance to grant a state; *next damaging strike consumes it for +200% ATK* | Needs a one-shot damage payoff spent by the next landed hit, granted by a *different* skill than the one that spends it. | Either make Feint itself deal the bonus (collapse setup+payoff into one skill), or name a specific consumer skill (1:1). |
| **Parry's next-strike +50%** | Parry → next strike deals +50% | Same shape as Opening. | Fold the +50% into a named consumer, or drop it and let Parry stay a pure negate. |

**The deeper problem under 1:1:** a 1:1 pair requires the CONSUMER to name the GRANTER, and the consumer must be a *specific authored skill*. "Your next skill, whichever it is" has no consumer to name. So any rework must either name a consumer or stop being a next-act effect.

**Do NOT quietly author these as timed buffs and call them done** — a 5s +50% damage buff is a different skill from "+50% on your next strike". The user must approve any substitution.

---

## Committed this session (6 commits, all compile-clean, nothing pushed)

```
962ce43f  Sword roster assets: id rename repair, buff magnitudes, tuning
29b6ade3  Renaming a skill id re-points everything that referenced it
54473237  Mobs deal damage by the same rules as the player; resolve crit -> hit -> damage
ee008c17  Check C# without opening Unity: tools/check-unity-code.sh
666a4305  Magnitude always means a bonus fraction: 0.3 = +30%
2eeb5ee1  One damage-landing choke point: UnitMitigation.Landed
```

### 1. `tools/check-unity-code.sh` — RUN THIS AFTER EVERY C# EDIT

Checks the game's C# for compile errors **without opening Unity and without a force-refresh** (~4s). Unity's own compiler lives in the app bundle; the script runs the same check and never touches the running editor. Checks BOTH halves (game scripts + `Tools ▸ unwritten` editor windows — an editor-only mistake is otherwise invisible until someone opens that window).

```bash
tools/check-unity-code.sh
```

Exit 0 = clean, 1 = compile errors (printed with file+line), 2 = setup problem. Verified to CATCH deliberate errors in both halves, not merely report "clean". Needs `unity/Library/ScriptAssemblies/` (present on any project Unity has opened once).

### 2. `magnitude` is ALWAYS a bonus fraction — `0.3` = +30%

It used to mean two different things depending on `stacking` (non-stacking = absolute multiplier, stacking = per-stack bonus), so Momentum authored at `1.03` ran as **+103% per stack** and Lock Blades at `1.05 × 6 stacks` as **+630%**. Now `State.Mult = 1 + magnitude` and `Slow = 1 − magnitude`, with no branch. Each entry point converts its own units once (`StatusController.Haste` and `Character.Buff*` still speak multipliers; the ailment Slow still authors a target-speed factor). Composer slider is `0..2` labelled "bonus (0.3 = +30%)"; tooltip appends "per stack" on a stacking buff.

### 3. `UnitMitigation.Landed` — the one INCOMING-damage choke

All 6 intakes hand their raw post-defence float to `UnitMitigation.Landed(states, raw)`, which owns amplify → round → guard. Vulnerability used to be hand-written into each formula and the player's two intakes had silently missed it.

### 4. `UnitOffense` — the OUTGOING twin (new file)

`UnitOffense.Dealt(states, rawRoll)` and `UnitOffense.AttackInterval(baseInterval, aspdMult)`. Player and mob both call them. **Mobs now obey damage buffs and attack-speed buffs** — previously a mob used its raw `attack` field and raw `touchInterval`, so a "cut this mob's damage" debuff did nothing at all. Mob attack speed is capped at the same 3× ceiling as the player (`UnitStatMath.MaxAttackSpeedMult`). Player damage and swing timing verified unchanged.

### 5. Damage buff now sums EVERY pile

`Character.DamageBuffMult` read only the FIRST `DamageBuff` pile via `States.Get`. Piles are keyed per granting skill, so Momentum + Lock Blades together lifted damage by only one of them (~27% loss, and cast order silently changed your damage). `UnitStates.DamageBuffMult` now walks all piles and folds each pile's ramp. It is computed **live, not cached**, because a ramp advances on its own clock with no expiry to wake a cache. Single-pile cases (including ramp + cap) verified byte-identical.

### 6. Attack resolution is now **crit → accuracy → damage**

`Combat.Lands` is a RANDOM roll, so it can only happen once. It moved to the ATTACKER (`Combat.Missed(crit, hit, flee)`) and its result travels in `DamageInfo.missed`; defenders only PRESENT the miss and never re-roll. A whiff now costs no damage roll. Only TWO accuracy rolls exist in the game (player→mob in `Player3DAttacker`, mob→player in `MonsterAI3D`); skill strikes always land (`attackerHit: -1`). Verified over 400k simulated attacks: miss rate and damage-on-hit unchanged.

### 7. Renaming a skill id re-points every reference

The composer's id field is now DELAYED (commits on Enter/focus-loss, not per keystroke) and a rename sweeps every persisted reference: `triggerSkillId`, `requireSkillId`, and **every Item's `grantedSkillIds`** (a weapon hands out its kit by id, so a rename would otherwise strip the skill off the weapon). One undo step; refuses a blank or duplicate id; logs what it re-pointed. **Known gap:** the player's SAVE stores hotbar slots by skill id and is NOT rewritten — a renamed skill drops off the hotbar and must be re-dragged once.

### 8. `ApplyOneToMob` now excludes self-verbs explicitly

Both passes are handed the SAME effect list; caster-side effects were only kept off mobs *accidentally* (that switch had no case for them). Adding one (a Buff, say) would have made the caster's own Overclock buff every mob it hit. Now stated once with a comment so it can't be broken by edit.

---

## Live roster state (read from disk 2026-08-13)

| id | file | trigger | MP | CD | notes |
| --- | --- | --- | --- | --- | --- |
| `skill.sword.lunge` | `skill.new 2.asset` | OnCast | 0 | 1s | Dash + strike. **Doc says 8 MP / 5s / reach 8; asset is 0 / 1s / range 4. UNRECONCILED.** |
| `skill.sword.auto_lunge` | `skill.new 5.asset` | OnBasicAttackStart | 0 | 1s | Spends a Lock Blades charge to re-lunge. `requireSkillId: skill.sword.lock_blades`, consume 1. |
| `skill.sword.lock_blades` | `skill.new 4.asset` | OnSkillCastComplete→Lunge | 0 | 5s | Grants **5** charges (doc says 3). Buff/Damage stacking, magnitude **0.05** = +5%/stack (+25% at 5). |
| `skill.sword.momentum` | `skill.new 3.asset` | OnBasicAttackLanded | 1 | 5s | Buff/Damage stacking, cap 99, magnitude **0.03** = +3%/stack. |
| `skill.sword.guard_stance` | own file | OnCast | 12 | 10s | Guard 50% / 5s, Def-scaled. Complete. |
| `skill.sword.committed_strike` | own file | OnCast | 14 | 6s | 200% Atk, 1.2s cast. Complete. |
| `skill.sword.parry` | own file | OnCast | 8 | **1s** | Parry 1 charge / 3s. **Missing the next-strike +50% — see the rework table above.** |
| `skill.sword.overclock` | own file | OnCast | 12 | 6s | +30% aspd 8s (magnitude 0.3) + self-Vulnerability 3s (magnitude 1 = +100% damage taken). Complete. |
| `skill.sword.deathblow` | own file | OnCast (ult) | 40 | 3s | 5.79× base, +0.43/stack, `requireSkillId: skill.sword.momentum`, consumes ALL. User-tuned. |

**The only two 1:1 stack pairs that exist:**

- `skill.sword.lock_blades` grants → `skill.sword.auto_lunge` consumes **1**
- `skill.sword.momentum` grants → `skill.sword.deathblow` consumes **ALL**

All 9 share ONE empty placeholder VFX (`cue_trigger_target_hit.asset`, tickId `follow_hit`) — no visuals yet, by design. Reuse it rather than creating more empties.

---

## What's left

**Authorable today, no engine work:**

1. Reconcile Lunge's MP/cooldown/range against the doc (asset 0 MP / 1s / range 4 vs doc 8 MP / 5s / reach 8).
2. Reconcile Lock Blades' charge count (asset 5 vs doc 3).
3. Parry's cooldown is 1s on disk — confirm intended (doc baseline was 6s).

**Needs a DESIGN rework first:** Composure, Opening, Parry's +50% (see the rework table at the top).

**Blocked on a missing engine channel:**

| Missing | Needs |
| --- | --- |
| Onslaught | "fire N real basic attacks" effect + the recursion guard (roster doc impl item #9) |
| Disarm | an outgoing-damage CUT on a mob. The engine now applies mob damage buffs, **but** `Buff` is a self-verb so it cannot target a mob — see gap #1 |
| Feint | interrupt-on-windup + mobs telegraphing ~0.25s early (they currently tell AT impact) |
| Perfect Form | same-target hit-streak tracking |
| Unwavering | knockback/interrupt immunity — the player has no knockback to be immune to |
| Pressure | mob HP regen (mobs have none) |
| Bladedance (ult) | 8s channel, full invuln, aspd-scaled hit count |
| Retribution (ult) | damage banking + scaling parry chain |
| Encore | depends on Bladedance |
| Last Stand | depends on Retribution |

Plus **1 active slot deliberately left open** in the design.

---

## Known gaps / open decisions (all deliberately NOT actioned)

1. **`Buff` cannot target a mob.** `SkillEffect.IsSelfVerb` decides which side an effect lands on *by kind*, and `Buff` is self-only. Flipping it would re-route Overclock / Momentum / Lock Blades onto enemies and break all three. The real fix is making the side an **authored** choice per effect. **Needs the user's call. This blocks Disarm.**
2. **Mobs have no stats / skills / weapons.** A mob's `attack` and `hit` are plain ints on `MonsterAI3D`; `SkillRunner` is hard-wired to the player (`Character.Instance`, `GetComponent<Player3DController>`). The user's stated goal is "player === mobs for SKILLS, WEAPONS, STATS" — a multi-session project, sequenced stats → equipment → skills. Already shared: incoming mitigation (guard/vuln/shield/parry), move speed, blind/stun/freeze/DoT, and now outgoing damage + attack speed.
3. **One-shot buffs on mobs.** `PassiveEvents` is player-only, so a mob can't hold or spend one. Moot while 1:1 stands.
4. **`skill.new 1.asset` ("Heal", id `skill.single_heal`)** carries a stacking damage buff at magnitude 1 = +100%/stack for 20.2s. Looks like a placeholder; behaviour unchanged by this session's work. Worth a look.
5. Renaming a skill does not rewrite the save's hotbar (see item 7 above).

---

## Proven facts (measured this session — trust these, don't re-derive)

- `Combat.Lands` is RANDOM → an accuracy check can only happen ONCE per attack.
- Only 2 accuracy rolls exist in the whole game; skill strikes always land.
- `Health.ApplyLeech` is THE single player-dealt-damage-landed choke (all 4 intakes call it) and is where `SkillTrigger.OnHit` fires. `PassiveEvents.Fire` is the single dispatcher for all 8 triggers.
- A player ACT is only ever: a basic attack, or a skill cast. Ultimates are just skills (`def.IsUltimate` adds a charge check). Dash/Teleport are skill *effects*. There is no item-use action.
- MP is spent in exactly 2 lines, both `Character.Instance.SpendMana(def.manaCost)`.
- One damage roll costs **21 nanoseconds** (benchmarked with the real .NET runtime). Combat math is never the performance problem here; per-frame allocation across many mobs is (see `PERFORMANCE.md`).
- `CombatRng` is unseeded Unity `Random` — no replay/netcode determinism to preserve.
- `SkillContext` (`SkillMod.cs`) is a dead shell: ~20 knobs, all unused, every strike passes `Default`.

**Hypotheses (NOT verified):** none outstanding.

---

## Next single step

Ask the user to settle the **Composure / Opening / Parry-+50% rework** (table at the top) — it unblocks 3 of the 12 remaining entries and needs only a design decision, no code. If the user would rather write code, the next best-value item is the **`Buff`-cannot-target-a-mob** decision (gap #1), which unblocks Disarm.

**Do NOT start the 1:any / effect-on-act system. That decision is made.**

## How to resume

```
Read handoffs/sword-skill-composition.md and continue the Sword roster.
```
