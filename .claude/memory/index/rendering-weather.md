# Rendering / lighting / reflection / weather

- [NightLight ranked vs STATIONARY pivot](../nightlight-shadow-stationary-pivot.md) — fix = cached `PlayerTransform()`.
- [Planar reflection clip-plane sign fix](../planar-reflection-clip-sign-fix.md) — `CameraSpacePlane`→`pos - normal*clipPlaneOffset`.
- [Water reflection anchored to ground](../water-reflection-quantized-plane-bug.md) — quantized grouping key wrongly used as mirror plane; use real `PlaneY`.
- [Ground shader must loop additional lights](../ground-shader-additional-lights.md) — add `_ADDITIONAL_LIGHTS` loop; needs URP Per-Pixel.
- [Billboard sprites receive lamp/torch lights](../billboard-additional-lights.md) — `BillboardLit` loops add-lights; `PropLight.cs`.
- [Weather system (rain/wet/reflection)](../weather-system.md) — storm gated behind `WeatherFX.Quality>=High`.
- [Snow weather](../snow-weather.md) — `SnowOverlay`/`SnowGround`/`FootstepTracker`+`SnowCapBaker`; shader-only volume DEAD-END.
- [X-ray occlusion silhouette](../occluded-silhouette-stencil.md) — stencil AREA mask (per-fragment depth self-occludes); two-bit `CoverBit` so held gear doesn't x-ray the body.
- [Ink ground black band → stencil exclude](../ink-ground-black-band-stencil.md) — crease×60 saturates far ground; exclude via stencil bit 32, ground drawn LAST so it can't stamp the character.
- [Ink outlines not fogging → draw order](../ink-fog-draw-order.md) — FogScreen `Queue "Overlay+100"` so fog draws OVER ink; hand-tuned in-shader fade is a DEAD-END; don't disable the bit-32 ground-skip (=floor immunity). Same fix also cured the drag-orbit ink flicker (ambiguous same-queue+same-distance sort was thrashing).
