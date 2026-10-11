---
name: bag-is-a-text-list
description: "The Inventory window's 40-square icon grid was replaced by sortable/searchable TEXT ROWS; each row carries an ItemSlot + SlotDragHandler so every old interaction still works."
metadata:
  node_type: memory
  type: project
---

**Ported 2026-09-09** from a clickable HTML prototype; made compact on 2026-10-11 (324×296, 20-tall rows) from `prototypes/compact-character-and-bag.html`, which is now its mock.

The bag no longer draws 40 icon squares. `InventoryList.cs` renders **text rows** in three columns — name
(rarity-coloured, with the `+N` refine prefix), tier, and how many are held — inside the same Inventory window,
same `B` key, same tabs, same footer.

**The load-bearing trick: each row carries an `ItemSlot` plus a `SlotDragHandler`.** Both drive themselves from
the bound `BagEntry` and draw nothing, so hovering, the equip→stat-delta preview, clicking, double-click-to-equip,
drag-to-reorder and drag-onto-the-ground all work with no interaction code on the rows. Do NOT reimplement any of
those on the rows.

**Decisions baked in:**
- **Sorting is a LENS, never the order.** A column header cycles ascending → descending → **off**, and off is the
  store's own order, which is the player's and is what dragging sets. Two owners of one order is a bug.
- Search is case-insensitive and reaches the **skills a weapon carries**, so "momentum" finds the swords with it.
- **Every column shows on every tab — the user's rule (2026-09-14): a column that only means something on some
  tabs does not exist.** Slot, Skills, Atk/Def and Out were deleted for that reason, and there is no per-tab column
  switch any more; gear reads `1` under Held.
- `ItemActionMenu.cs` is the game's first runtime right-click menu. Every enabled row calls an existing store
  method; actions the crafting design describes but nobody built are shown DISABLED and tagged "planned".

**TWO TRAPS THAT COST A ROUND (2026-09-10):**
- **`enableWordWrapping` IS OBSOLETE AND SILENTLY DOES NOTHING in this Unity version.** Setting it left every
  cell wrapping, so names wrapped to two lines and were clipped by the 22px row — they read as fragments or as
  blank, and it looked like weapons were missing from the list. The real switch is the `wrap` argument on
  `UiKit.MakeText`, which sets `textWrappingMode`. Pass `wrap: false`; never assign the old property.
- **Do not build a header from `UiKit.Btn`.** It is a bordered, bold, centre-aligned button with a
  Border → Fill → Text structure, so it can never line up with a row cell. Build header cells exactly like row
  cells and put the `Button` on that same object.

**Gotchas:**
- **Dropping an item goes through `InventoryStore.Drop`**, reached from the menu's Drop and from dragging a row onto
  the ground — both via `DropAmountPrompt.DropFromScreen`. It refuses worn pieces, quest items and the Ring.
- A drag's floating ghost has no icon to show. A readable ghost means giving `SlotDragHandler` a text ghost.
- `EquipSlots.Label/Order` in `Item.cs` name a slot in words for the list. `Order` is a body's order, not
  alphabetical.
- The list height is fixed (340px) on purpose, so the window is one size on every tab.
