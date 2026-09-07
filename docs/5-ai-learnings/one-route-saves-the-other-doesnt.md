---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# One Route Saves, the Other Doesn't

## Reach for this when

A feature has two ways to reach it — a drag and a click, a menu and a shortcut, a form and an import — and one of
them works while the other looks like it worked and then quietly comes undone: the change shows on screen, and it
is gone after a reload, or the far end refuses to act on it.

## The rule

**Do not debug the feature. Diff the two routes' LAST step — the one that commits.** Both routes almost certainly
run the same code to make the change; they differ in what they call afterwards. Find every place that mutates the
thing, list what each one calls next, and make them all call the same one. Then delete the possibility: the commit
belongs to the thing being changed, not to each caller's memory.

The reason this is invisible is worth naming, because it is what makes the search feel unnecessary: the wrong
commit helper is usually a SIBLING of the right one, and siblings share their opening lines. The visible half —
redraw the screen, play the click — runs correctly, so the change appears. Only the half that matters is missing.

## Why — the incident

A player could put a skill on their hotbar two ways: DRAG it out of the Grimoire, or CLICK its row. Dragging
worked. Clicking bound it, drew it in the slot, and made the icon look live — and then the skill did nothing at
all when pressed, and was gone from the bar at the next sign-in.

The click path called `PassivesSaved.Now()`. The drag path called `HotbarCommit.Now()`. The two helpers sit
seventy lines apart, and their first two lines are identical: rebuild what this character can use, redraw the bar.
The third line is where they differ, and it is the whole point of each of them — one tells the server the ticked
PASSIVES, the other tells it the BAR. So the server never heard about the binding: it refused every press of that
skill as not that character's, and its own write of them fifteen seconds later put the old bar back.

Cost: a two-client play-test session, and three bug reports filed for what was one wrong identifier — two of the
three ("the skill does nothing", "the bar is empty when I log in") were the same line.

A third route, a typed `/bind` command, had the same defect and nobody had noticed, because nobody used it.

## How to apply

1. **When one route works and another silently doesn't, grep for every caller of the mutation itself** — the
   method that actually changes the value — not for the feature's name. In a Unity/C# repo that is
   `grep -rn "\.Bind\|\.Set\|\.Clear" --include=*.cs`, narrowed to the type that owns the value.
2. **Print what each caller does NEXT, side by side.** The bug is in that column, every time. Two callers that
   call different helpers are the answer even before you read the helpers.
3. **Check the routes nobody uses too** — a command, a debug menu, an editor tool. They rot silently and become
   the next report.
4. **Then remove the choice.** One commit helper per concern, called from every mutation site, and say in a
   comment that it is the only one — a helper the caller may forget is a convention, and the compiler enforces
   none of it.
5. **Distrust "it looked right on screen" as evidence a change landed.** The screen is drawn by the half of the
   commit that always runs. Read back what was actually stored — the database row, the file, the server's copy.
