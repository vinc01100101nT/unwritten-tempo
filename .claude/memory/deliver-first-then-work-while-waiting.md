---
name: deliver-first-then-work-while-waiting
description: "When the user says \"give me X, and while waiting do Y\", hand X over in a reply FIRST, then start Y"
metadata:
  node_type: memory
  type: feedback
  originSessionId: dcadefdb-625b-4782-b510-025dc8e406de
  modified: 2026-10-24T14:14:10.512Z
---

"Give me the prompts, and while waiting for the result do the code" means: send the prompts in a reply right away,
then do the code. Doing both and handing the prompts over at the end is the failure.

**Why:** 2026-10-24 — I wrote the Leonardo prompts, kept them in a scratch file, and went straight into the code. The
user, waiting to paste them into Leonardo, had to stop me: "i said give me the prompts first".

**How to apply:** when a request contains "while waiting" / "meanwhile", the thing the user waits ON is delivered in
visible text before the first tool call of the other job.
