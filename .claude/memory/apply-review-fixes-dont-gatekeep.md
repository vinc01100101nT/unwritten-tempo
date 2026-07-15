---
name: apply-review-fixes-dont-gatekeep
description: "vince expects listed review findings to be FIXED, not triaged into \"worth it / minor\" offers — apply all, or say plainly it's a judgment call"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 03a09163-330e-4068-a637-2b6b0e261990
---

During the 2026-07-14 code review I listed 4 perf findings but only offered to fix 2, silently triaging the others as "minor"/"dev-only". vince's response: "why can't you do 1,2,3 and 4?" — the triage read as a capability limit, not a judgment call.

**Why:** when I present a numbered list of problems, vince reads every item as actionable; withholding fixes I could do looks like refusal and costs a round-trip.

**How to apply:** after a review/scan, either apply ALL listed fixes (safe ones auto-GO per the effort protocol), or explicitly label an item "skippable — low impact, say the word" rather than omitting it from the offer. Never let "not worth it" be implicit.
