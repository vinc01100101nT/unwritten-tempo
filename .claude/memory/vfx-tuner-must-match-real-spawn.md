# VFX dev-tuner must reproduce the REAL spawn exactly (or it's false info)

2026-07-01. The `EffectTuner` (F8 in Play, `EffectTuner.cs`) "Spawn on Mob" / "Spawn on Player"
knob is only trustworthy if it reproduces the ACTUAL combat spawn on ALL axes: **position, size,
playback speed, life**. User's rule: "you must stand it right when applying the live animation… or
else it would be false information."

## The bug (hit VFX floated above mobs)
- Tuner anchors the preview at the entity's RAW pivot: `Vfx.PlayAt(id, mob.transform.position)`
  → final world pos = `pivot + def.offset` (`Vfx.PlayAt` internally adds `def.offset`).
- Combat basic hit (`Player3DAttacker.SwingThenHit`) spawned at
  `fxPos = t.transform.position + Vector3.up * 0.4f`, then `Vfx.PlayAt(hitVfxId, fxPos, …)` added
  `def.offset` on top → final = `pivot + 0.4 + def.offset`.
- That hardcoded **+0.4 double-counted** with the tunable `def.offset`, so the user tuned `hit.holy`
  against the pivot (correct in preview) but combat rendered it ~0.4 above the mob.

## Fix
- Spawn the hit at the raw target pivot: `Vfx.PlayAt(hitVfxId, t.transform.position, …)` and let the
  tuned `def.offset` own all height. (`Player3DAttacker.cs`, basic-hit branch.)
- Left the SLASH VFX on `fxPos` (+0.4) — not the reported problem and its offset is 0, so +0.4 is its
  intended torso height. If a slash effect later gets a tuned offset, align it the same way.

## Root principle — single source of truth for VFX placement/size
`def.scale` / `def.offset` / `def.lifetime` in `EffectLibrary` are THE authority. Call sites must pass
the raw entity pivot and caller-scale 1 (defaults: `hitVfxScale=1`) so the def applies verbatim. Any
`+ Vector3.up * X` fudge before `Vfx.PlayAt` re-introduces the lie. Same family as the pivot-anchor rule.

## Post-fix state (all match for a NORMAL hit)
position ✅, size (`def.scale×1`) ✅, speed (×1) ✅, life (`def.lifetime`) ✅. Only deliberate divergence:
a CRIT spawns `def.scale × critVfxScale` (1.7×) — a gameplay punch on the tuned base, not false info.

## Gotchas / audit
- Other call sites still add ad-hoc height (`+ Vector3.up * X`) — meteor/fireball/frostnova/slash. If
  you tune any of those in the knob, reconcile the call site or the preview lies again.
- Robust guarantee against regression: route the tuner AND every combat call site through ONE shared
  spawn helper so nobody can silently re-add a base offset/scale. (Offered; not yet built.)
