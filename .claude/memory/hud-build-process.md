---
name: hud-build-process
description: "The ONE process for any HUD change: wire component+layout into UIShellBuilder, run Build UI Shell. GameUICanvas is a Resources prefab (not a scene object). Never invent one-off prefab edits."
metadata: 
  node_type: memory
  type: process
  originSessionId: f3e5f22b-f7a2-42c5-b701-e170d3811ed5
---

Learned 2026-07-01 (user corrected me hard on process consistency).

**GameUICanvas is a prefab, not a scene object.** `UIShellBuilder.Build` rebuilds the canvas and saves it via `PrefabUtility.SaveAsPrefabAssetAndConnect` → `Assets/Resources/GameUICanvas.prefab`. It auto-spawns at runtime (that's why `find_gameobjects GameUICanvas` returns nothing in town/Systems scenes, and why HotbarPanel/etc. aren't in any saved scene).

**THE single process for every HUD element — do it this way every time, no exceptions:**
1. Add the controller component in `UIShellBuilder` (e.g. `canvasGO.AddComponent<XpBarHUD>();`) — same as HotbarPanel/SkillDraftPanel/ProgressionWindows.
2. Put HUD LAYOUT (positions/sizes) in `UIShellBuilder` too — it is the single source of truth. A component must NOT reach into sibling HUD objects by name at runtime to reposition them (I did this once = the wrong pattern; moved the +28px lifts of HUD_HealthBar/HUD_ManaBar/HUD_Hotkeys/HUD_Hotbar into UIShellBuilder).
3. To apply: run **Tools ▸ unwritten ▸ Scene Setup ▸ Build UI Shell**.

**Do NOT invent bespoke paths** (surgical prefab edits, live component injection, runtime nudging). The user explicitly wants ONE consistent process for the whole HUD to avoid drift/unexpected bugs.

**Build UI Shell scope / safety (verified from source):** it only (a) rebuilds the GameUICanvas prefab, (b) refreshes starter Item/Card assets (idempotent). It does NOT touch the 3D world or lighting — character shadows (`BillboardShadow`), lamps (`NightLight`), ground shaders, mobs, RenderSettings are all safe. Side effects: it leaves a stray `GameUICanvas` instance in whatever scene is active and marks it dirty → **don't save that scene** (or delete the instance). The active scene you're on doesn't matter for the result (the prefab is the output).

Self-building controllers (fill a baked holder / build own children into the canvas at runtime) are the norm here (HotbarPanel fills HUD_Hotbar; XpBarHUD builds its two bars). Simple bars (HP/MP) are baked GameObjects in UIShellBuilder driven by a component (CharacterUIBinder/HealthBarHUD). Both patterns are fine — the constraint is that they're WIRED via UIShellBuilder and layout lives there.
