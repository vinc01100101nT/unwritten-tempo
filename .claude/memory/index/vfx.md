# VFX (particle/effect layer, cue choke, composer/tuner, gotchas)

- [VfxCue — event→VFX choke point](../vfx-cue-system.md) — `VfxCue.Fire`/`Begin`→`VfxCueCatalog`→`VfxCueLibrary.asset`.
- [One-shot VFX re-looped at runtime](../vfx-looping-oneshot-double-play.md) — fix `m.loop = oneShot ? false : baseLoop[i]`.
- [Vfx particle-effect layer](../vfx-effect-library.md) — `Vfx.PlayAt`/`Attach`→`EffectCatalog`→`EffectLibrary.asset` (pooled).
- [Vfx Composer window](../vfx-composer-window.md) — `VfxComposerWindow.cs` (Art ▸ Vfx Composer); reuses `VfxSequence`/`VfxStep`.
- [VFX dev-tuner must match REAL spawn](../vfx-tuner-must-match-real-spawn.md) — `def.scale/offset/lifetime` single source.
- [VFX caller scale ignored (scalingMode=Local)](../vfx-local-scaling-ignores-caller-scale.md) — fix = force `Hierarchy` in `Vfx.Prepare`.
- [NinjaAdventure VFX purged (mob SPRITES kept)](../ninja-adventure-vfx-purged.md) — strip at guarded CONSUMER not just assigner.
- [Phantom load splashes = pool clones baked into scene](../vfx-pool-clones-baked-into-scene.md) — 55 `Water_Splash_A(Clone)`+`VfxSystem` saved into `Systems.unity`; delete root, no code bug.
