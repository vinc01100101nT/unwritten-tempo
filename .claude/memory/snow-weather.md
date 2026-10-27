---
name: snow-weather
description: Snow system built 2026-06-21 (flakes + ground accumulation + footprints). Screen-space only — the geometry snow-cap baker was tried then REMOVED 2026-06-21 (never looked right). Snow-start transition is COVERAGE-based (area fill), not opacity.
metadata: 
  node_type: memory
  type: project
  originSessionId: a9cfb0cf-630d-4af5-bac7-b8b8b15f909b
---

Snow built on branch `experiment/3d-world` (Opus, June 2026), sibling to the rain stack ([[weather-system]]). All in `unity/Assets/Scripts/` + `Shaders/`, tuned live via the **DevSettings (P)** "Snow" block (Environment category).

**Three live layers (mirror the rain stack's full-screen-quad pattern):**
- `SnowOverlay` — the Rain Particles pack's snow (`Resources/Weather/Snow`, scaled ×0.19 by its transform, which also shrinks its speeds and noise), driven through `WeatherParticles`; runs on all tiers.
- `SnowGround` + `SnowGround.shader` — screen-space DEPTH pass (twin of WetGround): whitens UP-facing surfaces (`N.y` reconstructed from depth) + world-XZ patch noise + **fake thickness** (dome/lip/lumps shading) + **footprints**. The blanket `accum` RAMPS up/melts (target = `SnowOverlay.intensity`). Base pass runs all tiers; **footprints are High-tier only**. **Snow-start transition = COVERAGE FILL, not opacity**: the ramp (`_Accum`×`_Coverage`) lowers a noise THRESHOLD so snow appears in spots and SPREADS to full; covered area is fully opaque (alpha = patch coverage, NOT scaled by accum). Changed 2026-06-21 from the old uniform alpha fade-in.
- `FootstepTracker` — the SINGLE source of footstep positions (there's one character). WetGround ripples AND SnowGround prints both call `SampleInto(buffer, lifetime)`, which ages the same stamped steps by each effect's own lifetime (0.7 s ripple vs ~3 s heal). Gait knobs (gap/spacing/offset/jump-splash) live here now — were duplicated in WetGround+SnowGround before.

**Sparkle REMOVED 2026-06-23** (user: "remove the snow sparkle entirely") — the fresh-snow twinkle (`_Sparkle`/`_SparkleScale` props + cbuffer var + `step()` hash in `SnowGround.shader`, the C# fields/IDs/SetFloat, the "Snow sparkle" DevSettings knob, and the baked `Systems.unity` serialized values) is all gone. `hash21` stays (snow clump fbm still uses it).

**Snow is SCREEN-SPACE ONLY now — no geometry volume.** Decision 2026-06-21: the user is good without 3D roof snow; flat painted coverage that's COMPLETE beats patchy geometry.

**⚠️ Snow-volume DEAD-ENDS — reasoned out, DO NOT retry:**
1. Screen-space tint can't cross a silhouette → flat paint.
2. Single normal-extrusion of a thin surface → floating slab with open sides (just lifts the plane).
3. **Shell layering** (N concentric copies) → **sharp venetian-blind blades** on flat low-poly roofs; shells need DENSE geometry, our roofs are flat quads. Built then **REVERTED** (`SnowShell.cs`/`.shader` deleted).
4. **The snow-cap BAKER** (`SnowCapBaker.cs` + `SnowCap.cs` marker + `SnowCap.shader`/`.mat`) — domed meshes per roof via subdivide → geodesic distance-to-edge → lift+taper+Perlin lumps. Hit a long tail of issues (z-fight vs roof, thin tapered ridges, bare slopes/edges, lumps only on slopes, overhang/lip looked bad). Tried welding slopes into one patch, coverage floor, edge-roll — never looked right. **REMOVED entirely 2026-06-21** (all 4 files deleted). If real volume is ever wanted again, the only untried path is a bake-time PHYSICS-SCATTER + marching-cubes SKIN — but that's a real spike, timebox it.
→ Conclusion: thin flat meshes can't be inflated by a shader, and per-roof domes aren't worth the tuning. Lean on the screen-space `SnowGround` pass for coverage.

**Other facts:** the SnowGround shader GUID is in **Always-Included** (or stripped from builds). SystemsSceneBuilder adds SnowOverlay/SnowGround/FootstepTracker to the rig — re-run **Setup Systems Scene** to bake them. ⚠️ After the baker removal: any scene previously baked may still hold `__SnowCap` child objects (now missing-script white meshes with a missing material) — search the Hierarchy for `__SnowCap` and delete them. No local Play loop — user verifies; shaders can't be checked on-GPU from here. Snow is the sub-feature under the still-pending **weather calendar** ([[weather-system]]): snow days = drive the Snow knobs (intensity ramp) to a preset.
