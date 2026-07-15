# Rendering / lighting / reflection / weather

- [NightLight ranked vs STATIONARY pivot](../nightlight-shadow-stationary-pivot.md) — fix = cached `PlayerTransform()`.
- [Planar reflection clip-plane sign fix](../planar-reflection-clip-sign-fix.md) — `CameraSpacePlane`→`pos - normal*clipPlaneOffset`.
- [Water reflection anchored to ground](../water-reflection-quantized-plane-bug.md) — quantized grouping key wrongly used as mirror plane; use real `PlaneY`.
- [Ground shader must loop additional lights](../ground-shader-additional-lights.md) — add `_ADDITIONAL_LIGHTS` loop; needs URP Per-Pixel.
- [Billboard sprites receive lamp/torch lights](../billboard-additional-lights.md) — `BillboardLit` loops add-lights; `PropLight.cs`.
- [Weather system (rain/wet/reflection)](../weather-system.md) — storm gated behind `WeatherFX.Quality>=High`.
- [Snow weather](../snow-weather.md) — `SnowOverlay`/`SnowGround`/`FootstepTracker`+`SnowCapBaker`; shader-only volume DEAD-END.
- [X-ray occlusion silhouette](../occluded-silhouette-stencil.md) — stencil AREA mask (per-fragment depth self-occludes); two-bit `CoverBit` so held gear doesn't x-ray the body.
