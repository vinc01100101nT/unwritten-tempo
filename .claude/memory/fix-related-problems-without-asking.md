---
name: fix-related-problems-without-asking
description: "When fixing a bug, any RELATED problem found (same root cause, other callers/places) is fixed generally in the same turn, without offering or asking"
metadata:
  node_type: memory
  type: feedback
  originSessionId: e7818a53-cd36-426d-959c-990bbc29332a
  modified: 2026-10-28T15:50:06.933Z
---

When a fix turns up related problems — the same root cause hitting other callers, other places, other kinds of thing — fix them all, generally, in the same turn. Do not end with "want me to switch these too?".

**Why:** the user, 2026-10-28, after I offered to apply the under-bridge ground fix to loot drops, quest arrows and falling weapons: "yes make a general fix automatically whenever you find any related problem".

**How to apply:** this is a standing go for RELATED problems only (same cause or same class as the bug being fixed). An unrelated finding, or a design/behaviour choice, still gets asked about first (CONFIRM BEFORE CHANGING). Name every related fix in the report.
