---
name: whole-skill-runs-on-the-server
description: "A skill's WHOLE body now runs on the server via the same SkillRunner; the client only draws it. Effect lengths are baked into the effect table so beats fire without particles."
metadata: 
  node_type: memory
  type: project
  originSessionId: f3b0d73e-e6b2-44be-b82c-adab050b405d
  modified: 2026-09-01T09:54:53.558Z
---

Decided and built 2026-09-01, at the user's instruction ("all in the server, no client side").

**The bug it fixed:** only the Damage effect ever reached the server (`SkillStrikeRequest` named one strike at a
time). Every other effect — 45 of the 57 across the 42 composed skills — was applied on the caster's own machine,
against a `Character` the server never sees. So Brace armed nothing the server would honour, buffs protected
nobody, charges were spent on a copy, and the server took the full hit off the real HP.

**The shape now:**
- The client sends ONE `SkillCastRequest` (skill id, aimed netId, aim point in MAP coordinates) and then only
  DRAWS the cast. `SkillRunner.OnTick` returns immediately when `Watching`
  (`GameNetwork.TheWorldIsElsewhere || Caster.IsWatchedCopy`).
- The server runs the SAME `SkillRunner` component — `PlayerFighter` puts one per authored skill on the fighter
  host and calls `BindCaster`. `SkillRunner.CastFromRequest` is the server's entry; `Activate` stays the press.
- `SkillRunner` no longer reads `Character.Instance` / `StatusController.Player`. It reads `Caster`, `Status`,
  `Mine` (may I draw?) and `Watching` (may I decide?).

**Timing without particles.** A beat authored "at the end" used to end when the pooled particle instance said so
(`EffectHandle.ReturnAt`), which a server cannot know. `EffectTiming` now owns the one formula; `EffectTimingBaker`
writes `measuredPlaySeconds` / `measuredFadeSeconds` / `measuredLoops` into every `EffectDef` automatically (editor
load + any prefab/asset import). Verified: all 106 baked values equal a live instantiated measurement, and every
authored sequence step's window is identical old-vs-new. `Vfx.Blind`, `Sfx.Deaf`, `BattleNotice` and
`RepeatsDamageTotal.Begin` all no-op on a server.

**Passives became per-unit.** `PassiveEvents.Fire` now takes the ACTOR first and walks `ICombatUnit.SkillRunners`
(bound by `Character.BindSkillRunners`) instead of one 64-slot process table gated on `Character.Instance` — that
gate was skipped entirely on a server, and two players would have exhausted the table.

**What the server sends back:** mana beside health on `NetPlayer`; a full state snapshot to the owner only
(`UnitStates.SnapshotForOwner` / `MirrorFromServer`, times as SECONDS LEFT, not moments); and
`NetPlayer.Motion` for a dash/blink/shove, because the legs still belong to the owner's machine.

**Scatter is seeded, 2026-09-01.** Where a scattered shot lands IS where its damage is measured from (the step's
tick fires there), so it cannot be rolled per machine. `CombatRng.Stream` is a seeded xorshift; the server picks a
seed per PLAY (opening beat and each repeat), stamps `VfxContext.seed`, and announces `SkillCastPlayed` to every
observer INCLUDING the owner. Consequence, deliberate: the caster's own spell effect now plays on the server's
word, not at the press — the pose, cast bar and skill call still fire instantly. `SkillDef.StayForTick` /
`FanForTick` own the per-step answers so a watcher cannot draw one bolt where five were fired.

**All 11 triggers audited, 2026-09-01.** Three were not reaching the server, and TWO of those were dead everywhere
in a shared world: `OnBasicAttackStart` and `OnBasicAttackDealt`. Cause — `GameNetwork.OnHitRequest` rolled the
swing INLINE instead of calling `BasicSwing.Land`, a second copy of "what a swing is". That copy also skipped
`Combat.Missed`, so **no networked player swing could ever miss**, and skipped `UnitOffense.SpendCharges` and the
impact cue. Fixed by deleting the duplicate and calling the shared one. `OnItemUsed` now goes through a new
`ItemUsedRequest` naming the ITEM (never the skill — a message naming a skill could run any skill in the game).

**Still open** (see [[stack-piles-two-states-and-readings]] for the Brace design itself): `SkillStrikeRequest` and
`SendSkillStrikeToServer` are unreachable for skills and should be deleted once play-tested; whether the player
actually HAD the item is still their own machine's word, until inventory is server-side.
