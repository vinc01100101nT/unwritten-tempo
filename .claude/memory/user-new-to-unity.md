---
name: user-new-to-unity
description: "User has never used Unity — give beginner click-by-click UI steps, prefer automation over manual editor work"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 496673e1-dc83-45f8-bce5-b9a469fcccaa
---

The user is building **unwritten** in Unity but has **never used Unity before**. Shorthand like "Sprite Mode = Multiple → Slice → Grid By Cell Size → 16×16" left them lost and frustrated ("where the F should I see these settings").

**Why:** they don't know Unity's UI — chained jargon assumes familiarity they don't have, so they can't find *where* each control lives (Inspector vs Sprite Editor window vs a dropdown's options).

**How to apply:**
- For ANY Unity editor task, spell out exactly: which **window/panel**, the exact **button/field label**, and what to click **in order** — like a total beginner. Name the dropdown's current value (e.g. "the Type dropdown that says Automatic") not just the target value.
- Strongly prefer writing **editor scripts / automation** I copy into `D:\Unity3d\unwritten` (e.g. a `Tools ▸ unwritten ▸ …` menu item) so they click ONE thing instead of navigating menus. Manual editor steps are the last resort, fully hand-held.

Related: [[unwritten-foundation-first]].
