# Handoff — Sword skill composition

**Task** — Author the Sword weapon's full skill roster (9 actives + 9 passives + 3 ultimates) as Skill Composer assets, and build whatever engine channels the design needs. Design authority: `docs/active-skill-system-v3/sword-skill-roster.md`(what each skill is) + `skill-numbers-balance-baseline.md` (how much) + `state-system-buffs-debuffs-combo-marks.md`(stacks/charges rules). No Tempo issue.

**Status — IN PROGRESS.** 9 of 21 roster entries authored; the stack/payoff engine is built and working. One authored skill has a **numerically wrong value that will read as a huge balance bug** (Momentum, see Blockers #1) — it is NOT a code fault, just an authored number. Everything else listed as working was confirmed in play by the user ("all are working now") EXCEPT the final uncommitted refactor, which has never been compiled.

---

## What's done

### Engine (all committed, all confirmed working in play)

- `OnBasicAttackStart` **trigger** — fires before a basic attack's reach is judged, so a gap-closer moves you in first and the same frame's reach check sees the new position. Fired from `Player3DAttacker.AnnounceAttackIntent`. The announce + the swing it precedes are ONE attempt (`attemptOpen`), closed by `TryAttack` or `Cancel` — so a pre-attack passive fires exactly once per swing by construction. Do NOT re-introduce a timer here; see memory `pre-action-trigger-one-attempt`.
- **Stacks are keyed by the SKILL that granted them** (`UnitStates.State.sourceSkillId`), not by what they do. Two skills that both buff attack keep separate piles. `StacksFrom(skillId)` / `ConsumeFrom(skillId, …)` are the by-source reads. The composer's gate row picks a SKILL ("stacks from"), not a state.
- **Same-kind piles COMBINE additively** in `UnitStates.Recompute` — each pile contributes its bonus, summed, then converted to a multiplier once. Two +30% buffs = ×1.60. LOCKED, user's call; see memory `additive-not-compounding-buffs`. Slow is the deliberate exception (harshest wins).
- **Stack payoff channel** — `SkillDef.stackPayoff` (enum `StackPayoff`, one member `Damage`) + `damageMultPerStack`. Additive; rides `ApplyBurst`'s existing `extraMult` input, so it is not a second damage path. Count is snapshot at cast start (`SkillRunner.castStacks`), before anything is spent.
- **Gate and cost are separate** — `requireMinStacks: 0` = "no gate, just cash whatever is there". That's what makes Deathblow legal (must stay usable on a Sword that never rolled Momentum).
- **Caster-side Vulnerability + Execute** — target-side effects can now land on the caster. The player's damage intake previously did NOT read vulnerability at all (silent no-op); fixed.
- **Dash landing rule** — a relocation aimed at a unit lands at melee reach, stepped 216° around from where the caster stands (= 18° off your own line to the target). Five hops trace a five-pointed star. Stateless, derived from current position, no sequence counter.
- **VFX**: Attach steps accept a playback speed like PlayAt.

### Authored skills (9) — live values read from disk 2026-08-12

| id | file | trigger | MP | CD | notes |
| --- | --- | --- | --- | --- | --- |
| `skill.sword.lunge` | `skill.new 2.asset` | OnCast | 0 | 1s | Dash + strike. **Doc says 8 MP / 5s / reach 8; asset is 0 / 1s / range 4.** Unreconciled. |
| `skill.sword.auto_lunge` | `skill.new 5.asset` | OnBasicAttackStart | 0 | — | Spends a Lock Blades charge to re-lunge. `requireSkillId: skill.sword.lock_blades`, consume 1. |
| `skill.sword.lock_blades` | `skill.new 4.asset` | OnSkillCastComplete→Lunge | 0 | 5s | Grants **6** charges (doc says 3). Buff/Damage, stacking, magnitude **1.05**, 5s. See Blockers #1. |
| `skill.momentum` | `skill.new 3.asset` | OnBasicAttackLanded | 1 | 5s | Buff/Damage, stacking, no cap (99), magnitude **1.03**, 5s. See Blockers #1. |
| `skill.sword.guard_stance` | own file | OnCast | 12 | 10s | Guard 50% / 5s, Def-scaled. Complete. |
| `skill.sword.committed_strike` | own file | OnCast | 14 | 6s | 200% Atk, 1.2s cast. Complete. |
| `skill.sword.parry` | own file | OnCast | 8 | 6s | Parry 1 charge / 3s. **Missing the next-strike +50%.** |
| `skill.sword.overclock` | own file | OnCast | 12 | 6s | +30% aspd 8s + self-Vulnerability 3s. Complete. |
| `skill.sword.deathblow` | own file | OnCast (ult) | 40 | 3s | 5.79× base, +0.43/stack, consumes ALL Momentum, no gate. User-tuned. |

All 9 share ONE empty placeholder VFX (`cue_trigger_target_hit.asset`, tickId `follow_hit`) — no visuals yet, by design. Reuse it for new skills rather than creating more empties.

---

## Uncommitted working-tree changes

`Character.cs`, `Health.cs`, `UnitMitigation.cs` — the vulnerability centralisation. All six damage formulas (4 mob, 2 player) now hand their RAW post-defence float to the new `UnitMitigation.Landed(states, raw)`, which owns amplify → round → guard. Removed as dead: `Health.VulnMult`, `Character.VulnMultNow`, `Character.Guarded`. **Proven damage-identical** by replaying old vs new over 200k random cases (0 differences) — but **never compiled**. Compile first; if it's red, that's why.

---

## What's left

**Authorable today, no engine work:**

1. Fix Momentum's + Lock Blades' magnitude (Blockers #1). Highest value, \~2 minutes.
2. Reconcile Lunge's MP/cooldown/range against the doc.
3. Reconcile Lock Blades' charge count (6 vs the doc's 3).

**Blocked on a missing engine channel** (11 roster entries):

| Missing | Needs |
| --- | --- |
| Parry's +50%, Composure, Opening | a one-shot "next hit/cast consumes it" state — **ONE primitive unlocks three** |
| Onslaught | "fire N real basic attacks" effect |
| Disarm | an outgoing-damage cut on a mob |
| Feint | interrupt-on-windup + mobs telegraphing \~0.25s early (they currently tell AT impact) |
| Perfect Form | same-target hit-streak tracking |
| Unwavering | knockback/interrupt immunity — the player has no knockback to be immune to |
| Pressure | mob HP regen (mobs have none) |
| Bladedance (ult) | 8s channel, full invuln, aspd-scaled hit count |
| Retribution (ult) | damage banking + scaling parry chain |
| Encore | depends on Bladedance |
| Last Stand | depends on Retribution |

Plus **1 active slot deliberately left open** in the design.

---

## Blockers

**#1 — Momentum and Lock Blades are mis-tuned by 100×. PROVEN, not suspected**.A stacking state stores the per-stack **bonus**; `UnitStates.State.Mult` reads `1 + sum(magnitudes)`. Momentum authors `magnitude: 1.03`, so ONE stack = +103% damage, and three stacks = +309%. For the doc's \~3%/stack it must be **0.03**. Lock Blades has the same shape (`1.05` → should be `0.05`, if it's meant to buff at all — its real job is the charge count). I flagged this mid-session; the user replied "nevermind, I've done it", but the on-disk value is still 1.03, so the correction did not land. **Confirm with the user before changing — it's a balance number, their call.**

**#2 — Nothing is compiled.** The uncommitted refactor above has not met a compiler. Unity was never force-refreshed this session (project rule forbids it — the user recompiles).

---

## Dead ends / proven facts

**Proven (measured this session):**

- `Vfx` step kinds are only PlayAt / Attach / Carrier / Shake / Sound, anchored to Origin (caster, sampled LIVE when the step fires) or Target. A step's rotation is authored and fixed — **the composer cannot rotate a step to match a per-cast direction**, so the star's edges can't be drawn from the composer alone. `Vfx.PlayAt(id, pos, rot, scale)`from code can.
- Slash effect prefabs use MESH particles (render mode 4) → they DO respond to rotation and can lie flat on the ground.
- Effect prefabs simulate in **Local** space (`moveWithTransform: 0`), which is why Attach drags particles along instead of leaving a trail. Delayed PlayAt steps anchored to Origin stamp a footprint at each fire time — that's the no-code way to get a trail.
- Damage-formula equivalence for the `Landed` refactor: 200k random cases, 0 differences.

**Hypotheses (NOT verified):** none outstanding.

---

## Next single step

Read `unity/Assets/3_Data/Resources/Skills/skill.new 3.asset` line 68 and confirm with the user whether `magnitude: 1.03` should be `0.03`. That one number is the difference between Momentum being a \~3%/stack ramp and a +103%/stack game-breaker, and Deathblow's payoff sits on top of it.

Then: compile the uncommitted `Landed` refactor, and commit it if clean.

---

## How to resume

```
Read handoffs/sword-skill-composition.md and continue the Sword roster. Start with Blocker #1 (Momentum's
per-stack magnitude), then compile + commit the uncommitted UnitMitigation.Landed refactor.
```