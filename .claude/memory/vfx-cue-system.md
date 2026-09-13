# VfxCue — event→VFX choke point for status & verbs (built 2026-07-02)

The status/verb analog of the skill pipeline. Skills = **VFX→gameplay** (sequence ticks → damage). Cues = the
REVERSE: **gameplay→VFX** (the event already happened in code; just play a look at the actor). Same authoring
unit — a composed `VfxSequence` from the Vfx Composer — no tick/effect bindings (gameplay's already wired).

## Pipeline (same 3-stage shape as skills)
Vfx Composer (author look) → **Vfx Cue Composer** (`Tools ▸ unwritten ▸ Art ▸ Vfx Cue Composer`, `VfxCueComposerWindow.cs`) → in-game (events auto-fire cues).

## Files
- `VfxCueLibrary.cs` — `enum VfxCueId` (APPEND-ONLY, int stored in asset), `enum CueAnchor {Self,Target,Midpoint}`, `VfxCueBinding {id, vfx, anchor, offset, scale}`, `VfxCueLibrary` SO (`Resources/VfxCueLibrary.asset`).
- `VfxCueCatalog.cs` — load-once loader, indexed by `(int)id` into a flat `VfxCueBinding[]` (zero-alloc array index, mirrors `EffectCatalog`). `Get(id)`/`Reload()`.
- `VfxCue.cs` — the ONE front door. `Fire(id, actor, target=null)` = one-shot puff; `Begin(id, actor)` = persistent aura, sets `ctx.follow=actor`, RETURNS `VfxSequenceRunner` the caller must `.Stop()`. Both call `VfxDirector.Play(seq, ctx, null)` — null onTick (no gameplay, no capturing closure). Unbound cue = silent no-op.
- `Editor/VfxCueComposerWindow.cs` — flat table, one row per enum id (auto-reconciled), Save+Reload button.

## ⛔ WHO IS ALLOWED TO SAY A CUE HAPPENED (2026-09-07) — read this BEFORE adding a Fire site
**A joined client fires NOTHING and draws only what the server tells it.** `Fire`/`Begin` return immediately when
`GameNetwork.TheWorldIsElsewhere` — so a new `VfxCue.Fire(...)` added on a client-only path is a **silent no-op**,
which is exactly the trap that costs a round. The server announces every cue it fires to everyone watching that
unit (`GameNetwork.AnnounceCue` → `CuePlayed` → `VfxCue.PlayFromServer`), and draws nothing itself. A single
machine with no world attached is unchanged: it both fires and draws.

Consequence for a NEW cue on something only the client knows about (the sprint puff was the one such case): the
client cannot say it — tell the server the fact, and let the server fire the cue. Sprint does that via
`NetPlayer.CmdSprinting`.

A `Begin` aura is NAMED (an int key) so the server's later "it ended" can say which one; the client's handle is
fenced by `VfxSequenceRunner.Generation`, so a runner that finished and was re-pooled cannot be stopped by a stale
handle. `VfxCue.ForgetAurasOn(netId)` strips a body's auras when it leaves view.

## Fire sites (one-liners; centralized per the choke-point rule)
- `StatusController.cs` — DoT: `Apply` (new) → `StartAura` holds `aura[i]=VfxCue.Begin(*Start)`; `TickDot` → `VfxCue.Fire(*Tick)`; expiry sweep in `Update` + `OnDestroy` Stop the aura. CC/Haste apply → one-shot `VfxCue.Fire`. `aura[]` array parallels the ailment arrays. Covers MOBS too (StatusController is on both).
- `CombatAuthority.cs` — `NoteHPAdded` (the ONE door every HP gain passes through) → OnHPAdded. NOT `Character.Heal`: that only calls the door.
- `Character.cs` — `AddXP` levelup→OnLevelUp; `RaiseDeath()` + `SetHealthFromServer` death→OnDeath.
- `Health.cs` — basic hit→OnHitByNormalAttack/OnHitByCriticalAttack, skill hit→OnHitBySkill, `Die`/`PlayDeathVisuals`→OnDeath.
- `Character.cs` `LandOnMe` — the SAME three hit cues on the player. Every PHYSICAL intake on both sides (player, creature, creature overkill) picks through `VfxCue.HitCue`; the creature's MAGIC intakes play `OnHitBySkill` directly, which is what `HitCue` answers for magic anyway.
- `BasicSwing.cs` — fires NO hit cue since 2026-09-13: the victim's intake owns it (it used to fire too, so every swing showed twice).
- `SkillEffectApplier.cs` sub-stat buff/debuff — OnSlow / OnHaste / OnBlind when THAT effect starts (`UnitStates.ApplySubStat` returns true only when the granting skill had no pile running on the stat), judged per effect, never by the unit's total (`VfxCue.SubStatStartCue`). A first version used the total and missed a slow landing on a hasted unit.
- The hit sounds you HEAR ride the cue sequences: `CombatSfx`'s hit/crit/skillHit sets are empty in `Systems.unity` (measured 2026-09-13).
- `MonsterAI3D.cs` — mob levelup→OnLevelUp. (A creature's ordinary attack is always PHYSICAL since 2026-09-13 — the "magic mob" attack type and its OnCastStart were removed.)
- `SkillRunner.cs` — `BeginCastVisuals`→`Begin(OnCastStart)` aura for the wind-up.
- `Player3DController.cs` — sprint engaged→OnSprint. (`Player3DAttacker`'s OnAttack line is commented out — there is no swing cue.)

## Lifecycle decision (user 2026-07-02)
DoTs get **persistent aura (`*Start`) + per-tick puff (`*Tick`)**. `*Start` MUST be authored as an Attach/looping sequence (returnAt=0, lives until Stop) — else pooled-runner reuse could let a stale handle Stop a different play. Everything else = one-shot.

## Gotcha (cost real time)
New `.cs` files added via file-write don't compile until Unity **imports** them. `refresh_unity compile=request` alone returns `refresh_triggered:false` → new files never enter Assembly-CSharp → pre-existing files error `CS0246 VfxCueId not found` while the NEW files (also using it) show no errors (they aren't compiled at all). FIX: `refresh_unity scope=all mode=force` to trigger the asset import+domain reload. When only SOME files in one assembly can't see a brand-new type, suspect un-imported files, not the code.
