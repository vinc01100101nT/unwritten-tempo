---
name: filter-red-team-findings
description: "Before relaying a red-team finding, drop anything that is an obvious implementation step, already decided by the user, or a non-problem; relay only real design holes"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 83019dbf-fb4f-4716-9bf2-815f8aa8394c
  modified: 2026-10-15T16:58:16.396Z
---

A red-team report is raw material, not a list to forward. Before relaying, strike every finding that is (a) an
obvious part of doing the work (update the wiki, re-bake prices, stop servers before a data change, adjust the
existing materials rather than make new ones), (b) already decided by the user, or (c) a problem only under a
reading of the plan nobody would act on. Relay only real design holes, each as Example → Problem → Solution.

**Why:** on 2026-10-16 four red-team rounds on the creature-rarity plan were relayed nearly whole; the user had to
swat "update the wiki", "a late save undoes the payout" (servers are stopped first), and "no material matches the
new grade" (you just adjust it) one by one, furious: "putang inang mga problemang yan na di naman talaga problema".

**How to apply:** for each finding ask "would a competent builder just do this anyway?" — yes ⇒ drop it silently.
Also brief the red team up front to skip implementation chores. Related: [[apply-review-fixes-dont-gatekeep]].

**The model of a GOOD finding** (the user, 2026-10-16: "THIS ARE THE PROBLEMS I WANT TO SEE"): "Raw +0 may still be
too strong for a new player — Example: a Flutterby at Raw +0 has 86 attack; a level 1 player with 145 health and 15
defence loses 71 per hit and dies on the 3rd. Problem: most of that is the weapon, and Raw +0 is the floor, so
nothing is left to turn down. Solution: decide how many hits a new player must survive; name the fallback." A real
design conflict, measured numbers, and the decision named.
