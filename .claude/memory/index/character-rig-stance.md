# Character / rig / stance / animation

- [One-shot stances dead — Mixamo clip.name="mixamo.com"](../oneshot-stance-mixamo-clipname.md) — shared name → stateHash miss; `HumanoidClipPostprocessor` names clip after FILE.
- [Caster grounding "stretched foot" — UNSOLVED](../caster-grounding-stretched-foot-notodos.md) — lift/drop-root/SeatStandingFrame FAILED; never re-bake in Play.
- [Mixamo batch auto-rig via API](../mixamo-batch-autorig-api.md) — `tools/mixamo_batch_rig.py`; id=uuid, markers in `rigging_inputs`, S3 no-auth.
- [Mixamo caster clips: rebind PATHS onto model](../mixamo-clip-rebind-to-model-paths.md) — extra `CharacterArmature` wrapper; `RebindMixamoToModel`.
- [Stance authoring-surface split](../stance-authoring-surface-split.md) — Composer owns StanceId→action+timing; clip FILE is CODE (`Character3DClipMap`).
- [Mixamo→Quaternius 1:1 via Generic Copy-From-Other](../mixamo-on-quaternius-generic-1to1.md) — import Generic + `CopyFromOther`; inspect bones FIRST.
- [Live-3D player visual via IStancePose](../character-3d-visual-system.md) — `StanceController`→`IStancePose`; brain=`PlayerStanceBrain`.
- [Player visible model is a SEPARATE object](../player-visual-is-separate-object.md) — root holds only the hidden sprite; the `Character3DVisual` object holds `Body`/weapon renderers. Per-visible-player flags go there (built in `Player3DVisualBuilder.Attach`).
- [Forcing Humanoid on Quaternius drops clips](../quaternius-humanoid-drops-clips.md) — keep Generic; recover via `git checkout .fbx.meta`.
- [Stance system (held poses + single driver)](../stance-system.md) — Composer→`StanceLibrary.asset`; `StanceController` single `baked.Play` choke.
- [Gait speed-sync](../gait-speed-sync.md) — fps+steps scale w/ speed; `GaitSpeedMult` + `StatusController.Haste`.
- [Footstep walk/run gait selection](../footstep-walk-run-threshold-bug.md) — walk SFX/prints desynced; gait must key off active CLIP (`Character3DPose.Gait`/`LocoGait`), never a move-speed/blend threshold.
- [Character art = Quaternius charcoal pack](../character-art-quaternius-charcoal.md) — players+NPCs+mobs = Quaternius Ultimate Animated.
- [Animator CrossFade + stale normalizedTime](../animator-crossfade-stale-normalizedtime.md) — reading state info same-frame as CrossFade returns OUTGOING (looping Run) time; froze jump on a run frame. Guard `IsInTransition(0)` + `shortNameHash`.
- [Quaternius NPC reuse plan (+ combat-NPC later)](../quaternius-npc-reuse-plan.md) — 3D body 100% shared (`StanceController`+`Character3DPose`+`selfDrive`); brain per-role. Town NPC = tiny walk/idle driver `TownNPC3DVisual`; combat NPCs reuse Attack/Hurt/Die, brain like `MonsterAI3D`, need own stat source for speed mults.
- [Generic-import models T-pose on shared Humanoid controller](../humanoid-import-type-tpose.md) — player needs FBX `animationType:3` Humanoid + valid biped avatar; fix = Bulk Set Humanoid; `PlayerModelSwap` filters non-Humanoid.
