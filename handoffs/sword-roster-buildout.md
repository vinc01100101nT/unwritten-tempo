# Handoff — Sword roster buildout: what's authored, what isn't, what it still needs

**Task** — work the Sword skill roster (`docs/active-skill-system-v3/sword-skill-roster.md`) toward completion:
assess which skills are composable with today's Skill Composer, build the general mechanisms the rest need, and
author the ones that are now reachable.

**Status** — IN PROGRESS. Every code change below **compiles clean** (`tools/check-unity-code.sh`) and the skill data
passes (`tools/check-skill-data.py`, 22 skills), but **nothing from this session has been play-tested**. Treat every
"works" claim below as "builds and is wired", not "verified on screen".

---

## Where we stand

### Authored skills (22 total, 13 of them Sword)

Sword: `blade_dance · brace · committed_strike · deathblow · encore · guard_stance · lock_blades · lunge · momentum ·
onslaught · overclock · parry · retribution`. Non-sword: `blink_step · bulwark · ember_bolt · heal_circle · hex_field ·
mending_tide · meteor · single_heal · war_banner`.

**Onslaught was authored this session** and is the first skill using the new "resolve as a basic attack" checkbox:
5 hits at base power 1, plus an add-stacks effect feeding Momentum's pile. Note the roster doc still says Encore is
"NOT BUILT" — **that is stale**, Encore exists and uses the add-stacks modifier.

### Still unauthored, and what each actually needs

| Skill | Blocker (measured this session) |
| --- | --- |
| **Feint** | Mobs have **no attack wind-up**: `MonsterAI3D` rolls and applies its bite in one frame (~line 440) and `AttackLunge.Punch()` fires at that same instant. There is nothing to interrupt. Needs (a) a real wind-up phase, (b) it being cancellable, (c) a way to author "only if the target is winding up". (a) and (b) are general; (c) can stay Feint's own identity. |
| **Disarm** | Nearly composable. A mob's swing already runs through its own damage buffs (`MonsterAI3D` → `UnitOffense.Dealt`), so a negative Damage buff on the target WOULD work — but the composer clamps the buff bonus to 0..2 (`SkillComposerWindow`, the Stat Buff case), so a negative can't be authored. Haste already allows negatives for exactly this reason. "Weakens their NEXT attack" (one-shot) is still not authorable — only a duration. |
| **Perfect Form** | "Every 3rd hit" (gate minStacks + consumeAll), "add stacks to Momentum" and "extend Momentum" all exist. Missing only: **a streak that resets when you switch target or miss**. General — Pressure needs the same. |
| **Last Stand** | Design-blocked, not engine-blocked. What it modifies (Retribution's parry chain) does not exist, and "while below HP" is a standing change, which the hard-pinned modifier rule forbids. Redesign into a one-shot grant at the `OnLowHP` moment and it composes today. |
| **Composure** | Nothing anywhere touches a skill's MP cost. Needs a charge the next cast spends to waive its cost. Only one consumer — can stay narrow. |
| **Unwavering** | Timing composes (`OnSkillCastComplete` → Guard Stance, same seconds). Needs one new state meaning **"can't be shoved or interrupted"**. General — every future heavy wind-up wants it. |
| **Opening** | Payoff shape composes (a private pile spent by the next strike for bonus damage, via a gate + `useStackValue`). Two gaps: there is no "a Feint landed" moment, and a **chance roll can only be attached to a Status or Execute** (`SkillEffectApplier.Fires`), not to granting a state. |
| **Pressure** | Mobs have **no HP regen at all** (grepped: only the training dummy's tick mentions regen). Nothing to delay. Needs the mob-regen system, a regen-suppression state, and the same target-streak as Perfect Form. |
| **[1 open active]** | Never designed. Nothing to assess. |

### General mechanisms still wanted (each serves 2+ skills)

1. **Mob attack wind-up** — readable and cancellable (Feint, and every future reactive/telegraph skill).
2. **A streak that resets on target change** (Perfect Form, Pressure).
3. **A "can't be shoved or interrupted" state** (Unwavering, future wind-up skills).
4. **Mob HP regen** (Pressure; also wanted for healer mobs).

Two one-knob unblocks: allow a **negative** Stat Buff bonus (Disarm), and allow a **chance roll on granting a state**
(Opening).

---

## What was BUILT this session

- **`resolve as a basic attack`** — a checkbox on the Damage effect (`SkillEffect.resolveAsBasicAttack`). The strike
  becomes the caster's own swing: its crit, its accuracy vs the target's dodge (so it CAN miss), its weapon element,
  and it announces a basic attack, so it feeds Momentum and every on-basic passive. Base power still scales it.
  Damage type + element hide in the composer when it's on.
- **`BasicSwing.Land`** (new file) — the ONE implementation of "land one swing", both sides typed as `ICombatUnit`;
  nothing in it names the player. `Player3DAttacker.LandBasicHit` now routes through it (one copy of the swing).
- **`ICombatUnit`** gained the outgoing-swing half: `BasicHit · BasicElement · BasicIsMagic · RollBasicCrit ·
  RollBasicDamage · Flee · CritShieldLuk`. Implemented on `Character` (player) and `Health` → `MonsterAI3D` (mob);
  the mob's own attack site now reads the same two members, so there is one copy of its numbers.
- **Skill-produced swings announce BOTH moments** (`OnBasicAttackStart` then `OnBasicAttackDealt`) — that was the
  auto-lunge bug: Lunge's auto entry listens for `OnBasicAttackStart` (trigger 7), which only the real auto-attack
  raised. The ordinary swing still announces once (it announces its intent earlier, before reach is judged).
- **`showRepeatsTotalDamage`** — a Repeats-section checkbox ("show repeats total damage dealt"). The yellow total
  becomes the RUN's (`RepeatsDamageTotal`, new file), climbing across beats per mob and closing 1s after that mob was
  last struck. Off = today's behaviour (the multi-hit flurry owns it, one per meteor). Purely presentational; it
  reports from `Health.SpawnElementNumber`, the single place a white number is spawned.
- **`BoostOutgoingDamage`** — a new charge-based buff effect + `StateId.OutgoingDamageBoost`. One charge multiplies one
  outgoing attack and is spent by it, hit or miss. Applied at `UnitOffense.Boosted`, called once per attack from
  `BasicSwing.Land`, `SkillEffectApplier.LandStrike`, `ApplyExecute`, and the mob's attack site. DoT never reaches it.
  Multiplies BEFORE the target's armour (deliberate: flat armour is then paid out of a bigger number).
- **`SkillEffectApplier.LandStrike`** — extracted; the two damage sites (instant + multi-hit beat) now share one body.
- **Hotbar label** — an icon-less cell now shows the skill's own name; the shortened-name field is only used when
  someone actually shortened it (`SkillDef.HotbarLabel`). It is still NOT editable in the composer.
- **Damage popup fixes** — the live yellow total now sits above every per-hit number (lift derived from the per-hit
  rise × lifetime), pops once per change instead of riding an endless sine, doesn't flinch when it ends, and its pop
  is twice as strong. One `popAge` timer + `PopScale` drives both pop kinds.
- **`prove-skill-data-check.py` fixture repair** — its "a repeat that spends charges it never gated" case inserted a
  duplicate `repeatCostsCharge` key, which the real one further down overruled once assets began carrying that field;
  it now flips the asset's own value. All 11 faults caught again.

## Uncommitted working-tree changes

None expected — this handoff was written immediately before committing the whole tree to `main`. If the tree is dirty
when you read this, the commit did not complete; check `git log -1` first.

## Dead ends / corrections (don't repeat these)

- **PROVEN:** the add-stacks modifier DOES work on a lapsed pile — it starts the pile from the granting skill's own
  authored size and duration (`UnitStates.AddStacksTo`, the `else if (seconds > 0f)` branch). An out-of-date summary
  comment claimed otherwise and cost a round; the comment is fixed.
- **PROVEN:** composed skills are player-only today — mobs have no `SkillRunner`. That is a whole-system gap, NOT a
  property of any one effect. Do not present it as a design consequence of a feature (the user's standing law: mobs get
  everything players get; a player-only gap is a bug).
- **PROVEN:** a multi-hit strike never rolled accuracy (always-lands, attackerHit −1) — only the new basic-attack
  branch does.
- **UNKNOWN (needs play-test):** Onslaught holds the caster for its whole run AND now triggers auto-lunges mid-run.
  A passive-fired skill does not check that hold (`SkillRunner.FireAsPassive`), so the lunges will probably move the
  caster between beats. Whether that reads well is unresolved.

## Next single step

Play-test the batch (checklist in the session's last few reports): Onslaught + Lock Blades (charges spend per strike,
Momentum climbs, lunges vs the caster hold), Blade Dance's yellow total, and a Boost Outgoing Damage test skill
(3 charges at +100%, verify the 4th hit is normal and a whiff still spends one).

## How to resume

```
Read handoffs/sword-roster-buildout.md, then continue the Sword roster buildout from "Next single step".
```
