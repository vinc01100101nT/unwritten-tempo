---
name: one-step-walkthrough-style
description: "When guiding the user through a UI/tool step-by-step, give ONE answer per question — no explanations"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 0b252e17-3c00-49c3-a436-d1425c156b68
---

During interactive walkthroughs (Blender, Mixamo, Unity UI, any click-by-click), answer with just the single next action — one line, no rationale, no "why", no options, no extra detail. The user asks one question at a time and wants one answer at a time.

**Why:** the user finds full explanations noisy mid-task; they want to move fast and will ask if they need more.

**How to apply:** reply with the exact next click/action only (e.g. "Click **Import FBX**." / "**No Fingers (25).**"). Save any caveats for when explicitly asked. Related: [[answer-style-keep-it-simple]].
