# Rendering / lighting / reflection / weather

- [NightLight ranked vs STATIONARY pivot](../nightlight-shadow-stationary-pivot.md) — fix = cached `PlayerTransform()`.
- [Rain water gotchas](../water-reflection-quantized-plane-bug.md) — design in docs/2-systems/rain-and-standing-water.md; never tie water to a player body, shader arrays lock their first size, mirror hand-out order, 0.25 m ground for round level edges.
- [Ground shader must loop additional lights](../ground-shader-additional-lights.md) — add `_ADDITIONAL_LIGHTS` loop; needs URP Per-Pixel.
- [Billboard sprites receive lamp/torch lights](../billboard-additional-lights.md) — `BillboardLit` loops add-lights; `PropLight.cs`.
- [Weather system (rain/wet/reflection)](../weather-system.md) — storm gated behind `WeatherFX.Quality>=High`.
- [Snow weather](../snow-weather.md) — `SnowOverlay`/`SnowGround`/`FootstepTracker`+`SnowCapBaker`; shader-only volume DEAD-END.
- [X-ray occlusion silhouette](../occluded-silhouette-stencil.md) — stencil AREA mask (per-fragment depth self-occludes); two-bit `CoverBit` so held gear doesn't x-ray the body.
- [Ink ground black band → stencil exclude](../ink-ground-black-band-stencil.md) — crease×60 saturates far ground; exclude via stencil bit 32, ground drawn LAST so it can't stamp the character.
- [Ink outlines not fogging → draw order](../ink-fog-draw-order.md) — FogScreen `Queue "Overlay+100"` so fog draws OVER ink; hand-tuned in-shader fade is a DEAD-END; don't disable the bit-32 ground-skip (=floor immunity). Same fix also cured the drag-orbit ink flicker (ambiguous same-queue+same-distance sort was thrashing).
- [Shader.Find shaders missing in builds](../shader-find-missing-in-builds.md) — a shader looked up by name ships only if Always Included or used by a shipped material; Play mode hides it. Ink outline, paper grade and character colours were missing in every build until 2026-10-02.
- [Hand-drawn ink lost after sign-in](../hand-drawn-filter-lost-after-sign-in.md) — Systems reload sweep deleted HandDrawnFX while its re-create check saw it alive; fix: no-scene = gone, park in Systems, quad as child.
