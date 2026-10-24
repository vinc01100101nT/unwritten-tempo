---
name: check-ui-with-real-filled-state
description: "Before reporting a HUD/UI look change, watch it with the thing FILLED the way a real player has it — not only the empty state the test account shows"
metadata:
  node_type: memory
  type: feedback
  originSessionId: dcadefdb-625b-4782-b510-025dc8e406de
  modified: 2026-10-24T22:10:56.082Z
---

A UI look change is judged on the state players actually see. The test character "Alpha" has an EMPTY hotbar, so a
plate I put behind the hotbar looked fine on Alpha and was reported as watched — on the user's character, every
skill icon on the bar came out dimmed and the bar looked heavier. The user: "the hotbar you made is worse. icons are
now dark as well", then "rollback the hotbar changes".

**Why:** watching only the empty state proves nothing about the filled one; icons, counts and cooldown shades are
what a bar is FOR.

**How to apply:** before reporting any HUD/window look change, put real content in it first (bind skills and items
to the bar, fill the bag, open a quest) and capture THAT. If the test account cannot be given that state, say the
filled state was not watched, plainly, in the report.
