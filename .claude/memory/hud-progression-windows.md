---
name: hud-progression-windows
description: "Inventory/Character HUD windows — locked design + Ornament Kit v1; files, palette tokens, and drop-in art hooks"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4fe5957b-f8c9-4677-8a37-5e684d439214
---

The **Inventory (`B`) and Character (`C`) windows** are built at RUNTIME by `ProgressionWindows.cs` on top of the `UiKit.cs` construction kit — NOT by the editor builders (UIShellBuilder just `AddComponent`s it into `Assets/Resources/GameUICanvas.prefab`). Lives in the **Unity checkout** at `D:\tempo\projects\roguelike-mmo\unity\Assets\Scripts\` (see [[unity-2d-direction]] — NOT in the Tempo React worktree). There is no equipment window: what you wear is listed at the top of the inventory ([[bag-is-a-text-list]]).

**Locked design (GPT⇄Claude HUD relay, 2026-06-25):** charcoal **+** gold theme. Palette tokens live in `UiKit.cs`: `PanelFill #15161A`, `PanelRaise` (~6% lighter, for detail/sub-panels), `Gold #C8A862`, `Cyan` (hover/selection tints) and `CyanGlow` (in the palette, drawn nowhere), `TextMain`/`TextDim`. Rarity colors are the single source of truth in `Rarity.cs` (`Rarities.Border` = tooltip rarity LABEL + ground-drop sparkle; `Rarities.Text` = item NAME) — Legendary name is warm ivory `#F1E7D0` so it doesn't merge with the gold label. Fonts: Cinzel (titles, `title:true`) + Inter body, both in `Resources/Fonts/`; legacy uGUI Text has NO SemiBold and Cinzel is single-weight, so item names use Normal weight (one step under Bold titles).

**Ornament Kit v1** (`Ornament.cs`, runtime): procedural gold corner brackets + header divider-with-diamond on every `UiKit.Window`, + a `Backplate` (portrait/name plate) in the Character window. Has **drop-in sprite hooks** (`Ornament.cornerArt`/`dividerArt`, null⇒procedural) — assign final kit art via UIShellBuilder later, no layout change. GPT moved Ornament Kit to active production after locking the windows; next: corners/dividers/accents/portrait-backplate as actual sprites, then stat-row icons, THEN evaluate whether the warm wood HUD shell survives (deliberately NOT harmonized yet).

**Dragging (2026-06-26):** `SlotDragHandler.cs` sits beside the `ItemSlot` on every bag row and does **row→row = reorder · row→world = drop on the ground**, operating on the `InventoryStore` MODEL (via `boundEntry` + `InventoryStore.Reorder` / `DropAmountPrompt.DropFromScreen`), NOT on the rows, so it never desyncs on refresh. There is no drag-to-equip.

**⚠️ Tooltip-stuck-on-close fixed 2026-06-26:** the shared `ItemTooltip` lives on an always-on layer and was only hidden by `ItemSlot.OnPointerExit`; closing a window while hovering a slot left it stuck (uGUI doesn't fire OnPointerExit on a slot disabled out from under the cursor). Fix = `ItemSlot.OnDisable` calls `ItemTooltip.HideTip()`, covering every close path (X / B-C toggle / Esc / canvas teardown).

**⚠️ Bug fixed 2026-06-25:** `CharacterUIBinder` used to grab the first Text named "Stats" on the canvas and write a character summary into it — it hijacked another panel's "Stats" label. That stats-text hijack was removed (the binder now only drives HP/MP bars; character stats are owned by ProgressionWindows). If a detail/label looks clobbered, check for name collisions with `GetComponentsInChildren<Text>().FirstOrDefault(name==...)`.

Verify via Unity MCP: edit → `refresh_unity` (scope **all** when adding a NEW .cs so it imports) → rebake `Tools/unwritten/Scene Setup/Build UI Shell` only if serialized refs changed → `manage_editor play` → screenshot `capture_source:"game_view"` (first grab per session is a throwaway; overlay UI only shows via game_view, not a named camera). Windows are hidden by default — the objects are `Window_Inventory` / `Window_Character`; open them with `UiAnim.Open`, never SetActive ([[ui-window-motion-choke]]).
