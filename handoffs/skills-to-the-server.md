# Skills to the server

**Task** — make the SERVER resolve a player's skill strikes instead of taking the client's word for their damage.

## Status

**MOSTLY DONE, UNPLAYED (2026-08-29).** The server now rolls a skill's damage itself. Written and
compile-verified only — nobody has swung a skill since. Don't trust any "it works" claim about it.

## What's done

- A skill strike is **named, not measured**. The request carries the skill id, which beat of that skill, which
  effect of that beat, and the target — never a damage figure. `GameNetwork.SkillStrikeRequest` /
  `SendSkillStrikeToServer` / `OnSkillStrikeRequest`.
- The server looks the skill up in the same catalog (it loads from `Resources/Skills` on both sides), takes that
  exact authored effect, and rolls it through the SAME function the player's machine would have used —
  `SkillEffectApplier.Strike`, split out of the old private `LandStrike` so there is one implementation, not two.
- The client's `LandStrike` sends and returns 0, exactly as a basic swing does.
- The server refuses anything that is not an authored Damage effect, bounds-checks both indices, applies the same
  distance ceiling and flood guard as a swing, and announces the result as an ordinary `HitResult`.
- **The pile** (`StackVars`) is the one thing a client still states that can move a number, and it is accepted
  ONLY when the named effect's damage multiplier actually reads a pile — **3 of 42 skills**
  (`deathblow`, `retribution`, `flow`). For the other 39, nothing the client says affects the number.
- `MultiHitBurst`'s running yellow total is suppressed on a machine that no longer decides damage, because it was
  built by adding up local return values and would have drawn a confident `0`.

## Reviewed and fixed since (2026-08-30, still unplayed)

A read-through of the above found four things wrong with it. All four are fixed, compile-verified, and confirmed
present in `Library/ScriptAssemblies/Assembly-CSharp.dll`; the editor console holds nothing of ours. Still nobody
has cast a skill.

- **The announced number had already had armour taken out of it.** The handler broadcast `dealt` (post-mitigation)
  while every other path broadcasts the pre-mitigation power, and a watching machine mitigates whatever it is
  given — so every skill number on screen read low, and lower than a swing of the same power. `Strike` and
  `BasicSwing.Land` now hand back the `DamageInfo` they actually applied, and the handler announces that verbatim.
  **NOTE: `/dummy` cannot catch this** — `TrainingDummy` sets `defense = 0`, so both readings agree on a dummy.
  It needs a real mob.
- **A strike that resolves as a basic attack was described from the wrong thing.** `resolveAsBasicAttack` (one
  skill: `onslaught`) left the `crit` out-param unset and was announced with the skill's element and `missed:
  false` — so it could never crit, wore the wrong element, and a whiff drew a "0" instead of a MISS. Same fix: the
  swing's own resolved blow is what travels.
- **Skill strikes shared the 20/second blow ceiling.** A flurry sends one request per hit PER CREATURE, so 5 hits
  across 5 mobs is 25 — silently over. They now have their own ledger, `MaxSkillStrikesPerSecond = 200` (my
  number, deliberately far above any real cast).
- **REGRESSION, found and fixed 2026-08-30 by the user reporting "skills do not damage mobs".** The ownership
  check below first asked ONLY the equipped weapon's `skillIds`. A real character (Alpha) carries three bound
  hotbar skills and an EMPTY `equipSlots` in its saved progress — measured in the database — so every skill it
  cast was refused and nothing took damage. `Owns` now passes when the skill is **bound on the hotbar (a
  numbered slot or the ult slot) OR on the equipped weapon**; the weapon half still covers passives, which take
  no slot. **The lesson is bigger than the line: a server-side gate must only enforce a rule the GAME already
  enforces.** Inventing a stricter one does not harden the server, it breaks play. Server rebuilt and the fix
  confirmed inside `builds/server/.../Assembly-CSharp.dll`.
- **The server rolled any skill for anybody who named one** — no ownership check at all. `Owns` now requires the
  named skill to be in the caster's equipped weapon's `skillIds`, which the server's character already holds
  (`RestoreEquipment` decodes them). Refusal warns once per connection, because the usual cause is a character
  whose progress never loaded rather than cheating.

## What's left

1. **Play it.** Nothing below matters until a skill has been swung in a live client-server session.
2. ~~The yellow totals~~ **DONE (2026-08-30, unplayed)** — and the fix written here before it was wrong, which is
   worth keeping: `RepeatsDamageTotal` did NOT "already work this way" on a client. It reads
   `RepeatsDamageTotal.Current`, which is pushed and popped around the synchronous apply — and on a client that
   apply only SENDS, so every number arrived when `Current` was null. BOTH totals were gone, not just the flurry's.
   Built as option (a), the user's call: `HitResult.showsTotal` carries the SERVER's reading of the authored skill
   (`showRepeatsTotalDamage`, or `EffectiveHits > 1`), never a client's claim, and the client folds the arrived
   number in. `RepeatsDamageTotal` entries are now keyed by **(creature, attackerNetId)** — without the attacker
   half, two players flurrying one creature pooled into a single climbing number belonging to neither.
   Not verified: two overlapping totals hang at the same overhead anchor, so they may draw on top of each other.
3. **Weapon-modifier strikes are still trusted** — a weapon's own flurry/cleave split off a basic swing
   (`Player3DAttacker`, the two `attackerHit: -1` calls). No authored skill stands behind them, so they take the
   older `ResolvedStrikeRequest` path that carries a number. Closing this means resolving weapon modifiers on the
   server; smaller than the job just done.
4. **A heal is still the client's word** (`HealClaim`) — its size is trusted.
5. **A player's own buffs protect nothing.** The server's fighter carries no barriers, parry charges or sub-stat
   buffs, because everything that grants one still runs on the player's machine. Bigger than the rest.
6. **The pile reading is unbounded** — for those 3 skills a client could claim a large pile. Cap it against the
   effect's authored `stackLimit`.

## Dead ends / things not to retry

- **Do not** have the server re-roll a skill from the def alone without being told which strike: a skill's beats
  each carry their own effects, and "the third effect" is meaningless without naming the beat.
- **Do not** try to reproduce the strike TIMING on the server. The client keeps the timeline (it owns the
  visuals); only the NUMBER moved. That split is deliberate and works — an earlier note in this file claimed the
  timeline had to move too, which was wrong.

## Next single step

Play one flurry skill against a REAL MOB (not `/dummy` — its armour is 0 and hides the mitigation bug) in a live
client-server session, and confirm the per-hit numbers appear and match what the same build shows for a basic
swing of comparable power.

## How to resume

```text
Read handoffs/skills-to-the-server.md and continue. Everything from the 2026-08-29 networking session is
uncommitted and has never been played — treat every claim about its behaviour as unverified.
```
