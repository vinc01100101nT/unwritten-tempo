# Water reflection anchored to ground, not the water surface (quantized-plane bug)

## Symptom
Elevated water body reflected the scene as if the mirror plane sat at ground level — a character
above/at the water showed a detached reflection pushed downward ("reflects as if sitting on the ground").

## Root cause
`WaterReflection` (multi-height planar reflection for water bodies) grouped co-planar pools into shared
mirror passes by a QUANTIZED height (`WaterSurface.QuantizedHeight = Round(PlaneY/heightQuantize)*heightQuantize`,
default quantize 0.25). The bug: that **quantized value was also used as the actual mirror plane**. A single
pool at `PlaneY = 0.104` rounded to `0` → the mirror reflected about y=0 (ground), ±half-quantize error.

## Fix (WaterReflection.cs)
Separate the two roles: the quantized key is for GROUPING only; the pass's mirror plane must be the REAL
surface height. Added `float[] activeKeys` (quantized, for dedup) alongside `activeHeights` (now the real
`WaterSurface.PlaneY` of the first surface in each bucket). `GatherHeights` fills both; `NearestPass` matches
a surface to its pass by KEY; `MirrorPass.planeY = activeHeights[i]` (real). Pool at 0.104 now mirrors about 0.104.

## Gotchas / notes
- Grouped pools (within one `heightQuantize` bucket) still share the first surface's real height → up to
  `heightQuantize` error between pooled surfaces. Fine tradeoff; terraced pools >quantize apart each get an exact pass.
- The mirror MATH is shared with the puddle `PlanarReflection` via `PlanarReflectionMath` (SSOT); the puddle one
  was never affected because it uses `followGround` (correctly snaps to real ground Y, no quantization).
- Rule reinforced: never round a value that must stay physically exact; if you quantize for bucketing, keep the
  real value for the actual computation.
