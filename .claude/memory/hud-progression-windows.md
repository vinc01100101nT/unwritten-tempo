---
name: hud-progression-windows
description: "Inventory/Equipment/Character HUD trio — locked design + Ornament Kit v1; files, palette tokens, and drop-in art hooks"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4fe5957b-f8c9-4677-8a37-5e684d439214
---

The progression **window trio** (Inventory `B` · Equipment `V` · Character `C`) is built at RUNTIME by `ProgressionWindows.cs` on top of the `UiKit.cs` construction kit — NOT by the editor builders (UIShellBuilder just `AddComponent`s it + bakes serialized sprite refs into `Assets/Resources/GameUICanvas.prefab`). Lives in the **Unity checkout** at `D:\tempo\projects\roguelike-mmo\unity\Assets\Scripts\` (see [[unity-2d-direction]] — NOT in the Tempo React worktree).

**Locked design (GPT⇄Claude HUD relay, 2026-06-25):** charcoal **+** gold theme. Palette tokens live in `UiKit.cs`: `PanelFill #15161A`, `PanelRaise` (~6% lighter, for detail/sub-panels), `Gold #C8A862`, `Cyan`/`CyanGlow` (selection/hover), `TextMain`/`TextDim`. Rarity colors are the single source of truth in `Rarity.cs` (`Rarities.Border` = slot border + tooltip LABEL; `Rarities.Text` = item NAME) — Legendary name is warm ivory `#F1E7D0` so it doesn't merge with the gold label. Hover/selection hierarchy is **rarity border > select ring (cyan 0.78, 2px) > hover bloom (cyan 0.22, ~8px)** — never inverted. Fonts: Cinzel (titles, `title:true`) + Inter body, both in `Resources/Fonts/`; legacy uGUI Text has NO SemiBold and Cinzel is single-weight, so item names use Normal weight (one step under Bold titles).

**Empty equip-slot watermarks:** `ItemSlot.emptyPlaceholder` + faint-gold `emptyTint`; the 7 paperdoll silhouettes (Helmet/Armor/Scroll/Kunai/Guard/Ring/Amulet from Ninja "Items & Weapon") are serialized fields on `ProgressionWindows` wired by `UIShellBuilder` (runtime can't AssetDatabase-load — same pattern as the calendar weather icons). Empty slots stay framed+clickable (`ItemSlot.EmptyBorderColor` slate).

**Ornament Kit v1** (`Ornament.cs`, runtime): procedural gold corner brackets + header divider-with-diamond on every `UiKit.Window`, + a `Backplate` (portrait/name plate) in the Character window. Has **drop-in sprite hooks** (`Ornament.cornerArt`/`dividerArt`, null⇒procedural) — assign final kit art via UIShellBuilder later, no layout change. GPT moved Ornament Kit to active production after locking the trio; next: corners/dividers/accents/portrait-backplate/real watermark art as actual sprites, then stat-row icons, THEN evaluate whether the warm wood HUD shell survives (deliberately NOT harmonized yet).

**Drag-and-sort + drag-to-equip (2026-06-26):** new `SlotDragHandler.cs` (added to every bag + paperdoll `ItemSlot` by `ProgressionWindows`) does **bag→bag reorder · bag→matching equip = equip · equipped→bag = unequip**, operating on the `InventoryStore`/`Character` MODEL (via `boundEntry` + `InventoryStore.Reorder`), NOT on slot `Item`s, so it never desyncs on refresh. The legacy `DraggableItem`/`ItemSlot.OnDrop`/`TryMove` path is untouched (no-ops for these slots since they carry no `DraggableItem`). Valid targets light a green `dropHighlight` halo (new `ItemSlot` channel built in `UiKit.Slot`; `ItemSlot.HighlightDropTargets`/`ClearDropTargets`) — separate from the cyan hover/select channels so it never clobbers the rarity border. Cursor-following ghost is parented to the root canvas.

**⚠️ Tooltip-stuck-on-close fixed 2026-06-26:** the shared `ItemTooltip` lives on an always-on layer and was only hidden by `ItemSlot.OnPointerExit`; closing a window while hovering a slot left it stuck (uGUI doesn't fire OnPointerExit on a slot disabled out from under the cursor). Fix = `ItemSlot.OnDisable` clears hover/drop halos + calls `ItemTooltip.HideTip()`, covering every close path (X / B-V-C toggle / Esc / canvas teardown).

**⚠️ Bug fixed 2026-06-25:** `CharacterUIBinder` used to grab the first Text named "Stats" on the canvas and write a character summary into it — it hijacked the Equipment detail panel's "Stats" label. That stats-text hijack was removed (the binder now only drives HP/MP bars; character stats are owned by ProgressionWindows). If a detail/label looks clobbered, check for name collisions with `GetComponentsInChildren<Text>().FirstOrDefault(name==...)`.

Verify via Unity MCP: edit → `refresh_unity` (scope **all** when adding a NEW .cs so it imports) → rebake `Tools/unwritten/Scene Setup/Build UI Shell` only if serialized refs changed → `manage_editor play` → screenshot `capture_source:"game_view"` (first grab per session is a throwaway; overlay UI only shows via game_view, not a named camera). Windows are hidden by default — open via `execute_code` SetActive on `Window_Equipment`/`_Character`/`_Inventory`.
