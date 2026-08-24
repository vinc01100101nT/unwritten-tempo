---
name: editor-window
description: Use when writing or editing any Unity Editor window, inspector, or one-click tool under Tools ▸ unwritten for this project — layout widths, the shrink rule, keeping the tools catalog current, and tagging throwaway quick-fix tools.
---

# Building an editor window or tool

## EVERY WINDOW MUST SHRINK — max width only, never a fixed or minimum width

A window is docked at whatever width the user gives it. Any control that DEMANDS space forces a horizontal
scrollbar across the whole window and hides the fields beside it — and the damage is invisible while your own
window happens to be wide. One row with five hard-sized controls is enough to do it.

**The rule:**

- `GUILayout.MaxWidth(n)` — a ceiling the control may shrink below. **Use this.**
- `GUILayout.Width(n)` — an exact demand. **Never.**
- A minimum above zero. **Never.** `GUILayout.MinWidth(0f)` IS allowed and is the opposite of a minimum: it
  states "no floor", which is what lets a control shrink at all.
- Labels get `EditorLayoutFlow.ResponsiveLabelWidth` (a fraction of the space actually available, capped) rather
  than a typed number.
- `GUILayout.Height` is unaffected — vertical space scrolls, so it costs nothing.

**Self-check before shipping any editor UI:** drag the window narrow. If a horizontal scrollbar appears, some
control is demanding width and must be converted.

*(Scar: the Skill Composer's affinity row demanded 326 points across a dropdown, a lock, a slider, a percentage
and a delete button, so the whole composer scrolled sideways and cut off every field to its right.)*

## Keep the catalog in step

When you ADD or REMOVE a `Tools ▸ unwritten` menu item, update `unity/Assets/Scripts/Editor/TOOLS.md` **in the
same change**. It is the canonical catalog of every menu item.

**Never invent a menu path.** Read the real `[MenuItem("…")]` string from the source before quoting one to the
user — a filename is not the menu string, and a class name is not the label a user sees.

## Throwaway quick-fix tools — tag them so they're purgeable

When a tool exists only to patch a one-off problem (a temporary repair, a migration helper, a spike) — something
to delete once it has served its purpose:

1. Put a single-line marker on its own line directly above the class:
   `// QUICKFIX <yyyy-mm-dd> — <why it exists>; delete after <the condition that makes it obsolete>.`
2. Park its menu under **`Tools/unwritten/Quick Fixes/…`**, not the main menu.

To purge later: `grep -rl "QUICKFIX" Assets/Scripts/Editor/`, delete each hit **plus its `.meta`** — but
re-check references first. A "spike" can quietly become load-bearing: `RigBuilder25D` was self-labelled
throwaway as `Build25DSpike`, became the shared rig-builder used by ~15 files, and had to be renamed to reflect
that.

Do **not** tag recurring, idempotent maintenance tools you re-run on every re-import (e.g. `Fix Pxltgr
Materials`). Those are keepers.

## Two standing rules that outrank "just build a tool"

- **The user rejects new `Tools ▸ unwritten` tools by default.** Before building one, ask what the feature needs
  AT RUNTIME — it is often already live — and prefer a GM chat command.
- **Don't hand the user a menu click for something you can run yourself.** If the action can go through the live
  editor, run it. Menu paths are for the user only when the editor link genuinely can't do it (a live Scene-view
  interaction, Play mode, or their own judgement). See the `unity-live-editor` skill.

## After any edit here

`tools/check-unity-code.sh` checks the editor assembly too. An editor-only mistake is otherwise invisible until
someone opens the window.
