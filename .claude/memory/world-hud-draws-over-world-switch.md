---
name: world-hud-draws-over-world-switch
description: Floating names / damage numbers hidden by grass while HP bars were not — the text material's on-top switch was set under a property name no text shader has, so it was silently skipped
metadata:
  type: project
---

**Symptom (2026-10-11):** names over heads, damage numbers and battle notices were hidden behind tall grass (and any
scenery); health and cast bars were not.

**Root cause:** `UiKit.OutlinedMaterial` did `if (m.HasProperty("_ZTestMode")) m.SetFloat("_ZTestMode", Always)`.
No TMP shader in the project has `_ZTestMode` — they all say `ZTest [unity_GUIZTestMode]` — so the branch never ran
(measured live: font shader `TextMeshPro/Mobile/Distance Field`, HasProperty false). The bars set
`unity_GUIZTestMode` directly, which works.

**Fix:** one door, `UiKit.DrawOverTheWorld(Material)`. A TEXT material is moved onto its shader's always-on-top twin
(`TextMeshPro/Mobile/Distance Field Overlay` — differs from the original only in `ZTest Always` and `Queue Overlay`),
with its render queue put back to what it was (3000) so nothing reorders against the bars; the twin is in Always
Included Shaders because it is found by name. Anything else (the bars' `UI/Default`, which has no twin) gets
`unity_GUIZTestMode` = Always. Words reach the door through `OutlinedMaterial` / `WorldNameMaterial`, bars and tiles
through `MobNameplate.SharedOnTop` (CastBar's duplicate material was deleted).

**Gotchas, all measured:**
- `HasProperty("unity_GUIZTestMode")` is false on UI/Default and TMP shaders even though `SetInt` on it works. Never
  gate the write on HasProperty.
- `new Material(other)` does NOT copy it (copy read 0); `CopyPropertiesFromMaterial` does. `WorldNameMaterial` is such
  a copy and must set it again.
- `GetInt` on a material where it was never set logs a console error — probe with HasProperty first.
- TMP builds fallback-font materials with `new Material(source)`, which drops the switch but keeps the shader and
  the render queue (measured on a copy) — the reason words use the twin shader and not the switch.

Related: [[occluded-silhouette-stencil]]
