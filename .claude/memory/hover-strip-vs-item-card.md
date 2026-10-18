---
name: hover-strip-vs-item-card
description: Hover text goes on the dark hover strip (ItemTooltip.ShowName); the big scrolling item card is the right-click card only
metadata:
  node_type: memory
  type: feedback
  originSessionId: f9ac37a3-6833-4355-8206-3cd4a85266be
  modified: 2026-10-17T06:08:01.280Z
---

Anything shown on HOVER uses the dark see-through strip by the cursor — `ItemTooltip.ShowName(text)` + `HideTip`, wired with `HoverRelay.Set`. It auto-sizes and takes several lines. The big item card (`ItemTooltip.ShowText` / `Pin*`) has a picture panel and a scroll and is the RIGHT-CLICK card — never use it for a hover.

**Why:** 2026-10-17 I put the station Ink tab's "i" hover list in the item card; the user: "you put the onhover tooltip in the itemcard tooltip that has scroll and is meant for right click?"

**How to apply:** for any new hover info, call ShowName; only a right-click opens the card. See [[bag-is-a-text-list]] for the bag's hover/right-click split.
