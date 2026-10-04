---
name: delete-scaffolding-every-response
description: "Delete my own throwaway files (scratch probes, before-copies, test scripts) at the end of EVERY response, not the session."
metadata:
  node_type: memory
  type: feedback
  originSessionId: d620a2f5-7cd2-4f15-840c-2dfff515140a
  modified: 2026-10-04T06:54:30.819Z
---

Every throwaway file I make — a calculator probe, a before-copy taken for a safe edit, a copied checker, a temp text
file in /tmp — is deleted as the LAST step of the same response that made it, and the report says it is gone.

**Why:** 2026-10-04 the user asked "do you auto delete your scaffoldings every response? or you just leave your trash
in our storage?" — I had left probes, before-copies and a copied checker in the session scratch folder across several
replies, reporting them as "kept in my temporary folder".

**How to apply:** before-copies exist to make an edit undoable, so keep them only until the edit is verified
(compile check + diff), then delete them in the same response. Never carry scratch files into the next reply.
Related: [[mobs-get-everything-players-get]] (the session it happened in).
