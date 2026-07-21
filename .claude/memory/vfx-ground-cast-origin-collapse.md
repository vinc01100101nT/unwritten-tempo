---
name: vfx-ground-cast-origin-collapse
description: "Ground-cast VFX had origin==target (VfxContext.At), so the Origin anchor wrongly played at the impact point; fixed to origin=caster, target=aim"
metadata:
  node_type: memory
  type: project
---

**Bug (2026-07-21, MEASURED):** A ground-aimed skill's VFX `Origin` anchor played at the DESTINATION, not the caster. E.g. Blink authored 2 steps — #0 @0s (Origin, a departure flash), #1 delayed (Origin, carries the teleport tick) — but #0 played at the teleport target instead of the caster's current spot.

**Root cause:** `SkillRunner.Cast(center)` (and `ComboCastGround`) built the context with `VfxContext.At(center)`, and `VfxContext.At` sets **BOTH `origin` and `target` to the same point** (`VfxSequence.cs:214`). So for a ground cast origin==target==reticle point → the `VfxAnchor.Origin` (caster) and `VfxAnchor.Target` (impact) anchors were identical, both at the aim point. The documented enum meaning (Origin=`VfxContext.origin` caster, Target=`VfxContext.target` impact) only held for Target/Self aim (`CastTargeted` correctly sets `origin=transform.position, target=unit.position`).

**Fix:** both ground-cast sites in `SkillRunner` now build `new VfxContext { origin = transform.position, target = center, ... }` — origin = caster, target = aimed point. Self cast: `center == transform.position` so they still coincide (unchanged). `origin` is a Vector3 SNAPSHOT captured at cast, so an Origin-anchored step stays at the caster's pre-teleport spot for the whole sequence even after the delayed teleport moves the player.

**Regression check (done):** among Ground-aimed skills only `skill.blink_step` used the Origin anchor; meteor / hex_field / heal_circle use Target exclusively (unchanged — target still = reticle). Verified via execute_code enumerating SkillDef.aim==Ground + their sequence step anchors.

**⚠️ Follow-up regression + real fix (same session):** making origin≠target broke the teleport itself — Blink's teleport tick was anchored **Origin**, and the teleport DESTINATION was read from the tick's anchor point (`OnTick` → `ApplyBurst(tick.point …)` → `Teleport(point)`). With origin now = caster, it teleported the player to itself (no move). Coupling a gameplay relocation to a VFX visual anchor is the fragility. **Fix:** `ApplyBurst` gained an `aimPoint` param (defaulted to point); `OnTick` passes `lastCtx.target`; the PLAYER path (`ApplyEffectsToPlayer`) uses `selfPoint = aimPoint ?? point`. So **Teleport/Dash always relocate to the AIM point regardless of the tick's VFX anchor**. `point` still drives the mob AoE gather.

**Authoring note:** a blink's visual is free — step #0 Origin = poof at old spot, teleport-tick step can be Origin OR Target for its LOOK; the teleport still lands on the aimed point either way. See [[vfx-cue-system]], [[dash-skill-and-aftercast-root]], skill VFX context in `SkillRunner.Cast` + `OnTick`.
