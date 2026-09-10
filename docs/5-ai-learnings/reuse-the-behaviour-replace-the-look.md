---
status:
owner: vince
last-updated: 2026-09-09
tags:
  - ai-learnings
---

# Reuse the Widget's Behaviour, Replace Only Its Look

## Reach for this when

You are about to replace how something is PRESENTED — a grid becomes a list, cards become a table, icons become
text — and you have started sketching out how to re-implement its hover, its clicks, its drag-and-drop.

## The rule

**Find the component that owns the interactions and check what it actually needs.** If it drives itself from
DATA rather than from the pixels, attach it to the new presentation unchanged and write only the new rendering.
A widget's behaviour and its look are usually separable even when they were never designed to be, and the tell
is null-guarded visual references: every `if (icon != null)` in that component is permission to run without one.

Then name the one thing that genuinely does break, and say it out loud instead of discovering it later.

## Why

Replacing a 40-square icon inventory with a text list looked like a rewrite of hover, tooltips, the
equip-preview, click, double-click, drag-to-reorder and drag-to-equip — seven behaviours, all working, all
server-authoritative, all with edge cases nobody remembers.

Reading the two components first showed they took every decision from a bound data entry and null-guarded every
visual field they had. So each text row was given the **same two components with none of their visual references
wired**, and all seven behaviours kept working with zero lines of interaction code written. The port became one
new rendering file plus a handful of lines in the window.

The estimate before reading was "port the drag, port the preview". The estimate after reading was "attach the
component". Same result, and one of them is a day.

**And the honest half:** exactly one thing broke, and it was cosmetic — the drag ghost is a copy of the icon, so
with no icon there is nothing to see while dragging. Found by reading, stated up front, not discovered by the
user.

## How to apply

1. **Open the interaction component before designing anything.** List what it reads: data, or geometry and
   sprites? Data means it will travel.
2. **Grep its own null guards.** Optional references are the contract that says "safe to leave null" — usually in
   as many words, in a tooltip or a comment.
3. **Check the required components.** A `[RequireComponent]` tells you the minimum the new presentation must
   still carry.
4. **Attach it to the new thing and write only the renderer.** Never fork the behaviour: two copies of a drag
   handler is the dual-path bug waiting to happen.
5. **Name what genuinely degrades**, in the report, before anyone plays it.
6. **Keep the new files additive until they compile.** Write and compile the new renderer while the old one is
   still wired up, then make the swap in one small, reversible edit. The window is never broken in between.
