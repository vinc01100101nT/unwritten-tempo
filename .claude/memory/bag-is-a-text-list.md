---
name: bag-is-a-text-list
description: "The Inventory window's 40-square icon grid was replaced by sortable/searchable TEXT ROWS; each row carries an ItemSlot + SlotDragHandler so every old interaction still works."
metadata:
  node_type: memory
  type: project
---

**Ported 2026-09-09**, from a clickable HTML prototype in `prototypes/text-inventory.html`.

The bag no longer draws 40 icon squares. `InventoryList.cs` renders **text rows** — name (rarity-coloured, with
the `+N` refine prefix), slot, tier, skill count, attack-or-defence, and the rarity output lift — inside the same
Inventory window, same `B` key, same tabs, same footer.

**The load-bearing trick: each row carries an `ItemSlot` plus a `SlotDragHandler` with none of their visual
references wired.** Both components drive themselves from the bound `BagEntry` and null-guard every visual field,
so hovering, the equip→stat-delta preview, clicking, double-click-to-equip, drag-to-reorder and drag-to-equip all
kept working with zero interaction code rewritten. Do NOT reimplement any of those on the rows.

**Decisions baked in:**
- **Sorting is a LENS, never the order.** A column header cycles ascending → descending → **off**, and off is the
  store's own order, which is the player's and is what dragging sets. Two owners of one order is a bug.
- Search is case-insensitive and reaches the **skills a weapon carries**, so "momentum" finds the swords with it.
- The gear columns hide on a tab of potions rather than filling with dashes. The `All` tab keeps them.
- `ItemActionMenu.cs` is the game's first runtime right-click menu. Every enabled row calls an existing store
  method; actions the crafting design describes but nobody built are shown DISABLED and tagged "planned".

**TWO TRAPS THAT COST A ROUND (2026-09-10):**
- **`enableWordWrapping` IS OBSOLETE AND SILENTLY DOES NOTHING in this Unity version.** Setting it left every
  cell wrapping, so names wrapped to two lines and were clipped by the 22px row — they read as fragments or as
  blank, and it looked like weapons were missing from the list. The real switch is the `wrap` argument on
  `UiKit.MakeText`, which sets `textWrappingMode`. Pass `wrap: false`; never assign the old property.
- **A show/hide toggle that early-returns when the new value equals the starting field value never fires on its
  first call** — because that starting value had never actually been APPLIED. The Held column stayed visible on
  every tab and ate the name column's width. Apply the state on BUILD as well as on change, with no equality
  guard.
- **Do not build a header from `UiKit.Btn`.** It is a bordered, bold, centre-aligned button with a
  Border → Fill → Text structure, so it can never line up with a row cell. Build header cells exactly like row
  cells and put the `Button` on that same object.

**Gotchas:**
- **There is no per-item Drop in the game** — `InventoryStore` has `Equip`, `Unequip`, `Consume`, `Reorder`,
  `Give`, `GiveEquipment`, `ClearAll` and nothing else. Drop is disabled in the menu for that reason. Don't call
  a `Discard` — it does not exist.
- A drag's floating ghost is a copy of the icon, so with no icon **there is nothing visible while dragging**. The
  drop still resolves. Fixing it means giving `SlotDragHandler` a text ghost.
- `EquipSlots.Label/Order` in `Item.cs` are new: nothing in the game named a slot in words before (the paperdoll
  draws silhouettes). `Order` is the PAPERDOLL's order, not alphabetical.
- The list height is fixed (300px) on purpose, so the window is one size on every tab.
