---
name: fix-the-scope-never-remove-the-feature
description: "When the user says a feature \"should only do X\", change its scope to X — never switch the feature off as the fix."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a1ea220d-c29a-4901-8573-809691c5eaf3
  modified: 2026-10-21T07:38:08.006Z
---

"Cloudburst's total accumulates on the next set — it should only total per volley" meant: keep the yellow total, make
it reset each volley. I switched the total off instead, and the user lost the yellow digits entirely (2026-10-21,
two angry rounds: "tinanggal yung yellow digits").

**Why:** removing a feature to stop it misbehaving is not a fix; the user wanted the feature, narrowed.

**How to apply:** "it should only …" / "it should not … across …" = keep the thing, change when or how far it applies.
If the existing switches cannot express the narrower scope, build the narrower option in the same round — never ship
"off" and ask afterwards. Related: [[the-five-anti-invention]].
