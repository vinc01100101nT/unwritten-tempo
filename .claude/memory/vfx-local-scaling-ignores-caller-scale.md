# VFX caller scale silently ignored (particle scalingMode = Local)

**Date:** 2026-07-01

## Symptom
The F9 Sequence Tuner scale slider (and in-game crit/AoE size multipliers) had NO visible effect on
the meteor fire — cranking scale to 5× just made a giant washed-out blob that wouldn't shrink back.
Looked like "the tuner is broken." It wasn't.

## Root cause
`Vfx` (and the tuner preview) apply caller scale by setting `transform.localScale` on the **ROOT**
of the effect instance. But many vendor prefabs — e.g. Hovl `Magic fire pro orange.prefab`
(`fire.magic-pro-orange`) — author their child ParticleSystems with **`scalingMode = Local` (enum
value 1)**. Local scaling sizes each system from its OWN transform, **ignoring the parent/root scale**.
Since the systems are children of the root, root `localScale` never reached them → scale did nothing.
(Enum: Hierarchy=0, Local=1, Shape=2. Hierarchy uses full `lossyScale` = parent chain.)

This affected EVERY Local-mode effect, not just the meteor — any crit-punch / AoE sizing was a silent
no-op on those prefabs.

## Fix (general, not per-effect)
Force **Hierarchy** scaling on every particle system at instance-build time so root scale flows down:
- `Vfx.cs` → `EffectHandle.Prepare()` loop: `m.scalingMode = ParticleSystemScalingMode.Hierarchy;`
  (runs ONCE per pooled instance of every effect id → covers all gameplay VFX: PlayAt/Attach,
  Projectile3D trails, etc. No hot-path cost.)
- `SequenceTuner.cs` → `RebuildPreview()` system loop: same line (mirrors it for the F9 preview).

Not reachable: particle systems never spawned through `Vfx` (a prefab dropped straight into a scene) —
but those aren't caller-scaled, so nothing to fix.

## Gotchas / lessons
- When "scale does nothing" on a particle effect, FIRST check the prefab's `scalingMode` (grep the
  `.prefab` for `scalingMode:` — `1` = Local = ignores parent scale). Don't assume the scaling code is wrong.
- Setting scale on the root only works if systems are Hierarchy mode (or the system is ON the root).
- Forcing Hierarchy is safe: at scale 1 it's identical to Local; it only differs (correctly) once scaled.
- Related but separate bug in the same session: the F9 tuner had TWO preview engines (real-time
  VfxDirector replay + frozen scrub) → collapsed to one clock (Play advances scrubT, Pause holds),
  `RenderPreviewAt` is the single render path.
