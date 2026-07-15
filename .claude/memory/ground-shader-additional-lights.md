---
name: ground-shader-additional-lights
description: "GroundBlend ground shader must loop additional lights or night lamps don't pool light on the ground (only main light + SH)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4d20eadf-0b80-43ae-a1bb-eab297225101
---

2026-06-29: **Night lamp lit the player/props but NOT the ground.** Symptom: at night a `NightLight` point light visibly lit the charcoal character billboard and nearby props, but the grass/ground stayed black under it — no warm pool.

Root cause: `unity/Assets/Shaders/GroundBlend.shader` ForwardLit pass only computed **main light (sun) + ambient SH** — it had **no additional-lights loop**, so point/spot lights (lamps, torches, portals = URP "additional lights") physically could not touch the ground. The character lit up only because `BillboardLit.shader` *does* loop additional lights (see [[billboard-additional-lights]]).

⚠️ CORRECTION (verified via git): this was **NOT a regression** — `GetAdditionalLight` has NEVER been in GroundBlend's git history. Ground-under-lamp lighting was a brand-new feature added here, not something a tool deleted. Do not blame the map generators (RecipeGenerator/WorldCreator only write scene/prefab content; only Build25DSpike touches the URP asset and it just appends a renderer, never the lights mode).

Related trap (the REAL recurring issue): `m_AdditionalLightsRenderingMode` in `UniversalRP.asset` was Per Vertex (1) on disk; the "Per Pixel" (2) from the billboard-lights session was apparently a live editor change that was never committed, so it reverted on reserialize. Lighting-without-shadows = this flag on Per Vertex. **Lesson: editor-only render settings silently revert — commit them.** A "Verify Render Settings" guardrail tool is the fix.

Fix (in `GroundBlend.shader` ForwardLit pass):
- Added pragmas: `_ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS`, `_ADDITIONAL_LIGHT_SHADOWS`.
- In frag, after main light, loop `GetAdditionalLightsCount()` / `GetAdditionalLight(...)` and accumulate `al.color * (N·L * distanceAttenuation * shadowAttenuation)` into the lighting term before multiplying albedo.
- Classic loop (renderer is plain **Forward**, not Forward+ — per [[billboard-additional-lights]], URP set to PerPixel additional lights).

⚠️⚠️ THE CRITICAL GOTCHA (verified against URP 17.4 RealtimeLights.hlsl, MCP runtime-bisected 2026-06-29): the **2-arg** `GetAdditionalLight(uint i, float3 positionWS)` returns `shadowAttenuation = 1` — it does NOT sample shadows. Only the **3-arg** `GetAdditionalLight(i, positionWS, half4 shadowMask)` calls `AdditionalLightShadow(...)`. First fix used the 2-arg form → ground LIT under the lamp but NEVER darkened (no cast shadow). Correct call for fully-dynamic (no lightmap): `GetAdditionalLight(li, positionWS, half4(1,1,1,1))`. Symptom signature: lit-but-no-shadow on a custom lit shader = wrong overload.

Diagnosis method that nailed it (reusable): via Unity MCP `execute_code`, entered Play, forced night (`TimeOfDay.time01=0.75`, `paused=true`), moved the REAL movement root **Player3D** (NOT `Character.Instance` — there are 3 player objects: Character=logic, Player3D=movement+camera target, PlayerBakedVisual=sprite+BillboardShadow proxy; moving Character alone left the proxy at origin), confirmed `lamp.shadows=Hard`+proxy ShadowsOnly, then isolated receive by dropping a default URP/Lit plane+cube beside the player → cube shadowed on URP/Lit but NOT on GroundBlend → proved it was GroundBlend's receive, not the cast. ⚠️ manual `cam.Render()` to a RenderTexture does NOT render additional-light shadows (URP caveat) — use `ScreenCapture.CaptureScreenshot` for a real frame.

GENERALIZED (2026-06-29): extracted the loop into a single shared include **`Assets/Shaders/Lib/AdditionalLights.hlsl`** → `ApplyAdditionalLightsDiffuse(positionWS, N, doubleSided)` (always 3-arg overload + shadows). `GroundBlend` and `FoliageWind` now both `#include "Lib/AdditionalLights.hlsl"` and call it instead of hand-rolling the loop — one source of truth so the wrong-overload trap can't be copied per-shader again. New custom lit shaders should include it too. Pragmas still must be declared per-pass (`_ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS` + `_ADDITIONAL_LIGHT_SHADOWS`) — the include documents this requirement. `WetGround`/`SnowGround` are screen-space overlays (no lighting) — correctly NOT included. `BillboardLit` kept its own custom loop (intentional fake-normal/albedo-tint lighting). Verified live via MCP: player + lamp-post both cast shadows on refactored GroundBlend grass.

Gotchas:
- URP must be **Additional Lights = Per Pixel** (UniversalRP.asset) or the per-pixel loop is a no-op — editing the .asset on disk while Unity is open may not reimport; flip it in the inspector.
- Any OTHER custom lit surface shader (WetGround, SnowGround) has the same blind spot if it omits the loop — check them if their ground also stays dark under lamps.
- Unrelated but co-occurring: a building placed at raw **scale 4** needs a much larger NightLight `range` (its 6-unit default is tiny relative to a 13×-bigger mesh); and lamp position is per-object `localOffset` (×scale) — added a Scene-view drag gizmo `Editor/NightLightEditor.cs`.
