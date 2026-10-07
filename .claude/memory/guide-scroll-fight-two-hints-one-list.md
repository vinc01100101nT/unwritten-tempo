---
name: guide-scroll-fight-two-hints-one-list
description: Tutorial guide vanished at the smith's Forge page and the list was stuck at the bottom — two hints in one scrolling list each scrolled the other away every frame.
metadata:
  type: project
---

Symptom (2026-10-06, after the smith's list grew to 18 forgeable pieces, alphabetical, Sword 16th): clicking Forge
showed no dark, no arrow, no text, the list sat at the bottom and could not be scrolled.

Root cause: GuideOverlay.Decide checked every hint of the step in order and its view check SCROLLED any out-of-view
part into view as a side effect. The forge step's hints are Back (top of list) then the forge row (near bottom), more
than one window-height apart, so each frame Back scrolled to the top, the forge row scrolled to the bottom, neither
counted as in view, nothing was lit, and the list was pinned where the last scroll left it.

Fix: choose the hint first (last hint whose part exists in an open window), then scroll ONLY that part into view
(GuideOverlay.InView). Any future list long enough to separate two hints of one step would have hit the same fight.

Related: [[quest-guide-is-mandatory-and-separate-from-path]]
