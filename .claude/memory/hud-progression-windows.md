---
name: hud-progression-windows
description: "Inventory/Character HUD windows — built at runtime on UiKit; the one Ragnarok-style light theme (2026-09-28); dragging and tooltip gotchas"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4fe5957b-f8c9-4677-8a37-5e684d439214
---

The **Inventory (`B`) and Character (`C`) windows** are built at RUNTIME by `ProgressionWindows.cs` on top of the `UiKit.cs` construction kit — NOT by the editor builders (UIShellBuilder just `AddComponent`s it into `Assets/Resources/GameUICanvas.prefab`). Lives in the **Unity checkout** at `D:\tempo\projects\roguelike-mmo\unity\Assets\Scripts\` (see [[unity-2d-direction]] — NOT in the Tempo React worktree). There is no equipment window: what you wear is listed at the top of the inventory ([[bag-is-a-text-list]]).

**Theme (decided by the user 2026-09-28, replacing the charcoal + gold look):** the Ragnarok-style light theme —
light window bodies, a pale-blue title bar that fades top to bottom, a soft blue-grey edge, rounded corners (5 on
windows, 3 on buttons/fields), dark text, NO corner ornaments (`Ornament.cs` was deleted), one font: DejaVu Sans
Condensed (the free stand-in for Tahoma, whose licence forbids shipping it; Cinzel and Inter were removed).
Every interface colour is a named row in `UiKit`'s palette and every shape comes from its helpers (`DressWindow`,
`Btn`, `Field`, `Card`, `Plate`, `TitleBar`, `Round`); the shapes are tinted white images in `Resources/UI/Theme`
written by `tools/ui-theme-sprites.py`. The user's words: "game-wide universal theme". Damage popups keep their
own colours by the user's choice. The full token sheet is `docs/2-systems/hud-ui-system.md`.

**Dragging (2026-06-26):** `SlotDragHandler.cs` sits beside the `ItemSlot` on every bag row and does **row→row = reorder · row→world = drop on the ground**, operating on the `InventoryStore` MODEL (via `boundEntry` + `InventoryStore.Reorder` / `DropAmountPrompt.DropFromScreen`), NOT on the rows, so it never desyncs on refresh. There is no drag-to-equip.

**⚠️ Tooltip-stuck-on-close fixed 2026-06-26:** the shared `ItemTooltip` lives on an always-on layer and was only hidden by `ItemSlot.OnPointerExit`; closing a window while hovering a slot left it stuck (uGUI doesn't fire OnPointerExit on a slot disabled out from under the cursor). Fix = `ItemSlot.OnDisable` calls `ItemTooltip.HideTip()`, covering every close path (X / B-C toggle / Esc / canvas teardown).

**⚠️ Bug fixed 2026-06-25:** `CharacterUIBinder` used to grab the first Text named "Stats" on the canvas and write a character summary into it — it hijacked another panel's "Stats" label. That stats-text hijack was removed (the binder now only drives HP/MP bars; character stats are owned by ProgressionWindows). If a detail/label looks clobbered, check for name collisions with `GetComponentsInChildren<Text>().FirstOrDefault(name==...)`.

Verify via Unity MCP: edit → `refresh_unity` (scope **all** when adding a NEW .cs so it imports) → rebake `Tools/unwritten/Scene Setup/Build UI Shell` only if serialized refs changed → `manage_editor play` → screenshot `capture_source:"game_view"` (first grab per session is a throwaway; overlay UI only shows via game_view, not a named camera). Windows are hidden by default — the objects are `Window_Inventory` / `Window_Character`; open them with `UiAnim.Open`, never SetActive ([[ui-window-motion-choke]]).
