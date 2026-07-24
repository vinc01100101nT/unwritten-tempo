---
name: carrier-start-tick-fires-at-launch
description: "AoE damage landing off-center from the cast circle / visible impact = a Carrier step set to Start tick (fires at launch point, offset by carrierFrom), not End."
metadata: 
  node_type: memory
  type: project
  originSessionId: 0c8f4cc2-0bbd-48b7-b741-b3008705550f
---

**Symptom:** a ground AoE skill's damage lands horizontally OFF-CENTER from the cast-circle telegraph and the visible impact (e.g. meteor's big hit lands ~4 units to the side; a far target takes damage while the circle/explosion is elsewhere).

**Root cause:** the Carrier VFX step's `tickWhen` is **Start** (enum 0), so the gameplay tick fires at the carrier's **launch point**, not its landing. In `VfxDirector` the launch point = `from = fromBase + carrierFrom` (fromBase = the landing for a from-above carrier, or the caster for `carrierFromCaster`). For a **from-above** meteor with `carrierFrom = {4,10,0}`, the launch is +4 sideways and +10 up from the landing. The hit test ignores Y (`d.y=0`), so the +4 becomes a horizontal offset of the whole 4.75-radius damage circle.

**Fix:** for a from-above Carrier, use **End tick** (`tickWhen = 1`) so damage fires on arrival at the landing = the aim point = the telegraph center. Start-at-launch only makes sense for a `carrierFromCaster` projectile (a muzzle hit at the caster).

**Gotcha:** `VfxStep.tickWhen` DEFAULTS to Start (0). A carrier re-authored/migrated into the new tick model (which replaced the old `tickCount==1 → fire on arrival`) silently becomes Start unless you pick End. See [[vfx-tick-timing-model]] (Start/End/Interval placement replaced tickCount/spreadTicks). A composer warning for "Start tick on a from-above carrier" was offered but not built.
