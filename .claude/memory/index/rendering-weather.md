# Rendering / lighting / reflection / weather

- [NightLight ranked vs STATIONARY pivot](../nightlight-shadow-stationary-pivot.md) — fix = cached `PlayerTransform()`.
- [Rain water gotchas](../water-reflection-quantized-plane-bug.md) — design in docs/2-systems/rain-and-standing-water.md; never tie water to a player body, shader arrays lock their first size, mirror hand-out order, 0.25 m ground for round level edges.
- [Ground shader must loop additional lights](../ground-shader-additional-lights.md) — add `_ADDITIONAL_LIGHTS` loop; needs URP Per-Pixel.
- [Billboard sprites receive lamp/torch lights](../billboard-additional-lights.md) — `BillboardLit` loops add-lights; `PropLight.cs`.
- [Weather system (rain/wet/reflection)](../weather-system.md) — storm gated behind `WeatherFX.Quality>=High`.
- [Rain scatter freezes over time](../rain-scatter-phase-precision.md) — unbounded `_Phase` overflows float32 in `hash21`; fix = wrap scroll+cell.y by `PERIOD 1024` in shader + `Mathf.Repeat(phase,10240)` in `RainOverlay`.
- [Snow weather](../snow-weather.md) — `SnowOverlay`/`SnowGround`/`FootstepTracker`+`SnowCapBaker`; shader-only volume DEAD-END.
- [X-ray occlusion silhouette](../occluded-silhouette-stencil.md) — stencil AREA mask (per-fragment depth self-occludes); two-bit `CoverBit` so held gear doesn't x-ray the body.
- [Ink ground black band → stencil exclude](../ink-ground-black-band-stencil.md) — crease×60 saturates far ground; exclude via stencil bit 32, ground drawn LAST so it can't stamp the character.
- [Ink outlines not fogging → draw order](../ink-fog-draw-order.md) — FogScreen `Queue "Overlay+100"` so fog draws OVER ink; hand-tuned in-shader fade is a DEAD-END; don't disable the bit-32 ground-skip (=floor immunity). Same fix also cured the drag-orbit ink flicker (ambiguous same-queue+same-distance sort was thrashing).
