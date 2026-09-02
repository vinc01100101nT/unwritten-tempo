# Skills to the server

**Task** — make the SERVER resolve a player's skill strikes instead of taking the client's word for their damage.

## Status

**DONE, UNPLAYED (2026-09-02).** The server rolls a skill's damage itself and now also SAYS SO, which it did not
before. Written and compile-verified, and the server binary has been rebuilt and restarted carrying it — but
nobody has swung a skill since. Don't trust any "it works" claim about it.

## The per-strike path is NOT how a skill reaches the server any more — read this first

Everything below under "What's done" describes a message that names ONE strike. That is not the mechanism a
joined player uses. A press sends the WHOLE CAST across (`SkillCastRequest` → `OnSkillCastRequest` →
`SkillRunner.CastFromRequest`), the player's own machine resolves none of it (`SkillRunner.cs:586`, `if
(Watching) return;`), and the server runs the entire skill against the character it holds. So
`SendSkillStrikeToServer` never runs on a client and `OnSkillStrikeRequest` never fires — the whole per-strike
mechanism is unreachable in a live session. It is left in place because it still answers a strike named by
anything that is not a whole cast; nothing routes to it today.

**That is what caused "skills damage the mob but draw no number" (fixed 2026-09-02).** The three places that
announce a blow on a creature are all handlers for messages a client sends, so a blow the server landed on its
own initiative was announced by nobody, while the creature's health still reached every screen as a plain
SyncVar. Fixed at the two places a skill's damage actually lands on the server — `SkillEffectApplier.LandStrike`
and `ApplyExecute` — both calling the new `GameNetwork.AnnounceBlowOnMob`, the mirror of the
`AnnounceBlowOnPlayer` line that has always sat at the damage door. `NetMob.ShellForServerMob` is the
server-side twin of `ShellForView` that lets it name the creature.

## What the per-strike message does, for the day something routes to it again

Read with the section above: nothing sends one of these today. Kept because the rules it enforces are the rules
any future per-strike route will need, and because the pieces it introduced are what the live cast path runs on.

- A skill strike is **named, not measured**. The request carries the skill id, which beat of that skill, which
  effect of that beat, and the target — never a damage figure. `GameNetwork.SkillStrikeRequest` /
  `SendSkillStrikeToServer` / `OnSkillStrikeRequest`.
- The server looks the skill up in the same catalog (it loads from `Resources/Skills` on both sides), takes that
  exact authored effect, and rolls it through the SAME function the player's machine would have used —
  `SkillEffectApplier.Strike`, split out of the old private `LandStrike` so there is one implementation, not two.
  **This half is very much alive**: the server's own cast path lands every strike through `Strike` as well.
- The handler refuses anything that is not an authored Damage effect, bounds-checks both indices, applies the same
  distance ceiling and flood guard as a swing, and announces the result as an ordinary `HitResult`.
- **The pile** (`StackVars`) was the one thing a client stated that could move a number, and it was accepted ONLY
  when the named effect's damage multiplier actually reads a pile — **3 of 42 skills** (`deathblow`,
  `retribution`, `flow`). The live cast path sends no pile at all, so today no client states one.
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

## The four mitigations were silent too, and are fixed (2026-09-02, unplayed)

Same shape as the missing damage numbers, found the same afternoon by the user reporting "the brace's banked
damage is not showing". A barrier soaking part of a hit, a brace holding one, a parry erasing one and an off-hand
block all pop their own word or number **at the moment they decide**, and the machine that decides is now the
server — which suppresses every popup it makes. The person was then told only the figure that SURVIVED, and for
a fully mitigated hit that is 0, which draws nothing. So a brace ate a blow and the wearer saw nothing at all.

- Deciding and drawing are now separate: `UnitMitigation.Outcome` records what was done,
  `UnitMitigation.Show` draws it. The three primitives and the off-hand block branch no longer pop anything.
- It crosses the wire on `PlayerHitResult.mitigated`, filled from `Character.TakeMitigation` (read-and-clear,
  the `UnitStates.TakeExpiredDebt` idiom) and drawn by `NetPlayer.ShowBlow` for the owner only.
- **A second bug fixed in the same pass:** `Character.ApplyHitToHP` was re-running both mitigations on the
  WATCHED copy against its mirrored states, so a barrier came off twice and mirrored charges were spent on a
  decision that machine had no business making. It is now skipped when `IsWatchedCopy`.
- **Verified, because it is the one line that could switch a brace off entirely:** the server's per-connection
  fighter is NOT marked as a watched copy — `NetPlayer.cs:153` returns on `isServer` before the branch that
  marks it, and `PlayerFighter` never marks one.
- **A creature's mitigation is still silent** — nothing carries it to a watching machine. Closing it means the
  creature's announcement carrying the same thing the player's now does.

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
4. **A heal a player's OWN machine decides is still their word** (`HealClaim`, sent from `Character.cs:1218`) —
   its size is trusted. A skill's Heal effect is no longer one of these: the whole cast runs on the server, so
   that heal is applied there like everything else the skill does.
5. ~~A player's own buffs protect nothing~~ **NO LONGER TRUE, and unplayed.** The server runs the WHOLE cast
   against the character it holds, so the barriers, parry charges and sub-stat buffs a skill grants are now on
   the server's own fighter — see the comment at `SkillRunner.cs:578`, which says exactly why this was the
   point of moving the cast. Nobody has tested that a brace actually holds a blow in a live session.
6. ~~The pile reading is unbounded~~ **NO LONGER REACHABLE.** `SkillCastRequest` carries only the skill id, what
   was picked and where it was aimed — no stack count — so the pile is read entirely off the character the
   server holds. The unbounded reading survives only on `SkillStrikeRequest`, which nothing routes to.

## Dead ends / things not to retry

- **Do not** have the server re-roll a skill from the def alone without being told which strike: a skill's beats
  each carry their own effects, and "the third effect" is meaningless without naming the beat.
- **Do not** put the deciding back on a player's machine. The server runs the whole cast — the timeline, every
  effect and every number — and the player's machine draws the picture of it. That is the split, and everything
  it fixed (a brace that holds nothing, a buff that protects nobody) comes straight back if the halves are
  separated again.

## Next single step

Play one flurry skill against a REAL MOB (not `/dummy` — its armour is 0 and hides the mitigation bug) in a live
client-server session, and confirm every beat draws its own number, the rising yellow total appears, and a basic
swing at the same mob still draws exactly ONE number per hit rather than two. Then cast Brace and let a mob hit
you: the blue held number must appear and your health must not move.

Both the server and the client MUST be rebuilt together for this — `PlayerHitResult` changed shape on
2026-09-02, so a build from before that will disagree on the wire. Both were rebuilt and the server restarted
carrying it at 18:33.

## How to resume

```text
Read handoffs/skills-to-the-server.md and continue. Everything from the 2026-08-29 networking session is
uncommitted and has never been played — treat every claim about its behaviour as unverified.
```
