# SESSION_STATE — resume here (keep this SHORT; git holds history)

*2026-06-22 · branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols (full text in `~/.claude/.../memory/`)
- **Effort**: model is now **always Opus 4.8** (dropped from assessment — updated `effort-level-protocol.md`).
  Assess **EFFORT only**; auto-GO on Low/Medium; PAUSE for >Medium until user sets dial + GO. Tag each reply `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE current state (never append — git holds history); on milestones + before `/clear`; keep short.

## 🔨 ACTIVE: Weather → channel + sequence architecture (BUILT, awaiting Play-test)

User's design call: weather "micro events" must be **standalone, then composed into reusable timelines**
(not renderers following each other). Full details + supersession notes in memory `weather-system.md`.

**Built (all `unity/Assets/Scripts/`):**
- **`WeatherChannels.cs`** (NEW) — 4 independent eased 0..1 channels `{Rain,Cloud,Snow,Puddle}`; central easing
  (~0 GC); `SetTarget/Snap/Shown`; **sequencer** (`Play`/`Settle`) + **auto-puddle** (Rain→Puddle when no seq owns it).
  Defines `WeatherChannel` enum, `WeatherKey`, `WeatherSequence`.
- **`WeatherSequences.cs`** (NEW) — composable timelines per Preset (THE file to tune). `K(at,channel,target,ramp)`.
  Channels hold last target between keys → after-rain linger = late `Puddle→0`. LightRain = clouds-first then
  rain 0→0.45 over 10s. Thunderstorm = user's example (clouds→light rain→light puddles→storm→more puddles).
- **`WeatherDirector.cs`** — same deterministic season pick → `Play(seq)` on change / `Settle(seq)` on load.
  Removed old `TargetsFor`, cloud-ease-in-Update, preRainCloudLead coroutine.
- **Renderers = dumb followers** (each w/ self-ease fallback): RainOverlay←Rain, CloudShadows←Cloud (added
  `Ensure()`), SnowOverlay←Snow, SnowGround←Snow, WetGround←Puddle(×`puddleAmount`)+Rain, RainAudio←Rain.
- **`SystemsSceneBuilder.cs`** adds `WeatherChannels` to rig. **`DevSettings.cs`** masters now snap channels +
  "Puddle coverage"/"Auto puddle"/Puddle timers.

**Status / next:**
- ⏳ **Not Play-tested** (no local Play loop — user presses Play). Fix any first-Play compile errors.
- Optional: re-run **Setup Systems Scene** to bake `WeatherChannels` into the rig (director self-hosts one otherwise).

## 🔨 ACTIVE: Dedicated Fog renderer — DEPTH FOG (BUILT, awaiting compile/Play-test)

Fog was a fake (just `Cloud→1.0`, looked like heavy Cloudy). Now **true depth fog**: fogs each pixel by its real
distance from the camera (far trees/ground dissolve into haze), built on the WetGround/CloudShadow depth-quad pattern.
5th weather channel. (First pass was a screen-space edge-bias fake — user correctly noted it can't fade by true
distance; upgraded to depth.)
- **`WeatherChannels.cs`** — added `Fog = 4` to the `WeatherChannel` enum, bumped `Count 4→5` (channel array sizes off it).
- **`Shaders/FogScreen.shader`** (`Unwritten/FogScreen`) — samples `_CameraDepthTexture` (`SampleSceneDepth` +
  `LinearEyeDepth`), fog factor = saturate((eye−`_FogStart`)/(`_FogEnd`−`_FogStart`)); sky (far clip) = full fog.
  Veil textured by WORLD-pinned value-noise FBM (via `ComputeWorldSpacePosition`, world X/Z), kept above `_Floor`.
  **Queue = Overlay** (draws after billboard chars so it tints them too; sprites aren't in depth so they fog by the
  ground behind them ≈ their distance). Needs URP depth texture ON (already on for WetGround/CloudShadow).
- **`FogOverlay.cs`** — dumb follower of `WeatherChannel.Fog`; `Ensure()`/`Instance`, zero-cost when clear (quad off
  at fog≈0). Knobs: intensity / startDistance / fullDistance / coverage / floor / patchSize / driftSpeed / rampTime /
  color / nightTint. (Dropped the old edgeBias/parallax/density.)
- **`WeatherDirector.cs`** — `FogOverlay.Ensure()` in Apply(). **`SystemsSceneBuilder.cs`** adds FogOverlay to the rig.
- **`WeatherSequences.cs`** — Fog preset drives `Fog 0→0.85` + softer `Cloud 0.45` (distinct from Cloudy/Storm). Added
  `Fog→0` key to Clear/Cloudy/LightRain/Thunderstorm/Snow so switching away dissolves the haze (channels hold last target).
- **`DevSettings.cs`** — "Fog" sub-group (start/full dist, coverage, floor, patch size, drift, roll-in) + `fog` ref.
- **User-tuned defaults baked** (2026-06-22): FogOverlay start 10 / full 29 / coverage 1.0 / floor 0.8 / patch 10 /
  drift 4; Fog preset channel target → 1.0. (Ensure()/AddComponent uses these code defaults — no scene re-bake needed.)
- SO layer (below) auto-supports Fog (keys are the enum); regen assets to pick it up if you ever generated them.

**Status / next:**
- ⏳ **Not compiled / Play-tested** (no local Play loop). Force weather to Fog (DevSettings ▸ Calendar & Weather ▸
  Force weather, or the "Fog" toggle) to see it. Tune distance via DevSettings ▸ Fog ▸ start/full dist.
- ⚠️ `Unwritten/FogScreen` should go in **Always Included Shaders** before a real build (same gap as the other
  weather shaders — not yet registered; fine in-Editor).
- ⚠️ If fog doesn't fog distant geometry, confirm the URP renderer's **Depth Texture** is enabled (it is for
  WetGround/CloudShadow, so it should already be on).

## ScriptableObject authoring layer ("code now, assets later") — CODE BUILT, no assets yet

Done per the agreed next step: the SO data-source layer. **Engine unchanged** — only where the keys come from moved.
- **`WeatherSequenceAsset.cs`** (NEW, runtime SO) — Inspector-editable timeline (`preset` + `displayName` +
  `WeatherKeyData[]`); `ToSequence()` cached (0 GC on weather change), `OnValidate` drops cache for live re-tune;
  stable insertion-sort by `at`. `[CreateAssetMenu] unwritten/Weather/Sequence`. Added `[Serializable] WeatherKeyData`.
- **`WeatherSequenceLibrary.cs`** (NEW, runtime SO) — `preset→asset` registry; static `Resolve(p)`/`Active()` load
  the live lib from `Resources/WeatherSequenceLibrary`, **cache the miss** (1 null-check per change, no repeat
  Resources.Load); `InvalidateCache()` on edit. `[CreateAssetMenu] unwritten/Weather/Sequence Library`.
- **`WeatherSequences.cs`** — `For(p)` now = authored asset **?? `FromCode(p)`** (old switch renamed `FromCode`).
  Single seam; director call site untouched. With **no assets (today) everything still comes from code, identical.**
- **`Editor/WeatherSequenceAssetBaker.cs`** (NEW) — `Tools ▸ unwritten ▸ Weather ▸ Generate Sequence Assets from
  Code`: idempotent one-click that seeds per-preset assets (exact copy of `FromCode`) + the Resources library. **NOT
  run yet** (= "assets later"). Run it when you want to tune timelines in the Inspector.

**Status / next:**
- ⏳ **Not Play-tested / not compiled** in Editor yet (no local Play loop). Fix any first-compile errors.
- Assets intentionally NOT created yet ("scriptable object later"). To start authoring: run the baker menu above.

## ✅ Shipped earlier (memory: `weather-system.md`, `snow-weather.md`)
- Calendar + deterministic per-day weather (TimeOfDay/GameCalendar/WeatherDirector + Calendar HUD/panel + DevSettings
  "Calendar & Weather"). Storm stack (RainOverlay/WetGround/PlanarReflection/RainAudio/WeatherFX), Snow
  (SnowOverlay/SnowGround/FootstepTracker), rain top-splashes (SplashOnTop). **User happy.** Snow VOLUME = dead-end.

## Gotchas (don't relearn)
- Custom runtime shaders MUST be in **Always Included Shaders** or stripped from builds.
- No local Play loop — **user presses Play** to verify.
- Re-baking `Systems.unity` resets hand-tuned overlay/light values → prefer editing script default + serialized
  scene block over a full re-bake. UI shell HAS no hand-tuned values → re-bake freely (Build UI Shell).
