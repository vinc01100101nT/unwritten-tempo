# SESSION_STATE — resume here (keep this SHORT; git holds history)

*2026-06-21 · branch* `experiment/3d-world` *· dials: Sonnet 4.6 + Medium (default)*

## Protocols (full text in `~/.claude/.../memory/`)

- **Effort + model**: defaults **Sonnet 4.6 + Medium**. Assess each task for BOTH; auto-GO on Low/Medium fitting the model; PAUSE for &gt;Medium OR higher tier until user sets dials + GO. Tag each task reply `**EFFORT: X · MODEL: Y**`. Opus for hard/ambiguous; model switches bust the cache.
- **Checkpoint**: OVERWRITE current state (never append — git holds history); on milestones + before `/clear`; keep short.

## ✅ Shipped — weather/storm stack (High tier; WebGL forced Medium). Memory: `weather-system.md`
`RainOverlay`+`RainScreen.shader`, `WetGround`+`WetGround.shader` (puddles + footstep ripples), `PlanarReflection`, `RainAudio`, `WeatherFX.cs` gate. Tuned via **DevSettings (P)**. **User is happy with rain/storm.**

## ✅ Shipped THIS SESSION — SNOW (screen-space only; user happy). Memory: `snow-weather.md`
All in `unity/Assets/Scripts` + `Shaders`, tuned live via **DevSettings (P)** "Snow" block:
1. `SnowOverlay`+`SnowScreen.shader` — drifting flakes, cheap 2D overlay, **all tiers**.
   - **Entrance ramp**: `shown` eases toward `intensity` over `rampTime` (flakes fade in, no pop).
   - **Camera parallax** (`_Parallax`): flakes drift WITH camera (yaw→horiz ×4, pitch→vert, +translation) so they feel world-anchored. Free accumulation — NO `Mathf.Repeat` (it caused the pan "reset" jump).
   - **Depth**: 4 layers, bigger flakes fall faster + parallax more; sway amplitude ×4 so `drift` is visible.
2. `SnowGround`+`SnowGround.shader` — depth pass: whitens up-facing surfaces + **footprints** (heal over time, **High only**).
   - **Coverage FILL transition** (not opacity): ramp `_Coverage×_Accum` lowers a noise threshold so snow spreads in by area; covered area is **opaque** (`smoothstep(0,0.4,cov)`) → **pure-white roofs**, snow color now `(1,1,1)`, brighter shade floor.
3. `FootstepTracker` — SINGLE source of footstep positions; WetGround ripples + SnowGround prints both call `SampleInto(buf, lifetime)`. Gait knobs (gap .30/spacing .08) live here.
- **Preferred defaults baked into code** (from user screenshot): SnowOverlay fall 5.55 / drift 1 / density 16.52 / flake .71 / parallax 1 / ramp 3; SnowGround thickness 0 / coverage 1 / slopes 0 / build 14 / sparkle 2 / clumpiness .4 / white.
- DevSettings knobs incl. **Snow parallax** + **Flake fade in**.

## 🗑️ REMOVED this session — the snow-cap BAKER (geometry volume). DO NOT rebuild.
`SnowCapBaker.cs` + `SnowCap.cs` + `SnowCap.shader` + `SnowCap.mat` all **DELETED** (long tail of issues: z-fight, thin ridges, bare slopes/edges, lumps-only-on-slopes, bad overhang — never looked right). Screen-space coverage wins.

## ⚠️ Snow VOLUME dead-ends — DO NOT RETRY (all failed)
Screen-space tint can't cross a silhouette · normal-extrusion = floating slab · shell layering = blades on flat roofs (`SnowShell` deleted) · the **baker** = not worth the tuning (deleted). Only untried path = bake-time physics-scatter + marching-cubes skin = real spike, timebox if ever.

## ⏳ Do next
- **Re-run Setup Systems Scene** so the new code defaults bake into `Systems.unity`. Also search Hierarchy for leftover `__SnowCap` objects (now missing-script white meshes) and delete them.
- Confirm `SnowScreen` + `SnowGround` shader GUIDs are in **Always-Included** (SnowCap GUID can be pulled if present).
- THEN: **weather calendar** (plan in `weather-system.md`) — tie presets (sunny/cloudy/light rain/thunderstorm/snow) to the `TimeOfDay` clock via the Snow/Rain knobs (preset targets). Scope (per-day vs per-hour; authored vs weighted vs seasonal) + dials first; PRD offered.

## Gotchas (don't relearn)
- Custom runtime shaders MUST be in **Always Included Shaders** or stripped from builds.
- No local Play loop — **user presses Play** to verify; can't check shaders on-GPU from here.
- Snow is screen-space ONLY now; ground excluded from any per-prop work (it owns footprints).
