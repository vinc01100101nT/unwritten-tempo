# Cast-circle sizing — attempts log (UNSOLVED)

Goal (from user):

1. Remove the per-skill "circle size ×" knob; the cast circle should auto-size to the skill's AoE radius.
2. The pre-target (reticle) circle and the cast-time (wind-up) circle must match — and both must match the actual released AoE.

## What the code does (facts, verified)

- Cast circle is spawned by `Vfx.Attach(castCircleId, scaleMult)`; `EffectHandle.Begin` sets `transform.localScale = def.scale × scaleMult`. World radius = `prefabFootprintAtScale1 × def.scale × scaleMult`.
- BOTH the reticle (`GroundTargeter`) and the wind-up circle (`SkillRunner.BeginCastVisuals`) already pass the SAME value (`CastCircleScale()`), so they were NEVER different from each other. The real mismatch is telegraph-vs-actual-AoE.
- `circle.*` prefabs (Eric VFX Magic Circle pack) are authored HUGE: footprint at localScale 1 ≈ 13.2u (icearrow), 5.7u (freeze/healing), 2.1u (magic), 2.8u (magic-2). Root particle authored `scalingMode=Local`; `Vfx.Prepare` force-sets Hierarchy at runtime so localScale flows down.

## Attempt 1 — remove the mult knob, `CastCircleScale() => LargestAreaRadius`

- Removed `SkillDef.castCircleScaleMult`, the Skill Composer "circle size ×" field, and the `× mult` in `SkillRunner.CastCircleScale()`.
- RESULT: circle still huge, because with `def.scale=1` the world radius = 13.2 × 1.48 ≈ 19u. The mult was the only lever that had compensated for the oversized prefab. FAILED.

## Attempt 2 — hand-set `circle.icearrow01` `def.scale = 0.079` in EffectLibrary

- Reasoned world radius = `def.scale × 13.2 × R`; set scale ≈ 1/13.2 so it maps \~1:1.
- Verified by measurement: at the new localScale the mandala measured \~1.54u. Math correct.
- RESULT: user still saw huge (likely stale Play session — `EffectCatalog` caches per domain-load). ALSO only fixed one prefab. FAILED / incomplete.

## Attempt 3 — `CircleEffectNormalizer` tool: bake `def.scale = 1/footprint` for ALL `circle.*`

- Built `Tools ▸ unwritten ▸ Art ▸ Normalize Circle Effects` (`CircleEffectNormalizer.cs`); ran it. Set: icearrow01–04 → 0.076, freeze/healing → 0.176, magic → 0.469, magic-2 → 0.352.
- RESULT — **REGRESSION**: `def.scale` is SHARED. The same `circle.*` effects are ALSO used inside release VfxSequences (sized by the artist in the Vfx Composer). Rescaling `def.scale` shrank the released green circle \~5.7× (that was `circle.healing` at 0.176). Broke the release visual. FAILED — wrong lever.

## Root problem (the real lesson)

`def.scale` is a SHARED property used by two roles:

- **Telegraph role** (cast circle): wants world radius == skill AoE radius. Needs to divide out the prefab's huge intrinsic footprint.
- **Sequence/decor role** (release VFX): the artist already sized it in the Composer; `def.scale` must stay as authored. Changing `def.scale` to fix one role breaks the other. The normalization must live in the TELEGRAPH sizing path ONLY — not in shared `def.scale`.

## Correct approach (NOT yet implemented — reverted per user request)

Store the prefab's intrinsic footprint as a SEPARATE per-effect field (e.g. `EffectDef.unitRadius`, measured once), leave `def.scale` alone, and have the cast-circle path compute `scaleMult = R / (unitRadius × def.scale)` so world radius == R for the telegraph while every other use of the same effect keeps the artist's `def.scale`. (This is the "groundRadius field" option; it decouples the two roles.)

## State after this session

ALL of the above was REVERTED back to the pre-session state per user request. Nothing above is applied.