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

## Fire sites (one-liners; centralized per the choke-point rule)
- `StatusController.cs` — DoT: `Apply` (new) → `StartAura` holds `aura[i]=VfxCue.Begin(*Start)`; `TickDot` → `VfxCue.Fire(*Tick)`; expiry sweep in `Update` + `OnDestroy` Stop the aura. CC/Haste apply → one-shot `VfxCue.Fire`. `aura[]` array parallels the ailment arrays. Covers MOBS too (StatusController is on both).
- `Character.cs` — `Heal`→OnHeal, `AddXP` levelup→OnLevelUp, and new `RaiseDeath()` helper (replaces the 3 inline `OnDied?.Invoke()` sites)→OnDeath.
- `Player3DAttacker.cs` — swing→OnAttack, landed basic hit→OnHit/OnCrit (at target).

## Lifecycle decision (user 2026-07-02)
DoTs get **persistent aura (`*Start`) + per-tick puff (`*Tick`)**. `*Start` MUST be authored as an Attach/looping sequence (returnAt=0, lives until Stop) — else pooled-runner reuse could let a stale handle Stop a different play. Everything else = one-shot.

## Gotcha (cost real time)
New `.cs` files added via file-write don't compile until Unity **imports** them. `refresh_unity compile=request` alone returns `refresh_triggered:false` → new files never enter Assembly-CSharp → pre-existing files error `CS0246 VfxCueId not found` while the NEW files (also using it) show no errors (they aren't compiled at all). FIX: `refresh_unity scope=all mode=force` to trigger the asset import+domain reload. When only SOME files in one assembly can't see a brand-new type, suspect un-imported files, not the code.
