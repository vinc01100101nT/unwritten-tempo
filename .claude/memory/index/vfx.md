# VFX (particle/effect layer, cue choke, composer/tuner, gotchas)

- [VfxCue — event→VFX choke point](../vfx-cue-system.md) — `VfxCue.Fire`/`Begin`→`VfxCueCatalog`→`VfxCueLibrary.asset`. **A joined client fires NOTHING** — the server announces every cue and the client only draws what it is told, so a Fire added on a client-only path is a silent no-op.
- [Ground-cast Origin anchor collapse](../vfx-ground-cast-origin-collapse.md) — `VfxContext.At` set origin==target, so a ground skill's Origin anchor played at the impact; fixed to origin=caster/target=aim in `SkillRunner.Cast`.
- [One-shot VFX re-looped at runtime](../vfx-looping-oneshot-double-play.md) — fix `m.loop = oneShot ? false : baseLoop[i]`.
- [Vfx particle-effect layer](../vfx-effect-library.md) — `Vfx.PlayAt`/`Attach`→`EffectCatalog`→`EffectLibrary.asset` (pooled).
- [Vfx Composer window](../vfx-composer-window.md) — `VfxComposerWindow.cs` (Art ▸ Vfx Composer); reuses `VfxSequence`/`VfxStep`.
- [VFX dev-tuner must match REAL spawn](../vfx-tuner-must-match-real-spawn.md) — `def.scale/offset/lifetime` single source.
- [VFX caller scale ignored (scalingMode=Local)](../vfx-local-scaling-ignores-caller-scale.md) — fix = force `Hierarchy` in `Vfx.Prepare`.
- [NinjaAdventure VFX purged (mob SPRITES kept)](../ninja-adventure-vfx-purged.md) — strip at guarded CONSUMER not just assigner.
- [Phantom load splashes = pool clones baked into scene](../vfx-pool-clones-baked-into-scene.md) — 55 `Water_Splash_A(Clone)`+`VfxSystem` saved into `Systems.unity`; delete root, no code bug.
- [Rebuild Effect Library scanned dead folders](../effect-library-builder-stale-roots.md) — all 5 scan roots removed by the assets reorg → tool silently added nothing; now ONE root `1_Authored/Prefabs/VFX` (prefix = category subfolder).
- [Carrier Start tick fires at launch → damage off-center](../carrier-start-tick-fires-at-launch.md) — from-above meteor with `tickWhen=Start` fired damage at launch (+carrierFrom offset), not landing; use End. `tickWhen` defaults to Start.
- [Target effects named by the shell](../target-effects-named-by-shell.md) — a cast message must name a creature through its NetMob shell; the server object has no network id (target effects played on the caster).
