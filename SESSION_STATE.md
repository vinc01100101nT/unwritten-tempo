# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- Effort dial: auto-GO Low/Medium; PAUSE >Medium until user sets dial + GO. Tag every reply `**EFFORT: X**` first line.
- No Play mode / no commits without being asked. Prefer `Tools ▸ unwritten` editor tools; update `Editor/TOOLS.md` when adding menu items.
- Unity edits at `unity/Assets/Scripts/`. Read `PERFORMANCE.md` before runtime C#.

## Current task — Billboard additional lights + lamp shadows (DONE, tuning pending)
Goal: billboard sprites (player/mobs) receive real-time lamp/torch light at night (was sun + `_SpriteEnv`
global only → charcoal char stayed black by lights), with lit-side shading + real cast shadow from the lamp.
Memory: `billboard-additional-lights.md`.

### DONE (compiled clean via Unity MCP force-compile, 0 errors; verified in Play)
- `Assets/Shaders/BillboardLit.shader` — ForwardLit loops URP additional lights, ADD after `_SpriteEnv`
  (lamp re-brightens at night). Directional via fake billboard normal `N = normalize(sx·camRight + sy·camUp
  + _AddLightBulge·camFwd)` then `N·L`. 4 knobs are GLOBAL shader floats now (not per-material):
  `_AddLightStrength/_AddLightShape/_AddLightBulge/_AddLightAlbedoTint`. Pragmas added for `_ADDITIONAL_LIGHTS*`.
- `Assets/Settings/UniversalRP.asset` — AdditionalLights→Per Pixel(2); AdditionalLightShadowsSupported→1;
  PrefilteringModeAdditionalLightShadows→4. (User also flipped Per Pixel + Cast Shadows in inspector.)
- `Assets/Scripts/SpriteAddLight.cs` (NEW) — holds 4 globals, `Apply()` = SetGlobalFloat, seeded at init.
  Defaults: Strength 1, Shape 0.85, Bulge 0.7, AlbedoTint 0.5.
- `Assets/Scripts/NightLight.cs` — PRE-EXISTING warm-pool lamp (NOT added this session; it was the mystery
  "lights"). Added registry + `SelectNearest()` (0.3s timer) so ONLY the player-nearest lamp casts
  (`LightShadows.Hard`); `TryGetShadowLamp`; statics `GlobalShadows`/`MaxShadowDist`/`GlobalShadowStrength`.
- `Assets/Scripts/BillboardShadow.cs` — at night aims ShadowsOnly proxy at the casting lamp (`LampFacing`).
- `Assets/Scripts/DevSettings.cs` — P panel: Shadows got Lamp shadows/range/dark; NEW **Sprite Light** group
  (Lamp pickup strength / Directional split / Bulge / Albedo tint). Panel rebuilds only on entering Play.
- `Assets/Scripts/PropLight.cs` + `Editor/PropLightTools.cs` (`World ▸ Add Prop Light to Selection`, in TOOLS.md)
  — reusable warm lamp; mostly redundant w/ NightLight, kept for generators.

### NEXT
1. TUNING PENDING — user to set in P ▸ Sprite Light: Directional split ~1.0, Bulge ~0.3 (look was "too flat");
   maybe raise Lamp shadow dark. **When user gives final values → bake as defaults** in `SpriteAddLight.cs`
   (+ `NightLight.GlobalShadowStrength`).
2. Optional: wire PropLight/portal lights into RecipeGenerator (offered, not started).

### Gotchas
- Auto Refresh is ON, but Claude's external writes don't always trip Unity's watcher until focus → use Unity MCP
  `refresh_unity(mode=force, scope=scripts, compile=request)` then `read_console` to confirm. MCP is connected.
- No git commit made.
