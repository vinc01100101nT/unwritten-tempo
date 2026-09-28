---
name: ui-click-shield
description: Windows leaking clicks to the world (hit mobs behind UI) — fix via UiKit.ClickShield full-rect raycast catcher
metadata: 
  node_type: memory
  type: project
  originSessionId: 3d077027-5dd2-4f4b-8883-0609ffb0b1ec
---

2026-07-01: clicks on HUD windows leaked THROUGH to the 3D world (clicking a mob behind the window) because world-input scripts gate on `EventSystem.IsPointerOverGameObject()`, which is FALSE over any gap in a window that has no raycast-target graphic (transparent margins, space between elements). Moving the HUD to its own scene/layer does NOT fix this — it's an input-gating + raycast-coverage issue, not a scene-ownership one.

**Single-source fix = `UiKit.ClickShield(RectTransform root)`** (`UiKit.cs`): inserts a full-rect, fully transparent, `raycastTarget=true` Image as the BACKMOST child of a window root, so the WHOLE window area registers as UI. Idempotent (guards on `root.Find("ClickShield")`). Applied at every window root:
- `UiKit.Window` (covers the ProgressionWindows: Inventory/Character).
- Runtime in the panels that find a pre-baked UIShellBuilder window: `SkillDraftPanel` (SkillsPanel), `CalendarPanel` (CalendarPanel) — shield applied where it resolves `panel` (the window found by name), so existing baked scenes are fixed WITHOUT re-running Build UI Shell.
- `WorldMapHUD` was already fine (full-screen `dim` backdrop with raycastTarget).

⚠️ world-input already checks `!PointerOverUI()` (`Player3DController`, `UnitTargeter`); the shield just guarantees that returns true over the whole window. For any NEW window, call `UiKit.ClickShield(rootRect)` once on its root — don't rely on per-element raycast coverage. Shield sits behind content, transparent, so buttons/drag-handles on top still work (GraphicRaycaster picks topmost).

⚠️ SECOND ROOT CAUSE — IMGUI overlays (2026-07-01): the shield is uGUI-only. **IMGUI (`OnGUI`) overlays — `EffectTuner` (F8), `DevSettings` (P) — are INVISIBLE to `EventSystem.IsPointerOverGameObject()`**, so clicks leaked straight through them (this was the actual repro: clicking the Effect Tuner hit mobs behind it). Fix = new **single-source `UiBlock.PointerOverUI()`** (`UiBlock.cs`): returns `IsPointerOverGameObject() || ImguiHot`. IMGUI overlays call `UiBlock.MarkImguiHot()` in their OnGUI while the cursor is over their window (EffectTuner uses `area.Contains(Event.current.mousePosition)`; DevSettings folded its `Blocking` in). `ImguiHot` accepts the mark for the current OR previous frame (Update runs before OnGUI → the mark lands a frame late). Both world-input `PointerOverUI()` copies (`Player3DController`, `UnitTargeter`) now DELEGATE to `UiBlock.PointerOverUI()` → one source. RULE: any new IMGUI/OnGUI overlay must call `UiBlock.MarkImguiHot()` when the cursor is over it, or clicks leak.
