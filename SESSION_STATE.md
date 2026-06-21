# SESSION_STATE — resume here (keep this SHORT; git holds history)

*2026-06-21 · branch* `experiment/3d-world` *· dials: Sonnet 4.6 + Medium (default)*

## Protocols (full text in `~/.claude/.../memory/`)

- **Effort + model**: defaults **Sonnet 4.6 + Medium**. Assess each task for BOTH; auto-GO on Low/Medium fitting the model; PAUSE for >Medium OR higher tier until user sets dials + GO. Tag each task reply `**EFFORT: X · MODEL: Y**`. Opus for hard/ambiguous; model switches bust the cache.
- **Checkpoint**: OVERWRITE current state (never append — git holds history); on milestones + before `/clear`; keep short.

## ✅ Shipped (prior sessions) — weather/storm + snow. Memory: `weather-system.md`, `snow-weather.md`
- Storm stack (High tier; WebGL forced Medium): `RainOverlay`/`RainScreen.shader`, `WetGround`(+shader), `PlanarReflection`, `RainAudio`, `WeatherFX.cs` gate. **User happy.**
- Snow (screen-space only): `SnowOverlay`/`SnowGround`(+shaders), shared `FootstepTracker`. **User happy.** Snow VOLUME approaches are dead-ends — DO NOT retry (see `snow-weather.md`).
- Rain micro-splashes ON TOP of heads/canopies/roofs: `SplashOnTop.cs` (pooled, capped, High-tier) + `SplashTop.shader` (procedural crown ring + central pop + 5 droplets, no art) + `SplashTopVolume.cs` (static-prop opt-in).

## ✅ Done THIS SESSION — rain-splash polish (all live-tunable via DevSettings **P**)
1. **Top-splash defaults retuned** to user's screenshot → baked into `SplashOnTop.cs` defaults **and** `Systems.unity`: rate **80**, size **0.05**, life **0.15**, headGap **0.34**, headWidth **0.43**, range **24**.
2. **Rain now hits the WHOLE ground, not just puddles** (`WetGround.shader`): split into two masks — `groundWet` (all flat wet ground → impact rings only) vs `wet`/puddle (reflections + fresnel + footstep splashes + darken, **unchanged**, now behind an `if(wet>0.001)` branch so off-puddle ground stays cheap). New `_GroundImpact` (default **0.35**) → `WetGround.cs` `groundImpact` field + DevSettings **"Ground impact"** knob.
3. **Ground impacts restyled to MATCH the character top splash**: rewrote `rainRipple()` in `WetGround.shader` to the `SplashTop.shader` look (expanding+thinning crown ring + central pop + 5 droplets). ⚠️ `rainRipple` is shared by ground **and** puddle ambient ripples → **puddles now show crowns too** (flagged to user; offered to split into crown=ground / thin-ring=puddle if they want puddles to keep the old look).
   - Perf: `WetGround` runs **High tier only** (Heavy gated off on WebGL/Medium), so the extra droplet ALU is **PC-only**.

## ⏳ Do next
- **User Play-tests** the crown-look ground impacts (P → Heavy weather + rain). If they want puddles to keep the OLD thin rings, split `rainRipple` into two functions.
- Bigger feature still pending: **weather calendar** (plan in `weather-system.md`) — tie presets (sunny/cloudy/light rain/thunderstorm/snow) to the `TimeOfDay` clock via Snow/Rain knobs as preset targets. Decide scope (per-day vs per-hour; authored vs weighted vs seasonal) + dials first; PRD offered.

## Gotchas (don't relearn)
- Custom runtime shaders MUST be in **Always Included Shaders** or stripped from builds (`SplashTop`/`WetGround` GUIDs already there).
- No local Play loop — **user presses Play** to verify; can't check shaders on-GPU from here.
- New public fields added in code aren't in the baked `Systems.unity` → they fall back to the C# default (fine), but retuned EXISTING serialized fields must be edited in BOTH the script default and `Systems.unity`.
